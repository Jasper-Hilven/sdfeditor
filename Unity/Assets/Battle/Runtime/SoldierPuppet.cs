using SdfBody;
using SdfHeads;
using SdfLevel;
using SdfRig;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// The soldier drawn for a player (ticket 44), you or a bot: the team's skin prefab (<see cref="SoldierModels"/>) at the player's feet,
    /// turning with its yaw, played by a <see cref="SoldierAnimator"/> that this feeds every frame from the player's real movement: the
    /// velocity in the body's frame (the locomotion tree, its playback matched to the game's speeds: the flag carrier's 0.9, Frenzy's
    /// 1.6), off the ground or jumping, and the clip set of the weapon in hand (<see cref="BodyAnimationRules"/>). <see cref="Arsenal"/>
    /// reports the shots and <see cref="Combat"/> the hits and the death. Purely visual: the hit capsules of the prefab are removed (the
    /// player's own capsule is what shots hit). Your own body is hidden in first person (the death camera shows it), a bot's is drawn.
    /// <para>
    /// On death the body holds its last pose (<see cref="SoldierAnimator.Died"/>): ticket 51's ragdoll starts on
    /// <see cref="SoldierAnimator.WasKilled"/>.
    /// </para>
    /// </summary>
    public sealed class SoldierPuppet : MonoBehaviour
    {
        /// <summary>Seconds off the ground before the body is in the air for the animation (a step down a slope is not a fall).</summary>
        public const float AirDelay = 0.1f;

        /// <summary>The upward speed above which leaving the ground is a jump.</summary>
        public const float JumpSpeed = 2f;

        public SoldierAnimator Animator { get; private set; }

        /// <summary>The monster head on its head socket (ticket 45), null when the heads were not imported.</summary>
        public HeadWear Head { get; private set; }

        private Player player;
        private FirstPersonController controller;
        private CharacterController capsule;
        private Arsenal arsenal;
        private Renderer[] renderers;
        private bool visible = true;
        private bool wasGrounded = true;
        private float airSeconds;

        /// <summary>The skin key of a team: red or blue.</summary>
        public static string SkinOf(Team team) => team == Team.Red ? SoldierModels.RedSkin : SoldierModels.BlueSkin;

        /// <summary>
        /// The player's soldier, or null when the soldier models were not imported (a fresh clone before `sdf unity` and the skin build;
        /// <see cref="ScoutBody"/> is drawn then). Call it once the player is bound to its place in the match.
        /// </summary>
        public static SoldierPuppet Create(Player player)
        {
            var prefab = SoldierModels.Prefab(SkinOf(player.Team));
            if (prefab == null)
                return null;
            var controller = player.GetComponent<FirstPersonController>();
            var instance = Instantiate(prefab, player.transform, false);
            instance.name = "Body";
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            // Only the player's capsule is hit or collides; the prefab's hit capsules (ticket 45 sizes the head's) must not push the controller
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                Destroy(collider);
            var puppet = instance.AddComponent<SoldierPuppet>();
            // Ticket 45's stand-in: a random modelled head. Ticket 47 replaces this one line with the head the player chose or the bot drew.
            puppet.Head = HeadWear.Attach(instance.transform, MonsterHeads.RandomBuiltId(count => Random.Range(0, count)));
            puppet.player = player;
            puppet.controller = controller;
            puppet.capsule = player.GetComponent<CharacterController>();
            puppet.renderers = instance.GetComponentsInChildren<Renderer>(true);
            puppet.Animator = SoldierAnimator.Attach(instance);
            puppet.Animator.AlwaysAnimate = true;
            puppet.SetVisible(!player.IsYou);
            return puppet;
        }

        /// <summary>Draws the body or not (your own is hidden in first person).</summary>
        public void SetVisible(bool value)
        {
            visible = value;
            if (renderers == null)
                return;
            foreach (var renderer in renderers)
                if (renderer != null)
                    renderer.enabled = value;
        }

        public bool Visible => visible;

        /// <summary>The weapon sockets of the hands: the right hand's grip, then the left's (dual pistols); null entries when missing.</summary>
        public Transform[] Hands() => new[] { SoldierBody.FindSocket(transform, "grip"), SoldierBody.FindSocket(transform, "support") };

        /// <summary>Back on its feet after a death (the player appears again).</summary>
        public void Revive()
        {
            wasGrounded = true;
            airSeconds = 0;
            Animator.Revive();
        }

        private void Update()
        {
            if (Animator == null || Animator.IsDead || player == null || controller == null)
                return;
            if (arsenal == null)
                arsenal = player.GetComponent<Arsenal>();
            if (arsenal != null && arsenal.Inventory != null)
                Animator.SetStance(BodyAnimationRules.StanceOf(arsenal.Inventory.CurrentWeapon));

            var velocity = capsule != null && capsule.enabled ? capsule.velocity : Vector3.zero;
            // The animation sees the walk, not the push of gravity or a jump; a body held in place (the countdown) stands
            Animator.SetMove(controller.frozen ? Vector2.zero : LocomotionMath.BodyVelocity(velocity, transform.rotation));

            var grounded = controller.IsGrounded;
            if (wasGrounded && !grounded && velocity.y > JumpSpeed)
                Animator.Jump();
            wasGrounded = grounded;
            airSeconds = grounded ? 0 : airSeconds + Time.deltaTime;
            Animator.SetAirborne(airSeconds >= AirDelay);
        }
    }
}
