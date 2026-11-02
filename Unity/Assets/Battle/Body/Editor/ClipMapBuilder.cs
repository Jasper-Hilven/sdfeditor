using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SdfBody.Editor
{
    /// <summary>Builds <c>SoldierClipMap.asset</c> from the catalog, the gaps and the valid Mixamo drops (ticket 03).</summary>
    public static class ClipMapBuilder
    {
        public const string AssetPath = SoldierClips.Folder + "SoldierClipMap.asset";

        /// <summary>The slots the map should have now (nothing is written).</summary>
        public static List<SoldierClipMap.Slot> Slots()
        {
            var slots = new List<SoldierClipMap.Slot>();
            foreach (var e in SoldierClips.All)
            {
                if (e.IsAlt)
                    continue;
                slots.Add(new SoldierClipMap.Slot { name = e.Name, clip = SoldierClipAssets.Load(e), standIn = "", holdAt = -1f, loop = e.Loop });
            }
            foreach (var g in SoldierClips.Gaps)
            {
                var drop = SoldierClips.MixamoFolder + g.Name + ".fbx";
                if (File.Exists(drop) && MixamoDrop.Validate(drop).Count == 0)
                {
                    var own = SoldierClipAssets.LoadAt(drop, g.Name);
                    if (own != null)
                    {
                        slots.Add(new SoldierClipMap.Slot
                        {
                            name = g.Name, clip = own, standIn = "", holdAt = -1f,
                            loop = AnimationUtility.GetAnimationClipSettings(own).loopTime,
                        });
                        continue;
                    }
                }
                var stand = SoldierClips.Find(g.StandIn) ?? throw new InvalidOperationException($"gap {g.Name}: stand-in {g.StandIn} is not in the catalog");
                slots.Add(new SoldierClipMap.Slot
                {
                    name = g.Name, clip = SoldierClipAssets.Load(stand), standIn = g.StandIn, holdAt = g.HoldAt,
                    loop = g.HoldAt < 0f && stand.Loop,
                });
            }
            return slots;
        }

        public static bool SameSlots(IReadOnlyList<SoldierClipMap.Slot> a, IReadOnlyList<SoldierClipMap.Slot> b)
        {
            if (a.Count != b.Count)
                return false;
            for (var i = 0; i < a.Count; i++)
                if (a[i].name != b[i].name || a[i].clip != b[i].clip || (a[i].standIn ?? "") != (b[i].standIn ?? "")
                    || !Mathf.Approximately(a[i].holdAt, b[i].holdAt) || a[i].loop != b[i].loop)
                    return false;
            return true;
        }

        [MenuItem("Sdf/Body/Build Clip Map")]
        public static SoldierClipMap Build()
        {
            var slots = Slots();
            var map = AssetDatabase.LoadAssetAtPath<SoldierClipMap>(AssetPath);
            if (map == null)
            {
                map = ScriptableObject.CreateInstance<SoldierClipMap>();
                map.SetSlots(slots);
                AssetDatabase.CreateAsset(map, AssetPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"SoldierClipMap: created with {slots.Count} slots");
            }
            else if (!SameSlots(map.Slots, slots))
            {
                map.SetSlots(slots);
                EditorUtility.SetDirty(map);
                AssetDatabase.SaveAssets();
                Debug.Log($"SoldierClipMap: updated, {slots.Count} slots");
            }
            else
                Debug.Log($"SoldierClipMap: unchanged, {slots.Count} slots");
            return map;
        }
    }
}
