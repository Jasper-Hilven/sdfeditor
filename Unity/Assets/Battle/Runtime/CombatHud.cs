using SdfGui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SdfBattle
{
    /// <summary>
    /// Combat on the HUD (ticket 22), on top of what <see cref="MatchHud"/> writes into the GUI's <see cref="MatchView"/> (the health
    /// bar reads <see cref="PlayerState.Health"/>, the kill feed the match's deaths): the hit marker (bigger and red on a kill), the
    /// damage direction and the red of being hit, the "eliminated" pop-up (its title one of <see cref="DeathLines"/>), the crosshair's
    /// spread with the weapon's, and the four weapon slots (the HUD prefab's, <see cref="GuiHud.weaponSlots"/>) with the weapons' icons,
    /// the one in hand marked and named over them.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class CombatHud : MonoBehaviour
    {
        [Tooltip("Degrees of spread at which the crosshair is fully open")]
        public float fullSpread = 5;

        private Combat combat;
        private MatchHud matchHud;
        private MatchView view;
        private GuiIconSet icons;
        private TMP_Text weaponLabel;
        private GuiBar healthBar;
        private ToonShape[] slots;
        private Image[] slotIcons;
        private TMP_Text[] slotNumbers;
        private bool bound;

        public static CombatHud Create(Combat combat)
        {
            var hud = combat.gameObject.AddComponent<CombatHud>();
            hud.combat = combat;
            return hud;
        }

        private void OnDestroy()
        {
            if (!bound || combat == null)
                return;
            combat.HitLanded -= OnHit;
            if (combat.State != null)
            {
                combat.State.Damaged -= OnDamaged;
                combat.Match.State.Died -= OnDied;
            }
        }

        /// <summary>Finds the match's HUD (made after combat, in the same start) and builds the slots into it.</summary>
        private bool Bind()
        {
            if (bound)
                return true;
            if (combat == null || combat.State == null)
                return false;
            matchHud = FindAnyObjectByType<MatchHud>();
            if (matchHud == null || matchHud.view == null)
                return false;
            view = matchHud.view;
            var entry = matchHud.GetComponentInChildren<GuiKillFeedEntry>(true);
            icons = entry != null ? entry.icons : null;
            var hud = matchHud.GetComponentInChildren<GuiHud>(true);
            healthBar = hud != null ? hud.health : null;
            if (hud != null && hud.weaponSlots.Length >= Inventory.SlotCount)
            {
                weaponLabel = hud.weaponLabel;
                slots = hud.weaponSlots;
                slotIcons = hud.weaponIcons;
                slotNumbers = hud.weaponNumbers;
            }
            combat.HitLanded += OnHit;
            combat.State.Damaged += OnDamaged;
            combat.Match.State.Died += OnDied;
            bound = true;
            return true;
        }

        private void Update()
        {
            if (!Bind())
                return;
            var dt = Time.deltaTime;
            view.hitMarker = Mathf.MoveTowards(view.hitMarker, 0, dt * 4);
            view.damage = Mathf.MoveTowards(view.damage, 0, dt * 1.2f);
            view.eliminated = Mathf.Max(0, view.eliminated - dt);

            var you = combat.Match.You;
            ShowHealth(you);
            var arsenal = combat.ArsenalOf(you);
            var inventory = arsenal != null ? arsenal.Inventory : null;
            if (inventory == null)
                return;
            var weapon = arsenal.Weapon;
            view.spread = weapon != null ? Mathf.Clamp01(arsenal.Trigger.Spread(arsenal.Zoomed) / Mathf.Max(0.1f, fullSpread)) : 0;
            view.weapon = Mathf.Max(0, System.Array.IndexOf(MatchView.Weapons, WeaponIds.ModelOf(inventory.CurrentWeapon)));
            if (weaponLabel != null && weapon != null)
            {
                // The minigun's burst: its heat in the label, and the wait while it spins down
                var trigger = arsenal.Trigger;
                var text = weapon.displayName.ToUpperInvariant();
                if (trigger.Overheated)
                    text += "  SPINNING DOWN";
                else if (weapon.burstSeconds > 0 && trigger.BurstFraction > 0)
                    text += $"  {Mathf.CeilToInt((1 - trigger.BurstFraction) * weapon.burstSeconds * 10) / 10f:0.0}s";
                GuiText.Set(weaponLabel, text);
                weaponLabel.color = trigger.Overheated ? GuiPalette.Amanita : GuiPalette.Paper;
            }
            if (slots == null)
                return;
            for (var i = 0; i < Inventory.SlotCount; i++)
            {
                var id = inventory[(WeaponSlot)i];
                var held = id != null;
                var current = held && (int)inventory.Current == i;
                var box = slots[i];
                if (box != null)
                {
                    box.SetColor(current ? GuiPalette.Gold : held ? GuiPalette.Paper : GuiPalette.WithAlpha(GuiPalette.Night, 0.55f));
                    var scale = Vector3.one * (current ? 1.12f : 1);
                    if (box.transform.localScale != scale)
                        box.transform.localScale = scale;
                }
                var icon = i < slotIcons.Length ? slotIcons[i] : null;
                if (icon != null)
                {
                    var sprite = held && icons != null ? icons.Get(id) : null;
                    if (icon.sprite != sprite)
                        icon.sprite = sprite;
                    icon.enabled = sprite != null;
                }
                var number = i < slotNumbers.Length ? slotNumbers[i] : null;
                if (number != null)
                {
                    GuiText.Set(number, i == 0 && inventory.DualPistols ? "1 x2" : (i + 1).ToString());
                    number.color = held ? GuiPalette.Ink : GuiPalette.WithAlpha(GuiPalette.Paper, 0.6f);
                }
            }
        }

        /// <summary>
        /// Health over 100 from the health pods: the bar stays full and its number shows the health (MatchHud writes the bar against the
        /// spawn's 100, which would stop the number at 100).
        /// </summary>
        private void ShowHealth(Player you)
        {
            if (you == null || you.State == null)
                return;
            var shown = Mathf.Max(you.State.MaxHealth, you.State.Health);
            if (shown > 0)
                view.maxHealth = shown;
            if (healthBar != null && !Mathf.Approximately(healthBar.scale, view.maxHealth))
                healthBar.scale = view.maxHealth;
        }

        private void OnHit(Player shooter, Player victim, bool kill, bool head)
        {
            if (shooter == null || !shooter.IsYou)
                return;
            view.hitMarker = 1;
            view.hitMarkerKill = kill;
        }

        private void OnDamaged(PlayerState victim, Damage damage, float taken)
        {
            var you = combat.Match.You;
            if (you == null)
                return;
            // Your burns and clouds at work: a faint hit marker (direct hits mark through Combat.HitLanded)
            if (damage.OverTime && damage.Attacker == you.State && victim != you.State)
            {
                view.hitMarker = Mathf.Max(view.hitMarker, 0.45f);
                view.hitMarkerKill = !victim.Alive;
            }
            if (victim != you.State)
                return;
            // A tick of a burn or a cloud reddens a little, a hit more, a big hit all the way
            view.damage = Mathf.Max(view.damage, Mathf.Clamp01((damage.OverTime ? 0.2f : 0.45f) + taken / 40));
            if (damage.From is { } from)
            {
                var to = from - you.transform.position;
                to.y = 0;
                var forward = you.transform.forward;
                forward.y = 0;
                if (to.sqrMagnitude > 1e-4f && forward.sqrMagnitude > 1e-4f)
                    view.damageAngle = Vector3.SignedAngle(forward, to, Vector3.up);
            }
        }

        private void OnDied(Death death)
        {
            var you = combat.Match.You;
            if (you == null || death.Killer != you.State || !death.CountsAsKill)
                return;
            view.eliminated = 2.2f;
            view.eliminatedPlayer = death.Victim.Index;
            view.eliminatedHeadshot = death.Headshot;
            // The kind the match's HUD made of this death (it hears of deaths first), with its revenge or streak
            var kind = matchHud.LastVictim == death.Victim ? matchHud.LastKind : DeathLines.KindOf(death);
            view.eliminatedTitle = DeathLines.Shared.Title(kind, GuiSettings.Gore);
            view.hitMarker = 1;
            view.hitMarkerKill = true;
        }
    }
}
