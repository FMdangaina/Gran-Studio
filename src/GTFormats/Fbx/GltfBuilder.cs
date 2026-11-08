#nullable disable
namespace GTFormats.Fbx
{
    using System;
    using System.Buffers.Binary;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Numerics;
    using System.Text;
    using System.Text.Json.Nodes;

    // What the scene exports write through: the FBX and the glTF builders take the same calls, so both files get the
    // same hierarchy (same rule as Forza Studio's IModelSceneBuilder).
    public interface ISceneBuilder
    {
        int MeshCount { get; }

        // parent 0 = the scene root.
        long AddNull(string name, long parent);

        long AddMesh(string name, long parent, FbxMaterial material, Vector3[] position, Vector3[] normal, Vector2[] uv, int[] triangles);

        byte[] ToBytes();
    }

    // Writes a glTF 2.0 binary file (.glb): nodes without mesh for the hierarchy, one mesh node per AddMesh with one
    // material, POSITION / NORMAL / TEXCOORD_0, uint32 triangle indices. Meters, Y up, right handed (as the FBX).
    // Textures go INSIDE the .glb as opaque PNG (glTF has no TGA), from FbxMaterial.Rgba; UV are "v up" like the FBX
    // input and are flipped back here (glTF has v down).
    public sealed class GltfBuilder : ISceneBuilder
    {
        private readonly MemoryStream bin = new MemoryStream();
        private readonly JsonArray nodes = new JsonArray();
        private readonly JsonArray meshes = new JsonArray();
        private readonly JsonArray accessors = new JsonArray();
        private readonly JsonArray views = new JsonArray();
        private readonly JsonArray materials = new JsonArray();
        private readonly JsonArray textures = new JsonArray();
        private readonly JsonArray images = new JsonArray();
        private readonly JsonArray roots = new JsonArray();
        private readonly Dictionary<long, JsonArray> children = new Dictionary<long, JsonArray>();
        private readonly Dictionary<FbxMaterial, int> materialOf = new Dictionary<FbxMaterial, int>();
        private readonly Dictionary<string, int> textureOf = new Dictionary<string, int>();
        private readonly string creator;

        public GltfBuilder(string creator)
        {
            this.creator = creator;
        }

        public int MeshCount { get { return this.meshes.Count; } }

        // Node ids are index + 1, so that 0 stays "the scene root" as in the FBX builder.
        private long Node(JsonObject node, long parent)
        {
            this.nodes.Add(node);
            int index = this.nodes.Count - 1;
            if (parent == 0)
            {
                this.roots.Add(index);
            }
            else
            {
                JsonArray list;
                if (!this.children.TryGetValue(parent, out list))
                {
                    this.children[parent] = list = new JsonArray();
                }

                list.Add(index);
            }

            return index + 1;
        }

        public long AddNull(string name, long parent)
        {
            return this.Node(new JsonObject { ["name"] = name }, parent);
        }

