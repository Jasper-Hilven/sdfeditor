using System.Collections.Generic;
using SdfGui;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SdfBattle
{
    /// <summary>
    /// The GUI on a real match (ticket 20). The GUI's views read a <see cref="MatchView"/> (the showcase's state of a round); in a
    /// battle scene this makes the GUI from its prefab, sets its MatchView to <see cref="MatchView.driven"/> (no showcase bots, no clock of
    /// its own) and writes the <see cref="Match"/> into it every frame before the views draw: the phase, the clock and countdown, the
    /// roster in team colours with who is alive and their kills, your health, who killed you, the kill feed, and the words for each death
    /// (<see cref="DeathLines"/>: the feed's line, the death screen's title and lines, picked as it happens). So the views stay as they
    /// are; the capture-the-flag HUD (team score, flag status) is ticket 21's.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class MatchHud : MonoBehaviour
    {
        public Match match;
        public MatchView view;

        private readonly List<ViewKill> feed = new List<ViewKill>();
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
            // The campaign's GUI (ticket 32.4) survives the scene loads: it serves every battle, and the scene's own copy goes
            var persistent = GuiFlow.Persistent;
            if (persistent != null && gui != persistent.gameObject)
            {
                if (gui.scene.IsValid())
                    Destroy(gui);
                gui = persistent.gameObject;
                Release(gui);
            }
            else
                persistent = null;
            if (!gui.scene.IsValid())
            {
                var prefab = gui;
                gui = Instantiate(prefab);
                gui.name = prefab.name;
            }
            var view = gui.GetComponent<MatchView>();
            if (view == null)
            {
                Debug.LogWarning($"Match: the GUI {gui.name} has no MatchView for its views to read; no HUD");
                Destroy(gui);
                return null;
            }
            ShowRoundStart(view, match.rules, match.gameObject.scene.name);
            var flow = gui.GetComponent<GuiFlow>();
            if (flow != null)
            {
                flow.testPanel = false;
                // The campaign's loader shows the HUD itself, when the player has clicked to play
                if (persistent == null)
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
        public static void ShowRoundStart(MatchView view, MatchRules rules, string arena = null)
        {
            view.driven = true;
            view.arena = arena ?? view.gameObject.scene.name;
            view.goal = $"First to {rules.capturesToWin} captures";
            view.autoEvents = false;
            view.phase = MatchPhase.Playing;
            view.roundSeconds = view.timeLeft = rules.roundSeconds;
            view.countdown = 0;
            view.maxHealth = view.health = rules.maxHealth;
            view.ammo = view.magazine;
            view.reload = 0;
            view.pickup = "";
            view.killedBy = view.spectating = -1;
            view.killedByName = view.killedTitle = view.killedDetail = view.eliminatedTitle = null;
            view.feed.Clear();
            view.end.Clear();
            view.caption.Clear();
            view.spread = view.hitMarker = view.damage = view.eliminated = 0;
            view.hitMarkerKill = view.eliminatedHeadshot = false;
        }

        /// <summary>
        /// Takes a GUI that outlives its matches off the last one: the HUD pieces added to it and its feed by that match go, and its view
        /// is the showcase's again.
        /// </summary>
        public static void Release(GameObject gui)
        {
            foreach (var flagHud in gui.GetComponentsInChildren<FlagHud>(true))
                Destroy(flagHud);
            foreach (var bossHud in gui.GetComponentsInChildren<BossHud>(true))
                Destroy(bossHud);
            foreach (var hud in gui.GetComponents<MatchHud>())
                Destroy(hud);
            if (gui.TryGetComponent<MatchView>(out var view))
            {
                view.driven = false;
                view.autoEvents = false;
            }
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
            if (view != null)
                view.end.Clear();
        }

        private void OnDied(Death death)
        {
            var victim = death.Victim.Index;
            var killer = death.Killer?.Index ?? -1;
            var weapon = System.Array.IndexOf(MatchView.Weapons, death.Weapon);
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
                feed.Add(new ViewKill
                {
                    killer = killer, killerName = machine, victim = victim, weapon = weapon, headshot = death.Headshot, line = line, streak = streak,
                });
                if (feed.Count > 6)
                    feed.RemoveAt(0);
            }
        }

        // How the round ended, once it has (the banner, Victory and Defeat read it); the campaign adds the medal and the taunt
        private void FeedEnd(MatchState state)
        {
            // Once: the clock goes on ticking after the end, and the numbers are the end's
            if (state.Phase != RoundPhase.RoundEnd || state.Winner == null || view.end.set)
                return;
            var end = view.end;
            var yours = match.You != null ? match.You.Team : match.yourTeam;
            var you = match.You != null ? match.You.State : null;
            end.set = true;
            end.won = state.Winner == yours;
            end.winnerBlue = state.Winner == Team.Blue;
            end.redScore = state.Score(Team.Red);
            end.blueScore = state.Score(Team.Blue);
            end.captures = you?.Captures ?? 0;
            end.kills = you?.Kills ?? 0;
            end.deaths = you?.Deaths ?? 0;
            end.seconds = state.RoundClock;
            if (state.Rules.objective == MatchObjective.Boss && Boss.Current != null)
                FeedBossEnd(end, Boss.Current);
        }

        // The boss fight's end (ticket 33.7): the phase reached, Prime's health left, the lives lost, damage dealt and the fight's time (the
        // match clock does not run); the campaign adds the tip for the phase reached
        private void FeedBossEnd(ViewEnd end, Boss boss)
        {
            var bossState = boss.State;
            end.boss = true;
            end.bossPhase = boss.PhaseReached;
            end.bossHealthLeft = bossState.Health / Mathf.Max(1, boss.Rules.TotalHealth);
            end.livesLost = bossState.LivesLost;
            end.damageDealt = Mathf.RoundToInt(bossState.DamageDealt);
            end.seconds = bossState.FightSeconds;
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

            // The roster in team colours; the round's winners are first, you before your team mates
            var players = view.players;
            while (players.Count < state.Players.Count)
                players.Add(new ViewPlayer());
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
                shown.alive = p.Alive;
                shown.you = !p.IsBot;
                var won = state.Winner == p.Team && !winnerShown;
                shown.placement = won ? 1 : 0;
                winnerShown |= won;
            }
            view.you = 0;
            FeedEnd(state);

            var you = match.You != null ? match.You.State : null;
            view.maxHealth = you?.MaxHealth > 0 ? you.MaxHealth : state.Rules.maxHealth;
            view.health = you?.Health ?? 0;
            view.killedBy = you != null && !you.Alive ? killedBy : -1;
            view.killedByWeapon = killedByWeapon;
            view.killedByName = you != null && !you.Alive ? killedByName : null;
            view.killedTitle = you != null && !you.Alive ? killedTitle : null;
            view.killedDetail = you != null && !you.Alive ? killedDetail : null;
            view.killedStreak = killedStreak;
            view.respawnIn = you != null && !you.Alive ? you.RespawnIn : 0;
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
