#nullable disable
namespace GTFormats.Fbx
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Numerics;

    // A material for the FBX: a colour and, when there is one, the diffuse texture file (TGA).
    public sealed class FbxMaterial
    {
        public string Name;
        public Vector3 Color = new Vector3(0.8f);
        public string TextureFile;        // full path of the texture file, or null
        public string TextureRelative;    // the same relative to the FBX file
    }

    // Writes an FBX scene (binary 7.4, the layout 3ds Max and Blender read): Null models for the hierarchy and Mesh
    // models with one material each, normals and UV per corner. Units: meters (UnitScaleFactor 100), Y up, as the
    // game data; UV are "v up" (OpenGL), the importers expect that.
    public sealed class FbxBuilder
    {
        private readonly FbxNode objects = new FbxNode("Objects");
        private readonly FbxNode connections = new FbxNode("Connections");
        private readonly Dictionary<string, long> materials = new Dictionary<string, long>();
        private readonly Dictionary<string, long> textures = new Dictionary<string, long>();
        private long nextId = 1000;
        private int models;
        private int geometries;
        private readonly string creator;

        public FbxBuilder(string creator)
        {
            this.creator = creator;
        }

        public int MeshCount { get { return this.geometries; } }

        private static string Named(string name, string cls)
        {
            return name + "\0\x01" + cls;
        }

        private void Connect(long child, long parent)
        {
            this.connections.Add("C", "OO", child, parent);
        }

        private static void Property(FbxNode properties, string name, string type, string label, params object[] values)
        {
            FbxNode p = properties.Add("P", name, type, label, "");
            p.Props.AddRange(values);
        }

        private static void Animatable(FbxNode properties, string name, params object[] values)
        {
            FbxNode p = properties.Add("P", name, name, "", "A");
            p.Props.AddRange(values);
        }

        private static void ModelProperties(FbxNode properties, bool mesh)
        {
            Property(properties, "InheritType", "enum", "", 1);
            Property(properties, "DefaultAttributeIndex", "int", "Integer", mesh ? 0 : -1);
            Animatable(properties, "Lcl Translation", 0.0, 0.0, 0.0);
            Animatable(properties, "Lcl Rotation", 0.0, 0.0, 0.0);
            Animatable(properties, "Lcl Scaling", 1.0, 1.0, 1.0);
        }

        // parent 0 = the scene root.
        public long AddNull(string name, long parent)
        {
            long id = this.nextId++;
            FbxNode model = this.objects.Add("Model", id, Named(name, "Model"), "Null");
            model.Add("Version", 232);
            ModelProperties(model.Add("Properties70"), false);
            model.Add("Shading", true);
            model.Add("Culling", "CullingOff");
            this.models++;
            this.Connect(id, parent);
            return id;
        }

        // A mesh from indexed triangles (triangle list). Positions are shared by exact value, normals and UVs go per corner.
        public long AddMesh(string name, long parent, FbxMaterial material, Vector3[] position, Vector3[] normal, Vector2[] uv, int[] triangles)
        {
            Dictionary<Vector3, int> vertexOf = new Dictionary<Vector3, int>();
            List<double> positions = new List<double>();
            int corners = triangles.Length / 3 * 3;
            int[] polygons = new int[corners];
            double[] normals = new double[corners * 3];
            double[] uvs = new double[corners * 2];
            int[] uvIndex = new int[corners];
            for (int k = 0; k < corners; k++)
            {
                int source = triangles[k];
                Vector3 p = position[source];
                int index;
                if (!vertexOf.TryGetValue(p, out index))
                {
                    index = vertexOf.Count;
                    vertexOf[p] = index;
                    positions.Add(p.X);
                    positions.Add(p.Y);
                    positions.Add(p.Z);
                }
                polygons[k] = (k % 3 == 2) ? ~index : index;
                if (normal != null)
                {
                    Vector3 n = normal[source];
                    normals[k * 3] = n.X;
                    normals[(k * 3) + 1] = n.Y;
                    normals[(k * 3) + 2] = n.Z;
                }
                if (uv != null)
                {
                    uvs[k * 2] = uv[source].X;
                    uvs[(k * 2) + 1] = uv[source].Y;
                }
                uvIndex[k] = k;
            }

            long geometryId = this.nextId++;
            FbxNode geometry = this.objects.Add("Geometry", geometryId, Named(name, "Geometry"), "Mesh");
            geometry.Add("Properties70");
            geometry.Add("GeometryVersion", 124);
            geometry.Add("Vertices", positions.ToArray());
            geometry.Add("PolygonVertexIndex", polygons);
            FbxNode layer = new FbxNode("Layer");
            layer.Props.Add(0);
            layer.Add("Version", 100);
            if (normal != null)
            {
                FbxNode element = geometry.Add("LayerElementNormal", 0);
                element.Add("Version", 101);
                element.Add("Name", "");
                element.Add("MappingInformationType", "ByPolygonVertex");
                element.Add("ReferenceInformationType", "Direct");
                element.Add("Normals", normals);
                LayerEntry(layer, "LayerElementNormal");
            }
            if (uv != null)
            {
                FbxNode element = geometry.Add("LayerElementUV", 0);
                element.Add("Version", 101);
                element.Add("Name", "UVMap");
                element.Add("MappingInformationType", "ByPolygonVertex");
                element.Add("ReferenceInformationType", "IndexToDirect");
                element.Add("UV", uvs);
                element.Add("UVIndex", uvIndex);
                LayerEntry(layer, "LayerElementUV");
            }
            FbxNode slot = geometry.Add("LayerElementMaterial", 0);
            slot.Add("Version", 101);
            slot.Add("Name", "");
            slot.Add("MappingInformationType", "AllSame");
            slot.Add("ReferenceInformationType", "IndexToDirect");
            slot.Add("Materials", new int[] { 0 });
            LayerEntry(layer, "LayerElementMaterial");
            geometry.Add(layer);
            this.geometries++;

            long modelId = this.nextId++;
            FbxNode model = this.objects.Add("Model", modelId, Named(name, "Model"), "Mesh");
            model.Add("Version", 232);
            ModelProperties(model.Add("Properties70"), true);
            model.Add("Shading", true);
            model.Add("Culling", "CullingOff");
            this.models++;
            this.Connect(modelId, parent);
            this.Connect(geometryId, modelId);
            this.Connect(this.Material(material), modelId);
            return modelId;
        }

        private static void LayerEntry(FbxNode layer, string type)
        {
            FbxNode entry = layer.Add("LayerElement");
            entry.Add("Type", type);
            entry.Add("TypedIndex", 0);
        }

        private long Material(FbxMaterial source)
        {
            long id;
            if (this.materials.TryGetValue(source.Name, out id))
            {
                return id;
            }
            id = this.nextId++;
            this.materials[source.Name] = id;
            FbxNode material = this.objects.Add("Material", id, Named(source.Name, "Material"), "");
            material.Add("Version", 102);
            material.Add("ShadingModel", "phong");
            material.Add("MultiLayer", 0);
            FbxNode properties = material.Add("Properties70");
            Vector3 color = (source.TextureFile != null) ? Vector3.One : source.Color;
            Property(properties, "DiffuseColor", "Color", "", (double) color.X, (double) color.Y, (double) color.Z);
            if (source.TextureFile != null)
            {
                this.connections.Add("C", "OP", this.Texture(source), id, "DiffuseColor");
            }
            return id;
        }

        // One Texture record per file, shared by the materials that use it.
        private long Texture(FbxMaterial source)
        {
            long id;
            if (this.textures.TryGetValue(source.TextureFile, out id))
            {
                return id;
            }
            id = this.nextId++;
            this.textures[source.TextureFile] = id;
            string name = System.IO.Path.GetFileNameWithoutExtension(source.TextureFile);
            FbxNode texture = this.objects.Add("Texture", id, Named(name, "Texture"), "");
            texture.Add("Type", "TextureVideoClip");
            texture.Add("Version", 202);
            texture.Add("TextureName", Named(name, "Texture"));
            FbxNode properties = texture.Add("Properties70");
            Property(properties, "UseMaterial", "bool", "", 1);
            texture.Add("Media", Named(name, "Video"));
            texture.Add("FileName", source.TextureFile);
            texture.Add("RelativeFilename", source.TextureRelative ?? source.TextureFile);
            texture.Add("ModelUVTranslation", 0.0, 0.0);
            texture.Add("ModelUVScaling", 1.0, 1.0);
            texture.Add("Texture_Alpha_Source", "None");
            texture.Add("Cropping", 0, 0, 0, 0);
            return id;
        }

        private const long OneSecond = 46186158000L;

        public byte[] ToBytes()
        {
            FbxNode top = new FbxNode("");
            // Header as the FBX SDK writes it: Max's importer is strict. Footer id and FileId derive from the
            // creation time, which is fixed (FbxFile writes the footer id of this time).
            DateTime now = new DateTime(1970, 1, 1, 10, 0, 0, 0);
            FbxNode header = top.Add("FBXHeaderExtension");
            header.Add("FBXHeaderVersion", 1003);
            header.Add("FBXVersion", 7400);
            FbxNode stamp = header.Add("CreationTimeStamp");
            stamp.Add("Version", 1000);
            stamp.Add("Year", now.Year);
            stamp.Add("Month", now.Month);
            stamp.Add("Day", now.Day);
            stamp.Add("Hour", now.Hour);
            stamp.Add("Minute", now.Minute);
            stamp.Add("Second", now.Second);
            stamp.Add("Millisecond", now.Millisecond);
            header.Add("Creator", this.creator);
            FbxNode info = header.Add("SceneInfo", "GlobalInfo\0\x01SceneInfo", "UserData");
            info.Add("Type", "UserData");
            info.Add("Version", 100);
            FbxNode meta = info.Add("MetaData");
            meta.Add("Version", 100);
            meta.Add("Title", "");
            meta.Add("Subject", "");
            meta.Add("Author", "");
            meta.Add("Keywords", "");
            meta.Add("Revision", "");
            meta.Add("Comment", "");
            FbxNode infoProperties = info.Add("Properties70");
            Property(infoProperties, "DocumentUrl", "KString", "Url", "");
            Property(infoProperties, "SrcDocumentUrl", "KString", "Url", "");
            top.Add("FileId", new byte[] { 0x28, 0xb3, 0x2a, 0xeb, 0xb6, 0x24, 0xcc, 0xc2, 0xbf, 0xc8, 0xb0, 0x2a, 0xa9, 0x2b, 0xfc, 0xf1 });
            top.Add("CreationTime", now.ToString("yyyy-MM-dd HH:mm:ss:fff", CultureInfo.InvariantCulture));
            top.Add("Creator", "FBX SDK/FBX Plugins version 2020.3.9 build=0");

            FbxNode settings = top.Add("GlobalSettings");
            settings.Add("Version", 1000);
            FbxNode properties = settings.Add("Properties70");
            Property(properties, "UpAxis", "int", "Integer", 1);
            Property(properties, "UpAxisSign", "int", "Integer", 1);
            Property(properties, "FrontAxis", "int", "Integer", 2);
            Property(properties, "FrontAxisSign", "int", "Integer", 1);
            Property(properties, "CoordAxis", "int", "Integer", 0);
            Property(properties, "CoordAxisSign", "int", "Integer", 1);
            Property(properties, "OriginalUpAxis", "int", "Integer", 1);
            Property(properties, "OriginalUpAxisSign", "int", "Integer", 1);
            Property(properties, "UnitScaleFactor", "double", "Number", 100.0);
            Property(properties, "OriginalUnitScaleFactor", "double", "Number", 100.0);
            Property(properties, "AmbientColor", "ColorRGB", "Color", 0.0, 0.0, 0.0);
            Property(properties, "DefaultCamera", "KString", "", "Producer Perspective");
            Property(properties, "TimeMode", "enum", "", 6);
            Property(properties, "TimeProtocol", "enum", "", 2);
            Property(properties, "SnapOnFrameMode", "enum", "", 0);
            Property(properties, "TimeSpanStart", "KTime", "Time", 0L);
            Property(properties, "TimeSpanStop", "KTime", "Time", OneSecond);
            Property(properties, "CustomFrameRate", "double", "Number", -1.0);
            Property(properties, "TimeMarker", "Compound", "");
            Property(properties, "CurrentTimeMarker", "int", "Integer", -1);

            FbxNode documents = top.Add("Documents");
            documents.Add("Count", 1);
            FbxNode document = documents.Add("Document", 1000000000L, "", "Scene");
            FbxNode documentProperties = document.Add("Properties70");
            Property(documentProperties, "SourceObject", "object", "");
            Property(documentProperties, "ActiveAnimStackName", "KString", "", "");
            document.Add("RootNode", 0L);
            top.Add("References");

            FbxNode definitions = top.Add("Definitions");
            definitions.Add("Version", 100);
            definitions.Add("Count", this.models + this.geometries + this.materials.Count + this.textures.Count + 3);
            AddType(definitions, "GlobalSettings", 1);
            AddType(definitions, "AnimationStack", 1);
            AddType(definitions, "AnimationLayer", 1);
            FbxNode modelType = AddType(definitions, "Model", this.models);
            FbxNode template = modelType.Add("PropertyTemplate", "FbxNode").Add("Properties70");
            Property(template, "InheritType", "enum", "", 0);
            Property(template, "DefaultAttributeIndex", "int", "Integer", -1);
            Property(template, "Show", "bool", "", 1);
            Animatable(template, "Lcl Translation", 0.0, 0.0, 0.0);
            Animatable(template, "Lcl Rotation", 0.0, 0.0, 0.0);
            Animatable(template, "Lcl Scaling", 1.0, 1.0, 1.0);
            Animatable(template, "Visibility", 1.0);
            Property(template, "Visibility Inheritance", "Visibility Inheritance", "", 1);
            FbxNode geometryType = AddType(definitions, "Geometry", this.geometries);
            FbxNode meshTemplate = geometryType.Add("PropertyTemplate", "FbxMesh").Add("Properties70");
            Property(meshTemplate, "Color", "ColorRGB", "Color", 0.8, 0.8, 0.8);
            Property(meshTemplate, "Primary Visibility", "bool", "", 1);
            Property(meshTemplate, "Casts Shadows", "bool", "", 1);
            Property(meshTemplate, "Receive Shadows", "bool", "", 1);
            AddType(definitions, "Material", this.materials.Count);
            if (this.textures.Count > 0)
            {
                AddType(definitions, "Texture", this.textures.Count);
            }

            long stack = this.nextId++;
            long layer = this.nextId++;
            FbxNode animationStack = this.objects.Add("AnimationStack", stack, Named("Take 001", "AnimStack"), "");
            FbxNode stackProperties = animationStack.Add("Properties70");
            Property(stackProperties, "LocalStop", "KTime", "Time", OneSecond);
            Property(stackProperties, "ReferenceStop", "KTime", "Time", OneSecond);
            this.objects.Add("AnimationLayer", layer, Named("BaseLayer", "AnimLayer"), "");
            this.Connect(layer, stack);

            top.Add(this.objects);
            top.Add(this.connections);
            FbxNode takes = top.Add("Takes");
            takes.Add("Current", "");
            FbxNode take = takes.Add("Take", "Take 001");
            take.Add("FileName", "Take_001.tak");
            take.Add("LocalTime", 0L, OneSecond);
            take.Add("ReferenceTime", 0L, OneSecond);
            return FbxFile.Write(top);
        }

        private static FbxNode AddType(FbxNode definitions, string name, int count)
        {
            FbxNode type = definitions.Add("ObjectType", name);
            type.Add("Count", count);
            return type;
        }
    }
}