        public long AddMesh(string name, long parent, FbxMaterial material, Vector3[] position, Vector3[] normal, Vector2[] uv, int[] triangles)
        {
            // only the vertices the triangles use, in first-use order
            int corners = triangles.Length / 3 * 3;
            Dictionary<int, int> remap = new Dictionary<int, int>();
            List<int> used = new List<int>();
            uint[] index = new uint[corners];
            for (int k = 0; k < corners; k++)
            {
                int source = triangles[k];
                int v;
                if (!remap.TryGetValue(source, out v))
                {
                    v = used.Count;
                    remap[source] = v;
                    used.Add(source);
                }

                index[k] = (uint)v;
            }

            int count = used.Count;
            byte[] pos = new byte[count * 12];
            Vector3 min = new Vector3(float.MaxValue), max = new Vector3(float.MinValue);
            for (int i = 0; i < count; i++)
            {
                Vector3 p = position[used[i]];
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                Put(pos, i * 12, p.X, p.Y, p.Z);
            }

            JsonObject attributes = new JsonObject();
            attributes["POSITION"] = this.Accessor(pos, count, "VEC3", 5126, 34962, new JsonArray(min.X, min.Y, min.Z), new JsonArray(max.X, max.Y, max.Z));
            if (normal != null)
            {
                byte[] nrm = new byte[count * 12];
                for (int i = 0; i < count; i++)
                {
                    Vector3 n = normal[used[i]];
                    float length = n.Length();
                    n = length > 1e-6f && !float.IsNaN(length) ? n / length : Vector3.UnitY;   // glTF wants unit normals
                    Put(nrm, i * 12, n.X, n.Y, n.Z);
                }

                attributes["NORMAL"] = this.Accessor(nrm, count, "VEC3", 5126, 34962);
            }

            if (uv != null)
            {
                byte[] tex = new byte[count * 8];
                for (int i = 0; i < count; i++)
                {
                    Vector2 t = uv[used[i]];
                    Put(tex, i * 8, t.X, 1 - t.Y);
                }

                attributes["TEXCOORD_0"] = this.Accessor(tex, count, "VEC2", 5126, 34962);
            }

            byte[] idx = new byte[corners * 4];
            for (int k = 0; k < corners; k++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(idx.AsSpan(k * 4), index[k]);
            }

            JsonObject primitive = new JsonObject
            {
                ["attributes"] = attributes,
                ["indices"] = this.Accessor(idx, corners, "SCALAR", 5125, 34963),
                ["mode"] = 4,
            };
            if (material != null)
            {
                primitive["material"] = this.Material(material);
            }

            this.meshes.Add(new JsonObject { ["name"] = name, ["primitives"] = new JsonArray(primitive) });
            return this.Node(new JsonObject { ["name"] = name, ["mesh"] = this.meshes.Count - 1 }, parent);
        }

        private int Material(FbxMaterial material)
        {
            int index;
            if (this.materialOf.TryGetValue(material, out index))
            {
                return index;
            }

            JsonObject pbr = new JsonObject { ["metallicFactor"] = 0.0, ["roughnessFactor"] = 0.6 };
            int texture = this.Texture(material);
            if (texture >= 0)
            {
                pbr["baseColorTexture"] = new JsonObject { ["index"] = texture };
            }
            else
            {
                pbr["baseColorFactor"] = new JsonArray(material.Color.X, material.Color.Y, material.Color.Z, 1.0);
            }

            this.materials.Add(new JsonObject { ["name"] = material.Name ?? "material", ["pbrMetallicRoughness"] = pbr });
            index = this.materials.Count - 1;
            this.materialOf[material] = index;
            return index;
        }

        // One image per distinct texture (by file name when there is one, else by the pixel buffer).
        private int Texture(FbxMaterial material)
        {
            if (material.Rgba == null || material.Width <= 0 || material.Height <= 0)
            {
                return -1;
            }

            string key = material.TextureFile ?? ("#" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(material.Rgba));
            int index;
            if (this.textureOf.TryGetValue(key, out index))
            {
                return index;
            }

            byte[] png = Png(material.Rgba, material.Width, material.Height);
            this.Align();
            int view = this.View(png, null);
            string imageName = material.TextureFile != null ? Path.GetFileNameWithoutExtension(material.TextureFile) : material.Name;
            this.images.Add(new JsonObject { ["name"] = imageName, ["bufferView"] = view, ["mimeType"] = "image/png" });
            if (this.textures.Count == 0)
            {
                this.samplers = new JsonArray(new JsonObject { ["magFilter"] = 9729, ["minFilter"] = 9987, ["wrapS"] = 10497, ["wrapT"] = 10497 });
            }

            this.textures.Add(new JsonObject { ["sampler"] = 0, ["source"] = this.images.Count - 1 });
            index = this.textures.Count - 1;
            this.textureOf[key] = index;
            return index;
        }

        private JsonArray samplers;

