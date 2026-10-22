using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SdfGui
{
    public enum MatchPhase
    {
        Countdown,
        Playing,
        Over,
    }

    [Serializable]
    public sealed class ViewPlayer
    {
        public string name;
        public Color color;
        [Tooltip("Index into the avatar icons")]
        public int avatar;
        public int kills;
        public bool alive = true;
        public bool you;
        [Tooltip("Place at the end of the round, 1 for the winner; 0 while alive")]
        public int placement;
    }

    [Serializable]
    public struct ViewKill
    {
        public int killer;
        [Tooltip("The killer's name (rich text) when it is no player: a machine (battle scenes); empty for a player")]
        public string killerName;
        public int victim;
        public int weapon;
        public bool headshot;
        [Tooltip("The feed's line (SdfBattle.DeathLines: {killer}, {victim}, {icon} where the weapon's icon sits); empty for the plain " +
                 "names around the icon")]
        public string line;
        [Tooltip("The killer's kills in a row, for a streak's line")]
        public int streak;
        [Tooltip("Seconds since it happened; the kill feed fades it out")]
        public float age;
    }

    /// <summary>
    /// The state of a round as the GUI shows it, and, in the GUI scene, the bots that play it for the showcase: everything the GUI
    /// shows. The GUI's views only read it (in the editor too, they are [ExecuteAlways]), so what the scene shows when opened is exactly
    /// what the first frame at runtime shows; only in play mode does the showcase round move on (bots eliminate each other, the clock
    /// runs) and do the transient effects (hit markers, damage, pop-ups) fade.
    /// <para>
    /// The player acts through <see cref="Fire"/>, <see cref="Reload"/> and the test commands (<see cref="GuiCommandId"/>); the bots
    /// through <see cref="Update"/>. The pause menu stops the time (<see cref="GuiFlow"/>), so the round stops with it.
    /// </para>
    /// <para>
    /// In a battle scene it is the HUD's view of the real match instead (<see cref="driven"/>): SdfBattle.MatchHud writes the match
    /// into it every frame and it does nothing of its own.
    /// </para>
    /// </summary>
    public sealed class MatchView : MonoBehaviour
    {
        /// <summary>Players in the showcase round (the scoreboard has a row for each): you and seven bots, 4 against 4.</summary>
        public const int RosterSize = 8;

        /// <summary>Rows on the scoreboard: room for the larger teams of the campaign (up to 5 against 5).</summary>
        public const int ScoreboardRows = 10;

        /// <summary>Prefab names of the weapons, which are also the names of their icons.</summary>
        public static readonly string[] Weapons = { "minigun", "pistol", "sniper", "flamethrower", "poison-gun", "electro-gun", "blob-gun" };

        public static readonly string[] WeaponNames = { "Minigun", "Pistol", "Sniper", "Flamethrower", "Poison Gun", "Electro Gun", "Blob Gun" };

        [Header("Round")]
        public MatchPhase phase = MatchPhase.Playing;
        public float roundSeconds = 180;
        public float timeLeft = 134;
        [Tooltip("The arena and the goal, the scoreboard's subtitle")]
        public string arena = "Meadow";
        public string goal = "First to 3 captures";
        [Tooltip("Seconds left of the countdown, or of the wait for the next round once it is over")]
        public float countdown;

        [Tooltip("How the match ended (the banner, Victory and Defeat); filled by the real match, or sampled for the showcase")]
        public ViewEnd end = new ViewEnd();

        [Header("Players")]
        public List<ViewPlayer> players = new List<ViewPlayer>();
        public int you;
        [Tooltip("Who eliminated you, -1 while you are alive")]
        public int killedBy = -1;
        public int killedByWeapon;
        [Tooltip("Who eliminated you when it is no player (rich text: a machine in its team's colour, battle scenes); empty otherwise")]
        public string killedByName;
        [Tooltip("The death screen's title for how you died (SdfBattle.DeathLines); empty for the plain one")]
        public string killedTitle;
        [Tooltip("The death screen's lines under it ({killer}, {weapon}, {streak}); empty for the plain ones")]
        public string killedDetail;
        public int killedStreak;
        [Tooltip("Seconds until you are back, while you are out in a battle (0 shows no wait)")]
        public float respawnIn;
        [Tooltip("Who the camera follows once you are out")]
        public int spectating = -1;
        public List<ViewKill> feed = new List<ViewKill>();

        [Header("You")]
        public float health = 72;
        public float maxHealth = 100;
        public int ammo = 164;
        public int magazine = 250;
        public int weapon;
        [Tooltip("0 when not reloading, else how far the reload is")]
        [Range(0, 1)] public float reload;
        [Tooltip("What the pick-up prompt offers; empty hides it")]
        public string pickup = "";

        [Header("Effects (fade by themselves)")]
        [Tooltip("1 right after firing, the crosshair's spread")]
        [Range(0, 1)] public float spread;
        [Range(0, 1)] public float hitMarker;
        public bool hitMarkerKill;
        [Range(0, 1)] public float damage;
        [Tooltip("Where the last hit came from, degrees clockwise from straight ahead")]
        public float damageAngle;
        [Tooltip("Seconds the elimination pop-up still shows")]
        public float eliminated;
        public int eliminatedPlayer;
        public bool eliminatedHeadshot;
        [Tooltip("The pop-up's title (SdfBattle.DeathLines); empty for the plain one")]
        public string eliminatedTitle;

        [Header("Bots")]
        [Tooltip("Bots eliminate each other and now and then hit you")]
        public bool autoEvents = true;
        [Tooltip("Written by a real match every frame (SdfBattle.MatchHud, in the battle scenes): nothing here moves on by itself, and " +
                 "firing, reloading and the test commands do nothing")]
        public bool driven;
        public Vector2 secondsBetweenKills = new Vector2(3, 8);

        private float nextKill;
        private float nextShot;
        private float fireCarry;

        // The bots wait as they would between moves before their first one, so the first frame of play is the scene as saved
        private void Start()
        {
            nextKill = Time.time + Random.Range(secondsBetweenKills.x, secondsBetweenKills.y);
            nextShot = Time.time + Random.Range(5f, 12f);
        }

        public ViewPlayer You => players.Count > you ? players[you] : null;
        public int Alive => players.Count(p => p.alive);
        public bool YouAreAlive => You == null || You.alive;

        public ViewPlayer Player(int index) => index >= 0 && index < players.Count ? players[index] : null;

        /// <summary>The roster a new match starts with: you and the cast, red against blue, each with a colour and an avatar.</summary>
        public static List<ViewPlayer> NewRoster(string yourName)
        {
            var roster = new List<ViewPlayer> { new ViewPlayer { name = yourName, you = true } };
            for (var i = 1; i < RosterSize; i++)
            {
                var team = i < RosterSize / 2 ? SdfBattle.Team.Red : SdfBattle.Team.Blue;
                roster.Add(new ViewPlayer { name = SdfBattle.Cast.Name(team, team == SdfBattle.Team.Red ? i + 1 : i - RosterSize / 2 + 1) });
            }
            for (var i = 0; i < roster.Count; i++)
            {
                roster[i].color = GuiPalette.Players[i % GuiPalette.Players.Length];
                roster[i].avatar = i < RosterSize / 2 ? 0 : 1;
            }
            return roster;
        }

        private void Update()
        {
            if (!Application.isPlaying || driven)
                return;
            var dt = Time.deltaTime;
            Fade(dt);
            if (phase == MatchPhase.Playing)
                Play(dt);
        }

        private void Fade(float dt)
        {
            spread = Mathf.MoveTowards(spread, 0, dt * 2.5f);
            hitMarker = Mathf.MoveTowards(hitMarker, 0, dt * 4);
            damage = Mathf.MoveTowards(damage, 0, dt * 1.2f);
            eliminated = Mathf.Max(0, eliminated - dt);
            for (var i = 0; i < feed.Count; i++)
            {
                var kill = feed[i];
                kill.age += dt;
                feed[i] = kill;
            }
            feed.RemoveAll(k => k.age > 8);
            if (reload > 0)
            {
                reload += dt / 1.6f;
                if (reload >= 1)
                {
                    reload = 0;
                    ammo = magazine;
                }
            }
        }

        private void Play(float dt)
        {
            timeLeft = Mathf.Max(0, timeLeft - dt);
            if (!autoEvents)
                return;
            if (Time.time >= nextKill)
            {
                nextKill = Time.time + Random.Range(secondsBetweenKills.x, secondsBetweenKills.y);
                BotKill();
            }
            if (YouAreAlive && Time.time >= nextShot)
            {
                nextShot = Time.time + Random.Range(5f, 12f);
                Damage(Random.Range(8, 22));
            }
        }

        // ---------------------------------------------------------------- Phases

        /// <summary>Starts the round now: everyone in, full health and ammo, the clock full.</summary>
        public void StartPlaying()
        {
            if (players.Count == 0)
                players = NewRoster(SdfBattle.Cast.You);
            foreach (var player in players)
            {
                player.alive = true;
                player.kills = 0;
                player.placement = 0;
            }
            phase = MatchPhase.Playing;
            end.Clear();
            timeLeft = roundSeconds;
            health = maxHealth;
            ammo = magazine;
            reload = 0;
            killedBy = -1;
            spectating = -1;
            feed.Clear();
            nextKill = Time.time + 2;
            nextShot = Time.time + 6;
        }

        /// <summary>
        /// The showcase round's end made up for the end screens (a real match fills <see cref="end"/> itself): a win 3 to 1 with a Silver
        /// medal, or the loss the other way round.
        /// </summary>
        public void SampleEnd(bool won)
        {
            end.set = true;
            end.won = won;
            end.winnerBlue = !won;
            end.redScore = won ? 3 : 1;
            end.blueScore = won ? 1 : 3;
            end.captures = won ? 2 : 0;
            end.kills = You?.kills ?? 0;
            end.deaths = players.Count(p => !p.alive);
            end.seconds = Mathf.Max(0, roundSeconds - timeLeft);
            end.medal = won ? 2 : 0;
            end.newBest = won;
            end.taunt = won ? "" : "Silver suits you.";
            end.canRetryEasier = !won;
        }

        /// <summary>Whether the showcase round, played to its end, was won by your team.</summary>
        public bool YourTeamWon()
        {
            var winner = players.Find(p => p.placement == 1);
            var you = You;
            return winner != null && you != null && winner.avatar == you.avatar;
        }

        // ---------------------------------------------------------------- What happens in a round

        /// <summary>Holds the trigger for <paramref name="dt"/> seconds: spends spores, spreads the crosshair, now and then hits.</summary>
        public void Fire(float dt)
        {
            if (driven || phase != MatchPhase.Playing || !YouAreAlive || reload > 0)
                return;
            if (ammo <= 0)
            {
                Reload();
                return;
            }
            spread = Mathf.Min(1, spread + dt * 6);
            fireCarry += dt * 18;
            while (fireCarry >= 1 && ammo > 0)
            {
                fireCarry -= 1;
                ammo--;
                if (Random.value < 0.18f)
                    Hit(Random.value < 0.06f, Random.value < 0.35f);
            }
        }

        public void Reload()
        {
            if (!driven && reload <= 0 && ammo < magazine)
                reload = 0.001f;
        }

        private void Hit(bool kill, bool headshot)
        {
            hitMarker = 1;
            hitMarkerKill = kill;
            if (kill)
                YouKill(headshot);
        }

        /// <summary>You eliminate a random bot that is still in.</summary>
        public void YouKill(bool headshot)
        {
            if (!YouAreAlive)
                return;
            var victim = RandomAlive(you);
            if (victim < 0)
                return;
            hitMarker = 1;
            hitMarkerKill = true;
            eliminated = 2.2f;
            eliminatedPlayer = victim;
            eliminatedHeadshot = headshot;
            eliminatedTitle = SdfBattle.DeathLines.Shared.Title(KindOf(weapon, headshot), GuiSettings.Gore);
            Eliminate(you, victim, weapon, headshot);
        }

        /// <summary>One bot eliminates another (or, with no one else left, you).</summary>
        public void BotKill()
        {
            if (phase != MatchPhase.Playing)
                return;
            var killer = RandomAlive(you);
            var victim = RandomAlive(you, killer);
            if (killer < 0 || victim < 0)
            {
                if (killer >= 0 && YouAreAlive)
                    Damage(maxHealth);
                return;
            }
            Eliminate(killer, victim, Random.Range(0, Weapons.Length), Random.value < 0.3f);
        }

        public void Damage(float amount)
        {
            if (phase != MatchPhase.Playing || !YouAreAlive)
                return;
            health = Mathf.Max(0, health - amount);
            damage = 1;
            damageAngle = Random.Range(-180f, 180f);
            if (health <= 0)
                Die();
        }

        public void Heal(float amount) => health = Mathf.Min(maxHealth, health + amount);

        /// <summary>You are eliminated by a random bot that is still in.</summary>
        public void Die()
        {
            if (phase != MatchPhase.Playing || !YouAreAlive)
                return;
            var killer = RandomAlive(you);
            health = 0;
            killedBy = killer;
            killedByWeapon = Random.Range(0, Weapons.Length);
            spectating = killer;
            var headshot = Random.value < 0.3f;
            // With no one left to do it, the island did
            var kind = killer >= 0 ? KindOf(killedByWeapon, headshot) : SdfBattle.DeathKind.Fall;
            killedTitle = SdfBattle.DeathLines.Shared.Title(kind, GuiSettings.Gore);
            killedDetail = SdfBattle.DeathLines.Shared.Detail(kind, GuiSettings.Gore);
            killedStreak = 0;
            respawnIn = 0;
            if (killer >= 0)
                Eliminate(killer, you, killedByWeapon, headshot);
            else
                You.alive = false;
        }

        /// <summary>Everyone else is eliminated: you win the round.</summary>
        public void Win()
        {
            if (phase != MatchPhase.Playing || !YouAreAlive)
                return;
            while (RandomAlive(you) >= 0)
                YouKill(Random.value < 0.4f);
        }

        public void TogglePickup() => pickup = string.IsNullOrEmpty(pickup) ? "Spore Sac" : "";

        private void Eliminate(int killer, int victim, int weaponIndex, bool headshot)
        {
            var dead = players[victim];
            dead.alive = false;
            dead.placement = Alive + 1;
            if (killer >= 0)
                players[killer].kills++;
            var names = (killer >= 0 ? players[killer].name?.Length ?? 0 : 0) + (dead.name?.Length ?? 0);
            var line = SdfBattle.DeathLines.Shared.Feed(KindOf(weaponIndex, headshot), GuiSettings.Gore, names);
            feed.Add(new ViewKill { killer = killer, victim = victim, weapon = weaponIndex, headshot = headshot, line = line });
            if (feed.Count > 6)
                feed.RemoveAt(0);
            if (spectating == victim)
                spectating = killer;
            if (Alive <= 1)
            {
                var winner = players.FindIndex(p => p.alive);
                if (winner >= 0)
                    players[winner].placement = 1;
                phase = MatchPhase.Over;
            }
        }

        /// <summary>The kind of death a weapon (an index into <see cref="Weapons"/>) deals, for its lines.</summary>
        public static SdfBattle.DeathKind KindOf(int weaponIndex, bool headshot) =>
            headshot ? SdfBattle.DeathKind.Headshot
            : SdfBattle.DeathLines.KindOfWeapon(weaponIndex >= 0 && weaponIndex < Weapons.Length ? Weapons[weaponIndex] : null);

        private int RandomAlive(params int[] except)
        {
            var candidates = Enumerable.Range(0, players.Count).Where(i => players[i].alive && !except.Contains(i)).ToList();
            return candidates.Count == 0 ? -1 : candidates[Random.Range(0, candidates.Count)];
        }
    }
}
