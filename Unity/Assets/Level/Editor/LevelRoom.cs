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
    /// The closed room of a level (level.json "room", from the level file's <c>room</c> line): the chessboard cube. The floor is the
    /// ground, built from its chunks as ever; <see cref="Add"/> builds the other five faces under Level/Room: a flat quad each (the four
    /// walls and the ceiling, facing into the room, each on its own material on SdfRoomFace.shader, which draws the squares from the world
    /// position with ruled-straight edges) and a box collider outside every one of the six faces, so that nothing can leave the room.
    /// <para>
    /// Which squares are dark is the pipeline's <c>RoomChecker</c>: the baked room block lists, per face, its origin, unit axes, inward
    /// normal and dark rows; the shader takes the origin, axes and the colour of the square (0, 0) and alternates from there, which is the
    /// same as the rows for a chessboard. <see cref="CheckEdges"/> checks that against the rows, and the colour across the cube's edges:
    /// it alternates across the floor's four edges, the ceiling's four edges and everywhere inside a face, and matches along the four
    /// vertical wall-to-wall edges (three squares meet at each corner and two colours cannot colour a triangle, so some repeat is forced;
    /// see RoomChecker).
    /// </para>
    /// Positions in level.json are the pipeline's; x is mirrored here, as in the rest of the builder.
    /// </summary>
    public static class LevelRoom
    {
        public const string RoomName = "Room";
        public const string ShaderPath = "Assets/Level/Shaders/SdfRoomFace.shader";

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

            public bool DarkAtOrigin => Dark[0][0] == '1';
            public bool IsFloor => Name == "floor";
            public Vector2 Size => new Vector2(SquaresU, SquaresV) * Square;

            /// <summary>The square of a point (on the face's plane): floor of its coordinates along u and v, in squares.</summary>
            public Vector2Int SquareOf(Vector3 point)
            {
                var offset = point - Origin;
                return new Vector2Int(Mathf.FloorToInt(Vector3.Dot(offset, U) / Square), Mathf.FloorToInt(Vector3.Dot(offset, V) / Square));
            }
        }

        public static bool HasRoom(Newtonsoft.Json.Linq.JObject data) => data["room"] is JObject;

        /// <summary>The faces of the level's room (floor, ceiling, wall-z-, wall-z+, wall-x-, wall-x+), x mirrored; none without a room.</summary>
        public static Face[] Faces(JObject data)
        {
            if (!(data["room"] is JObject room))
                return Array.Empty<Face>();
            var square = room.Value<float>("square");
            Vector3 Mirrored(JToken token) => new Vector3(-token[0].Value<float>(), token[1].Value<float>(), token[2].Value<float>());
            return ((JArray)room["faces"]).Select(f => new Face
            {
                Name = f.Value<string>("face"),
                Origin = Mirrored(f["origin"]),
                U = Mirrored(f["u"]),
                V = Mirrored(f["v"]),
                Normal = Mirrored(f["normal"]),
                SquaresU = f["squares"][0].Value<int>(),
                SquaresV = f["squares"][1].Value<int>(),
                Square = square,
                Dark = f["dark"].Values<string>().ToArray(),
            }).ToArray();
        }

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

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath) ?? throw new InvalidOperationException($"The shader {ShaderPath} is missing");
            var materials = (JObject)data["materials"];
            var light = LevelBuilder.MaterialOf(materials, "light") ?? throw new InvalidDataException("level.json has no light material; bake the level again");
            var dark = LevelBuilder.MaterialOf(materials, "dark") ?? throw new InvalidDataException("level.json has no dark material; bake the level again");

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
                    renderer.sharedMaterial = FaceMaterial(name, face, shader, light, dark);
                    // Walls and ceiling cast no shadow (the ceiling would shade the whole room from the lamp) and take none
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    drawn++;
                }
                AddCollider(root.transform, face, size);
            }
            AssetDatabase.SaveAssets();
            return drawn;
        }

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
