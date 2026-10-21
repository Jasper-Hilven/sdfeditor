using SdfLevel;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// A player's weapons in the scene (ticket 22), on every <see cref="Player"/>: the weapon in hand from the player's
    /// <see cref="Inventory"/>, its trigger (<see cref="WeaponTrigger"/>: draw time, rate, the minigun's spin-up, the spread), the
    /// sniper's zoom, and the drawn weapon (<see cref="WeaponView"/>). Shots leave from the eye along the aim: the camera for you, the
    /// scout's floating eyes for a bot; <see cref="Combat"/> finds what they hit.
    /// <para>
    /// You drive it with the mouse and keys (left button fires, right button zooms the sniper, 1-4 and the scroll wheel switch); a bot's
    /// brain (ticket 23) drives the same API: <see cref="SetTrigger"/>, <see cref="SetZoom"/>, <see cref="Select"/>.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(10)]
    public sealed class Arsenal : MonoBehaviour
    {
        [Tooltip("Degrees a second the view zooms in and out")]
        public float zoomSpeed = 200;
        [Tooltip("The look sensitivity's share while zoomed in")]
        public float zoomedSensitivity = 0.35f;

        private readonly WeaponTrigger trigger = new WeaponTrigger();
        private Combat combat;
        private Camera view;
        private float baseFieldOfView = 60;
        private float baseSensitivity;
        private string equipped;
        private bool triggerHeld;
        private bool zoomHeld;
        private Transform[] eyes;

        public Player Player { get; private set; }

        public WeaponView View { get; private set; }

        /// <summary>The weapon in hand's numbers; null before the match starts.</summary>
        public WeaponStats Weapon => trigger.Stats;

        public WeaponTrigger Trigger => trigger;

        public Inventory Inventory => combat != null && Player.State != null ? combat.State.InventoryOf(Player.State) : null;

        /// <summary>Zoomed in with the sniper (fully or on the way).</summary>
        public bool Zoomed { get; private set; }

        /// <summary>The trigger is held and the weapon is firing (or, the minigun, spinning up) this frame.</summary>
        public bool Firing { get; private set; }

        /// <summary>Where shots leave from: your camera, or a bot's eyes.</summary>
        public Vector3 EyePosition
        {
            get
            {
                if (view != null)
                    return view.transform.position;
                if (eyes != null && eyes.Length == 2 && eyes[0] != null && eyes[1] != null && eyes[0].gameObject.activeInHierarchy)
                    return (eyes[0].position + eyes[1].position) * 0.5f;
                return transform.position + Vector3.up * 1.5f;
            }
        }

        /// <summary>Where shots go: the camera's forward for you, the controller's aim for a bot.</summary>
        public Vector3 AimDirection => view != null ? view.transform.forward : Player.Controller.AimDirection;

        /// <summary>The muzzle shots and effects leave from now (the one that fires next, with two pistols).</summary>
        public Transform Muzzle => View != null ? View.Muzzle : null;

        // ---------------------------------------------------------------- The API (bots call it; your input feeds it)

        /// <summary>Holds the trigger down or lets it go.</summary>
        public void SetTrigger(bool held) => triggerHeld = held;

        /// <summary>Zooms in (the sniper's right mouse button) or out.</summary>
        public void SetZoom(bool held) => zoomHeld = held;

        /// <summary>Takes the weapon in a slot in hand; false when the slot is empty.</summary>
        public bool Select(WeaponSlot slot) => Inventory != null && Inventory.Select(slot);

        // ---------------------------------------------------------------- Set up

        /// <summary>Ties the arsenal to its player and the match's combat; <see cref="Combat"/> calls it once for every player.</summary>
        public void Bind(Combat owner, Player player)
        {
            combat = owner;
            Player = player;
            if (player.body != null)
                eyes = new[] { FindPart(player.body.transform, "eye.0"), FindPart(player.body.transform, "eye.1") };
            if (player.IsYou)
            {
                view = player.GetComponentInChildren<Camera>();
                if (view != null)
                    baseFieldOfView = view.fieldOfView;
                baseSensitivity = player.Controller.lookSensitivity;
            }
            View = WeaponView.Create(this, owner.Assets, view);
        }

        private static Transform FindPart(Transform root, string part)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == part)
                    return t;
            return null;
        }

        /// <summary>Back to the start of a life: trigger let go, zoom out, the weapon in hand drawn anew.</summary>
        public void ResetLife()
        {
            triggerHeld = false;
            zoomHeld = false;
            equipped = null;
            Zoomed = false;
            Firing = false;
            if (view != null)
            {
                view.fieldOfView = baseFieldOfView;
                Player.Controller.lookSensitivity = baseSensitivity;
            }
        }

        // ---------------------------------------------------------------- Every frame

        private void Update()
        {
            if (combat == null || Player.State == null)
                return;
            var state = Player.State;
            if (Player.IsYou)
                ReadInput();

            var inventory = Inventory;
            if (equipped != inventory.CurrentWeapon)
                Equip(inventory.CurrentWeapon);
            var stats = trigger.Stats;
            if (stats == null)
                return;

            var alive = state.Alive;
            var canFire = alive && combat.Match.State.CanMove && (!Player.IsYou || Cursor.lockState == CursorLockMode.Locked);
            var held = triggerHeld && canFire;
            UpdateZoom(alive && zoomHeld && stats.zoomFieldOfView > 0);

            var shots = trigger.Tick(Time.deltaTime, held);
            Firing = held && trigger.SpinFraction >= 1 && !trigger.Overheated;
            if (shots > 0)
            {
                combat.State.Fired(state);
                var eye = EyePosition;
                var aim = AimDirection;
                for (var i = 0; i < shots; i++)
                {
                    combat.Fire(this, eye, aim, trigger.Spread(Zoomed));
                    View?.Kick();
                }
            }
            if (stats.mode == FireMode.Cone && held && Muzzle != null)
                combat.Fx.Flame(Muzzle, AimDirection, combat.FlameLength(EyePosition, AimDirection, stats.range), Time.deltaTime, Player.IsYou);
            View?.Spin(stats.spinUp > 0 ? trigger.SpinFraction : 0);
        }

        private void ReadInput()
        {
            var locked = Cursor.lockState == CursorLockMode.Locked;
            triggerHeld = locked && Input.GetMouseButton(0);
            zoomHeld = locked && Input.GetMouseButton(1);
            var inventory = Inventory;
            for (var number = 1; number <= Inventory.SlotCount; number++)
                if (Input.GetKeyDown(KeyCode.Alpha0 + number) || Input.GetKeyDown(KeyCode.Keypad0 + number))
                    inventory.SelectNumber(number);
            var scroll = Input.mouseScrollDelta.y;
            if (locked && Mathf.Abs(scroll) > 0.01f)
                inventory.Scroll(scroll < 0 ? 1 : -1);
        }

        private void Equip(string weapon)
        {
            equipped = weapon;
            trigger.Equip(combat.Table.Get(weapon));
            View?.Show(weapon);
        }

        /// <summary>The sniper's zoom for you: the field of view narrows and the mouse slows; a bot's zoom only steadies its aim.</summary>
        private void UpdateZoom(bool wanted)
        {
            var stats = trigger.Stats;
            if (view == null)
            {
                Zoomed = wanted;
                return;
            }
            // At rest, follow the sensitivity the settings give
            if (!wanted && view.fieldOfView >= baseFieldOfView)
            {
                baseSensitivity = Player.Controller.lookSensitivity;
                Zoomed = false;
                View?.Hide(false);
                return;
            }
            var target = wanted ? stats.zoomFieldOfView : baseFieldOfView;
            view.fieldOfView = Mathf.MoveTowards(view.fieldOfView, target, zoomSpeed * Time.deltaTime);
            // Steady once most of the way in
            Zoomed = wanted && view.fieldOfView <= Mathf.Lerp(baseFieldOfView, stats.zoomFieldOfView, 0.8f);
            // The mouse slows with the view's narrowing, so a turn looks the same on screen
            Player.Controller.lookSensitivity = baseSensitivity * Mathf.Clamp(view.fieldOfView / baseFieldOfView, zoomedSensitivity, 1);
            View?.Hide(Zoomed);
        }
    }
}
