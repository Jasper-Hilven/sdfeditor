using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// The soldier's arms in first person (ticket 53): where each weapon's gloved hands hold it. A glove is a fist round a vertical grip
    /// (<c>soldier-glove-red</c> and <c>-blue</c>, a right hand; the left hand is it mirrored) placed in the weapon's own model space at
    /// full size and turned by Euler degrees; the sleeve (<c>soldier-sleeve-*</c>) runs from its wrist back to the shoulder, aimed each
    /// frame. A weapon with a support pose is held with two hands (dual pistols: one hand on each pistol, the same pose mirrored).
    /// </summary>
    public static class FirstPersonArms
    {
        /// <summary>One hand on a weapon: the fist's centre in the weapon's model space, its turn in degrees, its size against the model.</summary>
        public readonly struct Hand
        {
            public readonly Vector3 position;
            public readonly Vector3 euler;
            public readonly float size;
            public readonly bool left;

            public Hand(Vector3 position, Vector3 euler, float size, bool left = false)
            {
                this.position = position;
                this.euler = euler;
                this.size = size;
                this.left = left;
            }
        }

        /// <summary>Where the glove's wrist is, in the glove's own space: the sleeve starts here.</summary>
        public static readonly Vector3 Wrist = new Vector3(0.026f, 0, -0.1f);

        /// <summary>Where the arm's shoulder is in the camera's space at full size, for the right arm and the left (mirrored).</summary>
        public static readonly Vector3 Shoulder = new Vector3(0.24f, -0.46f, -0.2f);

        /// <summary>A weapon's primary hand (the pistol's, either pistol's of the pair).</summary>
        public static Hand Primary(string weapon)
        {
            switch (WeaponIds.ModelOf(weapon))
            {
                case WeaponIds.Pistol: return new Hand(new Vector3(0, 0.04f, -0.045f), new Vector3(14, 0, 0), 0.85f);
                case WeaponIds.Minigun: return new Hand(new Vector3(0.075f, 0.115f, -0.48f), Vector3.zero, 1.0f);
                case WeaponIds.Sniper: return new Hand(new Vector3(0, 0.085f, -0.15f), new Vector3(20, 0, 0), 1.1f);
                case WeaponIds.ElectroGun: return new Hand(new Vector3(0, 0.04f, -0.152f), new Vector3(10, 0, 0), 0.9f);
                case WeaponIds.Flamethrower: return new Hand(new Vector3(0, 0.07f, -0.115f), Vector3.zero, 1.0f);
                case WeaponIds.PoisonGun: return new Hand(new Vector3(0, 0.04f, -0.14f), new Vector3(10, 0, 0), 0.9f);
                case WeaponIds.BlobGun: return new Hand(new Vector3(0, 0.04f, -0.1f), new Vector3(10, 0, 0), 0.9f);
                default: return new Hand(Vector3.zero, Vector3.zero, 0.7f);
            }
        }

        /// <summary>The left hand's hold of a weapon held with two hands; null for the one-handed pistol, dual pistols and the sniper's bolt.</summary>
        public static Hand? Support(string weapon)
        {
            switch (WeaponIds.ModelOf(weapon))
            {
                case WeaponIds.Minigun: return new Hand(new Vector3(-0.075f, 0.115f, -0.48f), Vector3.zero, 1.0f, true);
                case WeaponIds.Sniper: return new Hand(new Vector3(0, 0.1f, 0.12f), new Vector3(0, 0, 90), 1.0f, true);
                case WeaponIds.ElectroGun: return new Hand(new Vector3(0, 0.12f, 0.1f), new Vector3(0, 0, 90), 0.9f, true);
                case WeaponIds.Flamethrower: return new Hand(new Vector3(0, 0.17f, 0.3f), new Vector3(0, 0, 90), 0.9f, true);
                case WeaponIds.PoisonGun: return new Hand(new Vector3(0, 0.1f, 0.12f), new Vector3(0, 0, 90), 0.9f, true);
                case WeaponIds.BlobGun: return new Hand(new Vector3(0, 0.1f, 0.1f), new Vector3(0, 0, 90), 0.9f, true);
                default: return null;
            }
        }
    }
}
