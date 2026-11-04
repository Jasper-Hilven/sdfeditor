using System;
using System.Collections.Generic;
using UnityEngine;

namespace SdfGui
{
    /// <summary>
    /// A screen's layout for a touch device (ticket 57): the menus are built for a mouse, and a thumb needs buttons about 64 px tall at
    /// 800 x 450 (154 in the 1920 x 1080 layout). Each entry moves, resizes or scales one rect, or hides it, while a touch device is
    /// in use; off a touch device (or when the device changes) the built layout is put back, so the desktop is as built.
    /// </summary>
    public sealed class GuiTouchLayout : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            public RectTransform target;
            [Tooltip("Not shown on a touch device (a keyboard-only row or hint)")]
            public bool hide;
            public bool move;
            public Vector2 position;
            public bool resize;
            public Vector2 size;
            [Tooltip("Scale on a touch device; 1 leaves it")]
            public float scale;
        }

        /// <summary>The height a thumb-sized button has in the 1920 x 1080 layout: 64 px at 800 x 450.</summary>
        public const float ThumbHeight = 154;

        public Entry[] entries = new Entry[0];

        private struct Home
        {
            public Vector2 position, size;
            public Vector3 scale;
            public bool active;
        }

        private readonly Dictionary<RectTransform, Home> homes = new Dictionary<RectTransform, Home>();
        private bool applied;

        private void Awake() => Capture();

        private void OnEnable() => Apply();

        private void Capture()
        {
            if (homes.Count > 0)
                return;
            foreach (var e in entries)
                if (e.target != null && !homes.ContainsKey(e.target))
                    homes[e.target] = new Home { position = e.target.anchoredPosition, size = e.target.sizeDelta, scale = e.target.localScale, active = e.target.gameObject.activeSelf };
        }

        /// <summary>Lays the screen out for the device in use now.</summary>
        public void Apply()
        {
            Capture();
            var touch = SdfPlatform.Device.IsTouch;
            if (!touch && !applied)
                return;
            applied = touch;
            foreach (var e in entries)
            {
                if (e.target == null || !homes.TryGetValue(e.target, out var home))
                    continue;
                if (e.hide)
                    e.target.gameObject.SetActive(touch ? false : home.active);
                e.target.anchoredPosition = touch && e.move ? e.position : home.position;
                e.target.sizeDelta = touch && e.resize ? e.size : home.size;
                e.target.localScale = touch && e.scale > 0 && e.scale != 1 ? home.scale * e.scale : home.scale;
            }
        }
    }
}
