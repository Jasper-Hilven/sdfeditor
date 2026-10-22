using System;
using System.Collections.Generic;
using SdfBattle;
using SdfGui;
using SdfLevel;
using UnityEngine;

namespace SdfCampaign
{
    /// <summary>
    /// Plays a <see cref="Beat"/> in the scene that is loaded (ticket 32.7): scouts of the beat's actors stand where the data puts them,
    /// the scene's camera moves slowly round the shot's focus, and the caption box shows one line at a time (the <see cref="GuiScreen.Cutscene"/>
    /// screen, no HUD). The timing and the skip rule are <see cref="BeatSequencer"/>'s: a click or Space moves on, holding Space, Escape or
    /// Start for 0.7 s jumps to the hand-over. After the last shot the camera moves into your eyes; with <c>handOver</c> the scene is then
    /// put back as it was and CLICK TO PLAY waits for the click a browser needs to lock the pointer, and only then <c>onDone</c> runs
    /// (the campaign lets the match go there). Without it (a replay) <c>onDone</c> runs as the arrival begins.
    /// <para>
    /// It only needs a <see cref="BeatWorld"/>, so the ending (32.8) reuses it in its own scene.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class BeatPlayer : MonoBehaviour
    {
        public const string SkipHint = "Hold SPACE to skip";
        public const string ClickToPlay = "CLICK TO PLAY";

        private Beat beat;
        private BeatWorld world;
        private GuiFlow flow;
        private Action onDone;
        private bool handOver;

        private BeatSequencer sequencer;
        private readonly List<GameObject> puppets = new List<GameObject>();
        private readonly Dictionary<string, (Vector3 position, float yaw)> places = new Dictionary<string, (Vector3, float)>();
        private readonly List<GameObject> hidden = new List<GameObject>();
        private Transform cameraParent;
        private Vector3 cameraLocalPosition;
        private Quaternion cameraLocalRotation;
        private FirstPersonController readsInput;
        private bool inputWas;
        private bool begun, waiting, finished;
        private Vector3 lastPosition;
        private Quaternion lastRotation;
        private Vector3 eyePosition;
        private Quaternion eyeRotation;

        public BeatSequencer Sequencer => sequencer;

        /// <summary>The beat is over (and, with a hand-over, the click was made).</summary>
        public bool Finished => finished;

        /// <summary>Waiting for the click that hands the scene over.</summary>
        public bool WaitingForClick => waiting;

        /// <summary>
        /// Sets the beat up in the scene, still covered by the loader's black: the actors stand, the camera is on the first shot, the
        /// Cutscene screen is up. It starts to run on <see cref="Begin"/>.
        /// </summary>
        public static BeatPlayer Prepare(Beat beat, BeatWorld world, GuiFlow flow, Action onDone, bool handOver)
        {
            var player = new GameObject("Beat " + beat.id).AddComponent<BeatPlayer>();
            player.beat = beat;
            player.world = world;
            player.flow = flow;
            player.onDone = onDone;
            player.handOver = handOver;
            player.sequencer = new BeatSequencer(beat);
            player.SetUp();
            return player;
        }

        public void Begin() => begun = true;

        // ---------------------------------------------------------------- Setting the stage

        private void SetUp()
        {
            var camera = world.camera;
            if (camera != null)
            {
                cameraParent = camera.transform.parent;
                cameraLocalPosition = camera.transform.localPosition;
                cameraLocalRotation = camera.transform.localRotation;
                eyePosition = camera.transform.position;
                eyeRotation = camera.transform.rotation;
                // The player's own input would turn the view under the camera's feet
                readsInput = camera.GetComponentInParent<FirstPersonController>();
                if (readsInput != null)
                {
                    inputWas = readsInput.readsInput;
                    readsInput.readsInput = false;
                }
            }
            foreach (var go in world.hide)
                if (go != null && go.activeSelf)
                {
                    go.SetActive(false);
                    hidden.Add(go);
                }
            foreach (var actor in beat.actors)
                Stand(actor);
            if (flow != null)
            {
                flow.Show(GuiScreen.Cutscene);
                if (flow.Match != null)
                    flow.Match.caption.Clear();
            }
            Aim(0);
            lastPosition = camera != null ? camera.transform.position : Vector3.zero;
            lastRotation = camera != null ? camera.transform.rotation : Quaternion.identity;
        }

        private void Stand(BeatActor actor)
        {
            var (anchor, yaw) = world.frame(actor.anchor);
            var position = anchor + Quaternion.Euler(0, yaw, 0) * new Vector3(actor.right, 0, actor.forward);
            position = Grounded(position);
            var facing = yaw + actor.facing;
            places[actor.name] = (position, facing);
            var prefab = world.scout != null ? world.scout(actor.team) : null;
            if (prefab == null)
                return;
            var puppet = Instantiate(prefab, position, Quaternion.Euler(0, facing, 0));
            puppet.name = actor.name;
            // Only the real players collide or are hit
            foreach (var collider in puppet.GetComponentsInChildren<Collider>(true))
                Destroy(collider);
            puppet.AddComponent<ScoutBody>();
            puppets.Add(puppet);
        }

        // The ground under a spot, ignoring the players' capsules (they are about to be hidden, and stand at the same place)
        private static Vector3 Grounded(Vector3 position)
        {
            var hits = Physics.RaycastAll(position + Vector3.up * 4, Vector3.down, 12, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
                if (hit.collider.GetComponentInParent<Player>() == null)
                    return hit.point + Vector3.up * 0.02f;
            return position;
        }

        // ---------------------------------------------------------------- Playing

        private void Update()
        {
            if (!begun || finished || sequencer == null)
                return;
            if (waiting)
            {
                if (SceneLoader.AutoPlay || Input.GetMouseButtonDown(0) || Cursor.lockState == CursorLockMode.Locked)
                    Handed();
                return;
            }
            var skipHeld = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.Escape) || Input.GetKey(KeyCode.JoystickButton7);
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space))
                sequencer.Advance();
            sequencer.Tick(Time.unscaledDeltaTime, skipHeld);
            Caption();
            if (sequencer.Stage != BeatStage.Shots && !handOver)
                Finish();
            else if (sequencer.Done)
                Finish();
        }

        private void LateUpdate()
        {
            if (sequencer == null || finished || waiting || world.camera == null)
                return;
            if (sequencer.Stage == BeatStage.Shots)
                Aim(sequencer.ShotProgress);
            else
            {
                // The arrival: from where the last shot ended into your eyes
                var t = Mathf.SmoothStep(0, 1, sequencer.Arrival);
                world.camera.transform.SetPositionAndRotation(Vector3.Lerp(lastPosition, EyePosition(), t), Quaternion.Slerp(lastRotation, EyeRotation(), t));
            }
        }

        private Vector3 EyePosition() => cameraParent != null ? cameraParent.TransformPoint(cameraLocalPosition) : eyePosition;

        private Quaternion EyeRotation() => cameraParent != null ? cameraParent.rotation * cameraLocalRotation : eyeRotation;

        private void Caption()
        {
            var view = flow != null ? flow.Match : null;
            if (view == null)
                return;
            var shot = sequencer.Shot;
            view.caption.Set(shot != null ? shot.speaker : "", shot != null ? shot.line : "", sequencer.CaptionAlpha, sequencer.SkipProgress, SkipHint);
        }

        // The camera on the shot's move at progress t (0 to 1)
        private void Aim(float t)
        {
            var camera = world.camera;
            var shot = sequencer.Shot;
            if (camera == null || shot == null)
                return;
            var move = shot.move;
            var (focus, yaw) = Focus(move);
            var e = Mathf.SmoothStep(0, 1, t);
            var orbit = Mathf.Lerp(move.orbitFrom, move.orbitTo, e);
            var distance = Mathf.Lerp(move.distanceFrom, move.distanceTo, e);
            var height = Mathf.Lerp(move.heightFrom, move.heightTo, e);
            var position = focus + Quaternion.Euler(0, yaw + orbit, 0) * new Vector3(0, 0, -distance) + Vector3.up * height;
            var look = focus + Vector3.up * move.lookHeight;
            var rotation = Quaternion.LookRotation(look - position, Vector3.up);
            camera.transform.SetPositionAndRotation(position, rotation);
            lastPosition = position;
            lastRotation = rotation;
        }

        private (Vector3 position, float yaw) Focus(CameraMove move)
        {
            if (!string.IsNullOrEmpty(move.actor) && places.TryGetValue(move.actor, out var place))
                return place;
            var (anchor, yaw) = world.frame(move.anchor);
            return (anchor, yaw);
        }

        // ---------------------------------------------------------------- Handing over

        private void Finish()
        {
            if (finished)
                return;
            if (!handOver)
            {
                finished = true;
                Restore();
                Done();
                return;
            }
            // Back to the scene as it was, the view in your eyes; the click is the web's one chance to lock the pointer
            Restore();
            if (flow != null)
            {
                flow.Show(GuiScreen.Hud);
                if (flow.Match != null)
                    flow.Match.caption.Set("", ClickToPlay, 1, 0, "");
            }
            waiting = true;
        }

        private void Handed()
        {
            waiting = false;
            finished = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Done();
        }

        private void Done()
        {
            if (flow != null && flow.Match != null)
                flow.Match.caption.Clear();
            var callback = onDone;
            onDone = null;
            callback?.Invoke();
            Destroy(gameObject);
        }

        private void Restore()
        {
            foreach (var puppet in puppets)
                if (puppet != null)
                    Destroy(puppet);
            puppets.Clear();
            foreach (var go in hidden)
                if (go != null)
                    go.SetActive(true);
            hidden.Clear();
            var camera = world.camera;
            if (camera != null)
            {
                camera.transform.localPosition = cameraLocalPosition;
                camera.transform.localRotation = cameraLocalRotation;
                if (cameraParent == null)
                    camera.transform.SetPositionAndRotation(eyePosition, eyeRotation);
            }
            if (readsInput != null)
                readsInput.readsInput = inputWas;
        }

        private void OnDestroy()
        {
            // Left in the middle (the scene unloaded): put the view back
            if (!finished && world != null)
                Restore();
        }
    }
}
