using UnityEngine;
using UnityEngine.UI;

namespace SdfGui
{
    /// <summary>
    /// The settings: the render quality (the project's quality levels: the high preset and the integrated GPU / web preset), the gore
    /// level, mouse sensitivity, volume, screen shake and the FPS counter. Shows the current values in the editor too.
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
        }
    }
}
