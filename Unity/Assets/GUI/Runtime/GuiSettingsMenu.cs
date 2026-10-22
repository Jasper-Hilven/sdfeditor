using UnityEngine;
using UnityEngine.UI;

namespace SdfGui
{
    /// <summary>
    /// The settings: the render quality (the project's quality levels: ultra high, high, integrated GPU / web and ultra low), the gore
    /// level, mouse sensitivity, volume, screen shake, the FPS counter and dismissing the HUD's frame rate / quality hints. Shows the
    /// current values in the editor too.
    /// </summary>
    [ExecuteAlways]
    public sealed class GuiSettingsMenu : MonoBehaviour
    {
        public GuiSelector quality;
        public GuiSelector gore;
        public Slider sensitivity;
        public Slider volume;
        public Toggle screenShake;
        public Toggle showFps;
        public Toggle dismissQualityHints;

        private void Awake()
        {
            if (!Application.isPlaying)
                return;
            if (quality != null)
                quality.Changed += i => QualitySettings.SetQualityLevel(i, true);
            if (gore != null)
                gore.Changed += i => GuiSettings.Gore = i;
            sensitivity?.onValueChanged.AddListener(v => GuiSettings.Sensitivity = v);
            volume?.onValueChanged.AddListener(v => GuiSettings.Volume = v);
            screenShake?.onValueChanged.AddListener(v => GuiSettings.ScreenShake = v);
            showFps?.onValueChanged.AddListener(v => GuiSettings.ShowFps = v);
            dismissQualityHints?.onValueChanged.AddListener(v => GuiSettings.DismissQualityHints = v);
        }

        private void OnEnable()
        {
            if (quality != null)
            {
                quality.options = QualitySettings.names;
                quality.Set(QualitySettings.GetQualityLevel());
            }
            if (gore != null)
            {
                gore.options = GuiSettings.GoreLevels;
                gore.Set(GuiSettings.Gore);
            }
            sensitivity?.SetValueWithoutNotify(GuiSettings.Sensitivity);
            volume?.SetValueWithoutNotify(GuiSettings.Volume);
            screenShake?.SetIsOnWithoutNotify(GuiSettings.ScreenShake);
            showFps?.SetIsOnWithoutNotify(GuiSettings.ShowFps);
            dismissQualityHints?.SetIsOnWithoutNotify(GuiSettings.DismissQualityHints);
        }
    }
}
