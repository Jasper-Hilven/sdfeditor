using SdfBattle;
using UnityEditor;
using UnityEngine;

namespace SdfLevel.Editor
{
    /// <summary>
    /// Puts a <see cref="Spinner"/> on the parts of the level's placed models that turn (ticket 30.5): every object under the root that
    /// <see cref="SpinRules"/> knows by name (a case fan's Blades, the cooler's FanWest and FanEast, a graphics card's Fan0 to Fan2, and the
    /// objects of their coarser levels of detail). They lose their static flags, since a batched object does not move.
    /// </summary>
    public static class LevelSpinners
    {
        /// <summary>Adds the spinners below <paramref name="root"/>, after it has been marked static; returns how many.</summary>
        public static int Add(Transform root)
        {
            var count = 0;
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (!SpinRules.TryFind(transform.name, out var rule) || transform.GetComponent<MeshFilter>() == null || transform.GetComponent<Spinner>() != null)
                    continue;
                transform.gameObject.AddComponent<Spinner>().Apply(rule);
                // The ink outline is a child that turns along; none of it may be batched
                foreach (var part in transform.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(part.gameObject, 0);
                count++;
            }
            return count;
        }
    }
}
