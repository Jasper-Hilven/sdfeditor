using System;
using SdfPickups;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// What makes a pod seen from afar: a short glow column over it (<see cref="FlagGlowColumn"/>, far shorter and dimmer than a flag's)
    /// and a pulsing disc on the ground round it (<c>Resources/PodGroundDisc.shader</c>), gold over a weapon, green over health. Both
    /// are hidden while the pod is empty (a health pod waiting for its cross to come back), so what glows is what can be taken. Added to
    /// every pod of the arena when the battle starts (<see cref="BattleArena"/>); the neutral pods, with the strongest weapons, have the
    /// taller columns.
    /// </summary>
    public sealed class PodBeacon : MonoBehaviour
    {
        public const string DiscShaderName = "PodGroundDisc";

        public static readonly Color WeaponColour = new Color(1f, 0.7f, 0.15f);
        public static readonly Color HealthColour = new Color(0.25f, 0.95f, 0.35f);

        /// <summary>A side's pod's column, in metres; the neutral pods' is <see cref="NeutralColumnHeight"/>.</summary>
        public const float ColumnHeight = 5;
        public const float NeutralColumnHeight = 9;
        public const float ColumnWidth = 0.35f;
        public const float ColumnIntensity = 0.9f;

        /// <summary>The ground disc's radius, past the pod's 1.3 m foot, and its height over the pod's origin (the ground).</summary>
        public const float DiscRadius = 2.4f;
        public const float DiscHeight = 0.04f;

        private static Mesh discMesh;
        private Func<bool> available;
        private GameObject column;
        private GameObject disc;
        private Material discMaterial;

        public static PodBeacon Add(WeaponPickup pod, bool neutral) =>
            pod != null ? Add(pod.gameObject, () => pod.Available, WeaponColour, neutral ? NeutralColumnHeight : ColumnHeight) : null;

        public static PodBeacon Add(HealthPickup pod) =>
            pod != null ? Add(pod.gameObject, () => pod.Available, HealthColour, ColumnHeight) : null;

        private static PodBeacon Add(GameObject pod, Func<bool> available, Color colour, float columnHeight)
        {
            var beacon = pod.AddComponent<PodBeacon>();
            beacon.available = available;
            var glow = FlagGlowColumn.Create(pod.transform, colour, columnHeight, ColumnWidth, ColumnIntensity);
            if (glow != null)
                beacon.column = glow.gameObject;
            beacon.disc = beacon.CreateDisc(colour);
            return beacon;
        }

        private GameObject CreateDisc(Color colour)
        {
            var shader = Resources.Load<Shader>(DiscShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"PodBeacon: no shader Resources/{DiscShaderName}; the pods have no ground discs");
                return null;
            }
            var go = new GameObject("Ground Disc", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, DiscHeight, 0);
            go.transform.localScale = Vector3.one * DiscRadius;
            discMaterial = new Material(shader) { name = $"Ground Disc {name}" };
            discMaterial.SetColor("_Color", colour.linear);
            go.GetComponent<MeshFilter>().sharedMesh = DiscMesh();
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = discMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return go;
        }

        private void Update()
        {
            var on = available == null || available();
            if (column != null && column.activeSelf != on)
                column.SetActive(on);
            if (disc != null && disc.activeSelf != on)
                disc.SetActive(on);
        }

        private void OnDestroy()
        {
            if (discMaterial != null)
                Destroy(discMaterial);
        }

        /// <summary>The shared quad, flat on the ground, a metre out each way: the disc's transform scales it to its radius.</summary>
        private static Mesh DiscMesh()
        {
            if (discMesh != null)
                return discMesh;
            discMesh = new Mesh { name = "Pod Ground Disc" };
            discMesh.vertices = new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(1, 0, 1), new Vector3(-1, 0, 1) };
            discMesh.uv = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            discMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            discMesh.RecalculateBounds();
            discMesh.UploadMeshData(true);
            return discMesh;
        }
    }
}
