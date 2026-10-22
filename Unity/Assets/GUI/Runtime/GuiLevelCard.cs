using SdfCampaign;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SdfGui
{
    /// <summary>
    /// One arena's card on Level Select (ticket 32.5): its snapshot (<c>Resources/LevelCards/&lt;scene&gt;</c>, a coloured stand-in without
    /// one), name, best medal and time; a padlock and "Win the previous arena" when locked, "Coming soon" when its scene is not in the
    /// build; PLAY when it can be played.
    /// </summary>
    public sealed class GuiLevelCard : MonoBehaviour
    {
        public ToonShape standIn;
        public RawImage picture;
        public TMP_Text title;
        public TMP_Text info;
        public GameObject cover;
        public TMP_Text coverText;
        public GameObject padlock;
        public GuiCommand play;

        public const string CardFolder = "LevelCards/";

        private static readonly Color Bronze = GuiPalette.Hex(0xC9875A);
        private static readonly Color Silver = GuiPalette.Hex(0xD3D8E0);

        /// <summary>The stand-in's colour per arena, in campaign order.</summary>
        public static readonly Color[] StandIns =
        {
            GuiPalette.Moss, GuiPalette.Violet, GuiPalette.Paper, GuiPalette.Sky, GuiPalette.Chanterelle, GuiPalette.SporeDeep, GuiPalette.Dusk, GuiPalette.Amanita,
        };

        public void Show(CampaignStep step, int index, CampaignProgress progress, bool available)
        {
            var unlocked = available && progress.IsUnlocked(step.id);
            var texture = Resources.Load<Texture2D>(CardFolder + step.scene);
            picture.texture = texture;
            picture.enabled = texture != null;
            standIn.color = StandIns[index % StandIns.Length];
            GuiText.Set(title, $"{index + 1}  {step.title.ToUpperInvariant()}");

            var medal = progress.BestMedal(step.id);
            var record = progress.Level(step.id);
            if (!unlocked)
                GuiText.Set(info, "");
            else if (medal == Medal.None)
                GuiText.Set(info, "Not won yet");
            else
                GuiText.Set(info, $"<color=#{GuiPalette.HexOf(MedalColour(medal))}>{medal.ToString().ToUpperInvariant()}</color>  ·  {GuiText.Clock(record.bestSeconds)}");

            GuiText.Show(cover, !unlocked);
            GuiText.Show(padlock, available && !unlocked);
            GuiText.Set(coverText, available ? "Win the previous arena" : "Coming soon");
            GuiText.Show(play, unlocked);
            if (play != null)
                play.level = step.id;
        }

        public static Color MedalColour(Medal medal) => medal switch { Medal.Gold => GuiPalette.Gold, Medal.Silver => Silver, Medal.Bronze => Bronze, _ => GuiPalette.Paper };
    }
}
