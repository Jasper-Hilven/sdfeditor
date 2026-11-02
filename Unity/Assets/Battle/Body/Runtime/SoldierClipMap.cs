using System;
using System.Collections.Generic;
using UnityEngine;

namespace SdfBody
{
    /// <summary>
    /// The soldiers' clips by convention name (ticket 03), what 05, 06, 16 and 17 read: every clip copied from medievalminiguns (not the
    /// <c>_alt</c> duplicates) and every gap of <see cref="SoldierClips.Gaps"/>, the gap resolved to its Mixamo download when one is in
    /// <see cref="SoldierClips.MixamoFolder"/> and to its stand-in until then. Built by <c>ClipMapBuilder.Build</c> (editor), never by hand:
    /// <c>Assets/Battle/Body/Animations/SoldierClipMap.asset</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "Sdf/Body Clip Map", fileName = "SoldierClipMap")]
    public sealed class SoldierClipMap : ScriptableObject
    {
        [Serializable]
        public struct Slot
        {
            /// <summary>The convention name asked for (<c>soldier_crouch_idle</c>).</summary>
            public string name;

            /// <summary>The clip that plays: its own, or the stand-in's.</summary>
            public AnimationClip clip;

            /// <summary>Empty when <see cref="clip"/> is the slot's own clip; else the name of the clip standing in.</summary>
            public string standIn;

            /// <summary>Below 0: play <see cref="clip"/>; else hold it at this normalized time (a pose, e.g. the jump's fall for freefall).</summary>
            public float holdAt;

            /// <summary>Whether <see cref="clip"/> loops.</summary>
            public bool loop;
        }

        [SerializeField] private List<Slot> slots = new List<Slot>();

        private Dictionary<string, int> _index;

        public IReadOnlyList<Slot> Slots => slots;

        /// <summary>The clip that plays for <paramref name="name"/>, or null for a name the map does not have.</summary>
        public AnimationClip Get(string name) => TryGet(name, out var s) ? s.clip : null;

        /// <summary>The slot of <paramref name="name"/>: its clip, stand-in and hold time.</summary>
        public bool TryGet(string name, out Slot slot)
        {
            if (_index == null || _index.Count != slots.Count)
            {
                _index = new Dictionary<string, int>();
                for (var i = 0; i < slots.Count; i++)
                    _index[slots[i].name] = i;
            }
            if (name != null && _index.TryGetValue(name, out var at))
            {
                slot = slots[at];
                return true;
            }
            slot = default;
            return false;
        }

        /// <summary>Whether <paramref name="name"/> plays a stand-in (its own clip is not downloaded yet).</summary>
        public bool IsStandIn(string name) => TryGet(name, out var s) && !string.IsNullOrEmpty(s.standIn);

        /// <summary>Replaces every slot (the editor's builder).</summary>
        public void SetSlots(IEnumerable<Slot> value)
        {
            slots = new List<Slot>(value);
            _index = null;
        }
    }
}
