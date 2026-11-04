using UnityEngine;

namespace SdfGui
{
    /// <summary>Marks the root of a screen prefab, which <see cref="GuiFlow"/> turns on and off.</summary>
    public sealed class GuiScreenRoot : MonoBehaviour
    {
        public GuiScreen screen;
        [Tooltip("Shows whatever the screen, while the flow's test panel is on (the test panel itself)")]
        public bool alwaysOn;
        [Tooltip("The touch controls (ticket 56): shown over the HUD and the scoreboard on a touch device, and never elsewhere")]
        public bool touchLayer;
    }
}
