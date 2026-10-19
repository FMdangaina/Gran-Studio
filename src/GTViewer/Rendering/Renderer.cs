using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace GTViewer.Rendering;

/// <summary>Vertex sent to the GPU: position, normal, UV, vertex colour (colorSet1: GT6 grey occlusion; white when none) (48 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GpuVertex(Vector3 p, Vector3 n, Vector2 uv, Vector4 color)
{
    public Vector3 Position = p;
    public Vector3 Normal = n;
    public Vector2 Uv = uv;
    public Vector4 Color = color;
}

/// <summary>How a material is drawn (from its RSX render state): opaque, alpha test, alpha blend, additive.</summary>
public enum BlendMode { Opaque, AlphaTest, Blend, Additive }

/// <summary>One decoded shape on the GPU.</summary>
public sealed class GpuMesh : IDisposable
{
    public ID3D11Buffer Vertices = null!, Indices = null!;
    public int IndexCount;
    public Vector3 Min, Max;
    public void Dispose() { Vertices.Dispose(); Indices.Dispose(); }
}

/// <summary>A texture on the GPU (RGBA8, level 0).</summary>
public sealed class GpuTexture(ID3D11Texture2D tex, ID3D11ShaderResourceView view) : IDisposable
{
    public ID3D11ShaderResourceView View = view;
    public void Dispose() { View.Dispose(); tex.Dispose(); }
}

/// <summary>What to draw: a mesh with its colour (under the texture when there is one), its draw mode and alpha test reference (0..1).</summary>
public readonly record struct DrawItem(GpuMesh Mesh, Vector4 Color, GpuTexture? Texture, bool Selected, BlendMode Mode = BlendMode.Opaque, float AlphaRef = 0);

