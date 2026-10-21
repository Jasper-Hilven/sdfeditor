using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SdfGui
{
    public enum MatchPhase
    {
        Lobby,
        Countdown,
        Playing,
        Over,
    }

    [Serializable]
    public sealed class FakePlayer
    {
        public string name;
        public Color color;
        [Tooltip("Index into the avatar icons")]
        public int avatar;
        public int ping;
        public int kills;
        public bool joined = true;
        public bool alive = true;
        public bool you;
        [Tooltip("Place at the end of the round, 1 for the winner; 0 while alive")]
        public int placement;
    }

    [Serializable]
    public struct FakeKill
    {
        public int killer;
        [Tooltip("The killer's name (rich text) when it is no player: a machine (battle scenes); empty for a player")]
        public string killerName;
        public int victim;
        public int weapon;
        public bool headshot;
        [Tooltip("Seconds since it happened; the kill feed fades it out")]
        public float age;
    }

    /// <summary>
    /// The state of a round of last mushroom standing against fake players, and the bots that play it: everything the GUI shows. The
    /// GUI's views only read it (in the editor too, they are [ExecuteAlways]), so what the scene shows when opened is exactly what the
    /// first frame at runtime shows; only in play mode does the match move on (players join the lobby, bots eliminate each other, the
    /// island crumbles, the clock runs) and do the transient effects (hit markers, damage, pop-ups) fade.
    /// <para>
    /// The player acts through <see cref="Fire"/>, <see cref="Reload"/> and the test commands (<see cref="GuiCommandId"/>); the bots
    /// through <see cref="Update"/>. Pausing does not stop it: the match is "online".
    /// </para>
    /// <para>
    /// In a battle scene it is the HUD's view of the real match instead (<see cref="driven"/>): SdfBattle.MatchHud writes the match
    /// into it every frame and it does nothing of its own.
    /// </para>
    /// </summary>
    public sealed class FakeMatch : MonoBehaviour
    {
        public const int LobbySize = 12;

        /// <summary>Prefab names of the weapons, which are also the names of their icons.</summary>
        public static readonly string[] Weapons = { "minigun", "pistol", "sniper", "flamethrower", "poison-gun", "electro-gun", "blob-gun" };

        public static readonly string[] WeaponNames = { "Minigun", "Pistol", "Sniper", "Flamethrower", "Poison Gun", "Electro Gun", "Blob Gun" };

        public static readonly string[] BotNames =
        {
            "xX_Morel_Xx", "SporeLord", "Shiitake_Snipes", "fungi4life", "ChanterELLE", "PortobelloPaul", "MycoMayhem", "truffle_shuffle",
            "Enoki_Ninja", "DeathCapDan", "puffball99", "GillsNThrills", "oyster_boi", "LionsMane", "toadstool_tom", "hyphae_hero",
        };

        [Header("Round")]
        public MatchPhase phase = MatchPhase.Playing;
        public float roundSeconds = 180;
        public float timeLeft = 134;
        [Tooltip("Seconds left of the lobby countdown, or of the wait for the next round once it is over")]
        public float countdown;
        [Tooltip("Rings of the island's edge that have fallen")]
        public int ringsFallen = 1;
        public int rings = 4;

        [Header("Players")]
        public List<FakePlayer> players = new List<FakePlayer>();
        public int you;
        [Tooltip("Who eliminated you, -1 while you are alive")]
        public int killedBy = -1;
        public int killedByWeapon;
        [Tooltip("Who the camera follows once you are out")]
        public int spectating = -1;
        public List<FakeKill> feed = new List<FakeKill>();

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
        [Tooltip("Seconds the crumble warning still shows")]
        public float crumbleWarning;
        [Tooltip("Seconds the elimination pop-up still shows")]
        public float eliminated;
        public int eliminatedPlayer;
        public bool eliminatedHeadshot;

        [Header("Bots")]
        [Tooltip("Bots eliminate each other, crumble the island and now and then hit you")]
        public bool autoEvents = true;
        [Tooltip("Written by a real match every frame (SdfBattle.MatchHud, in the battle scenes): nothing here moves on by itself, and " +
                 "firing, reloading and the test commands do nothing")]
        public bool driven;
        public Vector2 secondsBetweenKills = new Vector2(3, 8);
        public float seed = 1;

        private float nextKill;
        private float nextJoin;
        private float nextShot;
        private float nextPing;
        private float fireCarry;

        // The bots wait as they would between moves before their first one, so the first frame of play is the scene as saved
        private void Start()
        {
            nextKill = Time.time + Random.Range(secondsBetweenKills.x, secondsBetweenKills.y);
            nextShot = Time.time + Random.Range(5f, 12f);
            nextJoin = Time.time + 0.6f;
            nextPing = Time.time + 1;
        }

        public FakePlayer You => players.Count > you ? players[you] : null;
        public int Alive => players.Count(p => p.joined && p.alive);
        public int Joined => players.Count(p => p.joined);
        public bool YouAreAlive => You == null || You.alive;
        public float RingTime => roundSeconds / (rings + 1);
        public int YourPlacement => You == null ? 0 : You.alive ? Alive : You.placement;

        public FakePlayer Player(int index) => index >= 0 && index < players.Count ? players[index] : null;

        /// <summary>The roster a new match starts with: you and the bots, each with a colour, an avatar and a ping.</summary>
        public static List<FakePlayer> NewRoster(string yourName, int seed)
        {
            var state = Random.state;
            Random.InitState(seed);
            var names = BotNames.OrderBy(_ => Random.value).Take(LobbySize - 1).ToList();
            Random.state = state;
            var roster = new List<FakePlayer> { new FakePlayer { name = yourName, you = true, ping = 24 } };
            roster.AddRange(names.Select((n, i) => new FakePlayer { name = n, ping = 18 + (i * 37 + seed * 13) % 140 }));
            for (var i = 0; i < roster.Count; i++)
            {
                roster[i].color = GuiPalette.Players[i % GuiPalette.Players.Length];
                roster[i].avatar = i % 2;
            }
            return roster;
        }

        private void Update()
        {
            if (!Application.isPlaying || driven)
                return;
            var dt = Time.deltaTime;
            Fade(dt);
            UpdatePings();
            switch (phase)
            {
                case MatchPhase.Lobby:
                    if (Time.time >= nextJoin)
                        JoinOne();
                    break;
                case MatchPhase.Countdown:
                    countdown -= dt;
                    if (countdown <= 0)
                        StartPlaying();
                    break;
                case MatchPhase.Playing:
                    Play(dt);
                    break;
                case MatchPhase.Over:
                    countdown = Mathf.Max(0, countdown - dt);
                    break;
            }
        }

        private void Fade(float dt)
        {
            spread = Mathf.MoveTowards(spread, 0, dt * 2.5f);
            hitMarker = Mathf.MoveTowards(hitMarker, 0, dt * 4);
            damage = Mathf.MoveTowards(damage, 0, dt * 1.2f);
            crumbleWarning = Mathf.Max(0, crumbleWarning - dt);
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

        private void UpdatePings()
        {
            if (Time.time < nextPing)
                return;
            nextPing = Time.time + 1;
            foreach (var player in players)
                player.ping = Mathf.Clamp(player.ping + Random.Range(-6, 7), 12, 220);
        }

        private void Play(float dt)
        {
            timeLeft = Mathf.Max(0, timeLeft - dt);
            var ring = Mathf.Min(rings, Mathf.FloorToInt((roundSeconds - timeLeft) / RingTime));
            if (ring > ringsFallen)
                Crumble();
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

        /// <summary>Back to an empty lobby that fills up with players, then counts down to a new round.</summary>
        public void StartLobby()
        {
            players = NewRoster(You?.name ?? "You", Mathf.RoundToInt(seed += 1));
            foreach (var player in players.Where(p => !p.you))
                player.joined = false;
            phase = MatchPhase.Lobby;
            feed.Clear();
            killedBy = -1;
            spectating = -1;
            nextJoin = Time.time + 0.6f;
        }

        private void JoinOne()
        {
            var waiting = players.Where(p => !p.joined).ToList();
            if (waiting.Count == 0)
            {
                phase = MatchPhase.Countdown;
                countdown = 5;
                return;
            }
            waiting[Random.Range(0, waiting.Count)].joined = true;
            nextJoin = Time.time + Random.Range(0.25f, 1.1f);
        }

        /// <summary>Starts the round now, with everyone in the lobby filled in.</summary>
        public void StartPlaying()
        {
            if (players.Count == 0)
                players = NewRoster("You", Mathf.RoundToInt(seed));
            foreach (var player in players)
            {
                player.joined = true;
                player.alive = true;
                player.kills = 0;
                player.placement = 0;
            }
            phase = MatchPhase.Playing;
            timeLeft = roundSeconds;
            ringsFallen = 0;
            health = maxHealth;
            ammo = magazine;
            reload = 0;
            killedBy = -1;
            spectating = -1;
            feed.Clear();
            nextKill = Time.time + 2;
            nextShot = Time.time + 6;
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
            if (killer >= 0)
                Eliminate(killer, you, killedByWeapon, Random.value < 0.3f);
            else
                You.alive = false;
        }

        /// <summary>Everyone else is eliminated: you are the last mushroom standing.</summary>
        public void Win()
        {
            if (phase != MatchPhase.Playing || !YouAreAlive)
                return;
            while (RandomAlive(you) >= 0)
                YouKill(Random.value < 0.4f);
        }

        /// <summary>The next ring of the island's edge breaks off, with a warning.</summary>
        public void Crumble()
        {
            if (phase != MatchPhase.Playing)
                return;
            ringsFallen = Mathf.Min(rings, ringsFallen + 1);
            crumbleWarning = 3.5f;
        }

        public void TogglePickup() => pickup = string.IsNullOrEmpty(pickup) ? "Spore Sac" : "";

        private void Eliminate(int killer, int victim, int weaponIndex, bool headshot)
        {
            var dead = players[victim];
            dead.alive = false;
            dead.placement = Alive + 1;
            if (killer >= 0)
                players[killer].kills++;
            feed.Add(new FakeKill { killer = killer, victim = victim, weapon = weaponIndex, headshot = headshot });
            if (feed.Count > 6)
                feed.RemoveAt(0);
            if (spectating == victim)
                spectating = killer;
            if (Alive <= 1)
            {
                var winner = players.FindIndex(p => p.joined && p.alive);
                if (winner >= 0)
                    players[winner].placement = 1;
                phase = MatchPhase.Over;
                countdown = 10;
            }
        }

        private int RandomAlive(params int[] except)
        {
            var candidates = Enumerable.Range(0, players.Count).Where(i => players[i].joined && players[i].alive && !except.Contains(i)).ToList();
            return candidates.Count == 0 ? -1 : candidates[Random.Range(0, candidates.Count)];
        }
    }
}
