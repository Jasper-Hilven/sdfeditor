using TMPro;
using UnityEngine;

namespace SdfGui
{
    /// <summary>The yes/no card (ticket 32.5): writes the question <see cref="GuiFlow.Ask"/> was given.</summary>
    [ExecuteAlways]
    public sealed class GuiConfirm : MonoBehaviour
    {
        public TMP_Text question;

        private void OnEnable() => Refresh();

        private void Update() => Refresh();

        private void Refresh()
        {
            var flow = GuiFlow.Find(this);
            if (flow != null)
                GuiText.Set(question, flow.ConfirmQuestion);
        }
    }
}
