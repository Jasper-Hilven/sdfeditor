using System;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>The kinds of player a bot leans to be (ticket 23); its <see cref="Temperament"/> starts from one and varies.</summary>
    public enum BotStyle
    {
        /// <summary>Goes for the enemy flag.</summary>
        Raider,
        /// <summary>Stays by its own flag and goes after whoever takes it.</summary>
        Guardian,
        /// <summary>Looks for fights.</summary>
        Hunter,
        /// <summary>Collects weapons and health, then fights.</summary>
        Scavenger,
        /// <summary>A bit of everything.</summary>
        AllRounder,
    }

    /// <summary>
    /// What a bot likes to do (ticket 23), as weights for its goals: attack the enemy flag, defend its own, chase the enemy carrier, hunt
    /// players, fetch weapons and health. No team plan: each bot of a team gets its own, so the team is a mix, and its goal comes from
    /// these weights and the situation (<see cref="BotBrain"/>).
    /// </summary>
    [Serializable]
    public struct Temperament
    {
        [Range(0, 2)] public float attack;
        [Range(0, 2)] public float defend;
        [Range(0, 2)] public float chase;
        [Range(0, 2)] public float hunt;
        [Range(0, 2)] public float fetch;

        public Temperament(float attack, float defend, float chase, float hunt, float fetch)
        {
            this.attack = attack;
            this.defend = defend;
            this.chase = chase;
            this.hunt = hunt;
            this.fetch = fetch;
        }

        /// <summary>A style's weights, before any variation.</summary>
        public static Temperament Of(BotStyle style) => style switch
        {
            BotStyle.Raider => new Temperament(1.4f, 0.3f, 0.7f, 0.6f, 0.5f),
            BotStyle.Guardian => new Temperament(0.4f, 1.4f, 1.2f, 0.5f, 0.5f),
            BotStyle.Hunter => new Temperament(0.6f, 0.4f, 1.0f, 1.4f, 0.6f),
            BotStyle.Scavenger => new Temperament(0.7f, 0.5f, 0.8f, 0.8f, 1.4f),
            _ => new Temperament(0.9f, 0.8f, 0.9f, 0.9f, 0.8f),
        };

        /// <summary>The styles a team's bots take in turn, so every team gets a mix.</summary>
        public static readonly BotStyle[] Rotation = { BotStyle.Raider, BotStyle.Guardian, BotStyle.Hunter, BotStyle.Scavenger, BotStyle.AllRounder };

        /// <summary>
        /// The style of a team's <paramref name="place"/>-th bot (0 first): the rotation from a random start per team, so the team is a
        /// mix that differs from match to match.
        /// </summary>
        public static BotStyle StyleFor(int place, int teamStart) => Rotation[((place + teamStart) % Rotation.Length + Rotation.Length) % Rotation.Length];

        /// <summary>A style's weights, each varied by up to <paramref name="variation"/> (0.25: ±25%).</summary>
        public static Temperament Varied(BotStyle style, System.Random random, float variation = 0.25f)
        {
            var t = Of(style);
            float Vary(float w) => w * (1 + variation * (float)(random.NextDouble() * 2 - 1));
            return new Temperament(Vary(t.attack), Vary(t.defend), Vary(t.chase), Vary(t.hunt), Vary(t.fetch));
        }

        public override string ToString() => $"attack {attack:0.0}, defend {defend:0.0}, chase {chase:0.0}, hunt {hunt:0.0}, fetch {fetch:0.0}";
    }
}
