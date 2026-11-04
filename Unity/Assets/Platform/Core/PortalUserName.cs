using System.Text;

namespace SdfPlatform
{
    /// <summary>
    /// The portal's username made fit for the game's GUI (ticket 65). CrazyGames usernames can hold any Unicode and be long; the GUI fonts
    /// (Fredoka, Lilita One) have the basic Latin set and Latin-1 letters only (anything else shows a box), and TextMeshPro reads
    /// <c>&lt;</c> as rich text. So: characters the fonts lack are dropped, spaces collapsed, the rest cut to the HUD's length.
    /// </summary>
    public static class PortalUserName
    {
        /// <summary>Punctuation that both fonts have and that is harmless in rich text.</summary>
        private const string Punctuation = " _-.'!?+*~()[]@#&,:=";

        /// <summary>The name as the game shows it, at most <paramref name="maxLength"/> characters; empty when nothing usable is left (or <paramref name="raw"/> is null).</summary>
        public static string Clean(string raw, int maxLength)
        {
            if (string.IsNullOrEmpty(raw) || maxLength <= 0)
                return "";
            var text = new StringBuilder(raw.Length);
            foreach (var c in raw)
            {
                if (c == ' ' || c == '\t' || c == ' ')
                {
                    if (text.Length > 0 && text[text.Length - 1] != ' ')
                        text.Append(' ');
                }
                else if (Allowed(c))
                    text.Append(c);
            }
            var name = text.ToString().Trim();
            if (name.Length > maxLength)
                name = name.Substring(0, maxLength).TrimEnd();
            // A name of punctuation alone ("..." or "__") is no name
            foreach (var c in name)
                if (char.IsLetterOrDigit(c))
                    return name;
            return "";
        }

        private static bool Allowed(char c)
        {
            if (c < 0x80)
                return char.IsLetterOrDigit(c) || Punctuation.IndexOf(c) >= 0;
            // Latin-1 letters (not the multiplication and division signs)
            return c >= 0xC0 && c <= 0xFF && c != 0xD7 && c != 0xF7;
        }
    }
}
