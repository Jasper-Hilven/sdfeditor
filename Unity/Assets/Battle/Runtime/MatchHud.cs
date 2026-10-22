using System.Collections.Generic;
using SdfGui;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SdfBattle
{
    /// <summary>
    /// The GUI on a real match (ticket 20). The GUI's views read a <see cref="FakeMatch"/> (the showcase's state of a round); in a
    /// battle scene this makes the GUI from its prefab, sets its FakeMatch to <see cref="FakeMatch.driven"/> (no fake bots, no clock of
    /// its own) and writes the <see cref="Match"/> into it every frame before the views draw: the phase, the clock and countdown, the
    /// roster in team colours with who is alive and their kills, your health, who killed you, the kill feed, and the words for each death
    /// (<see cref="DeathLines"/>: the feed's line, the death screen's title and lines, picked as it happens). So the views stay as they
    /// are; the capture-the-flag HUD (team score, flag status) is ticket 21's.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class MatchHud : MonoBehaviour
    {
        public Match match;
        public FakeMatch view;

        private readonly List<FakeKill> feed = new List<FakeKill>();
        private int killedBy = -1;
        private int killedByWeapon;
        private string killedByName;
        private string killedTitle;
        private string killedDetail;
        private int killedStreak;
        private readonly KillMemory memory = new KillMemory();

        /// <summary>The last death's kind (a revenge, a streak, a weapon's), for the elimination pop-up (CombatHud hears of it after this).</summary>
        public DeathKind LastKind { get; private set; }

        /// <summary>Whose death <see cref="LastKind"/> is.</summary>
        public PlayerState LastVictim { get; private set; }

        /// <summary>
        /// Puts <paramref name="gui"/> on the HUD, with its test panel off, fed by <paramref name="match"/>: the scene's own GUI (the
        /// level builder places the GUI prefab in the scene), or, given the prefab itself, a copy of it.
        /// </summary>
        public static MatchHud Create(Match match, GameObject gui)
        {
            if (!gui.scene.IsValid())
            {
                var prefab = gui;
                gui = Instantiate(prefab);
                gui.name = prefab.name;
            }
            var view = gui.GetComponent<FakeMatch>();
            if (view == null)
            {
                Debug.LogWarning($"Match: the GUI {gui.name} has no FakeMatch for its views to read; no HUD");
                Destroy(gui);
                return null;
            }
            ShowRoundStart(view, match.rules);
            var flow = gui.GetComponent<GuiFlow>();
            if (flow != null)
            {
                flow.testPanel = false;
                flow.Show(GuiScreen.Hud);
            }
            // The pause menu's buttons need an event system; the battle scenes have none of their own
            if (EventSystem.current == null)
                new GameObject("Event System", typeof(EventSystem), typeof(StandaloneInputModule));

            var hud = gui.AddComponent<MatchHud>();
            hud.match = match;
            hud.view = view;
            match.State.Died += hud.OnDied;
            match.State.RoundReset += hud.OnRoundReset;
            hud.Feed(0);
            return hud;
        }

        /// <summary>
        /// Sets <paramref name="view"/> to be driven by a match, and to what it shows as a round starts: the full clock, full health, no
        /// kills, pop-ups or hits yet. The level builder sets the scene's GUI so, so the scene shows the HUD as play opens.
        /// </summary>
        public static void ShowRoundStart(FakeMatch view, MatchRules rules)
        {
            view.driven = true;
            view.autoEvents = false;
            view.phase = MatchPhase.Playing;
            view.roundSeconds = view.timeLeft = rules.roundSeconds;
            view.countdown = 0;
            view.rings = view.ringsFallen = 0;
            view.maxHealth = view.health = rules.maxHealth;
            view.ammo = view.magazine;
            view.reload = 0;
            view.pickup = "";
            view.killedBy = view.spectating = -1;
            view.killedByName = view.killedTitle = view.killedDetail = view.eliminatedTitle = null;
            view.feed.Clear();
            view.spread = view.hitMarker = view.damage = view.crumbleWarning = view.eliminated = 0;
            view.hitMarkerKill = view.eliminatedHeadshot = false;
        }

        private void OnDestroy()
        {
            if (match != null && match.State != null)
            {
                match.State.Died -= OnDied;
                match.State.RoundReset -= OnRoundReset;
            }
        }

        private void Update() => Feed(Time.deltaTime);

        private void OnRoundReset()
        {
            feed.Clear();
            killedBy = -1;
            killedByName = killedTitle = killedDetail = null;
            memory.Clear();
        }

        private void OnDied(Death death)
        {
            var victim = death.Victim.Index;
            var killer = death.Killer?.Index ?? -1;
            var weapon = System.Array.IndexOf(FakeMatch.Weapons, death.Weapon);
            var kind = memory.Record(death, out var streak);
            LastKind = kind;
            LastVictim = death.Victim;
            var gore = GuiSettings.Gore;
            if (victim == 0)
            {
                killedBy = killer;
                killedByWeapon = Mathf.Max(0, weapon);
                // The death screen's words; the weapon's name now, while its own id (dual pistols) is known
                killedTitle = DeathLines.Shared.Title(kind, gore);
                killedDetail = DeathLines.Shared.Detail(kind, gore).Replace("{weapon}", DeathLines.WeaponName(death.WeaponId));
                killedStreak = streak;
            }
            // Kills by players and by machines (ticket 24: their name in their team's colour, no player behind it), and falls.
            // A weapon without an icon (a machine's cannon) shows none; a headshot shows the popped cap (ticket 22)
            // A hazard of the level (ticket 27.10's "Toy train") has a name and no team: its name plain
            var machine = death.Killer == null && death.KillerName != null
                ? death.KillerTeam != null
                    ? $"<color=#{GuiPalette.HexOf(BattleSide.ColorOf(BattleSide.SideOf(death.KillerTeam.Value)))}>{death.KillerName}</color>"
                    : death.KillerName
                : null;
            // The death screen names a machine that killed you (ticket 24), not "the island"
            if (victim == 0)
                killedByName = machine;
            if (killer >= 0 || machine != null || death.Cause == DeathCause.Fell)
            {
                // A line that fits with both names in the entry
                var line = DeathLines.Shared.Feed(kind, gore, (death.KillerName?.Length ?? 0) + (death.Victim.Name?.Length ?? 0));
                feed.Add(new FakeKill
                {
                    killer = killer, killerName = machine, victim = victim, weapon = weapon, headshot = death.Headshot, line = line, streak = streak,
                });
                if (feed.Count > 6)
                    feed.RemoveAt(0);
            }
        }

        private void Feed(float dt)
        {
            if (match == null || match.State == null || view == null)
                return;
            var state = match.State;
            view.phase = state.Phase switch
            {
                RoundPhase.Countdown => MatchPhase.Countdown,
                RoundPhase.Playing => MatchPhase.Playing,
                _ => MatchPhase.Over,
            };
            view.roundSeconds = state.Rules.roundSeconds;
            view.timeLeft = state.TimeLeft;
            view.countdown = state.PhaseTimeLeft;
            view.rings = 0;
            view.ringsFallen = 0;
            view.crumbleWarning = 0;

            // The roster in team colours; the round's winners are first, you before your team mates
            var players = view.players;
            while (players.Count < state.Players.Count)
                players.Add(new FakePlayer());
            if (players.Count > state.Players.Count)
                players.RemoveRange(state.Players.Count, players.Count - state.Players.Count);
            var winnerShown = false;
            for (var i = 0; i < state.Players.Count; i++)
            {
                var p = state.Players[i];
                var shown = players[i];
                shown.name = p.Name;
                shown.color = BattleSide.ColorOf(BattleSide.SideOf(p.Team));
                shown.avatar = (int)p.Team;
                shown.kills = p.Kills;
                shown.joined = true;
                shown.alive = p.Alive;
                shown.you = !p.IsBot;
                var won = state.Winner == p.Team && !winnerShown;
                shown.placement = won ? 1 : 0;
                winnerShown |= won;
            }
            view.you = 0;

            var you = match.You != null ? match.You.State : null;
            view.maxHealth = you?.MaxHealth > 0 ? you.MaxHealth : state.Rules.maxHealth;
            view.health = you?.Health ?? 0;
            view.killedBy = you != null && !you.Alive ? killedBy : -1;
            view.killedByWeapon = killedByWeapon;
            view.killedByName = you != null && !you.Alive ? killedByName : null;
            view.killedTitle = you != null && !you.Alive ? killedTitle : null;
            view.killedDetail = you != null && !you.Alive ? killedDetail : null;
            view.killedStreak = killedStreak;
            view.spectating = -1;
            // No weapons yet (ticket 22): a full tank, nothing to reload or pick up
            view.ammo = view.magazine;
            view.reload = 0;
            view.pickup = "";

            for (var i = 0; i < feed.Count; i++)
            {
                var kill = feed[i];
                kill.age += dt;
                feed[i] = kill;
            }
            feed.RemoveAll(k => k.age > 8);
            view.feed.Clear();
            view.feed.AddRange(feed);
        }
    }
}
