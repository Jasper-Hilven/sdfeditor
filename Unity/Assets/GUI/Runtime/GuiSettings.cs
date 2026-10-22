using UnityEngine;

namespace SdfGui
{
    /// <summary>The player's settings, kept in PlayerPrefs (the browser's storage in a web build).</summary>
    public static class GuiSettings
    {
        public static readonly string[] GoreLevels = { "Off", "Splatty", "Extra Splatty", "Ultra Gore" };

        /// <summary>
        /// The gore level, an index into <see cref="GoreLevels"/>: off (a killed scout just vanishes, no blood), splatty (the default: it
        /// bursts, its pieces fly, its team's blood splats about) extra splatty (more blood) or ultra gore (three times the blood, and every piece flies far, trailing blood). SdfBattle.Gore
        /// reads it at each death.
        /// </summary>
        public static int Gore
        {
            get => PlayerPrefs.GetInt("gui.gore", 1);
            set => PlayerPrefs.SetInt("gui.gore", value);
        }

        public static float Sensitivity
        {
            get => PlayerPrefs.GetFloat("gui.sensitivity", 0.5f);
            set => PlayerPrefs.SetFloat("gui.sensitivity", value);
        }

        public static float Volume
        {
            get => PlayerPrefs.GetFloat("gui.volume", 0.8f);
            set
            {
                PlayerPrefs.SetFloat("gui.volume", value);
                AudioListener.volume = value;
            }
        }

        public static bool ScreenShake
        {
            get => PlayerPrefs.GetInt("gui.shake", 1) == 1;
            set => PlayerPrefs.SetInt("gui.shake", value ? 1 : 0);
        }

        public static bool ShowFps
        {
            get => PlayerPrefs.GetInt("gui.fps", 0) == 1;
            set => PlayerPrefs.SetInt("gui.fps", value ? 1 : 0);
        }

        /// <summary>Whether the HUD keeps quiet about the frame rate: no hint to change the quality level (off by default).</summary>
        public static bool DismissQualityHints
        {
            get => PlayerPrefs.GetInt("gui.qualityHints.dismissed", 0) == 1;
            set => PlayerPrefs.SetInt("gui.qualityHints.dismissed", value ? 1 : 0);
        }
    }
}
