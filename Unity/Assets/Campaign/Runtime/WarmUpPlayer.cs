using System;
using SdfBattle;
using SdfGui;
using SdfPickups;
using UnityEngine;

namespace SdfCampaign
{
    /// <summary>
    /// The meadow's warm-up in the loaded battle scene (ticket 32.7): the match is held, you may walk, jump and fire, and the caption box
    /// shows one prompt at a time ("WASD to walk, mouse to look", "SPACE to jump", walk onto the minigun pod, "Hold the left button to
    /// fire"), each cleared by doing it (the rules are <see cref="WarmUp"/>'s). A bobbing arrow shows the minigun pod. When the last is
    /// cleared the match is let go and Mushroom 7 says what to do. Holding Space or Start for 0.7 s skips it (Escape pauses, as in a
    /// match). Only on a first play: the runner does not start it on a retry or a replay.
    /// </summary>
    public sealed class WarmUpPlayer : MonoBehaviour
    {
        public const string SkipHint = "Hold SPACE to skip";
        public const float WalkMetres = 2.5f, LookDegrees = 25, CloserSeconds = 6;

        private Match match;
        private GuiFlow flow;
        private WarmUp rules;
        private readonly SkipHold skip = new SkipHold();
        private WeaponPickup pod;
        private GameObject arrow;
        private int prompt = -1;
        private Vector3 walkFrom;
        private float yawFrom, looked;
        private float closing = -1;

        public WarmUp Rules => rules;

        /// <summary>The warm-up is over (done or skipped) and the match let go.</summary>
        public bool Over { get; private set; }

        /// <summary>Holds the match with you free to move and starts the prompts.</summary>
        public static WarmUpPlayer Begin(Match match, GuiFlow flow)
        {
            var player = match.gameObject.AddComponent<WarmUpPlayer>();
            player.match = match;
            player.flow = flow;
            player.pod = FindMinigunPod(match);
            player.rules = WarmUp.Default(player.pod != null);
            match.Hold(true);
            WeaponPickup.PickedUp += player.OnPickedUp;
            return player;
        }

        /// <summary>The red side's minigun pod, if the scene has one.</summary>
        private static WeaponPickup FindMinigunPod(Match match)
        {
            if (match.Arena == null || match.Arena.west == null)
                return null;
            foreach (var pod in match.Arena.west.WeaponPods)
                if (pod.weaponId == "minigun")
                    return pod;
            return null;
        }

        /// <summary>Ends the warm-up where it is, the match let go (a retry, leaving).</summary>
        public void Stop()
        {
            if (!Over)
                Finish(false);
            Destroy(this);
        }

        private void OnDestroy()
        {
            WeaponPickup.PickedUp -= OnPickedUp;
            DestroyArrow();
            if (flow != null && flow.Match != null)
                flow.Match.caption.Clear();
        }

        private void OnPickedUp(WeaponPickup taken, GameObject who)
        {
            if (rules != null && taken.weaponId == "minigun" && match.You != null && who == match.You.gameObject)
                rules.Report(WarmUpAction.TakeMinigun);
        }

        private void Update()
        {
            if (match == null || match.You == null)
                return;
            if (Over)
            {
                // Mushroom 7's line stays for a few seconds while the match plays
                closing -= Time.deltaTime;
                if (closing <= 0)
                    Destroy(this);
                return;
            }
            if (skip.Tick(Time.deltaTime, Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.JoystickButton7)))
            {
                Finish(false);
                return;
            }
            Watch();
            if (rules.Finished)
            {
                Finish(true);
                return;
            }
            var view = flow != null ? flow.Match : null;
            if (view != null)
                view.caption.Set("", rules.Prompt.text, 1, skip.Progress, SkipHint);
            Arrow();
        }

        // What you do, as the rules want to hear it
        private void Watch()
        {
            var you = match.You;
            if (rules.Index != prompt)
            {
                prompt = rules.Index;
                walkFrom = you.transform.position;
                yawFrom = you.Controller.Yaw;
                looked = 0;
            }
            var flat = you.transform.position - walkFrom;
            flat.y = 0;
            if (flat.magnitude >= WalkMetres)
                rules.Report(WarmUpAction.Walk);
            var yaw = you.Controller.Yaw;
            looked += Mathf.Abs(Mathf.DeltaAngle(yawFrom, yaw));
            yawFrom = yaw;
            if (looked >= LookDegrees)
                rules.Report(WarmUpAction.Look);
            if (Input.GetButtonDown("Jump") && you.Controller.IsGrounded)
                rules.Report(WarmUpAction.Jump);
            var arsenal = you.GetComponent<Arsenal>();
            if (arsenal != null && arsenal.Firing)
                rules.Report(WarmUpAction.Fire);
        }

        private void Finish(bool completed)
        {
            Over = true;
            DestroyArrow();
            match.Release();
            var view = flow != null ? flow.Match : null;
            if (view == null)
                return;
            // Done or skipped, Mushroom 7 says what the game is: skipping the prompts must not skip how a point is scored
            view.caption.Set(WarmUp.CloserSpeaker, WarmUp.Closer, 1, 0, "");
            closing = CloserSeconds;
        }

        // ---------------------------------------------------------------- The arrow over the minigun pod

        private void Arrow()
        {
            var wanted = pod != null && rules.Prompt != null && (rules.Prompt.needs & WarmUpAction.TakeMinigun) != 0;
            if (!wanted)
            {
                DestroyArrow();
                return;
            }
            if (arrow == null)
                arrow = MakeArrow();
            var bob = Mathf.Sin(Time.time * 4) * 0.25f;
            arrow.transform.position = pod.transform.position + Vector3.up * (3.2f + bob);
            arrow.transform.rotation = Quaternion.Euler(0, Time.time * 90, 0);
        }

        private void DestroyArrow()
        {
            if (arrow != null)
                Destroy(arrow);
            arrow = null;
        }

        // A gold pyramid pointing down, double sided and unlit (a material that is always in a build)
        private static GameObject MakeArrow()
        {
            var go = new GameObject("Minigun Arrow");
            var mesh = new Mesh { name = "Arrow" };
            const float r = 0.55f, h = 1.1f, top = 1.1f;
            var tip = new Vector3(0, 0, 0);
            var ring = new[]
            {
                new Vector3(r, top, 0), new Vector3(0, top, r), new Vector3(-r, top, 0), new Vector3(0, top, -r),
            };
            var vertices = new[] { tip, ring[0], ring[1], ring[2], ring[3], new Vector3(0, top + h * 0.2f, 0) };
            mesh.vertices = vertices;
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 4, 3, 0, 1, 4, 5, 1, 2, 5, 2, 3, 5, 3, 4, 5, 4, 1 };
            var colour = new Color(1f, 0.78f, 0.15f);
            mesh.colors = new[] { colour, colour, colour, colour, colour, colour };
            mesh.RecalculateNormals();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var shader = Shader.Find("Sprites/Default");
            go.AddComponent<MeshRenderer>().sharedMaterial = shader != null ? new Material(shader) { color = Color.white } : null;
            return go;
        }
    }
}
