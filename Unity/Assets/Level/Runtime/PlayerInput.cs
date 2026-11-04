using System.Collections.Generic;
using SdfBattle;
using UnityEngine;

namespace SdfLevel
{
    /// <summary>
    /// One thing a player input source can say; every method is a no-op by default. A source answers for its own device only: the
    /// keyboard and mouse (<see cref="KeyboardMouseInput"/>) and, from ticket 56, the touch controls. <see cref="PlayerInput"/> adds the
    /// sources up, so a laptop with a touchscreen plays with both.
    /// </summary>
    public abstract class PlayerInputSource
    {
        /// <summary>Walking: x right, y forward, each -1..1.</summary>
        public virtual Vector2 Move => Vector2.zero;

        /// <summary>This frame's turn of the view, x right and y up, at sensitivity 1 (the controller scales it by its look sensitivity). Zero while the source is not in control (pointer not taken).</summary>
        public virtual Vector2 Look => Vector2.zero;

        public virtual bool FireHeld => false;
        public virtual bool ZoomHeld => false;
        public virtual bool JumpPressed => false;
        public virtual bool SlotPressed(int number) => false;

        /// <summary>The weapon scroll this frame: positive up (towards the previous weapon).</summary>
        public virtual float SlotScroll => 0;

        public virtual bool PowerPressed(SporePower power) => false;
        public virtual bool ReloadPressed => false;
        public virtual bool UsePressed => false;
        public virtual bool ScoreboardHeld => false;
        public virtual bool PausePressed => false;

        /// <summary>The pause key is down (the credits skip with it).</summary>
        public virtual bool PauseHeld => false;

        /// <summary>The skip control is held (Space, Enter or the pad's start; a touch hold).</summary>
        public virtual bool SkipHeld => false;

        public virtual bool HeadPreviousHeld => false;
        public virtual bool HeadNextHeld => false;

        /// <summary>The primary pointer (left button, a finger) is down, wherever it is.</summary>
        public virtual bool PointerHeld => false;

        /// <summary>The primary pointer went down this frame (a click or a tap).</summary>
        public virtual bool PointerPressed => false;

        // The spectator camera and the developer keys: keyboard and mouse only
        public virtual int SpectatePlayer => 0;
        public virtual int SpectateMachine => 0;
        public virtual bool SpectateFreeToggled => false;
        public virtual bool FlyLookHeld => false;
        public virtual Vector3 Fly => Vector3.zero;
        public virtual bool FlyFastHeld => false;
        public virtual bool DebugPressed(KeyCode key) => false;
    }

    /// <summary>
    /// What the game asks of the player, not which key (ticket 55): every gameplay reader asks here, and none reads
    /// <c>UnityEngine.Input</c> itself. The sources are added up (any source's press counts; the vectors add and clamp). The playing
    /// controls (walk, jump, weapon and power keys) do nothing while <see cref="GameKeys.Blocked"/> is set, a menu being over the game.
    /// </summary>
    public static class PlayerInput
    {
        static readonly KeyboardMouseInput keyboardMouse = new();
        static readonly List<PlayerInputSource> sources = new() { keyboardMouse };

        /// <summary>The sources in use, the keyboard and mouse first.</summary>
        public static IReadOnlyList<PlayerInputSource> Sources => sources;

        /// <summary>Adds a source (the touch controls); the same source twice counts once.</summary>
        public static void Add(PlayerInputSource source)
        {
            if (source != null && !sources.Contains(source))
                sources.Add(source);
        }

        public static void Remove(PlayerInputSource source)
        {
            if (source != null && source != keyboardMouse)
                sources.Remove(source);
        }

        /// <summary>
        /// The view is in the player's hands: the pointer is taken on a desktop; a touch device has no pointer to take, so always.
        /// </summary>
        public static bool Engaged => SdfPlatform.Device.IsTouch || Cursor.lockState == CursorLockMode.Locked;

        public static Vector2 Move
        {
            get
            {
                if (GameKeys.Blocked)
                    return Vector2.zero;
                var sum = Vector2.zero;
                foreach (var source in sources)
                    sum += source.Move;
                return Vector2.ClampMagnitude(sum, 1);
            }
        }

        /// <summary>This frame's turn of the view in degrees at sensitivity 1: x right, y up.</summary>
        public static Vector2 Look
        {
            get
            {
                var sum = Vector2.zero;
                foreach (var source in sources)
                    sum += source.Look;
                return sum;
            }
        }

        public static bool FireHeld => Any(s => s.FireHeld);
        public static bool ZoomHeld => Any(s => s.ZoomHeld);
        public static bool JumpPressed => !GameKeys.Blocked && Any(s => s.JumpPressed);

        /// <summary>The weapon slot key (1 to the number of slots) went down.</summary>
        public static bool SlotPressed(int number)
        {
            if (GameKeys.Blocked)
                return false;
            foreach (var source in sources)
                if (source.SlotPressed(number))
                    return true;
            return false;
        }

        public static float SlotScroll
        {
            get
            {
                var sum = 0f;
                foreach (var source in sources)
                    sum += source.SlotScroll;
                return sum;
            }
        }

        public static bool PowerPressed(SporePower power)
        {
            if (GameKeys.Blocked)
                return false;
            foreach (var source in sources)
                if (source.PowerPressed(power))
                    return true;
            return false;
        }

        public static bool ReloadPressed => Any(s => s.ReloadPressed);
        public static bool UsePressed => Any(s => s.UsePressed);
        public static bool ScoreboardHeld => Any(s => s.ScoreboardHeld);

        /// <summary>Escape or P went down: the pause menu (P by place, since CrazyGames advises against relying on Escape, which leaves fullscreen).</summary>
        public static bool PausePressed => Any(s => s.PausePressed);

        public static bool PauseHeld => Any(s => s.PauseHeld);
        public static bool SkipHeld => Any(s => s.SkipHeld);
        public static bool HeadPreviousHeld => Any(s => s.HeadPreviousHeld);
        public static bool HeadNextHeld => Any(s => s.HeadNextHeld);
        public static bool PointerHeld => Any(s => s.PointerHeld);
        public static bool PointerPressed => Any(s => s.PointerPressed);

        public static int SpectatePlayer => Sum(s => s.SpectatePlayer);
        public static int SpectateMachine => Sum(s => s.SpectateMachine);
        public static bool SpectateFreeToggled => Any(s => s.SpectateFreeToggled);
        public static bool FlyLookHeld => Any(s => s.FlyLookHeld);

        public static Vector3 Fly
        {
            get
            {
                var sum = Vector3.zero;
                foreach (var source in sources)
                    sum += source.Fly;
                return sum;
            }
        }

        public static bool FlyFastHeld => Any(s => s.FlyFastHeld);

        /// <summary>A developer key (the test keys, the effects bench) went down.</summary>
        public static bool DebugPressed(KeyCode key)
        {
            foreach (var source in sources)
                if (source.DebugPressed(key))
                    return true;
            return false;
        }

        static bool Any(System.Func<PlayerInputSource, bool> ask)
        {
            foreach (var source in sources)
                if (ask(source))
                    return true;
            return false;
        }

        static int Sum(System.Func<PlayerInputSource, int> ask)
        {
            var sum = 0;
            foreach (var source in sources)
                sum += ask(source);
            return sum;
        }
    }
}
