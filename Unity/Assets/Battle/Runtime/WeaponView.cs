using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace SdfBattle
{
    /// <summary>
    /// The weapon a player is seen holding (ticket 22): for you a view model in the camera's corner (the weapon's prefab, scaled down so
    /// it stays inside your capsule and never reaches into a wall; two, one in each corner, for dual pistols), kicking back with each
    /// shot; for everyone else the weapon in their scout's floating hand (both hands for dual pistols), pointing where they aim. Each
    /// copy has a muzzle point the shots' effects leave from. The models are made the first time a weapon is held and kept, hidden,
    /// for the next time; their colliders are taken off and the view model casts no shadow.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class WeaponView : MonoBehaviour
    {
        private sealed class Held
        {
            public Transform[] models;
            public Transform[] muzzles;
            public WeaponTableAsset.Look look;
            public string armsFor;
            public Team armsTeam = (Team)(-1);
            /// <summary>First person: the gloves on each model, and the sleeve behind each glove (same order).</summary>
            public List<Arm>[] arms;
        }

        /// <summary>One gloved hand in first person: the glove on its weapon model, the sleeve from its wrist to the shoulder.</summary>
        private sealed class Arm
        {
            public Transform glove;
            public Transform sleeve;
            public bool left;
            public float size;
        }

        [Tooltip("Metres the view model kicks back with a shot")]
        public float kick = 0.012f;
        [Tooltip("How fast the kick settles")]
        public float settle = 18;

        private readonly Dictionary<string, Held> models = new Dictionary<string, Held>();
        private Arsenal arsenal;
        private WeaponTableAsset assets;
        private Transform viewCamera;
        private Transform[] hands;
        private float[] reach;
        private Held current;
        private bool dual;
        private int nextMuzzle;
        private float recoil;
        private float spinAngle;
        private bool hidden;
        private bool handsAreSockets;

        public bool FirstPerson => viewCamera != null;

        /// <summary>The muzzle the next shot leaves from (dual pistols take turns).</summary>
        public Transform Muzzle
        {
            get
            {
                if (current == null || current.muzzles.Length == 0)
                    return null;
                return current.muzzles[Mathf.Min(nextMuzzle, current.muzzles.Length - 1)];
            }
        }

        /// <summary>The view of a player's weapons: in <paramref name="view"/>'s corner for you, in the scout's hands otherwise.</summary>
        public static WeaponView Create(Arsenal arsenal, WeaponTableAsset assets, Camera view)
        {
            var root = new GameObject("Weapons").transform;
            var body = arsenal.Player.BodyRoot;
            // A bot's weapons go with its body, so they vanish with it when it dies
            root.SetParent(view != null ? view.transform : body != null ? body : arsenal.transform, false);
            var weaponView = root.gameObject.AddComponent<WeaponView>();
            weaponView.arsenal = arsenal;
            weaponView.assets = assets;
            weaponView.viewCamera = view != null ? view.transform : null;
            if (view == null && body != null)
            {
                var soldier = arsenal.Player.soldier;
                if (soldier != null)
                {
                    // The soldier holds a weapon at the grip socket of its right hand (dual pistols: the left hand's socket too), in its own size
                    weaponView.hands = System.Array.FindAll(soldier.Hands(), t => t != null);
                    weaponView.reach = new float[weaponView.hands.Length];
                    weaponView.handsAreSockets = true;
                }
                else
                {
                    weaponView.hands = FindHands(body);
                    weaponView.reach = weaponView.hands != null ? System.Array.ConvertAll(weaponView.hands, Reach) : null;
                }
            }
            // Every weapon made now, hidden, so picking one up mid-round makes nothing
            if (assets != null && assets.looks != null)
                foreach (var look in assets.looks)
                    if (look != null && !string.IsNullOrEmpty(look.id))
                        weaponView.Get(look.id);
            return weaponView;
        }

        /// <summary>The scout's hands, right one first (the hand on the body's +x side).</summary>
        private static Transform[] FindHands(Transform body)
        {
            Transform a = null, b = null;
            foreach (var t in body.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "hand.0")
                    a = t;
                else if (t.name == "hand.1")
                    b = t;
            }
            if (a == null || b == null)
                return a != null ? new[] { a } : b != null ? new[] { b } : null;
            return body.InverseTransformPoint(a.position).x >= body.InverseTransformPoint(b.position).x ? new[] { a, b } : new[] { b, a };
        }

        /// <summary>
        /// How far in front of a hand's middle a weapon's grip goes, metres: most of the way to the front of the hand. A scout's hand is
        /// a blob about 30 cm across, and a pistol (about 17 cm) gripped at its middle was all inside it, so a bot with only its pistol
        /// looked empty-handed.
        /// </summary>
        private static float Reach(Transform hand)
        {
            var extents = Corpse.LocalBounds(hand, hand).extents;
            return (extents.x + extents.y + extents.z) / 3 * hand.lossyScale.x * 0.8f;
        }

        /// <summary>Holds this weapon (dual-pistols: two pistols).</summary>
        public void Show(string weapon)
        {
            if (current != null)
                for (var i = 0; i < current.models.Length; i++)
                    SetShown(current, i, false);
            dual = weapon == WeaponIds.DualPistols;
            current = Get(WeaponIds.ModelOf(weapon));
            nextMuzzle = 0;
            recoil = 0;
            if (current == null)
                return;
            if (FirstPerson)
                DressArms(current, weapon);
            for (var i = 0; i < current.models.Length; i++)
                SetShown(current, i, !hidden && (i == 0 || dual));
        }

        /// <summary>A model and its arms drawn or not.</summary>
        private static void SetShown(Held held, int i, bool shown)
        {
            held.models[i].gameObject.SetActive(shown);
            if (held.arms != null && held.arms[i] != null)
                foreach (var arm in held.arms[i])
                    if (arm.sleeve != null)
                        arm.sleeve.gameObject.SetActive(shown);
        }

        /// <summary>
        /// First person: the soldier's gloved hands on the weapon in the player's team colour (<see cref="FirstPersonArms"/>), made the first
        /// time the weapon is held and made again if the team changed. Dual pistols hold one pistol in each hand.
        /// </summary>
        private void DressArms(Held held, string weapon)
        {
            var team = arsenal != null && arsenal.Player != null ? arsenal.Player.Team : Team.Red;
            if (held.arms != null && held.armsTeam == team && held.armsFor == weapon)
                return;
            if (assets == null || assets.gloveModels == null || assets.gloveModels.Length <= (int)team || assets.sleeveModels == null || assets.sleeveModels.Length <= (int)team
                || assets.gloveModels[(int)team] == null || assets.sleeveModels[(int)team] == null)
                return;
            if (held.arms != null)
                foreach (var list in held.arms)
                    foreach (var arm in list)
                    {
                        Destroy(arm.glove.gameObject);
                        Destroy(arm.sleeve.gameObject);
                    }
            held.armsTeam = team;
            held.armsFor = weapon;
            held.arms = new List<Arm>[held.models.Length];
            var primary = FirstPersonArms.Primary(weapon);
            var support = FirstPersonArms.Support(weapon);
            for (var i = 0; i < held.models.Length; i++)
            {
                held.arms[i] = new List<Arm>();
                // The second pistol of a pair is held in the left hand
                held.arms[i].Add(MakeArm(held.models[i], team, i == 0 ? primary : new FirstPersonArms.Hand(primary.position, primary.euler, primary.size, true)));
                if (support.HasValue && i == 0)
                    held.arms[i].Add(MakeArm(held.models[i], team, support.Value));
            }
        }

        private Arm MakeArm(Transform model, Team team, FirstPersonArms.Hand hand)
        {
            var glove = Instantiate(assets.gloveModels[(int)team], model, false).transform;
            var sleeve = Instantiate(assets.sleeveModels[(int)team], transform, false).transform;
            foreach (var renderer in glove.GetComponentsInChildren<Renderer>(true).Concat(sleeve.GetComponentsInChildren<Renderer>(true)))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            foreach (var collider in glove.GetComponentsInChildren<Collider>(true).Concat(sleeve.GetComponentsInChildren<Collider>(true)))
                Destroy(collider);
            glove.localPosition = hand.position;
            glove.localRotation = Quaternion.Euler(hand.euler);
            glove.localScale = new Vector3(hand.left ? -hand.size : hand.size, hand.size, hand.size);
            sleeve.gameObject.SetActive(model.gameObject.activeSelf);
            return new Arm { glove = glove, sleeve = sleeve, left = hand.left, size = hand.size };
        }

        /// <summary>Hides the view model (zoomed in with the sniper) or shows it again.</summary>
        public void Hide(bool hide)
        {
            if (hidden == hide)
                return;
            hidden = hide;
            if (current != null)
                for (var i = 0; i < current.models.Length; i++)
                    SetShown(current, i, !hidden && (i == 0 || dual));
        }

        /// <summary>A shot left: the gun kicks back, and two pistols take turns.</summary>
        public void Kick()
        {
            recoil = Mathf.Min(recoil + 1, 2.5f);
            if (dual)
                nextMuzzle = 1 - nextMuzzle;
        }

        /// <summary>The minigun's barrels turning, 0 to 1 of full speed: the whole gun shivers with it.</summary>
        public void Spin(float fraction) => spinAngle = fraction;

        private Held Get(string model)
        {
            if (models.TryGetValue(model, out var held))
                return held;
            var look = assets != null ? assets.LookOf(model) : null;
            if (look == null || look.model == null)
            {
                models[model] = null;
                return null;
            }
            // Two of the pistol, for dual pistols; one of everything else
            var count = model == WeaponIds.Pistol ? 2 : 1;
            held = new Held { look = look, models = new Transform[count], muzzles = new Transform[count] };
            for (var i = 0; i < count; i++)
            {
                var instance = Instantiate(look.model, transform, false);
                instance.name = i == 0 ? model : $"{model} (left)";
                foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                    Destroy(collider);
                if (FirstPerson)
                    foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                    {
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                        renderer.receiveShadows = false;
                    }
                else
                    KeepInSight(instance);
                var muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(instance.transform, false);
                muzzle.localPosition = look.muzzle;
                instance.SetActive(false);
                held.models[i] = instance.transform;
                held.muzzles[i] = muzzle;
            }
            models[model] = held;
            return held;
        }

        /// <summary>
        /// A weapon in a scout's hand is small (a pistol about 17 cm), and its model's LOD group, made for weapons lying about, culls it
        /// below a hundredth of the screen's height: from about 15 m, so a bot shooting from further had an empty hand. Its last LOD
        /// now stays down to <see cref="HeldCullHeight"/>, a pistol's up to about 80 m.
        /// </summary>
        private static void KeepInSight(GameObject instance)
        {
            if (!instance.TryGetComponent<LODGroup>(out var group))
                return;
            var lods = group.GetLODs();
            if (lods.Length == 0)
                return;
            var last = lods.Length - 1;
            lods[last].screenRelativeTransitionHeight = Mathf.Min(lods[last].screenRelativeTransitionHeight, HeldCullHeight);
            group.SetLODs(lods);
        }

        /// <summary>The share of the screen's height below which a held weapon is no longer drawn.</summary>
        private const float HeldCullHeight = 0.002f;

        private void LateUpdate()
        {
            if (current == null)
                return;
            recoil = Mathf.MoveTowards(recoil, 0, settle * Time.deltaTime * Mathf.Max(0.3f, recoil));
            if (FirstPerson)
                PlaceInView();
            else
                PlaceInHands();
        }

        /// <summary>The view model in the camera's corner: the grip where the look puts it, scaled down, kicking back with each shot.</summary>
        private void PlaceInView()
        {
            var look = current.look;
            var scale = assets.viewScale;
            var shiver = spinAngle > 0 ? new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0) * (0.004f * spinAngle) : Vector3.zero;
            for (var i = 0; i < current.models.Length; i++)
            {
                var model = current.models[i];
                if (!model.gameObject.activeSelf)
                    continue;
                var side = i == 0 ? 1 : -1;
                var grip = new Vector3(look.viewGrip.x * side, look.viewGrip.y, look.viewGrip.z) * scale;
                var kicked = dual ? (nextMuzzle == i ? 0.4f : 1f) : 1f;
                grip += new Vector3(0, 0.25f, -1) * (kick * recoil * kicked) + shiver;
                var rotation = Quaternion.Euler(-4 * recoil * kicked, look.viewYaw * side, 0);
                model.localRotation = rotation;
                model.localScale = Vector3.one * scale;
                model.localPosition = grip - rotation * (look.grip * scale);
                PlaceSleeves(current, i);
            }
        }

        /// <summary>The sleeves of a model's arms: from each glove's wrist back to its shoulder, a fixed point under the camera's corners.</summary>
        private void PlaceSleeves(Held held, int i)
        {
            if (held.arms == null || held.arms[i] == null)
                return;
            foreach (var arm in held.arms[i])
            {
                var wrist = arm.glove.TransformPoint(FirstPersonArms.Wrist);
                var shoulderLocal = FirstPersonArms.Shoulder * assets.viewScale;
                if (arm.left)
                    shoulderLocal.x = -shoulderLocal.x;
                var shoulder = transform.TransformPoint(shoulderLocal);
                var back = shoulder - wrist;
                var length = back.magnitude;
                if (length < 1e-4f)
                    continue;
                arm.sleeve.SetPositionAndRotation(wrist, Quaternion.LookRotation(-back / length, transform.up));
                var thick = assets.viewScale * arm.size;
                arm.sleeve.localScale = new Vector3(thick, thick, length / transform.lossyScale.z);
            }
        }

        /// <summary>The weapon in the scout's hand (the left hand too for dual pistols), held out in front of it, pointing along the aim.</summary>
        private void PlaceInHands()
        {
            var look = current.look;
            var aim = Quaternion.LookRotation(arsenal.AimDirection);
            for (var i = 0; i < current.models.Length; i++)
            {
                var model = current.models[i];
                if (!model.gameObject.activeSelf)
                    continue;
                var which = hands != null && hands.Length > 0 ? Mathf.Min(i, hands.Length - 1) : -1;
                var hand = which >= 0 ? hands[which] : null;
                var at = hand != null ? hand.position + aim * Vector3.forward * reach[which] : arsenal.transform.position + arsenal.transform.rotation * new Vector3(0.3f * (i == 0 ? 1 : -1), 0.9f, 0.2f);
                var kicked = aim * Quaternion.Euler(-6 * recoil * (dual && nextMuzzle == i ? 0.4f : 1f), 0, 0);
                model.SetPositionAndRotation(at - kicked * (look.grip * (handsAreSockets ? 1 : look.handScale)), kicked);
                model.localScale = Vector3.one * (handsAreSockets ? 1 : look.handScale);
            }
        }
    }
}
