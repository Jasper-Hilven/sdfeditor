using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using InputKey = UnityEngine.InputSystem.Key;
#endif

namespace SdfLevel
{
    /// <summary>A key by its place on the keyboard, named as on a US keyboard: <see cref="Q"/> is the key right of Tab, whatever it says.</summary>
    public enum KeyPlace
    {
        W,
        A,
        S,
        D,
        Q,
        F,
        C,
    }

    /// <summary>
    /// The game's keys by their place on the keyboard, not by the letter printed on them (ticket 25), so every layout plays the same:
    /// on AZERTY you walk with ZQSD and sporulate with A, F and C; on QWERTZ and Dvorak likewise where the keys sit. Reads the Input
    /// System's keyboard, whose keys are places (on the web the browser's <c>KeyboardEvent.code</c>), when the project has it on
    /// (Project Settings > Player > Active Input Handling: Both); else the classic input manager's letters, as before.
    /// <see cref="Label"/> is what the key says in the player's layout, for the HUD.
    /// </summary>
    public static class GameKeys
    {
        /// <summary>Whether the key is held now.</summary>
        public static bool Held(KeyPlace place)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard[InputKeyOf(place)].isPressed;
#else
            return Input.GetKey(KeyCodeOf(place));
#endif
        }

        /// <summary>Whether the key went down this frame.</summary>
        public static bool Pressed(KeyPlace place)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard[InputKeyOf(place)].wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCodeOf(place));
#endif
        }

        /// <summary>What the key at this place says in the player's keyboard layout: "A" for <see cref="KeyPlace.Q"/> on AZERTY.</summary>
        public static string Label(KeyPlace place)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            var name = keyboard != null ? keyboard[InputKeyOf(place)].displayName : null;
            if (!string.IsNullOrEmpty(name))
                return name.ToUpperInvariant();
#endif
            return place.ToString();
        }

        /// <summary>
        /// The walking input: x right (D, or the right arrow) less left (A), y forward (W, or the up arrow) less back (S), each -1, 0
        /// or 1.
        /// </summary>
        public static Vector2 Walk()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return Vector2.zero;
            var x = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0);
            var y = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0);
            return new Vector2(x, y);
#else
            return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private static InputKey InputKeyOf(KeyPlace place) => place switch
        {
            KeyPlace.W => InputKey.W,
            KeyPlace.A => InputKey.A,
            KeyPlace.S => InputKey.S,
            KeyPlace.D => InputKey.D,
            KeyPlace.Q => InputKey.Q,
            KeyPlace.F => InputKey.F,
            _ => InputKey.C,
        };
#else
        private static KeyCode KeyCodeOf(KeyPlace place) => place switch
        {
            KeyPlace.W => KeyCode.W,
            KeyPlace.A => KeyCode.A,
            KeyPlace.S => KeyCode.S,
            KeyPlace.D => KeyCode.D,
            KeyPlace.Q => KeyCode.Q,
            KeyPlace.F => KeyCode.F,
            _ => KeyCode.C,
        };
#endif
    }
}
