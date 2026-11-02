using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SdfRig.Tests
{
    /// <summary>The soldier's body (ticket 43): sockets, skins, the colour remap and the head capsule, as plain logic and on the built prefabs.</summary>
    public sealed class SoldierBodyTests
    {
        const string RedPrefab = "Assets/Rig/Generated/Soldier/soldier_red.prefab";
        const string BluePrefab = "Assets/Rig/Generated/Soldier/soldier_blue.prefab";

        // ---------------------------------------------------------------- skin selection

        [Test]
        public void TheTwoSkinsAreRedAndBlueAndBlueIsTheDefault()
        {
            CollectionAssert.AreEqual(new[] { "red", "blue" }, SoldierModels.SkinKeys);
            Assert.AreEqual(0, SoldierModels.IndexOf("red"));
            Assert.AreEqual(1, SoldierModels.IndexOf("blue"));
            Assert.AreEqual("blue", SoldierModels.DefaultSkin);
        }

        [TestCase("red", 0)]
        [TestCase("blue", 1)]
        [TestCase("olive", 1)]
        [TestCase("", 1)]
        [TestCase(null, 1)]
        public void AnUnknownSkinKeyGivesTheDefault(string key, int index) => Assert.AreEqual(index, SoldierModels.IndexOrDefault(key));

        [TestCase("Red", "red")]
        [TestCase("red", "red")]
        [TestCase("Blue", "blue")]
        [TestCase("neutral", "blue")]
        [TestCase(null, "blue")]
        public void ATeamNameGivesItsSkin(string team, string skin) => Assert.AreEqual(skin, SoldierModels.SkinOfTeam(team));

        [Test]
        public void ThePrefabOfASkinIsFoundInTheModelsAsset()
        {
            var models = ScriptableObject.CreateInstance<SoldierModels>();
            var red = new GameObject("red");
            var blue = new GameObject("blue");
            models.prefabs = new[] { red, blue };
            Assert.AreSame(red, models.PrefabOf("red"));
            Assert.AreSame(blue, models.PrefabOf("blue"));
            Assert.AreSame(blue, models.PrefabOf("pink"), "unknown gives the default");
            Object.DestroyImmediate(models);
            Object.DestroyImmediate(red);
            Object.DestroyImmediate(blue);
        }

        // ---------------------------------------------------------------- socket lookup

        [Test]
        public void EverySocketHasABonePathAndAnUnknownOneHasNone()
        {
            foreach (var socket in SoldierBody.Sockets)
                Assert.IsNotNull(SoldierBody.BonePathOf(socket), socket);
            Assert.IsNull(SoldierBody.BonePathOf("mouth"));
            Assert.IsTrue(SoldierBody.BonePathOf("grip").EndsWith("Hand_R"));
            Assert.IsTrue(SoldierBody.BonePathOf("support").EndsWith("Hand_L"));
            Assert.IsTrue(SoldierBody.BonePathOf("head_top").EndsWith("Neck/Head"));
            Assert.IsTrue(SoldierBody.BonePathOf("back").EndsWith("Spine_03"));
        }

        [Test]
        public void ASocketIsFoundUnderItsBoneOnAHierarchy()
        {
            var root = new GameObject("soldier").transform;
            var path = SoldierBody.Head;
            var parent = root;
            foreach (var name in path.Split('/'))
            {
                var child = new GameObject(name).transform;
                child.SetParent(parent, false);
                parent = child;
            }
            var socket = new GameObject("socket_head_top").transform;
            socket.SetParent(parent, false);
            Assert.AreSame(socket, SoldierBody.FindSocket(root, "head_top"));
            Assert.IsNull(SoldierBody.FindSocket(root, "grip"));
            Assert.IsNull(SoldierBody.FindSocket(root, null));
            Assert.IsNull(SoldierBody.FindSocket(null, "grip"));
            Object.DestroyImmediate(root.gameObject);
        }

        // ---------------------------------------------------------------- the colour remap

        [Test]
        public void AVertexOfOneRoleTakesTheSkinColourAtItsBrightnessRelativeToTheBase()
        {
            var baseColours = new[] { new Vector3(0.2f, 0.3f, 0.1f), new Vector3(0.5f, 0.5f, 0.5f) };
            var skin = new[] { new Vector3(0.8f, 0.1f, 0.1f), new Vector3(0.1f, 0.1f, 0.8f) };
            // Exactly the base colour: becomes exactly the skin colour
            var same = SkinRemap.Tint(baseColours[0], new[] { 1f, 0f }, baseColours, skin);
            Assert.AreEqual(skin[0].x, same.x, 1e-4f);
            Assert.AreEqual(skin[0].y, same.y, 1e-4f);
            // Half as bright as the base: the skin colour at half brightness
            var dark = SkinRemap.Tint(baseColours[0] * 0.5f, new[] { 1f, 0f }, baseColours, skin);
            Assert.AreEqual(skin[0].x * 0.5f, dark.x, 1e-4f);
            Assert.AreEqual(skin[0].z * 0.5f, dark.z, 1e-4f);
        }

        [Test]
        public void AVertexOfNoRoleKeepsItsColourAndABlendedOneIsBetween()
        {
            var baseColours = new[] { new Vector3(0.4f, 0.4f, 0.4f) };
            var skin = new[] { new Vector3(0.9f, 0.1f, 0.1f) };
            var colour = new Vector3(0.7f, 0.5f, 0.3f);
            Assert.AreEqual(colour, SkinRemap.Tint(colour, new[] { 0f }, baseColours, skin));
            var half = SkinRemap.Tint(baseColours[0], new[] { 0.5f }, baseColours, skin);
            Assert.Greater(half.x, baseColours[0].x);
            Assert.Less(half.x, skin[0].x);
        }

        [Test]
        public void ARoleShareFollowsTheTwoMaterialsAndTheBlend()
        {
            Assert.AreEqual(1f, SkinRemap.RoleShare(3, 5, 0, 3), 1e-5f);
            Assert.AreEqual(0f, SkinRemap.RoleShare(3, 5, 0, 5), 1e-5f);
            Assert.AreEqual(0.25f, SkinRemap.RoleShare(3, 5, 0.25f, 5), 1e-5f);
            Assert.AreEqual(0.75f, SkinRemap.RoleShare(3, 5, 0.25f, 3), 1e-5f);
            Assert.AreEqual(0f, SkinRemap.RoleShare(3, 5, 0.25f, -1), 1e-5f);
            Assert.AreEqual(1f, SkinRemap.RoleShare(3, 3, 0.4f, 3), 1e-5f);
        }

        // ---------------------------------------------------------------- hit capsules

        [Test]
        public void AHeadCapsuleIsResizedKeepingItsBottom()
        {
            var go = new GameObject("head");
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.radius = 0.13f;
            capsule.height = 0.3f;
            capsule.center = new Vector3(0, 0.15f, 0);
            var bottom = CapsuleSizing.Extent(capsule).x;
            CapsuleSizing.FitHead(capsule, bottom, bottom + 0.5f, 0.2f);
            Assert.AreEqual(0.2f, capsule.radius, 1e-5f);
            Assert.AreEqual(0.5f, capsule.height, 1e-5f);
            Assert.AreEqual(bottom, CapsuleSizing.Extent(capsule).x, 1e-5f);
            // Never shorter than a ball
            CapsuleSizing.FitHead(capsule, bottom, bottom + 0.1f, 0.2f);
            Assert.AreEqual(0.4f, capsule.height, 1e-5f);
            Object.DestroyImmediate(go);
        }

        // ---------------------------------------------------------------- the built prefabs (after sdf unity and SoldierSkins)

        static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                Assert.Ignore($"{path} is not built: run `sdf unity`, then SdfRig.Editor.SoldierSkins.ImportAll");
            return prefab;
        }

        [TestCase(RedPrefab)]
        [TestCase(BluePrefab)]
        public void ASkinPrefabHasASavedGenericAvatarAndTheSkeleton(string path)
        {
            var prefab = Load(path);
            var animator = prefab.GetComponent<Animator>();
            Assert.IsNotNull(animator);
            Assert.IsNotNull(animator.avatar, "the avatar is saved with the prefab");
            Assert.IsTrue(animator.avatar.isValid);
            Assert.IsFalse(animator.avatar.isHuman, "a Generic avatar, so Mixamo clips play by bone path");
            Assert.IsTrue(AssetDatabase.Contains(animator.avatar), "the avatar is an asset");
            Assert.IsNotNull(prefab.transform.Find(SoldierBody.Head), "Head");
            Assert.IsNotNull(prefab.transform.Find(SoldierBody.HandR), "Hand_R");
        }

        [TestCase(RedPrefab)]
        [TestCase(BluePrefab)]
        public void ASkinPrefabHasEverySocket(string path)
        {
            var prefab = Load(path);
            foreach (var socket in SoldierBody.Sockets)
                Assert.IsNotNull(SoldierBody.FindSocket(prefab.transform, socket), socket);
            // Headless: nothing sits on the head socket yet
            Assert.AreEqual(0, SoldierBody.FindSocket(prefab.transform, "head_top").childCount);
            Assert.IsTrue(SoldierBody.FindSocket(prefab.transform, "head_top").position.y > 1.7f);
        }

        [TestCase(RedPrefab)]
        [TestCase(BluePrefab)]
        public void ASkinPrefabHasTriggerHitCapsulesIncludingTheHead(string path)
        {
            var prefab = Load(path);
            var hits = prefab.GetComponentsInChildren<RigHit>(true);
            Assert.AreEqual(18, hits.Length);
            Assert.IsTrue(hits.All(h => h.GetComponent<CapsuleCollider>().isTrigger));
            var head = SoldierBody.HeadCapsule(prefab.transform);
            Assert.IsNotNull(head);
            Assert.AreEqual(0.13f, head.radius, 0.005f);
        }

        [Test]
        public void TheTwoSkinsShareTheAvatarButHaveTheirOwnMeshAndColours()
        {
            var red = Load(RedPrefab).GetComponentInChildren<SkinnedMeshRenderer>();
            var blue = Load(BluePrefab).GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.AreSame(Load(RedPrefab).GetComponent<Animator>().avatar, Load(BluePrefab).GetComponent<Animator>().avatar);
            Assert.AreNotSame(red.sharedMesh, blue.sharedMesh);
            Assert.AreEqual(red.sharedMesh.vertexCount, blue.sharedMesh.vertexCount);
            Assert.AreEqual(red.sharedMesh.GetIndexCount(0), blue.sharedMesh.GetIndexCount(0));
            Assert.Less(red.sharedMesh.GetIndexCount(0) / 3, 6500, "the budget: 8 bodies at about 6.5k triangles");
            Assert.AreEqual(red.bones.Length, red.sharedMesh.bindposes.Length);
            Assert.AreEqual(50, red.bones.Length);
        }

        [Test]
        public void TheModelsAssetListsBothPrefabs()
        {
            var models = AssetDatabase.LoadAssetAtPath<SoldierModels>("Assets/Rig/Generated/Resources/SoldierModels.asset");
            if (models == null)
                Assert.Ignore("SoldierModels.asset is not built");
            Assert.AreSame(Load(RedPrefab), models.PrefabOf("red"));
            Assert.AreSame(Load(BluePrefab), models.PrefabOf("blue"));
        }
    }
}
