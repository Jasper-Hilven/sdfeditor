using System;
using System.Collections.Generic;
using System.Text;

namespace SdfCampaign
{
    public enum CreditsKind { Title, Heading, Line, Small, Gap }

    /// <summary>One line of the credits; a <see cref="CreditsKind.Gap"/> has no text.</summary>
    public readonly struct CreditsLine
    {
        public readonly CreditsKind kind;
        public readonly string text;

        public CreditsLine(CreditsKind kind, string text = "")
        {
            this.kind = kind;
            this.text = text;
        }
    }

    /// <summary>
    /// What the credits say (ticket 32.8): the game's author, the cast, the tools, and the notices the fonts' open licence asks for
    /// (Fredoka and Lilita One, SIL Open Font License 1.1; the full texts sit next to the fonts in Assets/GUI/Fonts). Plain data, so a
    /// later credit is one more line here; <see cref="RichText"/> turns it into the one block of text the Credits screen scrolls.
    /// </summary>
    public static class CreditsRoll
    {
        public const string Author = "Jasper Hilven";

        public static readonly IReadOnlyList<CreditsLine> Lines = new[]
        {
            new CreditsLine(CreditsKind.Title, "MINIGUN MUSHROOMS: SECOND PLACE"),
            new CreditsLine(CreditsKind.Small, "Last year's champion. Probably still."),
            new CreditsLine(CreditsKind.Gap),
            new CreditsLine(CreditsKind.Heading, "A game by"),
            new CreditsLine(CreditsKind.Line, Author),
            new CreditsLine(CreditsKind.Gap),
            new CreditsLine(CreditsKind.Heading, "Starring"),
            new CreditsLine(CreditsKind.Line, "Mushroom 42, as themselves"),
            new CreditsLine(CreditsKind.Line, "Mushroom 7 and Mushroom 13"),
            new CreditsLine(CreditsKind.Line, "Mushroom 99, who joined late"),
            new CreditsLine(CreditsKind.Line, "Ref Puffball, as the referee"),
            new CreditsLine(CreditsKind.Line, "Chanterelle Prime, as himself, regrettably"),
            new CreditsLine(CreditsKind.Gap),
            new CreditsLine(CreditsKind.Heading, "Made with"),
            new CreditsLine(CreditsKind.Line, "Unity 6 and its Universal Render Pipeline"),
            new CreditsLine(CreditsKind.Line, "C# and .NET"),
            new CreditsLine(CreditsKind.Line, "JetBrains Rider"),
            new CreditsLine(CreditsKind.Line, "Claude, by Anthropic"),
            new CreditsLine(CreditsKind.Small, "Every mushroom, castle and chess piece is a signed distance function,"),
            new CreditsLine(CreditsKind.Small, "baked to a mesh before the game ever runs."),
            new CreditsLine(CreditsKind.Gap),
            new CreditsLine(CreditsKind.Heading, "Fonts"),
            new CreditsLine(CreditsKind.Line, "Fredoka"),
            new CreditsLine(CreditsKind.Small, "Copyright 2016 The Fredoka Project Authors"),
            new CreditsLine(CreditsKind.Small, "(https://github.com/hafontia/Fredoka-One)"),
            new CreditsLine(CreditsKind.Line, "Lilita One"),
            new CreditsLine(CreditsKind.Small, "Copyright (c) 2011 Juan Montoreano (juan@remolacha.biz),"),
            new CreditsLine(CreditsKind.Small, "with Reserved Font Name Lilita"),
            new CreditsLine(CreditsKind.Small, "Both fonts are licensed under the SIL Open Font License, Version 1.1,"),
            new CreditsLine(CreditsKind.Small, "available with a FAQ at https://openfontlicense.org"),
            new CreditsLine(CreditsKind.Gap),
            new CreditsLine(CreditsKind.Gap),
            new CreditsLine(CreditsKind.Heading, "Thanks for playing"),
            new CreditsLine(CreditsKind.Small, "No mushrooms were harmed. Several were sauteed."),
        };

        /// <summary>Whether the notices the fonts' licence asks for are all here (a core test checks it).</summary>
        public static bool HasFontNotices()
        {
            var all = new StringBuilder();
            foreach (var line in Lines) all.Append(line.text).Append('\n');
            var text = all.ToString();
            return text.Contains("Fredoka") && text.Contains("Lilita One") && text.Contains("Juan Montoreano") &&
                   text.Contains("The Fredoka Project Authors") && text.Contains("SIL Open Font License");
        }

        /// <summary>The credits as one block of TextMeshPro rich text, centred by the screen's text.</summary>
        public static string RichText(string gold = "#FFD54F", string cream = "#FFF2D9")
        {
            var sb = new StringBuilder();
            foreach (var line in Lines)
            {
                switch (line.kind)
                {
                    case CreditsKind.Title:
                        sb.Append($"<size=170%><color={gold}>{line.text}</color></size>");
                        break;
                    case CreditsKind.Heading:
                        sb.Append($"<size=125%><color={gold}>{line.text.ToUpperInvariant()}</color></size>");
                        break;
                    case CreditsKind.Line:
                        sb.Append($"<color={cream}>{line.text}</color>");
                        break;
                    case CreditsKind.Small:
                        sb.Append($"<size=70%><color={cream}>{line.text}</color></size>");
                        break;
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// The credits' scroll as plain timing (ticket 32.8): <see cref="Seconds"/> from start to end, and the same skip rule as every cutscene
    /// (holding Space, Escape or Start for 0.7 s, on the first viewing too). The screen reads <see cref="Offset"/> and places the text.
    /// </summary>
    public sealed class CreditsScroll
    {
        public const float Seconds = 45f;

        private readonly SkipHold skip = new SkipHold();
        private float time;

        /// <summary>How far the scroll has got, 0 (text below the screen) to 1 (text gone off the top).</summary>
        public float Progress => Skipped ? 1 : Math.Min(1, time / Seconds);

        public bool Done { get; private set; }

        public bool Skipped { get; private set; }

        public float SkipProgress => skip.Progress;

        public void Tick(float dt, bool skipHeld)
        {
            if (Done) return;
            if (skip.Tick(dt, skipHeld))
            {
                Skipped = true;
                Done = true;
                return;
            }
            time += dt;
            if (time >= Seconds) Done = true;
        }

        /// <summary>
        /// How far the text block has moved up from where its top starts at the viewport's bottom edge: from 0 to
        /// <c>viewport + content</c>, so the last line leaves at the top exactly when the time is up.
        /// </summary>
        public float Offset(float viewportHeight, float contentHeight) => Progress * (viewportHeight + contentHeight);
    }
}
