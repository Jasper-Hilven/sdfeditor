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
    /// roster in team colours with who is alive and their kills, your health, who killed you, the kill feed. So the views stay as they
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

        /// <summary>Makes the GUI from <paramref name="guiPrefab"/>, on the HUD, with its test panel off, fed by <paramref name="match"/>.</summary>
        public static MatchHud Create(Match match, GameObject guiPrefab)
        {
            var gui = Instantiate(guiPrefab);
            gui.name = guiPrefab.name;
            var view = gui.GetComponent<FakeMatch>();
            if (view == null)
            {
                Debug.LogWarning($"Match: the GUI prefab {guiPrefab.name} has no FakeMatch for its views to read; no HUD");
                Destroy(gui);
                return null;
            }
            view.driven = true;
            view.autoEvents = false;
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
        }

        private void OnDied(Death death)
        {
            var victim = death.Victim.Index;
            var killer = death.Killer?.Index ?? -1;
            var weapon = System.Array.IndexOf(FakeMatch.Weapons, death.Weapon);
            if (victim == 0)
            {
                killedBy = killer;
                killedByWeapon = Mathf.Max(0, weapon);
            }
            // Kills by players and by machines (ticket 24: their name in their team's colour, no player behind it); a fall is not in it.
            // A weapon without an icon (a machine's cannon) shows none; a headshot shows the popped cap (ticket 22)
            var machine = death.Killer == null && death.KillerName != null && death.KillerTeam != null
                ? $"<color=#{GuiPalette.HexOf(BattleSide.ColorOf(BattleSide.SideOf(death.KillerTeam.Value)))}>{death.KillerName}</color>"
                : null;
            if (killer >= 0 || machine != null)
            {
                feed.Add(new FakeKill { killer = killer, killerName = machine, victim = victim, weapon = weapon, headshot = death.Headshot });
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
