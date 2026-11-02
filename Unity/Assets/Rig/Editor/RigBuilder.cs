using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SdfRig.Editor
{
    /// <summary>
    /// Builds a rigged model from a skinned GLB (the layout <c>kk mesh</c> writes, see docs/sdf-format-2.md "The GLB"): joints as a
    /// hierarchy, a skinned mesh, sockets, hit capsules and a <see cref="RigInfo"/>. glTF to Unity: see <see cref="RigConvert"/>.
    /// </summary>
    public static class RigBuilder
    {
        private const string SocketPrefix = "socket_";
        private const string HitPrefix = "hit_";

        /// <summary>Builds the model in memory (nothing is saved). The caller owns the returned object and its mesh and material.</summary>
        public static GameObject Build(byte[] glb, string name) => Build(glb, name, DefaultRootMotionNode);

        /// <summary>The root motion node of a Generic avatar built for an external skeleton (empty: none).</summary>
        public const string DefaultRootMotionNode = "Root";

        /// <summary>As <see cref="Build(byte[], string)"/>; <paramref name="rootMotionNode"/> is used for a GLB rigged onto an external skeleton.</summary>
        public static GameObject Build(byte[] glb, string name, string rootMotionNode)
        {
            var file = GlbFile.Parse(glb);
            var nodes = file.Nodes;
            var skins = file.Skins;
            if (skins.Count == 0)
                throw new FormatException("The GLB has no skin; a rigged model needs one");
            var jointNodes = ToInts(MiniJson.List(MiniJson.Get(skins[0], "joints")));
            var jointSet = new HashSet<int>(jointNodes);

            var root = new GameObject(name);
            var transforms = new Dictionary<int, Transform>();
            var sceneNodes = SceneRoots(file);
            foreach (var n in sceneNodes)
                CreateNode(file, n, root.transform, jointSet, transforms);

            var skinTransforms = new Transform[jointNodes.Length];
            for (var i = 0; i < jointNodes.Length; i++)
                if (!transforms.TryGetValue(jointNodes[i], out skinTransforms[i]))
                    throw new FormatException($"Joint node {jointNodes[i]} is not reachable from the scene");

            // The mesh
            var meshNodeIndex = -1;
            for (var i = 0; i < nodes.Count; i++)
                if (MiniJson.Get(nodes[i], "mesh") != null && !jointSet.Contains(i))
                {
                    meshNodeIndex = i;
                    break;
                }
            if (meshNodeIndex < 0)
                throw new FormatException("The GLB has no mesh node");
            var meshObject = new GameObject("skin");
            meshObject.transform.SetParent(root.transform, false);
            var smr = meshObject.AddComponent<SkinnedMeshRenderer>();
            var mesh = BuildMesh(file, MiniJson.Int(MiniJson.Get(nodes[meshNodeIndex], "mesh")), jointNodes.Length, name);
            smr.sharedMesh = mesh;
            smr.bones = skinTransforms;
            var rootBone = skinTransforms[0];
            for (var i = 0; i < skinTransforms.Length; i++)
                if (skinTransforms[i].name == "root")
                    rootBone = skinTransforms[i];
            smr.rootBone = rootBone;
            var bind = new Matrix4x4[skinTransforms.Length];
            for (var i = 0; i < bind.Length; i++)
                bind[i] = skinTransforms[i].worldToLocalMatrix * meshObject.transform.localToWorldMatrix;
            mesh.bindposes = bind;
            smr.localBounds = mesh.bounds;
            var material = PlaceholderMaterial();
            var materials = new Material[mesh.subMeshCount];
            for (var i = 0; i < materials.Length; i++)
                materials[i] = material;
            smr.sharedMaterials = materials;

            // Sockets (created with the nodes), hit capsules and bone data
            var info = root.AddComponent<RigInfo>();
            var boneInfos = new List<RigBoneInfo>();
            for (var i = 0; i < jointNodes.Length; i++)
            {
                var extras = MiniJson.Get(nodes[jointNodes[i]], "extras");
                var parent = skinTransforms[i].parent;
                var b = new RigBoneInfo
                {
                    name = skinTransforms[i].name,
                    parent = parent != null && parent != root.transform ? parent.name : "",
                    falloff = MiniJson.Float(MiniJson.Get(extras, "falloff")),
                    hit = HitText(MiniJson.Get(extras, "hit")),
                };
                var spin = MiniJson.Floats(MiniJson.Get(extras, "spin"));
                if (spin != null && spin.Length == 3)
                {
                    b.hasSpin = true;
                    b.spin = RigConvert.Direction(new Vector3(spin[0], spin[1], spin[2]));
                }
                var hinge = MiniJson.Floats(MiniJson.Get(extras, "hinge"));
                if (hinge != null && hinge.Length == 3)
                {
                    b.hasHinge = true;
                    b.hinge = RigConvert.Direction(new Vector3(hinge[0], hinge[1], hinge[2]));
                }
                var limit = MiniJson.Floats(MiniJson.Get(extras, "limit"));
                if (limit != null && limit.Length == 2)
                {
                    b.hasLimit = true;
                    b.limit = new Vector2(limit[0], limit[1]);
                }
                boneInfos.Add(b);
            }
            info.bones = boneInfos.ToArray();

            var socketNames = new List<string>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith(SocketPrefix, StringComparison.Ordinal))
                    socketNames.Add(t.name.Substring(SocketPrefix.Length));
            info.sockets = socketNames.ToArray();

            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = null;
            var skeleton = MiniJson.String(MiniJson.Get(MiniJson.Get(MiniJson.Get(file.Json, "extras"), "rig"), "skeleton"), "");
            info.skeleton = skeleton;
            if (skeleton.Length > 0)
            {
                info.rootMotionNode = rootMotionNode ?? "";
                var avatar = AvatarBuilder.BuildGenericAvatar(root, info.rootMotionNode);
                avatar.name = name + "_avatar";
                animator.avatar = avatar;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            }
            return root;
        }

        private static int[] SceneRoots(GlbFile file)
        {
            var scenes = MiniJson.List(MiniJson.Get(file.Json, "scenes"));
            var sceneIndex = MiniJson.Int(MiniJson.Get(file.Json, "scene"));
            if (scenes != null && scenes.Count > 0)
                return ToInts(MiniJson.List(MiniJson.Get(scenes[Math.Min(sceneIndex, scenes.Count - 1)], "nodes")));
            var all = new int[file.Nodes.Count];
            for (var i = 0; i < all.Length; i++)
                all[i] = i;
            return all;
        }

        private static void CreateNode(GlbFile file, int index, Transform parent, HashSet<int> joints, Dictionary<int, Transform> transforms)
        {
            var node = file.Nodes[index];
            var nodeName = MiniJson.String(MiniJson.Get(node, "name"), "node" + index.ToString(CultureInfo.InvariantCulture));
            var extras = MiniJson.Get(node, "extras");

            if (nodeName.StartsWith(HitPrefix, StringComparison.Ordinal) && MiniJson.String(MiniJson.Get(extras, "kind")) == "capsule")
            {
                AddHit(parent, node, extras);
                return;
            }
            if (MiniJson.Get(node, "mesh") != null && !joints.Contains(index))
                return; // the mesh node: the skinned renderer replaces it

            var go = new GameObject(nodeName);
            go.transform.SetParent(parent, false);
            var t = MiniJson.Floats(MiniJson.Get(node, "translation"));
            if (t != null && t.Length == 3)
                go.transform.localPosition = RigConvert.Position(new Vector3(t[0], t[1], t[2]));
            var r = MiniJson.Floats(MiniJson.Get(node, "rotation"));
            if (r != null && r.Length == 4)
                go.transform.localRotation = RigConvert.Rotation(new Quaternion(r[0], r[1], r[2], r[3]));
            var s = MiniJson.Floats(MiniJson.Get(node, "scale"));
            if (s != null && s.Length == 3)
                go.transform.localScale = new Vector3(s[0], s[1], s[2]);
            transforms[index] = go.transform;
            var children = MiniJson.List(MiniJson.Get(node, "children"));
            if (children != null)
                foreach (var c in ToInts(children))
                    CreateNode(file, c, go.transform, joints, transforms);
        }

        private static void AddHit(Transform joint, object node, object extras)
        {
            var go = joint.gameObject;
            var rot = MiniJson.Floats(MiniJson.Get(node, "rotation"));
            if (rot != null && rot.Length == 4)
            {
                var child = new GameObject(HitPrefix + joint.name);
                child.transform.SetParent(joint, false);
                var ct = MiniJson.Floats(MiniJson.Get(node, "translation"));
                if (ct != null && ct.Length == 3)
                    child.transform.localPosition = RigConvert.Position(new Vector3(ct[0], ct[1], ct[2]));
                child.transform.localRotation = RigConvert.Rotation(new Quaternion(rot[0], rot[1], rot[2], rot[3]));
                var cc = child.AddComponent<CapsuleCollider>();
                cc.center = Vector3.zero;
                cc.direction = 1;
                cc.radius = MiniJson.Float(MiniJson.Get(extras, "radius"));
                cc.height = MiniJson.Float(MiniJson.Get(extras, "height"));
                cc.isTrigger = true;
                child.AddComponent<RigHit>().Bone = joint.name;
                return;
            }
            var collider = go.AddComponent<CapsuleCollider>();
            var t = MiniJson.Floats(MiniJson.Get(node, "translation"));
            collider.center = t != null && t.Length == 3 ? RigConvert.Position(new Vector3(t[0], t[1], t[2])) : Vector3.zero;
            collider.direction = 1;
            collider.radius = MiniJson.Float(MiniJson.Get(extras, "radius"));
            collider.height = MiniJson.Float(MiniJson.Get(extras, "height"));
            collider.isTrigger = true;
            go.AddComponent<RigHit>().Bone = go.name;
        }

        private static Mesh BuildMesh(GlbFile file, int meshIndex, int boneCount, string name)
        {
            var primitives = MiniJson.List(MiniJson.Get(file.Meshes[meshIndex], "primitives"));
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color>();
            var weights = new List<BoneWeight>();
            var submeshes = new List<int[]>();
            var firstVertex = new Dictionary<int, int>(); // POSITION accessor -> its first vertex, so primitives that share vertices share them
            var hasNormals = true;
            var hasColors = true;
            var hasWeights = true;

            foreach (var primitive in primitives)
            {
                var attributes = MiniJson.Get(primitive, "attributes");
                var positionAccessor = MiniJson.Int(MiniJson.Get(attributes, "POSITION"), -1);
                if (positionAccessor < 0)
                    throw new FormatException("A primitive has no POSITION");
                if (!firstVertex.TryGetValue(positionAccessor, out var baseVertex))
                {
                    baseVertex = positions.Count;
                    firstVertex[positionAccessor] = baseVertex;
                    var count = file.AccessorCount(positionAccessor);

                    var p = file.ReadFloats(positionAccessor, out _);
                    for (var i = 0; i < count; i++)
                        positions.Add(RigConvert.Position(new Vector3(p[3 * i], p[3 * i + 1], p[3 * i + 2])));

                    var nAccessor = MiniJson.Int(MiniJson.Get(attributes, "NORMAL"), -1);
                    if (nAccessor >= 0)
                    {
                        var n = file.ReadFloats(nAccessor, out _);
                        for (var i = 0; i < count; i++)
                            normals.Add(RigConvert.Position(new Vector3(n[3 * i], n[3 * i + 1], n[3 * i + 2])));
                    }
                    else
                        hasNormals = false;

                    var cAccessor = MiniJson.Int(MiniJson.Get(attributes, "COLOR_0"), -1);
                    if (cAccessor >= 0)
                    {
                        var c = file.ReadFloats(cAccessor, out var cc);
                        for (var i = 0; i < count; i++)
                            colors.Add(new Color(c[cc * i], c[cc * i + 1], c[cc * i + 2], cc == 4 ? c[cc * i + 3] : 1f));
                    }
                    else
                        hasColors = false;

                    var jAccessor = MiniJson.Int(MiniJson.Get(attributes, "JOINTS_0"), -1);
                    var wAccessor = MiniJson.Int(MiniJson.Get(attributes, "WEIGHTS_0"), -1);
                    if (jAccessor >= 0 && wAccessor >= 0)
                    {
                        var j = file.ReadInts(jAccessor, out _);
                        var w = file.ReadFloats(wAccessor, out _);
                        for (var i = 0; i < count; i++)
                            weights.Add(MakeWeight(j, w, 4 * i, boneCount));
                    }
                    else
                        hasWeights = false;
                }

                int[] indices;
                var iAccessor = MiniJson.Int(MiniJson.Get(primitive, "indices"), -1);
                if (iAccessor >= 0)
                    indices = file.ReadInts(iAccessor, out _);
                else
                {
                    indices = new int[file.AccessorCount(positionAccessor)];
                    for (var i = 0; i < indices.Length; i++)
                        indices[i] = i;
                }
                var triangles = new int[indices.Length - indices.Length % 3];
                for (var i = 0; i < triangles.Length; i += 3)
                {
                    // The X mirror flips the winding: swap two indices of every triangle
                    triangles[i] = baseVertex + indices[i];
                    triangles[i + 1] = baseVertex + indices[i + 2];
                    triangles[i + 2] = baseVertex + indices[i + 1];
                }
                submeshes.Add(triangles);
            }

            var mesh = new Mesh { name = name };
            if (positions.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(positions);
            if (hasNormals && normals.Count == positions.Count)
                mesh.SetNormals(normals);
            if (hasColors && colors.Count == positions.Count)
                mesh.SetColors(colors);
            mesh.subMeshCount = submeshes.Count;
            for (var i = 0; i < submeshes.Count; i++)
                mesh.SetTriangles(submeshes[i], i);
            if (!(hasNormals && normals.Count == positions.Count))
                mesh.RecalculateNormals();
            if (hasWeights && weights.Count == positions.Count)
                mesh.boneWeights = weights.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static BoneWeight MakeWeight(int[] joints, float[] weights, int at, int boneCount)
        {
            var sum = weights[at] + weights[at + 1] + weights[at + 2] + weights[at + 3];
            if (sum <= 0)
                return new BoneWeight { boneIndex0 = 0, weight0 = 1 };
            for (var k = 0; k < 4; k++)
                if (weights[at + k] > 0 && joints[at + k] >= boneCount)
                    throw new FormatException($"A vertex is weighted to joint {joints[at + k]} but the skin has {boneCount}");
            return new BoneWeight
            {
                boneIndex0 = joints[at], weight0 = weights[at] / sum,
                boneIndex1 = joints[at + 1], weight1 = weights[at + 1] / sum,
                boneIndex2 = joints[at + 2], weight2 = weights[at + 2] / sum,
                boneIndex3 = joints[at + 3], weight3 = weights[at + 3] / sum,
            };
        }

        private static string HitText(object hit)
        {
            if (hit is double d)
                return d.ToString("R", CultureInfo.InvariantCulture);
            return MiniJson.String(hit, "auto");
        }

        private static int[] ToInts(List<object> list)
        {
            if (list == null)
                return Array.Empty<int>();
            var r = new int[list.Count];
            for (var i = 0; i < r.Length; i++)
                r[i] = MiniJson.Int(list[i]);
            return r;
        }

        private static Material PlaceholderMaterial()
        {
            // The toon material arrives with ticket 07; until then a lit URP material
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            return shader != null ? new Material(shader) { name = "RigPlaceholder" } : null;
        }
    }
}
