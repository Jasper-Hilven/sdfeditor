using SdfCampaign;
using UnityEngine;
using UnityEngine.UI;

namespace SdfGui
{
    /// <summary>
    /// The settings: the render quality (the project's quality levels: ultra high, high, integrated GPU / web and ultra low), the gore
    /// level, the campaign's difficulty (ticket 32.5; saved with the campaign, so only where there is one), mouse sensitivity, volume,
    /// screen shake, the FPS counter and dismissing the HUD's frame rate / quality hints. Shows the current values in the editor too.
    /// </summary>
    [ExecuteAlways]
    public sealed class GuiSettingsMenu : MonoBehaviour
    {
        public GuiSelector quality;
        public GuiSelector gore;
        public GuiSelector difficulty;
        [Tooltip("The menus' backdrop: Auto, the 3D desk or the flat wall of panels (ticket 35)")]
        public GuiSelector backdrop;
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
                quality.Changed += i => QualitySettings.SetQualityLevel(QualityLevel(i), true);
            if (gore != null)
                gore.Changed += i => GuiSettings.Gore = i;
            if (backdrop != null)
                backdrop.Changed += i => GuiSettings.MenuBackdrop = i;
            if (difficulty != null)
                difficulty.Changed += i => GuiCampaign.Progress.SetDifficulty((Difficulty)i);
            sensitivity?.onValueChanged.AddListener(v => GuiSettings.Sensitivity = v);
            volume?.onValueChanged.AddListener(v => GuiSettings.Volume = v);
            screenShake?.onValueChanged.AddListener(v => GuiSettings.ScreenShake = v);
            showFps?.onValueChanged.AddListener(v => GuiSettings.ShowFps = v);
            dismissQualityHints?.onValueChanged.AddListener(v => GuiSettings.DismissQualityHints = v);
        }

        /// <summary>
        /// The quality level for a menu row and back (the same flip): the project lists its levels best first, which the saved quality index
        /// and the code reading it rely on, so only the menu runs them from low to high.
        /// </summary>
        private static int QualityLevel(int index) => QualitySettings.names.Length - 1 - index;

        private void OnDisable()
        {
            if (Application.isPlaying)
                GuiSettings.Save();
        }

        private void OnEnable()
        {
            if (quality != null)
            {
                var names = QualitySettings.names;
                System.Array.Reverse(names);
                quality.options = names;
                quality.Set(QualityLevel(QualitySettings.GetQualityLevel()));
            }
            if (gore != null)
            {
                gore.options = GuiSettings.GoreLevels;
                gore.Set(GuiSettings.Gore);
            }
            if (backdrop != null)
            {
                backdrop.options = BackdropChoice.Names;
                backdrop.Set(GuiSettings.MenuBackdrop);
            }
            if (difficulty != null)
            {
                difficulty.options = new[] { "Easy", "Normal", "Hard" };
                difficulty.Set((int)GuiCampaign.Progress.Difficulty);
                // A scene played alone has no campaign to keep it in; the editor shows the row
                GuiText.Show(difficulty.transform.parent, !Application.isPlaying || CampaignRunner.Instance != null);
            }
            sensitivity?.SetValueWithoutNotify(GuiSettings.Sensitivity);
            volume?.SetValueWithoutNotify(GuiSettings.Volume);
            screenShake?.SetIsOnWithoutNotify(GuiSettings.ScreenShake);
            showFps?.SetIsOnWithoutNotify(GuiSettings.ShowFps);
            dismissQualityHints?.SetIsOnWithoutNotify(GuiSettings.DismissQualityHints);
        }
    }
}
