using System;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// The one data asset of combat (ticket 22), Assets/Battle/Resources/Weapons.asset: every weapon's numbers (<see cref="table"/>, the
    /// balance, tuned here and not in code) and how each is drawn (<see cref="looks"/>: its model, where its muzzle and grip are, where
    /// it sits in the first-person view, its tracer), plus the effects' material and the goop models of the blob gun. The editor makes
    /// the asset with the first balance when it is missing (Tools > SDF Battle > Reset Weapon Table); the game loads it from Resources,
    /// and falls back to <see cref="WeaponTable.Default"/> without it.
    /// </summary>
    [CreateAssetMenu(menuName = "SDF Battle/Weapon Table")]
    public sealed class WeaponTableAsset : ScriptableObject
    {
        /// <summary>Where it lives under a Resources folder, without the extension.</summary>
        public const string ResourceName = "Weapons";

        /// <summary>How a weapon is drawn; the model's coordinates are the prefab's (Unity's: x mirrored from the .sdf).</summary>
        [Serializable]
        public sealed class Look
        {
            [Tooltip("The weapon's id (its model's name)")]
            public string id = "";
            [Tooltip("The weapon's prefab, Assets/Mushrooms/<id>.prefab")]
            public GameObject model;
            [Tooltip("The end of the barrel in the model, where shots and effects leave")]
            public Vector3 muzzle;
            [Tooltip("Where a hand holds it in the model")]
            public Vector3 grip;
            [Tooltip("First person: where the grip is in the camera's space, at full size; the view model is this scaled by viewScale")]
            public Vector3 viewGrip = new Vector3(0.2f, -0.22f, 0.35f);
            [Tooltip("First person: degrees the weapon turns in towards the middle of the view")]
            public float viewYaw = -4;
            [Tooltip("Third person: its size in a scout's hand")]
            public float handScale = 0.85f;

            [Header("Tracer (bullet weapons)")]
            public Color tracer = new Color(1f, 0.55f, 0.1f);
            public float tracerWidth = 0.05f;
            public float tracerLength = 1.4f;
            public float tracerSpeed = 110;
            [Tooltip("Seconds a trail along the shot's line lasts; 0 for none")]
            public float tracerTrail;
            public float flashSize = 0.09f;
        }

        public WeaponTable table = WeaponTable.Default();

        public Look[] looks = DefaultLooks();

        [Tooltip("First person: the view model's size against the weapon's own, so it sits within the player's capsule and never reaches into a wall")]
        public float viewScale = 0.3f;

        [Header("Effects")]
        [Tooltip("The effects' material (Assets/Effects/Materials/Effect.mat)")]
        public Material effectMaterial;
        [Tooltip("The blob gun's gobs in flight (goop-blob prefabs)")]
        public GameObject[] blobModels = Array.Empty<GameObject>();
        [Tooltip("The blob gun's splats on the ground (goop-splat prefabs)")]
        public GameObject[] splatModels = Array.Empty<GameObject>();

        private static WeaponTableAsset loaded;

        /// <summary>The asset in Resources, or null when there is none.</summary>
        public static WeaponTableAsset Load()
        {
            if (loaded == null)
                loaded = Resources.Load<WeaponTableAsset>(ResourceName);
            return loaded;
        }

        /// <summary>A weapon's look; dual pistols are drawn as pistols. Null when there is none.</summary>
        public Look LookOf(string id)
        {
            var model = WeaponIds.ModelOf(id);
            if (looks != null)
                foreach (var look in looks)
                    if (look != null && look.id == model)
                        return look;
            return null;
        }

        /// <summary>
        /// The looks the asset starts with, without the models (the editor sets those): muzzles and grips measured from the .sdf files
        /// in content/models/weapons, tracer colours from the special-effects bench (ticket 8).
        /// </summary>
        public static Look[] DefaultLooks() => new[]
        {
            new Look
            {
                id = WeaponIds.Pistol, muzzle = new Vector3(0, 0.128f, 0.105f), grip = new Vector3(0, 0.052f, -0.058f),
                viewGrip = new Vector3(0.17f, -0.2f, 0.36f), tracer = new Color(1f, 0.55f, 0.1f), tracerWidth = 0.05f, tracerLength = 1.4f, tracerSpeed = 110,
            },
            new Look
            {
                id = WeaponIds.Minigun, muzzle = new Vector3(0, 0.135f, 1.0f), grip = new Vector3(0, 0.1f, -0.45f),
                viewGrip = new Vector3(0.2f, -0.36f, 0.25f), viewYaw = -3, handScale = 0.7f, tracer = new Color(1f, 0.3f, 0.05f), tracerWidth = 0.045f,
                tracerLength = 2.2f, tracerSpeed = 140, flashSize = 0.13f,
            },
            new Look
            {
                id = WeaponIds.Sniper, muzzle = new Vector3(0, 0.172f, 0.69f), grip = new Vector3(0, 0.1f, -0.15f),
                viewGrip = new Vector3(0.17f, -0.24f, 0.3f), viewYaw = -2, handScale = 0.8f, tracer = new Color(0.3f, 0.7f, 1f), tracerWidth = 0.065f,
                tracerLength = 6, tracerSpeed = 320, tracerTrail = 0.7f, flashSize = 0.12f,
            },
            new Look
            {
                id = WeaponIds.ElectroGun, muzzle = new Vector3(0, 0.13f, 0.34f), grip = new Vector3(0, 0.055f, -0.152f),
                viewGrip = new Vector3(0.18f, -0.22f, 0.32f), tracer = new Color(0.25f, 0.75f, 1f),
            },
            new Look
            {
                id = WeaponIds.Flamethrower, muzzle = new Vector3(0, 0.17f, 0.53f), grip = new Vector3(0, 0.11f, -0.11f),
                viewGrip = new Vector3(0.18f, -0.28f, 0.3f), tracer = new Color(1f, 0.45f, 0.08f),
            },
            new Look
            {
                id = WeaponIds.PoisonGun, muzzle = new Vector3(0, 0.125f, 0.34f), grip = new Vector3(0, 0.06f, -0.14f),
                viewGrip = new Vector3(0.18f, -0.24f, 0.32f), tracer = new Color(0.5f, 0.9f, 0.2f),
            },
            new Look
            {
                id = WeaponIds.BlobGun, muzzle = new Vector3(0, 0.135f, 0.25f), grip = new Vector3(0, 0.055f, -0.1f),
                viewGrip = new Vector3(0.18f, -0.24f, 0.33f), tracer = new Color(0.85f, 0.18f, 0.65f),
            },
        };
    }
}