/// <summary>
/// Direct3D 11 renderer: a window swap chain or an off-screen target, one simple shader (two-sided Lambert with a
/// hemisphere ambient, optional diffuse texture), solid or wireframe. Right-handed view (System.Numerics matrices),
/// as the GT data. Falls back to WARP (software) when no hardware device can be made.
/// </summary>
public sealed class Renderer : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct Constants
    {
        public Matrix4x4 ViewProj;
        public Vector4 Color;
        public Vector4 Light;      // xyz = direction the light travels, w = vertex occlusion on
        public Vector4 Params;     // x = textured, y = selected, z = draw mode, w = alpha test reference
    }

    const string Hlsl = """
        cbuffer C : register(b0) { row_major float4x4 ViewProj; float4 Color; float4 Light; float4 Params; }
        struct VSIn { float3 p : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; float4 c : COLOR0; };
        struct PSIn { float4 p : SV_POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; float4 c : COLOR0; };
        Texture2D Tex : register(t0);
        SamplerState Smp : register(s0);
        PSIn VS(VSIn i) { PSIn o; o.p = mul(float4(i.p, 1), ViewProj); o.n = i.n; o.uv = i.uv; o.c = i.c; return o; }
        float4 PS(PSIn i) : SV_Target
        {
            float3 n = normalize(i.n + 1e-6);
            float d = abs(dot(n, -Light.xyz));                 // two-sided: the winding is not trusted
            float3 base = Color.rgb;
            float a = Color.a;
            if (Params.x > 0)
            {
                float4 t = Tex.Sample(Smp, i.uv);
                if (Params.z == 1) clip(t.a - Params.w);       // alpha test (GT5: GREATER 64)
                if (Params.z >= 2) { base = t.rgb; a = t.a; }  // blended decals and glass: the texture's own alpha
                // opaque decal sheets: where the texture is transparent the paint under it shows (their RGB there is junk)
                else base = lerp(base, t.rgb, t.a);
            }
            if (Light.w > 0) base *= lerp(1.0, i.c.rgb, 0.8); // vertex occlusion (colorSet1), softened
            float3 c = base * (0.30 + 0.25 * (n.y * 0.5 + 0.5) + 0.55 * d);
            if (Params.y > 0) c = lerp(c, float3(1.0, 0.55, 0.1), 0.6);
            return float4(c, a);
        }
        """;

    public ID3D11Device Device { get; }
    public ID3D11DeviceContext Context { get; }
    public bool Software { get; }
    readonly ID3D11VertexShader vs;
    readonly ID3D11PixelShader ps;
    readonly ID3D11InputLayout layout;
    readonly ID3D11Buffer constants;
    readonly ID3D11RasterizerState solid, wire;
    readonly ID3D11DepthStencilState depth;
    readonly ID3D11SamplerState sampler;
    IDXGISwapChain1? swapChain;
    ID3D11Texture2D? color, depthTex;
    ID3D11RenderTargetView? rtv;
    ID3D11DepthStencilView? dsv;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool Wireframe { get; set; }
    public Vector4 Background { get; set; } = new(0.16f, 0.17f, 0.19f, 1);

    public Renderer()
    {
        FeatureLevel[] levels = [FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0];
        var flags = DeviceCreationFlags.BgraSupport;
        ID3D11Device dev;
        ID3D11DeviceContext ctx;
        if (D3D11.D3D11CreateDevice(null, DriverType.Hardware, flags, levels, out dev, out ctx).Failure)
        {
            D3D11.D3D11CreateDevice(null, DriverType.Warp, flags, levels, out dev, out ctx).CheckError();
            Software = true;
        }
        Device = dev;
        Context = ctx;

        var vsCode = Compiler.Compile(Hlsl, "VS", "viewer.hlsl", "vs_4_0");
        var psCode = Compiler.Compile(Hlsl, "PS", "viewer.hlsl", "ps_4_0");
        vs = Device.CreateVertexShader(vsCode.Span);
        ps = Device.CreatePixelShader(psCode.Span);
        layout = Device.CreateInputLayout(
        [
            new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 24, 0),
            new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 32, 0),
        ], vsCode.Span);
        constants = Device.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<Constants>(), BindFlags.ConstantBuffer));
        solid = Device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid) { DepthClipEnable = true });
        wire = Device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Wireframe) { DepthClipEnable = true });
        depth = Device.CreateDepthStencilState(DepthStencilDescription.Default);
        depthNoWrite = Device.CreateDepthStencilState(DepthStencilDescription.DepthRead);
        alphaBlend = Device.CreateBlendState(BlendDescription.NonPremultiplied);
        additive = Device.CreateBlendState(BlendDescription.Additive);
        sampler = Device.CreateSamplerState(SamplerDescription.AnisotropicWrap);
    }

    readonly ID3D11DepthStencilState depthNoWrite;
    readonly ID3D11BlendState alphaBlend, additive;

    /// <summary>Darken by the vertex colours (GT6 colorSet1 = baked occlusion).</summary>
    public bool VertexOcclusion { get; set; } = true;

    /// <summary>Render into a window (its client area).</summary>
    public void AttachWindow(IntPtr hwnd, int width, int height)
    {
        using var dxgiDevice = Device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();
        using var factory = adapter.GetParent<IDXGIFactory2>();
        var desc = new SwapChainDescription1((uint)Math.Max(1, width), (uint)Math.Max(1, height), Format.B8G8R8A8_UNorm)
        {
            BufferCount = 2,
            SwapEffect = SwapEffect.FlipDiscard,
            BufferUsage = Usage.RenderTargetOutput,
            SampleDescription = new SampleDescription(1, 0),
        };
        swapChain = factory.CreateSwapChainForHwnd(Device, hwnd, desc);
        factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter);
        CreateTargets(width, height);
    }

    /// <summary>Render off screen (for PNG shots).</summary>
    public void CreateOffscreen(int width, int height) => CreateTargets(width, height);

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0 || (width == Width && height == Height)) return;
        CreateTargets(width, height);
    }

    void CreateTargets(int width, int height)
    {
        width = Math.Max(1, width); height = Math.Max(1, height);
        Context.OMSetRenderTargets((ID3D11RenderTargetView?)null, null);
        rtv?.Dispose(); dsv?.Dispose(); color?.Dispose(); depthTex?.Dispose();
        if (swapChain != null)
        {
            swapChain.ResizeBuffers(0, (uint)width, (uint)height, Format.Unknown, SwapChainFlags.None).CheckError();
            color = swapChain.GetBuffer<ID3D11Texture2D>(0);
        }
        else
            color = Device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1, BindFlags.RenderTarget));
        rtv = Device.CreateRenderTargetView(color);
        depthTex = Device.CreateTexture2D(new Texture2DDescription(Format.D32_Float, (uint)width, (uint)height, 1, 1, BindFlags.DepthStencil));
        dsv = Device.CreateDepthStencilView(depthTex);
        Width = width; Height = height;
    }

    public GpuMesh CreateMesh(GpuVertex[] vertices, int[] indices)
    {
        var mesh = new GpuMesh
        {
            Vertices = Device.CreateBuffer(vertices, BindFlags.VertexBuffer),
            Indices = Device.CreateBuffer(indices, BindFlags.IndexBuffer),
            IndexCount = indices.Length,
            Min = vertices.Length == 0 ? Vector3.Zero : vertices.Select(v => v.Position).Aggregate(Vector3.Min),
            Max = vertices.Length == 0 ? Vector3.Zero : vertices.Select(v => v.Position).Aggregate(Vector3.Max),
        };
        return mesh;
    }

    /// <summary>RGBA8 pixels (level 0) as a texture, with its mip levels made on the GPU (no shimmer on big liveries seen from afar).</summary>
    public GpuTexture CreateTexture(byte[] rgba, int width, int height)
    {
        var desc = new Texture2DDescription(Format.R8G8B8A8_UNorm, (uint)width, (uint)height, 1, 0,
            BindFlags.ShaderResource | BindFlags.RenderTarget, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.GenerateMips);
        var tex = Device.CreateTexture2D(desc);
        var handle = GCHandle.Alloc(rgba, GCHandleType.Pinned);
        try { Context.UpdateSubresource(tex, 0, null, handle.AddrOfPinnedObject(), (uint)(width * 4), 0); }
        finally { handle.Free(); }
        var view = Device.CreateShaderResourceView(tex);
        Context.GenerateMips(view);
        return new GpuTexture(tex, view);
    }

    public void Render(Matrix4x4 viewProj, Vector3 lightDir, IEnumerable<DrawItem> items)
    {
        if (rtv == null) return;
        Context.OMSetRenderTargets(rtv, dsv);
        Context.ClearRenderTargetView(rtv, new Color4(Background.X, Background.Y, Background.Z, Background.W));
        Context.ClearDepthStencilView(dsv!, DepthStencilClearFlags.Depth, 1, 0);
        Context.RSSetViewport(new Viewport(Width, Height));
        Context.RSSetState(Wireframe ? wire : solid);
        Context.OMSetDepthStencilState(depth);
        Context.IASetInputLayout(layout);
        Context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        Context.VSSetShader(vs);
        Context.PSSetShader(ps);
        Context.VSSetConstantBuffer(0, constants);
        Context.PSSetConstantBuffer(0, constants);
        Context.PSSetSampler(0, sampler);
        // opaque and alpha-tested first, then blended and additive ones (depth read, no write), as the game orders them
        var list = items.ToList();
        foreach (var it in list.Where(x => x.Mode <= BlendMode.AlphaTest).Concat(list.Where(x => x.Mode == BlendMode.Blend)).Concat(list.Where(x => x.Mode == BlendMode.Additive)))
        {
            if (it.Mesh.IndexCount == 0) continue;
            bool blended = it.Mode >= BlendMode.Blend && !Wireframe;
            Context.OMSetBlendState(!blended ? null : it.Mode == BlendMode.Blend ? alphaBlend : additive);
            Context.OMSetDepthStencilState(blended ? depthNoWrite : depth);
            var c = new Constants
            {
                ViewProj = viewProj,
                Color = it.Color,
                Light = new Vector4(Vector3.Normalize(lightDir), VertexOcclusion ? 1 : 0),
                Params = new Vector4(it.Texture != null ? 1 : 0, it.Selected ? 1 : 0, (int)it.Mode, it.AlphaRef),
            };
            Context.UpdateSubresource(in c, constants);
            Context.PSSetShaderResource(0, it.Texture?.View);
            Context.IASetVertexBuffer(0, it.Mesh.Vertices, (uint)Marshal.SizeOf<GpuVertex>());
            Context.IASetIndexBuffer(it.Mesh.Indices, Format.R32_UInt, 0);
            Context.DrawIndexed((uint)it.Mesh.IndexCount, 0, 0);
        }
        Context.OMSetBlendState(null);
    }

    public void Present() => swapChain?.Present(1, PresentFlags.None);

    /// <summary>The rendered image as BGRA rows (width x height x 4).</summary>
    public byte[] ReadPixels()
    {
        var desc = new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)Width, (uint)Height, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read);
        using var staging = Device.CreateTexture2D(desc);
        Context.CopyResource(staging, color!);
        var map = Context.Map(staging, 0, MapMode.Read);
        var bytes = new byte[Width * Height * 4];
        for (int y = 0; y < Height; y++)
            Marshal.Copy(map.DataPointer + (nint)(y * map.RowPitch), bytes, y * Width * 4, Width * 4);
        Context.Unmap(staging, 0);
        return bytes;
    }

    public void Dispose()
    {
        rtv?.Dispose(); dsv?.Dispose(); color?.Dispose(); depthTex?.Dispose(); swapChain?.Dispose();
        alphaBlend.Dispose(); additive.Dispose(); depthNoWrite.Dispose();
        sampler.Dispose(); depth.Dispose(); wire.Dispose(); solid.Dispose(); constants.Dispose(); layout.Dispose();
        ps.Dispose(); vs.Dispose(); Context.Dispose(); Device.Dispose();
    }
}
