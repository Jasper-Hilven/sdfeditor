using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SdfMushrooms;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SdfRig.Editor
{
    /// <summary>
    /// The soldier's prefabs (ticket 43, from BattleRoyale's SoldierSkins and RiggedImport): the skinned .glb that <c>sdf unity</c> meshes
    /// from content/models/characters/soldier/soldier.sdf (Assets/Rig/Generated/characters/soldier/soldier.glb) becomes the rig
    /// (<see cref="RigBuilder"/>: bones, sockets, hit capsules, a <see cref="RigInfo"/>), its mesh gets the shared toon look
    /// (<see cref="MushroomBuilder.FillSkinned"/>, the toon and outline materials), and one prefab per team skin is saved:
    /// soldier_red.prefab and soldier_blue.prefab.
    /// <para>
    /// The model is meshed once in the olive base set (materials/characters/soldier/olive); a skin is a vertex-colour remap at import
    /// (<see cref="SkinRemap"/>), the way BattleRoyale does it: each vertex's colour moves, by how much of each role's material it is
    /// (from the .glb's material ids and blend), to the skin's colour (materials/characters/soldier/&lt;skin&gt;) at its brightness
    /// relative to the base's. One mesh asset per skin, shared by every soldier wearing it. The Generic avatar is saved as an asset
    /// (BattleRoyale's rig import left it unsaved on the base prefab, so a prefab lost it) and shared by both prefabs. The head socket is
    /// empty: the monster head (ticket 45) goes there.
    /// </para>
    /// Batch: <c>-executeMethod SdfRig.Editor.SoldierSkins.ImportAll</c> (run <c>sdf unity</c> first).
    /// </summary>
    public static class SoldierSkins
    {
        public const string RigFolder = "Assets/Rig/Generated";
        public const string BodyGlb = RigFolder + "/characters/soldier/soldier.glb";
        public const string Folder = RigFolder + "/Soldier";
        public const string MeshFolder = Folder + "/Meshes";
        public const string ResourcesFolder = RigFolder + "/Resources";
        public const string ModelsAssetPath = ResourcesFolder + "/" + SoldierModels.ResourceName + ".asset";
        public const string AvatarPath = Folder + "/soldier_avatar.asset";
        public const string Body = "soldier";
        public const string BaseSkin = "olive";

        public static string SkinPrefab(string skin) => $"{Folder}/soldier_{skin}.prefab";

        static string MaterialsFolder => Path.Combine(ProjectRoot, "..", "materials", "characters", "soldier");

        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        /// <summary>The material name of a role in a skin: soldier_vest (the olive base), soldier_vest_red.</summary>
        public static string MaterialOf(string role, string skin) => skin == BaseSkin ? $"soldier_{role}" : $"soldier_{role}_{skin}";

        /// <summary>The baseColor of a role in a skin, as its .sdfmat writes it (sRGB-looking, 0 to 1).</summary>
        public static Color RoleColour(string role, string skin)
        {
            var path = Path.Combine(MaterialsFolder, skin, MaterialOf(role, skin) + ".sdfmat");
            if (!File.Exists(path))
                throw new FileNotFoundException($"the {skin} skin has no {role}", path);
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (!line.StartsWith("baseColor", StringComparison.Ordinal))
                    continue;
                var v = line.Substring(line.IndexOf('(') + 1).TrimEnd(')').Split(',')
                    .Select(s => float.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray();
                return new Color(v[0], v[1], v[2], 1);
            }
            throw new InvalidDataException($"{path} has no baseColor");
        }

        [MenuItem("Sdf/Rig/Build Soldier Skins")]
        public static void BuildFromMenu() => Debug.Log($"SoldierSkins: {Build()} skin prefabs");

        /// <summary>Batch: builds the skin prefabs. Exit 1 on any failure.</summary>
        public static void ImportAll()
        {
            var ok = false;
            try
            {
                var made = Build();
                ok = made == SoldierModels.SkinKeys.Length;
                Debug.Log($"SoldierSkins: {made} skin prefabs");
            }
            catch (Exception e)
            {
                Debug.LogError($"SoldierSkins failed: {e}");
            }
            if (Application.isBatchMode)
                EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>Builds every skin prefab and the <see cref="SoldierModels"/> list. Returns the prefabs made (0 when the soldier is not meshed).</summary>
        public static int Build()
        {
            var glbPath = Path.Combine(ProjectRoot, BodyGlb);
            if (!File.Exists(glbPath))
            {
                Debug.LogError($"SoldierSkins: {BodyGlb} is missing; run `sdf unity` (it meshes content/models/characters/soldier/soldier.sdf into Assets/Rig/Generated)");
                return 0;
            }
            MushroomBuilder.EnsureFolder(Folder);
            MushroomBuilder.EnsureFolder(MeshFolder);
            MushroomBuilder.EnsureFolder(ResourcesFolder);
            var bytes = File.ReadAllBytes(glbPath);
            var glb = SdfGlb.Parse(bytes, Body);

            var models = AssetDatabase.LoadAssetAtPath<SoldierModels>(ModelsAssetPath);
            if (models == null)
            {
                models = ScriptableObject.CreateInstance<SoldierModels>();
                AssetDatabase.CreateAsset(models, ModelsAssetPath);
            }
            models.prefabs = new GameObject[SoldierModels.SkinKeys.Length];
            var baseColours = SkinRemap.Roles.Select(r => RoleColour(r, BaseSkin)).ToArray();
            var made = 0;
            foreach (var skin in SoldierModels.SkinKeys)
            {
                var colours = SkinRemap.Roles.Select(r => RoleColour(r, skin)).ToArray();
                var rig = RigBuilder.Build(bytes, $"soldier_{skin}");
                try
                {
                    var mesh = ApplyLook(rig, glb, skin, baseColours, colours);
                    var avatar = SaveAvatar(rig);
                    rig.GetComponent<Animator>().avatar = avatar;
                    foreach (var info in rig.GetComponents<RigInfo>())
                        info.Refresh();
                    models.prefabs[SoldierModels.IndexOf(skin)] = PrefabUtility.SaveAsPrefabAsset(rig, SkinPrefab(skin));
                    made++;
                }
                finally
                {
                    Object.DestroyImmediate(rig);
                }
            }
            EditorUtility.SetDirty(models);
            AssetDatabase.SaveAssets();
            return made;
        }

        /// <summary>
        /// Gives the rig's skinned renderer the packed toon mesh in the skin's colours, saved as an asset, and the toon and outline
        /// materials. Returns the mesh.
        /// </summary>
        static Mesh ApplyLook(GameObject rig, SdfGlb glb, string skin, Color[] baseColours, Color[] colours)
        {
            var smr = rig.GetComponentInChildren<SkinnedMeshRenderer>() ?? throw new InvalidOperationException("the soldier has no skinned mesh");
            var old = smr.sharedMesh;
            var data = glb.Meshes.FirstOrDefault() ?? throw new InvalidOperationException("the .glb has no mesh");
            if (data.VertexCount != old.vertexCount)
                throw new InvalidOperationException($"{data.VertexCount} vertices in the file, {old.vertexCount} in the rig mesh");

            var mesh = new Mesh { name = $"soldier_{skin}" };
            MushroomBuilder.FillSkinned(mesh, data, glb.Materials, Body, old.boneWeights);
            mesh.name = $"soldier_{skin}";
            mesh.bindposes = old.bindposes;
            Tint(mesh, glb, data, baseColours, colours);

            var assetPath = $"{MeshFolder}/soldier_{skin}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                mesh = existing;
                EditorUtility.SetDirty(mesh);
            }
            else
                AssetDatabase.CreateAsset(mesh, assetPath);

            smr.sharedMesh = mesh;
            smr.localBounds = mesh.bounds;
            var (toon, outline) = MushroomBuilder.SharedMaterials();
            smr.sharedMaterials = outline != null && glb.Outline ? new[] { toon, outline } : new[] { toon };
            smr.shadowCastingMode = glb.Shadows == "off" ? ShadowCastingMode.Off : ShadowCastingMode.On;
            smr.updateWhenOffscreen = false;
            Object.DestroyImmediate(old);
            return mesh;
        }

        /// <summary>The Generic avatar of the rig (root motion node Root) as an asset the skin prefabs share.</summary>
        static Avatar SaveAvatar(GameObject rig)
        {
            var avatar = AvatarBuilder.BuildGenericAvatar(rig, RigBuilder.DefaultRootMotionNode);
            avatar.name = "soldier_avatar";
            var existing = AssetDatabase.LoadAssetAtPath<Avatar>(AvatarPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(avatar, existing);
                Object.DestroyImmediate(avatar);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(avatar, AvatarPath);
            return avatar;
        }

        /// <summary>The roles' material ids in the .glb's material table (-1 where a role is not in the file).</summary>
        public static int[] RoleIds(SdfGlb glb) =>
            SkinRemap.Roles.Select(r => glb.Materials.FindIndex(m => m.Name == MaterialOf(r, BaseSkin))).ToArray();

        /// <summary>The vertex colours of <paramref name="mesh"/> (packed with the vertices in file order, Color as UNorm8) moved to the skin.</summary>
        static void Tint(Mesh mesh, SdfGlb glb, SdfGlb.MeshData data, Color[] baseColours, Color[] skin)
        {
            if (!mesh.HasVertexAttribute(VertexAttribute.Color))
                return;
            var ids = data.Get("_MATERIAL_IDS", 2);
            if (ids == null)
                return;
            var blend = data.Get("_MATERIAL_BLEND", 1);
            var roleIds = RoleIds(glb);
            // The vertices are in the order the cache optimiser gave them: the same order (a function of the triangles) maps them back
            var indices = data.Primitives.SelectMany(p => p.Indices).ToArray();
            var turned = new int[indices.Length];
            for (var t = 0; t < indices.Length; t += 3)
                (turned[t], turned[t + 1], turned[t + 2]) = (indices[t], indices[t + 2], indices[t + 1]);
            var order = VertexCacheOptimizer.Optimize(turned, data.VertexCount).Order;
            if (order.Length != mesh.vertexCount)
                throw new InvalidOperationException($"{order.Length} vertices ordered, {mesh.vertexCount} in the mesh");

            var baseLinear = baseColours.Select(c => { var l = c.linear; return new Vector3(l.r, l.g, l.b); }).ToArray();
            var skinLinear = skin.Select(c => { var l = c.linear; return new Vector3(l.r, l.g, l.b); }).ToArray();
            var stream = mesh.GetVertexAttributeStream(VertexAttribute.Color);
            var offset = mesh.GetVertexAttributeOffset(VertexAttribute.Color);
            var format = mesh.GetVertexAttributeFormat(VertexAttribute.Color);
            if (format != VertexAttributeFormat.UNorm8)
                throw new NotSupportedException($"vertex colours as {format}");
            var stride = mesh.GetVertexBufferStride(stream);
            byte[] bytes;
            using (var meshData = Mesh.AcquireReadOnlyMeshData(mesh))
                bytes = meshData[0].GetVertexData<byte>(stream).ToArray();
            var weights = new float[roleIds.Length];
            for (var v = 0; v < mesh.vertexCount; v++)
            {
                var f = order[v];
                var any = false;
                for (var r = 0; r < roleIds.Length; r++)
                {
                    weights[r] = SkinRemap.RoleShare((int)ids[2 * f], (int)ids[2 * f + 1], blend != null ? blend[f] : 0, roleIds[r]);
                    any |= weights[r] > 0;
                }
                if (!any)
                    continue;
                var at = v * stride + offset;
                var colour = new Vector3(bytes[at] / 255f, bytes[at + 1] / 255f, bytes[at + 2] / 255f);
                var result = SkinRemap.Tint(colour, weights, baseLinear, skinLinear);
                bytes[at] = (byte)Mathf.RoundToInt(result.x * 255);
                bytes[at + 1] = (byte)Mathf.RoundToInt(result.y * 255);
                bytes[at + 2] = (byte)Mathf.RoundToInt(result.z * 255);
            }
            mesh.SetVertexBufferData(bytes, 0, 0, bytes.Length, stream, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
        }
    }
}
