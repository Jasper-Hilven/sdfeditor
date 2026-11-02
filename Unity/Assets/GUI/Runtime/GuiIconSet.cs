using System;
using UnityEngine;

namespace SdfGui
{
    /// <summary>
    /// The GUI's icons by name: renders of the models (monster heads, weapons) in their toon look, made by GuiIcons in the editor
    /// (Tools > SDF GUI > Render Icons), so they always match the game.
    /// </summary>
    [CreateAssetMenu(menuName = "SDF GUI/Icon Set")]
    public sealed class GuiIconSet : ScriptableObject
    {
        [Serializable]
        public struct Icon
        {
            public string name;
            public Sprite sprite;
        }

        public Icon[] icons = Array.Empty<Icon>();

        /// <summary>The monster heads' icons, which a player without a head of their own falls back on, by <see cref="ViewPlayer.avatar"/>.</summary>
        public string[] avatars = { "yeti", "vampire", "leprechaun", "swamp", "cyclops", "cthulhu", "alien", "mummy", "werewolf", "pumpkin", "frankenstein", "santa" };

        public Sprite Get(string iconName)
        {
            foreach (var icon in icons)
                if (icon.name == iconName)
                    return icon.sprite;
            return null;
        }

        public Sprite Avatar(int index) => avatars.Length == 0 ? null : Get(avatars[Mathf.Abs(index) % avatars.Length]);

        public Sprite Weapon(int index) => index >= 0 && index < MatchView.Weapons.Length ? Get(MatchView.Weapons[index]) : null;
    }
}
