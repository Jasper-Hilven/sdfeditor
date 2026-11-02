using SdfLevel;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// A participant's body in the scene (ticket 20), you or a bot: the <see cref="FirstPersonController"/> that moves it by the game's
    /// rules (your input drives yours, a bot's brain drives its own through the same API), its <see cref="CharacterController"/> capsule,
    /// which is also what shots hit (ticket 22), and the team's soldier drawn round it (<see cref="SoldierPuppet"/>, hidden for you).
    /// What the rules know of it is <see cref="State"/>, in <see cref="Match.State"/>; the match makes the players at its start, none
    /// are placed in the scene by hand. Find the player behind a collider with <see cref="Of"/>.
    /// </summary>
    [RequireComponent(typeof(FirstPersonController))]
    public sealed class Player : MonoBehaviour
    {
        [Tooltip("The team's scout drawn for this player: only when the soldier models were not imported (see soldier); none for you (first person)")]
        public ScoutBody body;

        /// <summary>The soldier drawn for this player (ticket 44), hidden for you in first person; null when the soldier models were not imported.</summary>
        public SoldierPuppet soldier;

        /// <summary>The root of whatever body is drawn (the soldier, else the scout); null for none.</summary>
        public Transform BodyRoot => soldier != null ? soldier.transform : body != null ? body.transform : null;

        public PlayerState State { get; private set; }

        public FirstPersonController Controller { get; private set; }

        public CharacterController Capsule { get; private set; }

        /// <summary>The player you are: your input drives it, the camera is in it, its body is not drawn.</summary>
        public bool IsYou => State != null && !State.IsBot;

        public Team Team => State?.Team ?? Team.Red;

        /// <summary>The player a collider (a capsule a shot hit, a trigger entered) belongs to; null for anything else.</summary>
        public static Player Of(Component component) => component != null ? component.GetComponentInParent<Player>() : null;

        private void Awake()
        {
            Controller = GetComponent<FirstPersonController>();
            Capsule = GetComponent<CharacterController>();
        }

        /// <summary>Ties this body to its place in the match; the match calls it once, as it makes the player.</summary>
        public void Bind(PlayerState state)
        {
            if (Controller == null)
                Awake();
            State = state;
            name = state.IsBot ? state.Name : name;
            Controller.readsInput = !state.IsBot;
            if (soldier == null)
                soldier = SoldierPuppet.Create(this);
        }

        /// <summary>The player (re)appears with its feet at <paramref name="feet"/>, facing <paramref name="yaw"/>.</summary>
        public void Appear(Vector3 feet, float yaw)
        {
            Capsule.enabled = true;
            Controller.enabled = true;
            Controller.speedMultiplier = 1;
            Controller.Teleport(feet, yaw);
            if (body != null)
                body.gameObject.SetActive(true);
            if (soldier != null)
            {
                soldier.gameObject.SetActive(true);
                soldier.Revive();
                soldier.SetVisible(!IsYou);
            }
        }

        /// <summary>
        /// The player died: it neither moves nor can be hit until it appears again. A scout's body is gone; the soldier stays where it
        /// fell, holding its last pose (<see cref="Combat"/> tells it, and hides it after a fall). Your view stays where you died (the
        /// death screen shows over it).
        /// </summary>
        public void Vanish()
        {
            Controller.enabled = false;
            Capsule.enabled = false;
            if (body != null)
                body.gameObject.SetActive(false);
        }
    }
}
