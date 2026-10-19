using System;
using UnityEngine;

namespace SdfGui
{
    /// <summary>
    /// The GUI's icons by name: renders of the models (characters, weapons) in their toon look, made by GuiIcons in the editor
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

        /// <summary>The avatars the fake players are drawn with, by <see cref="FakePlayer.avatar"/>.</summary>
        public string[] avatars = { "amanita-scout", "chanterelle-scout" };

        public Sprite Get(string iconName)
        {
            foreach (var icon in icons)
                if (icon.name == iconName)
                    return icon.sprite;
            return null;
        }

        public Sprite Avatar(int index) => avatars.Length == 0 ? null : Get(avatars[Mathf.Abs(index) % avatars.Length]);

        public Sprite Weapon(int index) => index >= 0 && index < FakeMatch.Weapons.Length ? Get(FakeMatch.Weapons[index]) : null;
    }
}
