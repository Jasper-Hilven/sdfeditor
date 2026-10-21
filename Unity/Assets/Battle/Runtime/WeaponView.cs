using System.Collections.Generic;
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
        private Held current;
        private bool dual;
        private int nextMuzzle;
        private float recoil;
        private float spinAngle;
        private bool hidden;

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
            var body = arsenal.Player.body;
            // A bot's weapons go with its body, so they vanish with it when it dies
            root.SetParent(view != null ? view.transform : body != null ? body.transform : arsenal.transform, false);
            var weaponView = root.gameObject.AddComponent<WeaponView>();
            weaponView.arsenal = arsenal;
            weaponView.assets = assets;
            weaponView.viewCamera = view != null ? view.transform : null;
            if (view == null && body != null)
                weaponView.hands = FindHands(body.transform);
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

        /// <summary>Holds this weapon (dual-pistols: two pistols).</summary>
        public void Show(string weapon)
        {
            if (current != null)
                foreach (var model in current.models)
                    model.gameObject.SetActive(false);
            dual = weapon == WeaponIds.DualPistols;
            current = Get(WeaponIds.ModelOf(weapon));
            nextMuzzle = 0;
            recoil = 0;
            if (current == null)
                return;
            for (var i = 0; i < current.models.Length; i++)
                current.models[i].gameObject.SetActive(!hidden && (i == 0 || dual));
        }

        /// <summary>Hides the view model (zoomed in with the sniper) or shows it again.</summary>
        public void Hide(bool hide)
        {
            if (hidden == hide)
                return;
            hidden = hide;
            if (current != null)
                for (var i = 0; i < current.models.Length; i++)
                    current.models[i].gameObject.SetActive(!hidden && (i == 0 || dual));
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
            }
        }

        /// <summary>The weapon in the scout's hand (the left hand too for dual pistols), pointing along the aim.</summary>
        private void PlaceInHands()
        {
            var look = current.look;
            var aim = Quaternion.LookRotation(arsenal.AimDirection);
            for (var i = 0; i < current.models.Length; i++)
            {
                var model = current.models[i];
                if (!model.gameObject.activeSelf)
                    continue;
                var hand = hands != null && hands.Length > 0 ? hands[Mathf.Min(i, hands.Length - 1)] : null;
                var at = hand != null ? hand.position : arsenal.transform.position + arsenal.transform.rotation * new Vector3(0.3f * (i == 0 ? 1 : -1), 0.9f, 0.2f);
                var kicked = aim * Quaternion.Euler(-6 * recoil * (dual && nextMuzzle == i ? 0.4f : 1f), 0, 0);
                model.SetPositionAndRotation(at - kicked * (look.grip * look.handScale), kicked);
                model.localScale = Vector3.one * look.handScale;
            }
        }
    }
}
