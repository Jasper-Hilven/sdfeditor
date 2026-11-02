using SdfBody;

namespace SdfBattle
{
    /// <summary>
    /// What the soldier body does for each weapon (ticket 44), pure: how the weapon fires on the body (a single shot restarted each time,
    /// or the looped burst) and which clip set the locomotion plays (the stance). The animation itself is <see cref="SoldierAnimator"/>.
    /// </summary>
    public static class BodyAnimationRules
    {
        /// <summary>
        /// Whether the body fires the weapon as a loop (the burst clip while the trigger is held): the minigun, the flamethrower and the
        /// electro-gun. The sniper, the pistols, the poison-gun and the blob-gun fire single shots, each restarting the shot clip.
        /// </summary>
        public static bool AutomaticFire(string weapon) => weapon switch
        {
            WeaponIds.Minigun or WeaponIds.Flamethrower or WeaponIds.ElectroGun => true,
            _ => false,
        };

        /// <summary>The clip set of the weapon in hand: the pistols hold the pistol's, the minigun and the flamethrower the heavy weapon's, the rest the rifle's.</summary>
        public static LocomotionClips.Stance StanceOf(string weapon) => weapon switch
        {
            WeaponIds.Pistol or WeaponIds.DualPistols => LocomotionClips.Stance.Pistol,
            WeaponIds.Minigun or WeaponIds.Flamethrower => LocomotionClips.Stance.Heavy,
            _ => LocomotionClips.Stance.Rifle,
        };

        /// <summary>
        /// Whether a weapon animates the body each frame the trigger is held, not per shot: the flamethrower has no shots, only a cone
        /// that burns while held.
        /// </summary>
        public static bool FiresWhileHeld(string weapon) => weapon == WeaponIds.Flamethrower;
    }
}
