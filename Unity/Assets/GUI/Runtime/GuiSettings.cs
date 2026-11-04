using UnityEngine;

namespace SdfGui
{
    /// <summary>The player's settings, kept in PlayerPrefs (the browser's storage in a web build).</summary>
    public static class GuiSettings
    {
        public static readonly string[] GoreLevels = { "Off", "Splatty", "Extra Splatty", "Ultra Gore" };

        /// <summary>
        /// Writes the settings out. A web build writes PlayerPrefs to the browser's storage only on Save, so the settings menu calls it when it
        /// closes rather than at every slider step.
        /// </summary>
        public static void Save() => PlayerPrefs.Save();

        /// <summary>
        /// The gore level, an index into <see cref="GoreLevels"/>: off (a killed monster's head pops in a puff of its blood colour, no pieces, no blood), splatty (the default: the
        /// head bursts into pieces and its own coloured blood splats about) extra splatty (more blood) or ultra gore (three times the blood, and every piece flies far, trailing blood). The body never bursts or bleeds at any level, it ragdolls. SdfBattle.Gore
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

        /// <summary>How far a swipe turns the view on a touch device, 0 to 1 (ticket 56); the middle is half a turn across the screen (<c>SdfBattle.TouchRules</c>).</summary>
        public static float TouchLook
        {
            get => PlayerPrefs.GetFloat("gui.touchLook", SdfBattle.TouchRules.DefaultLook);
            set => PlayerPrefs.SetFloat("gui.touchLook", value);
        }

        /// <summary>Whether the touch controls fire by themselves while the crosshair is on an enemy (on by default).</summary>
        public static bool AutoFire
        {
            get => PlayerPrefs.GetInt("gui.autoFire", 1) == 1;
            set => PlayerPrefs.SetInt("gui.autoFire", value ? 1 : 0);
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

        private static float music = -1, sound = -1;

        /// <summary>The music's volume, 0 to 1, on top of the master <see cref="Volume"/> (SdfAudio.MusicDirector reads it).</summary>
        public static float MusicVolume
        {
            get => music >= 0 ? music : (music = PlayerPrefs.GetFloat("gui.musicVolume", 1));
            set
            {
                music = value;
                PlayerPrefs.SetFloat("gui.musicVolume", value);
            }
        }

        /// <summary>The sound effects' volume, 0 to 1, on top of the master <see cref="Volume"/> (SdfAudio.AudioPlayer applies it to each sound as it starts).</summary>
        public static float SoundVolume
        {
            get => sound >= 0 ? sound : (sound = PlayerPrefs.GetFloat("gui.soundVolume", 1));
            set
            {
                sound = value;
                PlayerPrefs.SetFloat("gui.soundVolume", value);
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

        /// <summary>Whether the quality level stays put: no automatic change of quality level and no hint to change it (off by default).</summary>
        public static bool DismissQualityHints
        {
            get => PlayerPrefs.GetInt("gui.qualityHints.dismissed", 0) == 1;
            set => PlayerPrefs.SetInt("gui.qualityHints.dismissed", value ? 1 : 0);
        }
    }
}
