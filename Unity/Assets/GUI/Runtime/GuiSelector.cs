using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SdfGui
{
    /// <summary>Picks one of a few options with arrows on either side, like a quality or gore setting.</summary>
    public sealed class GuiSelector : MonoBehaviour
    {
        public Button previous;
        public Button next;
        public TMP_Text label;
        public string[] options = { "Low", "Medium", "High" };
        public int index;

        public event System.Action<int> Changed;

        private void Awake()
        {
            if (previous != null)
                previous.onClick.AddListener(() => Step(-1));
            if (next != null)
                next.onClick.AddListener(() => Step(1));
        }

        public void Set(int value, bool notify = false)
        {
            if (options.Length == 0)
                return;
            index = (value % options.Length + options.Length) % options.Length;
            GuiText.Set(label, options[index]);
            if (notify)
                Changed?.Invoke(index);
        }

        private void Step(int by) => Set(index + by, true);

        private void OnValidate() => Set(index);
    }
}