        private static void Put(byte[] data, int at, params float[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at + (i * 4)), values[i]);
            }
        }

        private void Align()
        {
            while (this.bin.Length % 4 != 0)
            {
                this.bin.WriteByte(0);
            }
        }

        private int View(byte[] data, int? target)
        {
            JsonObject view = new JsonObject { ["buffer"] = 0, ["byteOffset"] = this.bin.Length, ["byteLength"] = data.Length };
            if (target.HasValue)
            {
                view["target"] = target.Value;
            }

            this.bin.Write(data, 0, data.Length);
            this.views.Add(view);
            return this.views.Count - 1;
        }

        private int Accessor(byte[] data, int count, string type, int componentType, int target, JsonArray min = null, JsonArray max = null)
        {
            this.Align();
            JsonObject accessor = new JsonObject
            {
                ["bufferView"] = this.View(data, target),
                ["componentType"] = componentType,
                ["count"] = count,
                ["type"] = type,
            };
            if (min != null)
            {
                accessor["min"] = min;
                accessor["max"] = max;
            }

            this.accessors.Add(accessor);
            return this.accessors.Count - 1;
        }

        public byte[] ToBytes()
        {
            foreach (KeyValuePair<long, JsonArray> pair in this.children)
            {
                ((JsonObject)this.nodes[(int)pair.Key - 1])["children"] = pair.Value;
            }

            this.Align();
            JsonObject root = new JsonObject
            {
                ["asset"] = new JsonObject { ["version"] = "2.0", ["generator"] = this.creator },
                ["scene"] = 0,
                ["scenes"] = new JsonArray(new JsonObject { ["nodes"] = this.roots }),
                ["nodes"] = this.nodes,
            };
            if (this.meshes.Count > 0) root["meshes"] = this.meshes;
            if (this.materials.Count > 0) root["materials"] = this.materials;
            if (this.textures.Count > 0)
            {
                root["textures"] = this.textures;
                root["images"] = this.images;
                root["samplers"] = this.samplers;
            }

            if (this.accessors.Count > 0) root["accessors"] = this.accessors;
            if (this.views.Count > 0) root["bufferViews"] = this.views;
            if (this.bin.Length > 0) root["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = this.bin.Length });

            byte[] json = Encoding.UTF8.GetBytes(root.ToJsonString());
            int jsonLength = (json.Length + 3) & ~3;
            int binLength = (int)this.bin.Length;
            int total = 12 + 8 + jsonLength + (binLength > 0 ? 8 + binLength : 0);
            byte[] glb = new byte[total];
            Span<byte> s = glb;
            BinaryPrimitives.WriteUInt32LittleEndian(s, 0x46546C67);           // "glTF"
            BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(4), 2);
            BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(8), (uint)total);
            BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(12), (uint)jsonLength);
            BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(16), 0x4E4F534A);  // "JSON"
            json.CopyTo(glb, 20);
            for (int i = 20 + json.Length; i < 20 + jsonLength; i++)
            {
                glb[i] = 0x20;
            }

            if (binLength > 0)
            {
                int at = 20 + jsonLength;
                BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(at), (uint)binLength);
                BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(at + 4), 0x004E4942);   // "BIN\0"
                this.bin.ToArray().CopyTo(glb, at + 8);
            }

            return glb;
        }

        // Reads a .glb back and checks it (header and chunk lengths, every accessor inside its view and the buffer, index
        // values < vertex count, unit normals, min/max = the positions, node tree without loops or double parents, PNG
        // images with a valid signature). Returns a one-line report; problems start with "FAIL".
        public static string Check(byte[] glb)
        {
            List<string> fail = new List<string>();
            if (glb.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(glb) != 0x46546C67 || BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(8)) != glb.Length)
            {
                return "FAIL: not a glb or wrong length";
            }

            int jsonLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
            JsonObject root = JsonNode.Parse(Encoding.UTF8.GetString(glb, 20, jsonLength)).AsObject();
            int binAt = 20 + jsonLength + 8;
            int binLength = binAt <= glb.Length ? (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(binAt - 8)) : 0;
            if (binAt + binLength > glb.Length)
            {
                fail.Add("bin chunk past the end");
            }

            JsonArray views = root["bufferViews"] as JsonArray ?? new JsonArray();
            JsonArray accessors = root["accessors"] as JsonArray ?? new JsonArray();
            JsonArray nodes = root["nodes"] as JsonArray ?? new JsonArray();
            JsonArray meshList = root["meshes"] as JsonArray ?? new JsonArray();
            JsonArray imageList = root["images"] as JsonArray ?? new JsonArray();
            int Size(string type) => type == "SCALAR" ? 1 : type == "VEC2" ? 2 : type == "VEC3" ? 3 : 4;
            (int At, int Count, int Components) Data(int a)
            {
                JsonObject acc = accessors[a].AsObject();
                JsonObject view = views[(int)acc["bufferView"]].AsObject();
                int at = (int)view["byteOffset"], length = (int)view["byteLength"], count = (int)acc["count"], comp = Size((string)acc["type"]);
                if (at % 4 != 0 || at + length > binLength || count * comp * 4 > length)
                {
                    fail.Add($"accessor {a} outside its view");
                }

                return (binAt + at, count, comp);
            }

            int triangles = 0;
            for (int m = 0; m < meshList.Count; m++)
            {
                foreach (JsonNode p in meshList[m]["primitives"].AsArray())
                {
                    JsonObject attributes = p["attributes"].AsObject();
                    (int pAt, int vertices, _) = Data((int)attributes["POSITION"]);
                    JsonObject pa = accessors[(int)attributes["POSITION"]].AsObject();
                    float[] mn = { float.MaxValue, float.MaxValue, float.MaxValue }, mx = { float.MinValue, float.MinValue, float.MinValue };
                    for (int v = 0; v < vertices; v++)
                    {
                        for (int c = 0; c < 3; c++)
                        {
                            float f = BinaryPrimitives.ReadSingleLittleEndian(glb.AsSpan(pAt + (v * 12) + (c * 4)));
                            mn[c] = Math.Min(mn[c], f);
                            mx[c] = Math.Max(mx[c], f);
                        }
                    }

                    for (int c = 0; c < 3; c++)
                    {
                        if ((float)pa["min"][c] != mn[c] || (float)pa["max"][c] != mx[c])
                        {
                            fail.Add($"mesh {m} min/max");
                            break;
                        }
                    }

                    if (attributes["NORMAL"] != null)
                    {
                        (int nAt, int nCount, _) = Data((int)attributes["NORMAL"]);
                        for (int v = 0; v < nCount; v++)
                        {
                            Vector3 n = new Vector3(BinaryPrimitives.ReadSingleLittleEndian(glb.AsSpan(nAt + (v * 12))), BinaryPrimitives.ReadSingleLittleEndian(glb.AsSpan(nAt + (v * 12) + 4)), BinaryPrimitives.ReadSingleLittleEndian(glb.AsSpan(nAt + (v * 12) + 8)));
                            if (Math.Abs(n.Length() - 1) > 1e-3f)
                            {
                                fail.Add($"mesh {m} normal not unit");
                                break;
                            }
                        }

                        if (nCount != vertices) fail.Add($"mesh {m} normal count");
                    }

                    if (attributes["TEXCOORD_0"] != null && Data((int)attributes["TEXCOORD_0"]).Count != vertices) fail.Add($"mesh {m} uv count");
                    (int iAt, int indices, _) = Data((int)p["indices"]);
                    if (indices % 3 != 0) fail.Add($"mesh {m} index count");
                    for (int i = 0; i < indices; i++)
                    {
                        if (BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(iAt + (i * 4))) >= vertices)
                        {
                            fail.Add($"mesh {m} index out of range");
                            break;
                        }
                    }

                    triangles += indices / 3;
                }
            }

            int[] parentOf = new int[nodes.Count];
            for (int i = 0; i < parentOf.Length; i++) parentOf[i] = -1;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i]["children"] is JsonArray kids)
                {
                    foreach (JsonNode k in kids)
                    {
                        if (parentOf[(int)k] != -1) fail.Add($"node {(int)k} has two parents");
                        parentOf[(int)k] = i;
                    }
                }
            }

            int depth = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                int d = 0;
                for (int at = i; parentOf[at] >= 0; at = parentOf[at])
                {
                    if (++d > nodes.Count) { fail.Add("loop in the node tree"); break; }
                }

                depth = Math.Max(depth, d);
            }

            int roots = root["scenes"][0]["nodes"].AsArray().Count, orphans = 0;
            HashSet<int> sceneRoots = new HashSet<int>();
            foreach (JsonNode r in root["scenes"][0]["nodes"].AsArray()) sceneRoots.Add((int)r);
            for (int i = 0; i < nodes.Count; i++) if (parentOf[i] < 0 && !sceneRoots.Contains(i)) orphans++;
            if (orphans > 0) fail.Add($"{orphans} nodes outside the scene");
            for (int i = 0; i < imageList.Count; i++)
            {
                (int at, _, _) = (binAt + (int)views[(int)imageList[i]["bufferView"]]["byteOffset"], 0, 0);
                if (glb[at] != 0x89 || glb[at + 1] != (byte)'P' || glb[at + 2] != (byte)'N' || glb[at + 3] != (byte)'G') fail.Add($"image {i} not PNG");
            }

            int materialCount = (root["materials"] as JsonArray)?.Count ?? 0;
            string report = $"{nodes.Count} nodes (depth {depth}, {roots} root), {meshList.Count} meshes, {triangles} triangles, {materialCount} materials, {imageList.Count} images";
            return fail.Count == 0 ? "OK " + report : "FAIL " + string.Join("; ", fail) + " | " + report;
        }

        // Opaque RGB PNG (alpha dropped, as Forza Studio's glTF export: viewers would otherwise show junk alpha).
        public static byte[] Png(byte[] rgba, int w, int h)
        {
            MemoryStream file = new MemoryStream();
            file.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            byte[] ihdr = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(ihdr, w);
            BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), h);
            ihdr[8] = 8;
            ihdr[9] = 2;
            Chunk(file, "IHDR", ihdr);
            MemoryStream packed = new MemoryStream();
            using (ZLibStream z = new ZLibStream(packed, CompressionLevel.Optimal, true))
            {
                byte[] row = new byte[1 + (w * 3)];
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int from = ((y * w) + x) * 4;
                        row[1 + (x * 3)] = rgba[from];
                        row[2 + (x * 3)] = rgba[from + 1];
                        row[3 + (x * 3)] = rgba[from + 2];
                    }

                    z.Write(row, 0, row.Length);
                }
            }

            Chunk(file, "IDAT", packed.ToArray());
            Chunk(file, "IEND", Array.Empty<byte>());
            return file.ToArray();
        }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            byte[] head = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(head, data.Length);
            s.Write(head);
            byte[] body = new byte[4 + data.Length];
            Encoding.ASCII.GetBytes(type, 0, 4, body, 0);
            data.CopyTo(body, 4);
            s.Write(body);
            BinaryPrimitives.WriteUInt32BigEndian(head, Crc(body));
            s.Write(head);
        }

        private static uint[] crcTable;

        private static uint Crc(byte[] data)
        {
            if (crcTable == null)
            {
                uint[] table = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++)
                    {
                        c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    }

                    table[n] = c;
                }

                crcTable = table;
            }

            uint crc = 0xFFFFFFFFu;
            foreach (byte b in data)
            {
                crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }

            return crc ^ 0xFFFFFFFFu;
        }
    }
}
