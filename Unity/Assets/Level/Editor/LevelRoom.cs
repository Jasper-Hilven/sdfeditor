using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SdfLevel.Editor
{
    /// <summary>
    /// The closed room of a level (level.json "room", from the level file's <c>room</c> line): the chessboard cube, or a room with named
    /// wall and ceiling materials (ticket 27.1). The floor is the ground, built from its chunks as ever; <see cref="Add"/> builds the
    /// other five faces under Level/Room: a flat quad each (the four walls and the ceiling, facing into the room, each on its own
    /// material: on SdfRoomFace.shader for the chessboard, which draws the squares from the world position with ruled-straight edges, or
    /// on SdfRoomPlanks.shader for a room of materials, whose pattern <see cref="Looks"/> picks by the material's name) and a box
    /// collider outside every one of the six faces, so that nothing can leave the room. A room may also have a strip of emissive light
    /// along the top of a wall (the room block's <c>glow</c>, Level/Room/Glow).
    /// <para>
    /// For a chessboard room, which squares are dark is the pipeline's <c>RoomChecker</c>: the baked room block lists, per face, its origin, unit axes, inward
    /// normal and dark rows; the shader takes the origin, axes and the colour of the square (0, 0) and alternates from there, which is the
    /// same as the rows for a chessboard. <see cref="CheckEdges"/> checks that against the rows, and the colour across the cube's edges:
    /// it alternates across the floor's four edges, the ceiling's four edges and everywhere inside a face, and matches along the four
    /// vertical wall-to-wall edges (three squares meet at each corner and two colours cannot colour a triangle, so some repeat is forced;
    /// see RoomChecker). A room of materials has no squares, so there is nothing to check.
    /// </para>
    /// Positions in level.json are the pipeline's; x is mirrored here, as in the rest of the builder.
    /// </summary>
    public static class LevelRoom
    {
        public const string RoomName = "Room";
        public const string ShaderPath = "Assets/Level/Shaders/SdfRoomFace.shader";
        public const string PlanksShaderPath = "Assets/Level/Shaders/SdfRoomPlanks.shader";
        public const string TilesShaderPath = "Assets/Level/Shaders/SdfRoomTiles.shader";
        public const string GlowName = "Glow";

        /// <summary>The colliders are this thick, outside the faces; the player cannot leave the room however fast it moves.</summary>
        private const float ColliderThickness = 4;

        /// <summary>How far the light leans toward a face's own normal in the shader (see SdfRoomFace.shader).</summary>
        private const float LightLean = 1.0f;

        /// <summary>One face of the room in Unity's coordinates.</summary>
        public sealed class Face
        {
            public string Name;
            public Vector3 Origin, U, V, Normal;
            public int SquaresU, SquaresV;
            public float Square;
            public string[] Dark;
            /// <summary>The face's lengths in metres along u and v: baked for a room of materials, else the squares times the square size.</summary>
            public Vector2 Extent;

            public bool DarkAtOrigin => Dark[0][0] == '1';
            public bool IsFloor => Name == "floor";
            public Vector2 Size => Extent;

            /// <summary>The square of a point (on the face's plane): floor of its coordinates along u and v, in squares.</summary>
            public Vector2Int SquareOf(Vector3 point)
            {
                var offset = point - Origin;
                return new Vector2Int(Mathf.FloorToInt(Vector3.Dot(offset, U) / Square), Mathf.FloorToInt(Vector3.Dot(offset, V) / Square));
            }
        }

        /// <summary>Whether the level has a closed room; a backdrop (ticket 28.2, <see cref="LevelBackdrop"/>) shares the "room" block but is not one.</summary>
        public static bool HasRoom(Newtonsoft.Json.Linq.JObject data) => data["room"] is JObject && !LevelBackdrop.IsBackdrop(data);

        /// <summary>The faces of the level's room (floor, ceiling, wall-z-, wall-z+, wall-x-, wall-x+), x mirrored; none without a room.</summary>
        public static Face[] Faces(JObject data)
        {
            if (!(data["room"] is JObject room))
                return Array.Empty<Face>();
            // A room of materials has no square size, squares or dark rows, but each face's extent in metres
            var square = room["square"] != null ? room.Value<float>("square") : 0;
            Vector3 Mirrored(JToken token) => new Vector3(-token[0].Value<float>(), token[1].Value<float>(), token[2].Value<float>());
            Face Read(JToken f)
            {
                var squares = f["squares"] as JArray;
                return new Face
                {
                    Name = f.Value<string>("face"),
                    Origin = Mirrored(f["origin"]),
                    U = Mirrored(f["u"]),
                    V = Mirrored(f["v"]),
                    Normal = Mirrored(f["normal"]),
                    SquaresU = squares != null ? squares[0].Value<int>() : 0,
                    SquaresV = squares != null ? squares[1].Value<int>() : 0,
                    Square = square,
                    Dark = f["dark"]?.Values<string>().ToArray(),
                    Extent = squares != null
                        ? new Vector2(squares[0].Value<int>(), squares[1].Value<int>()) * square
                        : new Vector2(f["extent"][0].Value<float>(), f["extent"][1].Value<float>()),
                };
            }
            return ((JArray)room["faces"]).Select(Read).ToArray();
        }

        /// <summary>Whether the room is the chessboard's checker (no wall and ceiling materials named).</summary>
        public static bool IsChecker(JObject data) => !(data["room"]["walls"] is JObject);

        /// <summary>The room's size in metres (x, height, z).</summary>
        public static Vector3 Size(JObject data) =>
            new Vector3(data["room"]["size"][0].Value<float>(), data["room"]["size"][1].Value<float>(), data["room"]["size"][2].Value<float>());

        /// <summary>
        /// Level/Room: the five quads of walls and ceiling and the six colliders. Returns the number of faces drawn. Call before the
        /// level is marked static.
        /// </summary>
        public static int Add(Transform levelRoot, string name, string meshFolder, JObject data)
        {
            var faces = Faces(data);
            if (faces.Length == 0)
                return 0;
            var root = new GameObject(RoomName);
            root.transform.SetParent(levelRoot, false);
            var size = Size(data);

            var checker = IsChecker(data);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(checker ? ShaderPath : PlanksShaderPath)
                         ?? throw new InvalidOperationException($"The shader {(checker ? ShaderPath : PlanksShaderPath)} is missing");
            SdfGlb.Material light = null, dark = null;
            if (checker)
            {
                var materials = (JObject)data["materials"];
                light = LevelBuilder.MaterialOf(materials, "light") ?? throw new InvalidDataException("level.json has no light material; bake the level again");
                dark = LevelBuilder.MaterialOf(materials, "dark") ?? throw new InvalidDataException("level.json has no dark material; bake the level again");
            }

            MushroomBuilder.EnsureFolder(meshFolder);
            var drawn = 0;
            foreach (var face in faces)
            {
                // The floor is the ground's chunks; only its collider is made here
                if (!face.IsFloor)
                {
                    var faceObject = new GameObject(face.Name);
                    faceObject.transform.SetParent(root.transform, false);
                    faceObject.AddComponent<MeshFilter>().sharedMesh = FaceMesh(face, $"{meshFolder}/room_{face.Name.Replace("+", "pos").Replace("-", "neg")}.asset");
                    var renderer = faceObject.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = checker
                        ? FaceMaterial(name, face, shader, light, dark)
                        : PlanksMaterial(name, face, shader, data);
                    // Walls and ceiling cast no shadow (the ceiling would shade the whole room from the lamp) and take none
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    drawn++;
                }
                AddCollider(root.transform, face, size);
            }
            if (data["room"]["glow"] is JObject glow)
                AddGlow(root.transform, name, meshFolder, faces, glow);
            AssetDatabase.SaveAssets();
            return drawn;
        }

        /// <summary>One look of a room's wall or ceiling on SdfRoomPlanks.shader: what the shader draws on it (see the shader's top).</summary>
        public sealed class Look
        {
            /// <summary>Width of the boards, which run along the face's v axis (up a wall).</summary>
            public float BoardWidth = 6;
            /// <summary>How far each board's tone steps from the next (fraction either way).</summary>
            public float ToneStep = 0.07f;
            /// <summary>The alphabet frieze's bottom and top (metres up), its cells' width and the letters' height; none if top is not above bottom.</summary>
            public Vector4 Frieze;
            /// <summary>The frieze's four paint colours (sRGB), taken in turn.</summary>
            public Color[] FriezePaint = new Color[4];
            /// <summary>Dovetail tails at the face's ends: length from the corner, row height; none if the length is 0.</summary>
            public Vector2 Dovetail;
            /// <summary>Where the cross battens lie, as fractions of the face's length along v (below 0 for none), and their width.</summary>
            public Vector3 Battens = new Vector3(-1, -1, 3);
            /// <summary>
            /// Draws the face on SdfRoomTiles.shader instead (ticket 29.1): <see cref="Tile"/> metres square tiles in a checker of the
            /// material's colour and <see cref="TileAlt"/>, grout between them; a tile size of 0 is one plain colour. The boards, frieze,
            /// dovetail and batten settings above are not used.
            /// </summary>
            public bool Tiled;
            public float Tile = 5;
            /// <summary>The second tile's colour (sRGB).</summary>
            public Color TileAlt = Color.white;
            /// <summary>How far each tile's tone steps from the next (fraction either way).</summary>
            public float TileToneStep = 0.02f;
            /// <summary>The colours' scale: the toon bands darken a white face to grey, so a white room takes a little over 1.</summary>
            public float TileBrightness = 1;
            /// <summary>Set for a look drawn by SdfRoomCase.shader (the computer case, ticket 30.2, see LevelRoomCase) instead of the planks.</summary>
            public LevelRoomCase.CaseLook Case;
        }

        /// <summary>
        /// The looks of rooms, by the name of the wall or ceiling material in the level file (<c>walls=toybox-wall</c>): the pipeline's
        /// materials/room/&lt;name&gt;.sdfmat gives the paint (colours, seams), this the pattern drawn on it. A level that wants a new
        /// room look (the kitchen, the bathroom, the computer) adds its entry here.
        /// </summary>
        public static readonly Dictionary<string, Look> Looks = new Dictionary<string, Look>
        {
            // The toy chest's walls: 6 m boards (10 cm real at 60x), the stencilled alphabet from 8 to 12 m up in pastel yellow, mint green,
            // lilac and peach (nothing red or blue), dovetails at the corners
            ["toybox-wall"] = new Look
            {
                Frieze = new Vector4(8, 12, 3, 2.4f),
                FriezePaint = new[] { Srgb(0xF2D45C), Srgb(0x8CCB9A), Srgb(0xB59ADB), Srgb(0xF5B48A) },
                Dovetail = new Vector2(2.5f, 4),
            },
            // The lid's underside: the same boards with two cross battens a quarter and three quarters across
            ["toybox-lid"] = new Look { Battens = new Vector3(0.25f, 0.75f, 3) },
            // The bathroom's walls (ticket 29.1): glazed tiles 5 m square (17 cm real at 30x), white (the material's colour) and pale mint
            // in a checker with a grout line between; and its ceiling, plain white paint
            ["bathroom-wall"] = new Look { Tiled = true, Tile = 5, TileAlt = Srgb(0xCFE6D8), TileBrightness = 1.25f },
            ["bathroom-ceiling"] = new Look { Tiled = true, Tile = 0, TileBrightness = 1.25f },
            // The computer case (30.2): gunmetal panels, honeycomb vents on the upper half of the west and east walls; smoked glass lid
            // with a diagonal reflection streak and a warm patch off-centre
            ["case-wall"] = new Look
            {
                Case = new LevelRoomCase.CaseLook
                {
                    Panels = new Vector4(16, 16, 0.06f, 0.08f),
                    Vent = new Vector4(36, 58, 0.2f, 0.8f),
                    VentFaces = new[] { "wall-x-", "wall-x+" },
                },
            },
            ["case-glass"] = new Look
            {
                Case = new LevelRoomCase.CaseLook
                {
                    Lid = true,
                    Streak = new Vector2(95, 10),
                    Patch = new Vector4(0.68f, 0.3f, 56, 0.16f),
                    PatchColor = Srgb(0xFFB06A),
                },
            },
        };

        private static Color Srgb(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        /// <summary>Which rows have a dovetail tail at the start and at the far end of a wall, so that two walls meeting at a corner take opposite ones.</summary>
        private static Vector2 DovetailParity(string face)
        {
            switch (face)
            {
                case "wall-z-":
                case "wall-x+":
                    return new Vector2(0, 1);
                default:
                    return new Vector2(1, 0);
            }
        }

        /// <summary>The face's material on SdfRoomPlanks.shader: the ground's toon settings, the face's place and size, its look and its paint.</summary>
        private static Material PlanksMaterial(string name, Face face, Shader shader, JObject data)
        {
            var room = (JObject)data["room"];
            var which = face.Name == "ceiling" ? "ceiling" : "walls";
            var paint = room[which] as JObject ?? throw new InvalidDataException($"level.json's room has no {which} material; bake the level again");
            var materialName = paint.Value<string>("name");
            if (!Looks.TryGetValue(materialName, out var look))
                throw new InvalidDataException($"The room material '{materialName}' has no look in LevelRoom.Looks (it has {string.Join(", ", Looks.Keys)}); add one");
            if (look.Case != null)
                return LevelRoomCase.Material(name, face, materialName, look.Case, paint, LightLean);
            var parameters = new SdfGlb.Material { Name = materialName, Parameters = (JObject)paint["parameters"] };
            if (look.Tiled)
                shader = AssetDatabase.LoadAssetAtPath<Shader>(TilesShaderPath) ?? throw new InvalidOperationException($"The shader {TilesShaderPath} is missing");

            var path = $"{LevelBuilder.MaterialFolder}/{name} Room {face.Name.Replace("+", "pos").Replace("-", "neg")}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                MushroomBuilder.EnsureFolder(LevelBuilder.MaterialFolder);
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            MushroomStyle.Load().ApplyToSurface(material);
            material.SetVector("_ToonRim", Vector4.zero);
            material.SetVector("_RoomOrigin", face.Origin);
            material.SetVector("_RoomU", face.U);
            material.SetVector("_RoomV", face.V);
            // Text reads left to right seen from inside where u x v points away from the viewer, against the face's normal
            var reading = Vector3.Dot(Vector3.Cross(face.U, face.V), face.Normal) < 0 ? 1 : -1;
            material.SetVector("_RoomExtent", new Vector4(face.Size.x, face.Size.y, reading, LightLean));
            if (look.Tiled)
            {
                var surface = LevelBuilder.Surface(parameters);
                material.SetVector("_TileBase", LevelBuilder.Color(parameters, "baseColor", 0));
                material.SetVector("_TileAlt", look.TileAlt.linear);
                material.SetVector("_TileGrout", LevelBuilder.Color(parameters, "edgeColor", 0));
                material.SetVector("_TileSurface", new Vector4(surface.x, surface.y, 0, 0));
                material.SetVector("_Tile", new Vector4(look.Tile, look.TileToneStep, parameters.Number("edgeThickness") / 2, look.TileBrightness));
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                return material;
            }
            material.SetVector("_WoodBase", LevelBuilder.Color(parameters, "baseColor", 0));
            material.SetVector("_WoodVariation", LevelBuilder.Color(parameters, "variationColor", parameters.Number("variationScale")));
            material.SetVector("_WoodSurface", LevelBuilder.Surface(parameters));
            material.SetVector("_WoodSpeckle", LevelBuilder.Color(parameters, "speckleColor", parameters.Number("speckleScale")));
            material.SetVector("_WoodPattern", new Vector4(parameters.Number("speckleDensity"), parameters.Number("speckleSize"), 0, 0));
            material.SetVector("_WoodEdge", LevelBuilder.Color(parameters, "edgeColor", 0));
            material.SetVector("_Planks", new Vector4(look.BoardWidth, look.ToneStep, parameters.Number("edgeThickness") / 2, 0));
            material.SetVector("_Battens", new Vector4(look.Battens.x, look.Battens.y, look.Battens.z, 0));
            material.SetVector("_Frieze", look.Frieze);
            for (var i = 0; i < 4; i++)
                material.SetVector($"_FriezeColor{i}", look.FriezePaint[i].linear);
            var parity = DovetailParity(face.Name);
            material.SetVector("_Dovetail", new Vector4(look.Dovetail.x, look.Dovetail.y, parity.x, parity.y));
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// The glow strip: a quad over the top <c>height</c> metres of the named wall, a hand's width inside it, in an unlit emissive colour
        /// brighter than the bloom's threshold. Casts no shadow, takes none, has no collider. With <c>offset</c> its lower edge is that many
        /// metres up; for the wall "all" there is a strip on each of the four walls (the computer case's LED strip, ticket 30.2).
        /// </summary>
        private static void AddGlow(Transform root, string name, string meshFolder, Face[] faces, JObject glow)
        {
            var wallName = glow.Value<string>("wall");
            if (wallName == "all")
            {
                foreach (var side in faces.Where(f => f.Name.StartsWith("wall-")))
                    AddGlowStrip(root, name, meshFolder, side, glow, "_" + side.Name.Replace("+", "pos").Replace("-", "neg"));
                return;
            }
            var wall = faces.FirstOrDefault(f => f.Name == wallName) ?? throw new InvalidDataException($"The room's glow is on '{wallName}', which is not a face");
            AddGlowStrip(root, name, meshFolder, wall, glow, "");
        }

        private static void AddGlowStrip(Transform root, string name, string meshFolder, Face wall, JObject glow, string suffix)
        {
            var height = glow.Value<float>("height");
            var bottom = glow["offset"] != null ? glow.Value<float>("offset") : wall.Size.y - height;
            var c = glow["color"];
            var color = new Color(c[0].Value<float>(), c[1].Value<float>(), c[2].Value<float>());

            var strip = new GameObject(GlowName + suffix);
            strip.transform.SetParent(root, false);
            var path = $"{meshFolder}/room_glow{suffix}.asset";
            var (u, v) = (wall.U * wall.Size.x, wall.V * height);
            var origin = wall.Origin + wall.V * bottom + wall.Normal * GlowOffset;
            var mesh = new Mesh { name = "room_glow" + suffix };
            mesh.vertices = new[] { origin, origin + u, origin + u + v, origin + v };
            mesh.normals = Enumerable.Repeat(wall.Normal, 4).ToArray();
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = Vector3.Dot(Vector3.Cross(wall.U, wall.V), wall.Normal) > 0 ? new[] { 0, 1, 2, 0, 2, 3 } : new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null)
                AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            strip.AddComponent<MeshFilter>().sharedMesh = mesh;

            var materialPath = $"{LevelBuilder.MaterialFolder}/{name} Room glow.mat";
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? throw new InvalidOperationException("URP's Unlit shader is missing");
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                MushroomBuilder.EnsureFolder(LevelBuilder.MaterialFolder);
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(materialPath) };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }
            // Over 1 so that the faint bloom of the level's post-processing catches it
            material.SetColor("_BaseColor", color * GlowBrightness);
            material.SetColor("_Color", color * GlowBrightness);
            EditorUtility.SetDirty(material);

            var renderer = strip.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>The glow strip floats this far in front of its wall, so that it never z-fights with it.</summary>
        private const float GlowOffset = 0.08f;

        /// <summary>The glow colour is multiplied by this, so it is brighter than the lit walls.</summary>
        private const float GlowBrightness = 1.25f;

        /// <summary>A flat quad over the face, facing into the room.</summary>
        private static Mesh FaceMesh(Face face, string path)
        {
            var (u, v) = (face.U * face.Size.x, face.V * face.Size.y);
            var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
            mesh.vertices = new[] { face.Origin, face.Origin + u, face.Origin + u + v, face.Origin + v };
            mesh.normals = Enumerable.Repeat(face.Normal, 4).ToArray();
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            // Unity's front faces wind clockwise: the triangle (0, 1, 2) faces along u x v
            mesh.triangles = Vector3.Dot(Vector3.Cross(face.U, face.V), face.Normal) > 0 ? new[] { 0, 1, 2, 0, 2, 3 } : new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null)
                AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>The face's material on SdfRoomFace.shader: the ground's toon settings, the face's place in the room, the two squares' materials.</summary>
        private static Material FaceMaterial(string name, Face face, Shader shader, SdfGlb.Material light, SdfGlb.Material dark)
        {
            var path = $"{LevelBuilder.MaterialFolder}/{name} Room {face.Name.Replace("+", "pos").Replace("-", "neg")}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                MushroomBuilder.EnsureFolder(LevelBuilder.MaterialFolder);
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            MushroomStyle.Load().ApplyToSurface(material);
            material.SetVector("_ToonRim", Vector4.zero);
            material.SetVector("_RoomOrigin", face.Origin);
            material.SetVector("_RoomU", face.U);
            material.SetVector("_RoomV", face.V);
            material.SetVector("_RoomFace", new Vector4(face.Square, face.DarkAtOrigin ? 0 : 1, LightLean, 0));
            SetSquare(material, "Light", light);
            SetSquare(material, "Dark", dark);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetSquare(Material material, string role, SdfGlb.Material squares)
        {
            material.SetVector($"_{role}Base", LevelBuilder.Color(squares, "baseColor", 0));
            material.SetVector($"_{role}Variation", LevelBuilder.Color(squares, "variationColor", squares.Number("variationScale")));
            material.SetVector($"_{role}Surface", LevelBuilder.Surface(squares));
            material.SetVector($"_{role}Speckle", LevelBuilder.Color(squares, "speckleColor", squares.Number("speckleScale")));
            material.SetVector($"_{role}Pattern", new Vector4(squares.Number("speckleDensity"), squares.Number("speckleSize"), 0, 0));
        }

        /// <summary>A box just outside the face: as thick as <see cref="ColliderThickness"/>, covering the face and the thickness beyond its edges.</summary>
        private static void AddCollider(Transform root, Face face, Vector3 size)
        {
            var collider = new GameObject($"Collider {face.Name}");
            collider.transform.SetParent(root, false);
            var centre = new Vector3(0, size.y / 2, 0);
            var box = size + Vector3.one * 2 * ColliderThickness;
            // The normal points into the room, along one axis: along it the box is as thick as asked, and sits behind the face
            for (var axis = 0; axis < 3; axis++)
            {
                if (Mathf.Abs(face.Normal[axis]) < 0.5f)
                    continue;
                centre[axis] -= face.Normal[axis] * (size[axis] / 2 + ColliderThickness / 2);
                box[axis] = ColliderThickness;
            }
            collider.transform.localPosition = centre;
            collider.AddComponent<BoxCollider>().size = box;
        }

        /// <summary>
        /// Checks the room as the shader draws it. First the squares of every wall and the ceiling against the baked dark rows (the
        /// shader's rule from the material's own values: the origin, axes, square size and the colour of the square (0, 0)); then, along all
        /// twelve edges of the cube, the colours of the two squares that meet at every square along the edge, one centimetre either side:
        /// they must differ across the floor's and the ceiling's edges and match along the four vertical wall-to-wall edges. Logs each
        /// failure, a summary, and returns how many there were.
        /// </summary>
        public static int CheckEdges(string level)
        {
            var data = JObject.Parse(File.ReadAllText($"{level}/{LevelBuilder.LevelJson}"));
            var faces = Faces(data);
            if (faces.Length == 0)
                return 0;
            var name = data.Value<string>("name") ?? Path.GetFileName(level);
            if (!IsChecker(data))
            {
                // No squares, so no edges to match: the faces are drawn from their materials (checked by the build and the snapshots)
                Debug.Log($"SDF level: room check: {name}'s room is drawn from its wall and ceiling materials, not a checker; nothing to check");
                return 0;
            }
            var failures = 0;
            void Fail(string message)
            {
                failures++;
                if (failures <= 20)
                    Debug.LogError($"SDF level: room check: {message}");
            }

            // Each face's colour function: a wall's or the ceiling's from its material, the floor's from its baked parity (the floor is the
            // ground, drawn from the region map: 14.1's tests hold that the regions match the rows)
            var darkAt = new Dictionary<string, Func<Vector3, bool>>();
            foreach (var face in faces)
            {
                var f = face;
                if (f.IsFloor)
                {
                    darkAt[f.Name] = p => Dark(f.Origin, f.U, f.V, f.Square, f.DarkAtOrigin ? 0 : 1, p);
                    continue;
                }
                var path = $"{LevelBuilder.MaterialFolder}/{name} Room {f.Name.Replace("+", "pos").Replace("-", "neg")}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    Fail($"the material of {f.Name} is missing ({path})");
                    darkAt[f.Name] = _ => false;
                    continue;
                }
                darkAt[f.Name] = p => Dark(material.GetVector("_RoomOrigin"), material.GetVector("_RoomU"), material.GetVector("_RoomV"),
                    material.GetVector("_RoomFace").x, (int)material.GetVector("_RoomFace").y, p);
            }

            // The squares against the rows
            foreach (var face in faces)
            {
                for (var row = 0; row < face.SquaresV; row++)
                for (var col = 0; col < face.SquaresU; col++)
                {
                    var centre = face.Origin + face.U * ((col + 0.5f) * face.Square) + face.V * ((row + 0.5f) * face.Square);
                    if (darkAt[face.Name](centre) != (face.Dark[row][col] == '1'))
                        Fail($"{face.Name} square ({col}, {row}) is drawn {(darkAt[face.Name](centre) ? "dark" : "light")} but the bake says the opposite");
                }
            }

            // The edges: every pair of faces that are not opposite meets along one
            var edges = 0;
            for (var i = 0; i < faces.Length; i++)
            for (var j = i + 1; j < faces.Length; j++)
            {
                var (a, b) = (faces[i], faces[j]);
                if (Vector3.Dot(a.Normal, b.Normal) != 0)
                    continue;
                edges++;
                var along = Vector3.Cross(a.Normal, b.Normal).normalized;
                // The edge's line: on both planes. Plane k: normal . x = normal . origin
                var line = EdgeStart(a, b, along, Size(data));
                var vertical = Mathf.Abs(along.y) > 0.5f;
                var length = vertical ? Size(data).y : Mathf.Abs(Vector3.Dot(along, new Vector3(Size(data).x, Size(data).y, Size(data).z)));
                var count = Mathf.RoundToInt(length / a.Square);
                for (var k = 0; k < count; k++)
                {
                    var point = line + along * ((k + 0.5f) * a.Square);
                    // On face a, a little way from the edge (into the room along b's normal); on face b, along a's
                    var onA = darkAt[a.Name](point + b.Normal * 0.01f);
                    var onB = darkAt[b.Name](point + a.Normal * 0.01f);
                    var expectSame = !a.IsFloor && !b.IsFloor && a.Name != "ceiling" && b.Name != "ceiling";
                    if ((onA == onB) != expectSame)
                        Fail($"edge {a.Name} / {b.Name} at square {k}: {(onA ? "dark" : "light")} against {(onB ? "dark" : "light")}, expected {(expectSame ? "the same" : "different")}");
                }
            }
            if (edges != 12)
                Fail($"found {edges} cube edges, not 12");
            Debug.Log(failures == 0
                ? $"SDF level: room check: {name}'s squares match the bake, and the colour alternates across the floor's and the ceiling's 8 edges and matches along the 4 vertical ones"
                : $"SDF level: room check: {name}: {failures} failures");
            return failures;
        }

        /// <summary>A point on the edge shared by two faces, at its lowest end (along the direction <paramref name="along"/>, towards its positive end).</summary>
        private static Vector3 EdgeStart(Face a, Face b, Vector3 along, Vector3 size)
        {
            // The cube spans x in [-w/2, w/2], y in [0, h], z in [-d/2, d/2]. A face with inward normal n lies at the low end of the axis
            // when n points up the axis, else at the high end; the edge's coordinate on the other axes is the planes', and along
            // its own axis it starts at the low end.
            var low = new Vector3(-size.x / 2, 0, -size.z / 2);
            var high = new Vector3(size.x / 2, size.y, size.z / 2);
            var point = low;
            foreach (var face in new[] { a, b })
                for (var axis = 0; axis < 3; axis++)
                    if (Mathf.Abs(face.Normal[axis]) > 0.5f)
                        point[axis] = face.Normal[axis] > 0 ? low[axis] : high[axis];
            // Along the edge: start at the low end (a positive direction was asked for, so flip if needed)
            if (along.x + along.y + along.z < 0)
                for (var axis = 0; axis < 3; axis++)
                    if (Mathf.Abs(along[axis]) > 0.5f)
                        point[axis] = high[axis];
            return point;
        }

        /// <summary>The shader's rule: dark when floor(a) + floor(b) + parity is even, a and b the point's coordinates in squares.</summary>
        private static bool Dark(Vector3 origin, Vector3 u, Vector3 v, float square, int parity, Vector3 point)
        {
            var offset = point - origin;
            var (a, b) = (Mathf.FloorToInt(Vector3.Dot(offset, u) / square), Mathf.FloorToInt(Vector3.Dot(offset, v) / square));
            return (((a + b + parity) % 2) + 2) % 2 == 0;
        }

        [MenuItem("Tools/SDF Level/Check Room Edges")]
        private static void CheckFromMenu()
        {
            foreach (var level in LevelBuilder.Levels())
                CheckEdges(level);
        }

        /// <summary>For batch mode: checks every level's room; exits with an error if any check failed.</summary>
        public static void CheckInBatch()
        {
            var failed = LevelBuilder.Levels().Sum(CheckEdges);
            if (failed > 0)
                EditorApplication.Exit(1);
        }
    }
}
