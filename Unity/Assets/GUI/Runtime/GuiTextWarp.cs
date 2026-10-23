using TMPro;
using UnityEngine;

namespace SdfGui
{
    /// <summary>
    /// Lettering for logos: bends a text's letters along an arch and gives each a little tilt and bounce, by moving the vertices of the
    /// text's mesh once when the mesh is built (no per-frame work). Put the same settings on the layers of one logo word so they stay together.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(TMP_Text))]
    public sealed class GuiTextWarp : MonoBehaviour
    {
        [Tooltip("How far the ends of the text sit below its middle, in canvas units")]
        public float arch = 24;
        [Tooltip("The tilt of the outermost letters, degrees, leaning away from the middle")]
        public float archTilt = 7;
        [Tooltip("How far each letter bounces off the baseline, canvas units")]
        public float bounce = 5;
        [Tooltip("The extra tilt of each letter, alternating, degrees")]
        public float wobble = 2.5f;

        private TMP_Text text;

        private void OnEnable()
        {
            text = GetComponent<TMP_Text>();
            text.OnPreRenderText += Warp;
            text.ForceMeshUpdate();
        }

        private void OnDisable()
        {
            if (text != null)
                text.OnPreRenderText -= Warp;
        }

        private void OnValidate()
        {
            if (text != null)
                text.ForceMeshUpdate();
        }

        private void Warp(TMP_TextInfo info)
        {
            if (info.characterCount == 0)
                return;
            float min = float.MaxValue, max = float.MinValue;
            for (var i = 0; i < info.characterCount; i++)
            {
                if (!info.characterInfo[i].isVisible)
                    continue;
                min = Mathf.Min(min, info.characterInfo[i].bottomLeft.x);
                max = Mathf.Max(max, info.characterInfo[i].topRight.x);
            }
            if (max <= min)
                return;
            var middle = (min + max) * 0.5f;
            var half = (max - min) * 0.5f;
            var step = 0;
            for (var i = 0; i < info.characterCount; i++)
            {
                var character = info.characterInfo[i];
                if (!character.isVisible)
                    continue;
                var vertices = info.meshInfo[character.materialReferenceIndex].vertices;
                var first = character.vertexIndex;
                var centre = new Vector3((character.bottomLeft.x + character.topRight.x) * 0.5f, (character.baseLine + character.ascender) * 0.5f, 0);
                var n = (centre.x - middle) / half;
                var angle = -n * archTilt + (step % 2 == 0 ? wobble : -wobble);
                var rise = -arch * n * n + bounce * Mathf.Sin(step * 2.1f + 0.6f);
                var rotation = Quaternion.Euler(0, 0, angle);
                for (var v = 0; v < 4; v++)
                    vertices[first + v] = rotation * (vertices[first + v] - centre) + centre + new Vector3(0, rise, 0);
                step++;
            }
        }
    }
}
