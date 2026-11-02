using SdfCampaign;
using SdfHeads;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SdfGui
{
    /// <summary>
    /// The settings: the render quality (the project's quality levels: ultra high, high, integrated GPU / web and ultra low), the gore
    /// level, mouse sensitivity, the master, music and sound volumes,
    /// screen shake, the FPS counter and disabling the automatic quality adjustment (with its hints). Shows the current values in the editor too.
    /// </summary>
    [ExecuteAlways]
    public sealed class GuiSettingsMenu : MonoBehaviour
    {
        public GuiSelector quality;
        public GuiSelector gore;
        [Tooltip("The head you wear: a cycle over the heads you have unlocked (ticket 47)")]
        public GuiSelector head;
        [Tooltip("The head's name and month under the preview")]
        public TMP_Text headCaption;
        public GuiHeadPreview headPreview;

        private System.Collections.Generic.List<string> headIds = new System.Collections.Generic.List<string>();
        public Slider sensitivity;
        public Slider volume;
        [Tooltip("The music's volume, on top of the master volume")]
        public Slider music;
        [Tooltip("The sound effects' volume, on top of the master volume")]
        public Slider sound;
        public Toggle screenShake;
        public Toggle showFps;
        public Toggle dismissQualityHints;

        private void Awake()
        {
            if (!Application.isPlaying)
                return;
            if (quality != null)
                quality.Changed += i => QualitySettings.SetQualityLevel(QualityLevel(i), true);
            if (head != null)
                head.Changed += i => PickHead(i);
            if (gore != null)
                gore.Changed += i => GuiSettings.Gore = i;
            sensitivity?.onValueChanged.AddListener(v => GuiSettings.Sensitivity = v);
            volume?.onValueChanged.AddListener(v => GuiSettings.Volume = v);
            music?.onValueChanged.AddListener(v => GuiSettings.MusicVolume = v);
            sound?.onValueChanged.AddListener(v => GuiSettings.SoundVolume = v);
            screenShake?.onValueChanged.AddListener(v => GuiSettings.ScreenShake = v);
            showFps?.onValueChanged.AddListener(v => GuiSettings.ShowFps = v);
            dismissQualityHints?.onValueChanged.AddListener(v => GuiSettings.DismissQualityHints = v);
        }

        /// <summary>
        /// The quality level for a menu row and back (the same flip): the project lists its levels best first, which the saved quality index
        /// and the code reading it rely on, so only the menu runs them from low to high.
        /// </summary>
        private static int QualityLevel(int index) => QualitySettings.names.Length - 1 - index;

        private static readonly string[] Months =
            { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };

        private void PickHead(int index)
        {
            var loop = GameLoop.Instance;
            if (loop != null && index >= 0 && index < headIds.Count)
                loop.SetHead(headIds[index]);
            ShowHead();
        }

        // The cycle lists the heads you can wear in calendar order; a head not modelled yet says so (it shows as a stand-in)
        private void FillHeads()
        {
            var loop = GameLoop.Instance;
            if (head == null || loop == null)
                return;
            headIds = loop.AvailableHeads;
            var labels = new string[headIds.Count];
            for (var i = 0; i < labels.Length; i++)
                labels[i] = HeadStandIn.Label(headIds[i]);
            head.options = labels;
            head.Set(Mathf.Max(0, headIds.IndexOf(loop.CurrentHead)));
            ShowHead();
        }

        private void ShowHead()
        {
            var loop = GameLoop.Instance;
            if (loop == null)
                return;
            var entry = MonsterHeads.ById(loop.CurrentHead);
            if (headCaption != null && entry != null)
                GuiText.Set(headCaption, Months[entry.Value.Month - 1] + (entry.Value.Built ? "" : "  (model to come)"));
            if (headPreview != null)
                headPreview.Show(loop.CurrentHead, loop.YourTeam == SdfBattle.Team.Blue ? SdfRig.SoldierModels.BlueSkin : SdfRig.SoldierModels.RedSkin);
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
                GuiSettings.Save();
        }

        private void OnEnable()
        {
            if (Application.isPlaying)
                FillHeads();
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
            sensitivity?.SetValueWithoutNotify(GuiSettings.Sensitivity);
            volume?.SetValueWithoutNotify(GuiSettings.Volume);
            music?.SetValueWithoutNotify(GuiSettings.MusicVolume);
            sound?.SetValueWithoutNotify(GuiSettings.SoundVolume);
            screenShake?.SetIsOnWithoutNotify(GuiSettings.ScreenShake);
            showFps?.SetIsOnWithoutNotify(GuiSettings.ShowFps);
            dismissQualityHints?.SetIsOnWithoutNotify(GuiSettings.DismissQualityHints);
        }
    }
}
