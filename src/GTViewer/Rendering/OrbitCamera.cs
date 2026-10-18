using System.Numerics;

namespace GTViewer.Rendering;

/// <summary>Orbit camera around a target: yaw/pitch in degrees, distance; right-handed, Y up.</summary>
public sealed class OrbitCamera
{
    public Vector3 Target;
    public float Yaw = 145, Pitch = 18, Distance = 10, FieldOfView = 40;   // GT cars face -Z: front three-quarter view

    public Vector3 Eye
    {
        get
        {
            float y = Yaw * MathF.PI / 180, p = Pitch * MathF.PI / 180;
            return Target + Distance * new Vector3(MathF.Cos(p) * MathF.Sin(y), MathF.Sin(p), MathF.Cos(p) * MathF.Cos(y));
        }
    }

    public Matrix4x4 ViewProj(float aspect)
    {
        var view = Matrix4x4.CreateLookAt(Eye, Target, Vector3.UnitY);
        float near = Math.Max(Distance * 0.002f, 0.001f), far = Distance * 20 + 100;
        return view * Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView * MathF.PI / 180, aspect, near, far);
    }

    /// <summary>Look at a box from the current angles so that it fills the view.</summary>
    public void Frame(Vector3 min, Vector3 max)
    {
        Target = (min + max) / 2;
        float radius = Math.Max(0.01f, (max - min).Length() / 2);
        Distance = radius / MathF.Tan(FieldOfView * MathF.PI / 360) * 0.85f;   // the box corners may leave the view a bit
    }

    public void Orbit(float dx, float dy)
    {
        Yaw -= dx * 0.4f;
        Pitch = Math.Clamp(Pitch + dy * 0.4f, -89, 89);
    }

    public void Zoom(float steps) => Distance = Math.Max(0.01f, Distance * MathF.Pow(0.88f, steps));

    /// <summary>Move the target in the view plane by pixels of a viewport of the given height.</summary>
    public void Pan(float dx, float dy, int viewportHeight)
    {
        var forward = Vector3.Normalize(Target - Eye);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Cross(right, forward);
        float perPixel = 2 * Distance * MathF.Tan(FieldOfView * MathF.PI / 360) / Math.Max(1, viewportHeight);
        Target += (-dx * right + dy * up) * perPixel;
    }
}
