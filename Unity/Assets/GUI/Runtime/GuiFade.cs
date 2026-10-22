using UnityEngine;
using UnityEngine.UI;

namespace SdfGui
{
    /// <summary>
    /// The black that scene changes fade through (ticket 32.4): a full-screen image on top of the GUI's other screens. It draws and
    /// blocks clicks only while it is not clear; the sound follows it.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class GuiFade : MonoBehaviour
    {
        [Range(0, 1)] public float alpha;

        private Image image;

        /// <summary>0 clear, 1 black; the master volume goes the other way.</summary>
        public float Alpha
        {
            get => alpha;
            set
            {
                alpha = Mathf.Clamp01(value);
                Apply();
            }
        }

        private void OnEnable() => Apply();
        private void OnValidate() => Apply();

        private void Apply()
        {
            if (image == null)
                image = GetComponent<Image>();
            image.color = new Color(0, 0, 0, alpha);
            image.enabled = alpha > 0.001f;
            image.raycastTarget = alpha > 0.001f;
            if (Application.isPlaying)
                AudioListener.volume = 1 - alpha;
        }
    }
}
