using System;
using System.Collections;
using System.Collections.Generic;
using SdfBattle;
using SdfGui;
using SdfLevel;
using SdfPickups;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SdfCampaign.Editor
{
    /// <summary>
    /// The touch controls' play test (ticket 56): Boot in Play mode on a forced touch device (the editor's mobile switch), the touchscreen
    /// replaced by a fake feed of fingers (<see cref="TouchControls.Feed"/>, as <c>GameKeys.Source</c> fakes a keyboard for the five-match
    /// play test), and a first match, with its warm-up, played with fingers only:
    /// <list type="bullet">
    /// <item>the touch layer shows, the touch source is registered, and the warm-up prompts speak of thumbs, not keys;</item>
    /// <item>a left finger pushes the joystick (full speed past the rim) while a right finger swipes the look: the yaw turns what the
    /// swipe's width says; a tap on JUMP jumps; the walk onto the minigun pod; FIRE held fires, and a drag from it looks as well;</item>
    /// <item>the weapon slots switch weapons; ZOOM (a sniper in hand) zooms in and out; the rage meter full shows the powers and a tap
    /// uses one; SCORE held shows the scoreboard and its release goes back; PAUSE pauses and the layer goes with the HUD;</item>
    /// <item>the auto-fire holds the trigger only while the crosshair is on an enemy (not on a teammate, not at the sky, not with the
    /// setting off) and the aim assist slows the look near an enemy and nowhere else;</item>
    /// <item>the settings show the touch rows; the end card's hint speaks of the arrows and a tap skips its countdown.</item>
    /// </list>
    /// Batch: <c>SDF_NO_QUIT=1 tools/unity-batch.sh touchtest -executeMethod SdfCampaign.Editor.TouchPlayTest.Run</c> (it exits itself, code 1 on a failure).
    /// </summary>
    [InitializeOnLoad]
    public static class TouchPlayTest
    {
        private const string ActiveKey = "SdfCampaign.TouchPlayTest";

        private static readonly List<string> errors = new List<string>();
        private static readonly List<string> trace = new List<string>();
        private static readonly Dictionary<int, Vector2> fingers = new Dictionary<int, Vector2>();
        private static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        private static bool finished, mobileBefore;
        private static float started = -1;

        static TouchPlayTest()
        {
            if (!SessionState.GetBool(ActiveKey, false))
                return;
            SceneLoader.AutoPlay = true;
            Application.logMessageReceived += (message, stackTrace, type) =>
            {
                if ((type == LogType.Exception || type == LogType.Error) && !stackTrace.Contains("UnityEditor.Search"))
                    errors.Add(message + "\n" + stackTrace);
            };
            EditorApplication.update += Update;
        }

        public static void Run()
        {
            PlayerPrefs.DeleteKey(GameSave.Key);
            PlayerPrefs.DeleteKey("gui.touchLook");
            PlayerPrefs.DeleteKey("gui.autoFire");
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(ActiveKey + ".before", EditorPrefs.GetBool(SdfPlatform.Device.EditorMobileKey, false));
            EditorPrefs.SetBool(SdfPlatform.Device.EditorMobileKey, true);
            EditorSceneManager.OpenScene(BootBuilder.ScenePath);
            EditorApplication.EnterPlaymode();
        }

        // ---------------------------------------------------------------- The runner: a script of waits

        private static void Update()
        {
            if (finished || !EditorApplication.isPlaying)
                return;
            var now = Time.realtimeSinceStartup;
            if (started < 0)
            {
                started = now;
                TouchControls.Feed = () => FeedNow();
                stack.Push(Script());
            }
            if (now - started > 600)
            {
                errors.Add("timed out");
                Finish();
                return;
            }
            try
            {
                while (stack.Count > 0)
                {
                    var top = stack.Peek();
                    if (!top.MoveNext())
                    {
                        stack.Pop();
                        continue;
                    }
                    if (top.Current is IEnumerator nested)
                    {
                        stack.Push(nested);
                        continue;
                    }
                    return;
                }
                trace.Add("the script ran to its end");
                Finish();
            }
            catch (Exception exception)
            {
                errors.Add(exception.ToString());
                Finish();
            }
        }

        private static readonly List<TouchSample> feed = new List<TouchSample>();

        private static IReadOnlyList<TouchSample> FeedNow()
        {
            feed.Clear();
            foreach (var pair in fingers)
                feed.Add(new TouchSample { id = pair.Key, position = pair.Value });
            return feed;
        }

        private static void Expect(bool ok, string what)
        {
            if (!ok)
                throw new Exception("Expected: " + what);
        }

        private static IEnumerator Until(Func<bool> done, float seconds, string what)
        {
            // A busy machine (another Unity build running) runs the editor at a few frames a second: the wait is over only when
            // both the real time (three times the limit) and the frames (30 a limit second) are used up.
            var from = Time.realtimeSinceStartup;
            var fromFrame = Time.frameCount;
            while (!done())
            {
                if (Time.realtimeSinceStartup - from > seconds * 3 && Time.frameCount - fromFrame > seconds * 30)
                    throw new Exception($"timed out ({seconds} s, {Time.frameCount - fromFrame} frames) waiting for {what}");
                yield return null;
            }
        }

        private static IEnumerator For(float seconds)
        {
            // Real time and at least a few frames, so a tap is seen by the game whatever the frame rate
            var from = Time.realtimeSinceStartup;
            var fromFrame = Time.frameCount;
            while (Time.realtimeSinceStartup - from < seconds || Time.frameCount - fromFrame < 4)
                yield return null;
        }

        private static IEnumerator Frames(int count)
        {
            var from = Time.frameCount;
            while (Time.frameCount - from < count)
                yield return null;
        }

        // ---------------------------------------------------------------- Fingers

        private static TouchControls Controls => TouchControls.Current;
        private static TouchLayer Layer => Controls != null ? Controls.Layer : null;

        private static Vector2 CentreOf(TouchButtonKind kind, int index = 0)
        {
            var button = Controls.Button(kind, index);
            Expect(button != null, $"a {kind} {index} button on the touch layer");
            return TouchControls.CentreOf(button);
        }

        // A point of the screen's left or right half that is on no button (a finger there is the stick or the look)
        private static Vector2 FreePoint(bool left, float yShare = 0.5f)
        {
            var w = Screen.width;
            var h = Screen.height;
            for (var dx = 0.0f; dx < 0.25f; dx += 0.02f)
                for (var dy = 0.0f; dy < 0.5f; dy += 0.03f)
                    foreach (var sign in new[] { 1, -1 })
                    {
                        var p = new Vector2(w * ((left ? 0.18f : 0.7f) + dx * (left ? 1 : sign)), h * (yShare + dy * sign));
                        if (p.x > 0 && p.x < w && p.y > 0 && p.y < h && Layer.ButtonAt(p) == null)
                            return p;
                    }
            throw new Exception("no free point on the screen");
        }

        private static IEnumerator Tap(Vector2 at, float seconds = 0.15f)
        {
            fingers[90] = at;
            yield return For(seconds);
            fingers.Remove(90);
            yield return Frames(2);
        }

        private static IEnumerator TapButton(TouchButtonKind kind, int index = 0) => Tap(CentreOf(kind, index));

        // ---------------------------------------------------------------- The aim

        private static void AimAt(Player you, Vector3 point)
        {
            var arsenal = Combat.Current.ArsenalOf(you);
            var to = point - arsenal.EyePosition;
            var flat = new Vector2(to.x, to.z).magnitude;
            you.Controller.SetAim(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -Mathf.Atan2(to.y, flat) * Mathf.Rad2Deg);
        }

        private static Vector3 Chest(Player p) => p.transform.position + Vector3.up * TouchAssist.ChestHeight;

        private static bool Ground(Vector3 around, out Vector3 ground)
        {
            ground = default;
            var count = Physics.RaycastNonAlloc(around + Vector3.up * 8, Vector3.down, hits, 40, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var best = float.MaxValue;
            for (var i = 0; i < count; i++)
                if (Player.Of(hits[i].collider) == null && hits[i].distance < best)
                {
                    best = hits[i].distance;
                    ground = hits[i].point;
                }
            return best < float.MaxValue;
        }

        private static readonly RaycastHit[] hits = new RaycastHit[16];

        // Puts you a distance from the target with nothing between (the shot's own trace says so), facing it
        private static bool Face(Player you, Player target, float distance)
        {
            var combat = Combat.Current;
            for (var k = 0; k < 16; k++)
            {
                var angle = k * 22.5f * Mathf.Deg2Rad;
                var around = target.transform.position + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * distance;
                if (!Ground(around, out var ground))
                    continue;
                var eye = ground + Vector3.up * 1.5f;
                var to = Chest(target) - eye;
                var hit = combat.Trace(eye, to.normalized, to.magnitude + 1, you);
                if (!hit.hit || hit.player != target)
                    continue;
                you.Controller.Teleport(ground + Vector3.up * 0.05f, 0);
                AimAt(you, Chest(target));
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- The script

        private static IEnumerator Script()
        {
            var loader = UnityEngine.Object.FindAnyObjectByType<SceneLoader>();
            Match match = null;
            GuiFlow flow = null;
            yield return Until(() =>
            {
                flow = UnityEngine.Object.FindAnyObjectByType<GuiFlow>();
                var l = UnityEngine.Object.FindAnyObjectByType<SceneLoader>();
                match = Match.Current;
                return flow != null && match != null && match.State != null && l != null && !l.Busy && !match.Held && match.You != null && SceneManager.GetActiveScene().name != "Boot";
            }, 180, "a live match");
            yield return For(1);
            var you = match.You;
            var combat = Combat.Current;
            var arsenal = combat.ArsenalOf(you);
            var inventory = arsenal.Inventory;
            GuiSettings.AutoFire = false;

            // The touch device, its layer and its source
            Expect(SdfPlatform.Device.IsTouch, "the editor's mobile switch makes a touch device");
            var anyControls = UnityEngine.Object.FindAnyObjectByType<TouchControls>(FindObjectsInactive.Include);
            Expect(Controls != null && Layer != null, $"the GUI has its touch controls (found {anyControls}, enabled {(anyControls != null ? anyControls.enabled : false)}, current {Controls}, flow on {flow.gameObject.name} has {flow.GetComponent<TouchControls>()})");
            yield return Until(() => Layer.isActiveAndEnabled, 5, "the touch layer over the HUD");
            Expect(PlayerInput.Sources.Count == 2, $"the touch source is added to the keyboard and mouse ({PlayerInput.Sources.Count} sources)");
            Expect(PlayerInput.Engaged, "a touch device is engaged without a pointer lock");
            trace.Add($"touch device, screen {Screen.width}x{Screen.height}, layer shown, {Layer.buttons.Length} buttons");

            // The warm-up speaks of thumbs
            var warm = UnityEngine.Object.FindAnyObjectByType<WarmUpPlayer>();
            Expect(warm != null, "the first match has the warm-up");
            Expect(warm.Rules.Prompt.text == WarmUp.TouchWalkLook, $"the first prompt is the touch one ({warm.Rules.Prompt.text})");

            // Walk and look with two thumbs at once: the left finger pushes the stick past its rim, the right one swipes
            var yawBefore = you.Controller.Yaw;
            var from = you.transform.position;
            var stickAt = FreePoint(true);
            fingers[1] = stickAt;
            yield return Frames(2);
            Expect(Layer.stickBase.gameObject.activeSelf, "the joystick shows where the finger landed");
            fingers[1] = stickAt + Vector2.up * Layer.StickPixels * 1.6f;
            var lookAt = FreePoint(false);
            var swipe = Screen.width * 0.3f;
            for (var i = 0; i < 12; i++)
            {
                fingers[2] = lookAt + Vector2.right * (swipe * (i + 1) / 12f);
                yield return null;
            }
            Expect(Controls.Move.magnitude > 0.95f, $"a push past the rim is full speed ({Controls.Move})");
            var turned = Mathf.DeltaAngle(yawBefore, you.Controller.Yaw);
            var expected = swipe / Screen.width * 180;
            trace.Add($"look: a swipe of 0.3 screen widths turned {turned:0.0} degrees (180 per width: {expected:0.0})");
            Expect(turned > expected * 0.4f && turned < expected * 1.1f, $"a swipe turns the view by its share of 180 degrees ({turned} for {expected})");
            yield return Until(() => (you.transform.position - from).magnitude > 3, 10, "three metres of walking");
            var forward = Vector3.Dot((you.transform.position - from).normalized, Quaternion.Euler(0, yawBefore, 0) * Vector3.forward);
            trace.Add($"walk: {(you.transform.position - from).magnitude:0.0} m with the stick pushed forward");
            Expect(forward > 0.2f, $"pushing the stick up walks forward, the way the view faced ({forward})");
            fingers.Remove(1);
            fingers.Remove(2);
            yield return For(0.4f);
            Expect(Controls.Move == Vector2.zero, "lifting the finger stops the walk");
            Expect(!Layer.stickBase.gameObject.activeSelf, "the joystick goes with the finger");

            // The warm-up moves on to the jump
            yield return Until(() => warm.Rules.Prompt != null && warm.Rules.Prompt.text == WarmUp.TouchJump, 5, "the jump prompt");
            yield return For(0.5f);
            var jumps = you.Controller.Jumps;
            // A jump asked while airborne is forgotten (no double jump), and a slow editor may take the tap in the air: tap again
            for (var attempt = 0; attempt < 3 && you.Controller.Jumps == jumps; attempt++)
            {
                yield return TapButton(TouchButtonKind.Jump);
                var tapped = Time.realtimeSinceStartup;
                var tappedFrame = Time.frameCount;
                while (you.Controller.Jumps == jumps && (Time.realtimeSinceStartup - tapped < 1.5f || Time.frameCount - tappedFrame < 30))
                    yield return null;
            }
            yield return Until(() => you.Controller.Jumps > jumps, 3, "the jump");
            trace.Add("jump: a tap on JUMP jumped");

            // The warm-up's last prompt is fire (no pod step: picking up is optional); walking onto the minigun pod with the stick is still checked
            yield return Until(() => warm.Rules.Prompt != null && warm.Rules.Prompt.text == WarmUp.TouchFire, 5, "the fire prompt");
            // Walk onto the minigun pod: stood a few metres off, facing it, the stick forward
            WeaponPickup pod = null;
            foreach (var p in UnityEngine.Object.FindObjectsByType<WeaponPickup>(FindObjectsSortMode.None))
                if (p.weaponId == "minigun" && (pod == null || (p.transform.position - you.transform.position).sqrMagnitude < (pod.transform.position - you.transform.position).sqrMagnitude))
                    pod = p;
            Expect(pod != null, "a minigun pod");
            var placed = false;
            for (var k = 0; k < 16 && !placed; k++)
            {
                var angle = k * 22.5f * Mathf.Deg2Rad;
                var around = pod.transform.position + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * 6;
                if (!Ground(around, out var ground) || Mathf.Abs(ground.y - pod.transform.position.y) > 1)
                    continue;
                if (Physics.Linecast(ground + Vector3.up, pod.transform.position + Vector3.up, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    continue;
                var toPod = pod.transform.position - ground;
                you.Controller.Teleport(ground + Vector3.up * 0.05f, Mathf.Atan2(toPod.x, toPod.z) * Mathf.Rad2Deg);
                placed = true;
            }
            Expect(placed, "a clear run at the pod");
            yield return For(0.3f);
            var start = FreePoint(true);
            fingers[1] = start;
            yield return Frames(2);
            fingers[1] = start + Vector2.up * Layer.StickPixels * 1.2f;
            yield return Until(() => inventory.Holds("minigun"), 10, "the minigun from the pod");
            fingers.Remove(1);
            trace.Add("pod: walked onto the minigun pod with the stick");

            // Fire: the big button held fires, and a drag from it looks as well (one thumb aims and fires)
            yield return Until(() => warm.Rules.Prompt != null && warm.Rules.Prompt.text == WarmUp.TouchFire, 5, "the fire prompt");
            var fireAt = CentreOf(TouchButtonKind.Fire);
            yawBefore = you.Controller.Yaw;
            fingers[3] = fireAt;
            yield return Frames(3);
            for (var i = 0; i < 10; i++)
            {
                fingers[3] = fireAt + Vector2.right * (Screen.width * 0.03f * (i + 1) / 10f);
                yield return null;
            }
            yield return Until(() => arsenal.Firing, 5, "the minigun firing");
            Expect(Mathf.Abs(Mathf.DeltaAngle(yawBefore, you.Controller.Yaw)) > 1, "a drag that starts on FIRE looks as well");
            yield return Until(() => warm.Over, 5, "the warm-up done by that fire");
            fingers.Remove(3);
            yield return For(0.5f);
            Expect(!arsenal.Firing, "lifting the finger stops the fire");
            trace.Add("fire: FIRE held fired the minigun and a drag from it looked");

            // The weapon slots
            var minigunSlot = inventory.Current;
            WeaponSlot? other = null;
            for (var i = 0; i < SdfBattle.Inventory.SlotCount; i++)
                if (inventory.Has((WeaponSlot)i) && (WeaponSlot)i != minigunSlot)
                    other = (WeaponSlot)i;
            Expect(other.HasValue, "a second weapon to switch to");
            yield return TapButton(TouchButtonKind.Slot, (int)other.Value);
            Expect(inventory.Current == other.Value, $"a tap on slot {(int)other.Value + 1} takes that weapon ({inventory.Current})");
            yield return TapButton(TouchButtonKind.Slot, (int)minigunSlot);
            Expect(inventory.Current == minigunSlot, "a tap on the minigun's slot takes it back");
            trace.Add("slots: tapped between two weapons");

            // Zoom is the sniper's: shown with it, a tap in, a tap out
            Expect(!Layer.Find(TouchButtonKind.Zoom).isActiveAndEnabled, "no zoom button without the sniper in hand");
            inventory.Take("sniper");
            yield return Until(() => arsenal.Weapon != null && arsenal.Weapon.zoomFieldOfView > 0 && Layer.Find(TouchButtonKind.Zoom).isActiveAndEnabled, 5, "the zoom button with the sniper");
            yield return TapButton(TouchButtonKind.Zoom);
            yield return Until(() => arsenal.Zoomed, 5, "the zoom in");
            yield return TapButton(TouchButtonKind.Zoom);
            yield return Until(() => !arsenal.Zoomed, 5, "the zoom out");
            trace.Add("zoom: the sniper zoomed in and out by taps");
            inventory.Select(minigunSlot);

            // The powers: shown when the rage meter is full
            var spores = Spores.Current;
            Expect(spores != null, "the match has spores");
            Expect(!Layer.Find(TouchButtonKind.Power, 1).isActiveAndEnabled, "no power button before the meter is full");
            spores.State.Enabled = true;
            spores.State.Gain(you.State, 100000);
            yield return Until(() => Layer.Find(TouchButtonKind.Power, 1).isActiveAndEnabled, 5, "the power buttons with a full meter");
            yield return TapButton(TouchButtonKind.Power, 1);
            yield return Until(() => spores.State.IsOn(you.State, SporePower.Regenerate), 5, "Regenerate from the tap");
            trace.Add("powers: a full meter showed them and a tap used Regenerate");

            // The scoreboard is held; the pause pauses and takes the layer with the HUD
            fingers[4] = CentreOf(TouchButtonKind.Scoreboard);
            yield return Until(() => flow.screen == GuiScreen.Scoreboard, 3, "the scoreboard while held");
            yield return Frames(3);
            Expect(Layer.isActiveAndEnabled, "the layer stays while the scoreboard is held, under the finger");
            fingers.Remove(4);
            yield return Until(() => flow.screen == GuiScreen.Hud, 3, "the HUD when the finger lifts");
            yield return TapButton(TouchButtonKind.Pause);
            yield return Until(() => flow.screen == GuiScreen.Pause, 3, "the pause menu from PAUSE");
            yield return Frames(3);
            Expect(!Layer.isActiveAndEnabled, "the touch layer goes with the HUD under the pause menu");
            // The settings: the touch rows show on this device, and the sliders write the settings
            flow.Show(GuiScreen.Settings);
            yield return Frames(3);
            var menu = UnityEngine.Object.FindAnyObjectByType<GuiSettingsMenu>(FindObjectsInactive.Include);
            Expect(menu != null && menu.touchRows.Length == 2 && menu.touchRows[0].activeInHierarchy && menu.touchRows[1].activeInHierarchy, "the settings show the touch look and the auto-fire");
            menu.touchLook.value = 0.9f;
            menu.autoFire.isOn = true;
            Expect(Mathf.Approximately(GuiSettings.TouchLook, 0.9f) && GuiSettings.AutoFire, "the settings are written");
            GuiSettings.TouchLook = TouchRules.DefaultLook;
            flow.Show(GuiScreen.Pause);
            flow.Execute(GuiCommandId.Resume, GuiScreen.Hud);
            yield return Until(() => flow.screen == GuiScreen.Hud && Layer.isActiveAndEnabled, 3, "the HUD and its layer back");
            trace.Add("scoreboard held and let go, pause paused, settings show the touch rows");

            // Auto-fire and the aim assist: on an enemy, on a teammate, at the sky, with the setting off
            inventory.Select(minigunSlot);
            Player enemy = null, mate = null;
            foreach (var p in match.Players)
            {
                if (p == you || p.State == null || !p.State.Alive)
                    continue;
                if (p.Team != you.Team && enemy == null)
                    enemy = p;
                else if (p.Team == you.Team && mate == null)
                    mate = p;
            }
            Expect(enemy != null && mate != null, "an enemy and a teammate");
            // At the sky: nothing to shoot, no slowing
            you.Controller.SetAim(you.Controller.Yaw, -80);
            GuiSettings.AutoFire = true;
            yield return For(0.5f);
            Expect(!Controls.AutoFiring, "no auto-fire at the sky");
            Expect(Controls.LookScale > 0.99f, $"no slowing of the look with no enemy near the crosshair ({Controls.LookScale})");
            // On a teammate in front of you (and what is behind it): no fire
            var checkedFrames = 0;
            Expect(Face(you, mate, 6), "a clear view of the teammate");
            for (var i = 0; i < 20; i++)
            {
                if (!mate.State.Alive)
                    break;
                AimAt(you, Chest(mate));
                var hit = combat.Trace(arsenal.EyePosition, arsenal.AimDirection, 60, you);
                yield return null;
                if (hit.hit && hit.player == mate)
                {
                    checkedFrames++;
                    Expect(!Controls.AutoFiring, "no auto-fire on a teammate");
                }
            }
            Expect(checkedFrames >= 3, $"the crosshair stayed on the teammate for a few frames ({checkedFrames})");
            trace.Add($"auto-fire: none at the sky or on a teammate ({checkedFrames} frames checked)");
            // On an enemy: it fires, and the look slows
            Expect(Face(you, enemy, 14), "a clear view of an enemy");
            // A quick swipe across the enemy (the finger sweeps 400 degrees a second through it): no shot, and no slowing of the swipe
            {
                AimAt(you, Chest(enemy));
                var enemyYaw = you.Controller.Yaw;
                var pitch = you.Controller.Pitch;
                you.Controller.SetAim(enemyYaw - 25, pitch);
                yield return Frames(2);
                var swipeFrom = FreePoint(false);
                fingers[2] = swipeFrom;
                yield return Frames(2);
                var swipeStart = Time.realtimeSinceStartup;
                var swipeFired = false;
                var swipeSlowed = false;
                while (Mathf.DeltaAngle(enemyYaw, you.Controller.Yaw) < 25 && Time.realtimeSinceStartup - swipeStart < 3)
                {
                    var degrees = (Time.realtimeSinceStartup - swipeStart) * 400;
                    fingers[2] = swipeFrom + Vector2.right * (degrees / 180 * Screen.width);
                    yield return null;
                    swipeFired |= Controls.AutoFiring;
                    swipeSlowed |= Controls.LookScale < 0.99f;
                }
                fingers.Remove(2);
                yield return Frames(2);
                trace.Add($"swipe across an enemy: fired {swipeFired}, slowed {swipeSlowed}");
                Expect(Mathf.DeltaAngle(enemyYaw, you.Controller.Yaw) >= 25, "the swipe went across the enemy");
                Expect(!swipeFired, "a quick swipe across an enemy fires no shot");
                Expect(!swipeSlowed, "the aim assist does not slow a quick swipe");
                yield return For(0.3f);
            }
            var fired = false;
            var slowed = 1f;
            var onSince = -1f;
            var dwelled = 0f;
            var until = Time.realtimeSinceStartup + 6;
            while (Time.realtimeSinceStartup < until && !(fired && slowed < 1))
            {
                if (!enemy.State.Alive || !you.State.Alive)
                    break;
                AimAt(you, Chest(enemy));
                yield return null;
                var hit = combat.Trace(arsenal.EyePosition, arsenal.AimDirection, 100, you);
                if (hit.hit && hit.player == enemy)
                {
                    if (onSince < 0)
                        onSince = Time.realtimeSinceStartup;
                    if (!fired && Controls.AutoFiring)
                        dwelled = Time.realtimeSinceStartup - onSince;
                    fired |= Controls.AutoFiring;
                    slowed = Mathf.Min(slowed, Controls.LookScale);
                }
            }
            trace.Add($"auto-fire on an enemy: fired {fired} after {dwelled:0.00} s, look scale {slowed:0.00}");
            Expect(!fired || dwelled >= TouchRules.MinigunDwell * 0.7f, $"holding on an enemy fires only after the minigun's dwell ({dwelled} s)");
            Expect(fired || !enemy.State.Alive || !you.State.Alive, "the auto-fire fires with the crosshair on an enemy");
            Expect(slowed <= TouchRules.AssistSlowest + 0.05f || !enemy.State.Alive || !you.State.Alive, $"the aim assist slows the look on an enemy ({slowed})");
            // The setting off: no auto-fire on an enemy
            if (enemy.State.Alive && you.State.Alive)
            {
                GuiSettings.AutoFire = false;
                yield return For(0.4f);
                var enemyFrames = 0;
                for (var i = 0; i < 15 && enemy.State.Alive && you.State.Alive; i++)
                {
                    AimAt(you, Chest(enemy));
                    yield return null;
                    var hit = combat.Trace(arsenal.EyePosition, arsenal.AimDirection, 100, you);
                    if (hit.hit && hit.player == enemy)
                    {
                        enemyFrames++;
                        Expect(!Controls.AutoFiring, "no auto-fire with the setting off");
                    }
                }
                trace.Add($"auto-fire off: none on an enemy ({enemyFrames} frames checked)");
            }
            GuiSettings.AutoFire = true;

            // The minigun's early auto-fire: an enemy NEAR the crosshair (not under it) starts it; a teammate in the line of fire stops it
            if (enemy.State.Alive && you.State.Alive && mate.State.Alive && Face(you, enemy, 14))
            {
                inventory.Select(minigunSlot);
                yield return For(0.6f);
                var nearFired = false;
                var framesNear = 0;
                for (var i = 0; i < 60 && !nearFired && enemy.State.Alive && you.State.Alive; i++)
                {
                    AimAt(you, Chest(enemy));
                    you.Controller.SetAim(you.Controller.Yaw + 3.5f, you.Controller.Pitch);
                    yield return null;
                    var hit = combat.Trace(arsenal.EyePosition, arsenal.AimDirection, 100, you);
                    if (hit.hit && hit.player == enemy)
                        continue;
                    framesNear++;
                    nearFired |= Controls.AutoFiring;
                }
                trace.Add($"minigun near an enemy: fired {nearFired} ({framesNear} frames off the body)");
                Expect(nearFired || !enemy.State.Alive || !you.State.Alive, "the minigun auto-fires with an enemy near the crosshair, not under it");
                // A teammate on the line to the enemy: no fire, though the enemy is near the crosshair
                if (enemy.State.Alive && you.State.Alive && mate.State.Alive && Face(you, enemy, 14))
                {
                    yield return For(0.5f);
                    var blockedFrames = 0;
                    var blockStart = Time.realtimeSinceStartup;
                    for (var i = 0; i < 400 && blockedFrames < 6 && enemy.State.Alive && you.State.Alive && mate.State.Alive; i++)
                    {
                        var eye = arsenal.EyePosition;
                        var middle = eye + (Chest(enemy) - eye) * 0.45f;
                        if (Ground(middle, out var groundAt))
                            mate.Controller.Teleport(groundAt + Vector3.up * 0.05f, mate.Controller.Yaw);
                        AimAt(you, Chest(enemy));
                        you.Controller.SetAim(you.Controller.Yaw + 0.8f, you.Controller.Pitch);
                        yield return null;
                        var hit = combat.Trace(arsenal.EyePosition, arsenal.AimDirection, 100, you);
                        if (i >= 4 && Time.realtimeSinceStartup - blockStart > 0.4f && hit.hit && hit.player == mate)
                        {
                            blockedFrames++;
                            Expect(!Controls.AutoFiring, "the minigun does not auto-fire with a teammate in the line of fire");
                        }
                    }
                    trace.Add($"minigun with a teammate in the way: no fire ({blockedFrames} frames checked)");
                }
            }

            // The poison-gun and the blob-gun by touch: pick up (a pod when the level has one), select by slot, tap and hold FIRE, auto-fire on their arc
            if (enemy.State.Alive && you.State.Alive)
            {
                yield return LobbedGun(WeaponIds.PoisonGun, match, you, enemy, mate, minigunSlot);
                yield return LobbedGun(WeaponIds.BlobGun, match, you, enemy, mate, minigunSlot);
                inventory.Select(minigunSlot);
            }
            GuiSettings.AutoFire = true;

            // The end: three captures, then the card's touch wording and a tap that skips the countdown
            if (!you.State.Alive)
                yield return Until(() => you.State.Alive, 15, "your respawn");
            var yours = match.yourTeam;
            for (var i = 0; i < 3; i++)
                match.State.ReportCapture(yours);
            yield return Until(() => (flow.screen == GuiScreen.Victory || flow.screen == GuiScreen.Defeat) && !flow.Ending, 30, "the end card");
            yield return For(1);
            var card = flow.GetComponentInChildren<GuiEndScreen>(false);
            Expect(card != null && card.isActiveAndEnabled, "the end card");
            Expect(card.keyHint.text == TouchWording.HeadKeys, $"the end card says to tap the arrows ({card.keyHint.text})");
            Expect(!Layer.isActiveAndEnabled, "no touch layer on the end card");
            Expect(card.CountdownRemaining > 3, $"the countdown is running ({card.CountdownRemaining})");
            var emptySpot = new Vector2(Screen.width * 0.5f, Screen.height * 0.08f);
            fingers[5] = emptySpot;
            yield return Until(() => card.CountdownDone, 4, "the tap that skips the countdown");
            fingers.Remove(5);
            trace.Add("end card: tap the arrows, and a tap skipped the countdown");
        }

        // Teleports you a few metres off a pod, facing it and with a clear run
        private static bool PlaceBefore(Player you, WeaponPickup pod)
        {
            for (var k = 0; k < 16; k++)
            {
                var angle = k * 22.5f * Mathf.Deg2Rad;
                var around = pod.transform.position + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * 6;
                if (!Ground(around, out var ground) || Mathf.Abs(ground.y - pod.transform.position.y) > 1)
                    continue;
                if (Physics.Linecast(ground + Vector3.up, pod.transform.position + Vector3.up, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    continue;
                var toPod = pod.transform.position - ground;
                you.Controller.Teleport(ground + Vector3.up * 0.05f, Mathf.Atan2(toPod.x, toPod.z) * Mathf.Rad2Deg);
                return true;
            }
            return false;
        }

        // The aim that lands a lobbed shot of the weapon in hand on a point
        private static void LobAt(Player you, Arsenal arsenal, Vector3 point)
        {
            var to = point - arsenal.EyePosition;
            Ballistics.AimPitch(arsenal.Weapon, arsenal.EyePosition, point, out var pitch, out _);
            you.Controller.SetAim(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, pitch);
        }

        private static IEnumerator LobbedGun(string gun, Match match, Player you, Player enemy, Player mate, WeaponSlot minigunSlot)
        {
            var combat = Combat.Current;
            var arsenal = combat.ArsenalOf(you);
            var inventory = arsenal.Inventory;
            var slot = WeaponIds.SlotOf(gun).Value;
            // The pod, if the level has one: walked onto with the stick
            WeaponPickup pod = null;
            foreach (var p in UnityEngine.Object.FindObjectsByType<WeaponPickup>(FindObjectsSortMode.None))
                if (p.weaponId == gun && p.Available && (pod == null || (p.transform.position - you.transform.position).sqrMagnitude < (pod.transform.position - you.transform.position).sqrMagnitude))
                    pod = p;
            if (pod != null && PlaceBefore(you, pod))
            {
                yield return For(0.3f);
                var start = FreePoint(true);
                fingers[1] = start;
                yield return Frames(2);
                fingers[1] = start + Vector2.up * Layer.StickPixels * 1.2f;
                yield return Until(() => inventory.Holds(gun), 12, $"the {gun} from its pod");
                fingers.Remove(1);
                trace.Add($"{gun}: walked onto its pod with the stick");
            }
            else
            {
                inventory.Take(gun);
                trace.Add($"{gun}: this level has no free pod for it, taken by code");
            }
            yield return For(0.3f);
            Expect(inventory.Holds(gun), $"the {gun} is held");
            yield return TapButton(TouchButtonKind.Slot, (int)minigunSlot);
            yield return TapButton(TouchButtonKind.Slot, (int)slot);
            Expect(inventory.CurrentWeapon == gun, $"a tap on slot {(int)slot + 1} takes the {gun} ({inventory.CurrentWeapon})");
            yield return For(0.6f);
            var stats = arsenal.Weapon;
            var lobs = 0;
            var landed = 0;
            Action<FireMode, Transform> onLob = (mode, muzzle) => lobs++;
            Action<Area> onArea = area => landed++;
            combat.Fx.Lobbed += onLob;
            combat.State.AreaAdded += onArea;
            var wasAuto = GuiSettings.AutoFire;
            GuiSettings.AutoFire = false;
            if (!Face(you, enemy, 12))
            {
                combat.Fx.Lobbed -= onLob;
                combat.State.AreaAdded -= onArea;
                GuiSettings.AutoFire = wasAuto;
                trace.Add($"{gun}: no clear view of an enemy, the firing steps skipped");
                yield break;
            }
            // A tap on FIRE lobs one; the button held keeps lobbing; the shots land
            LobAt(you, arsenal, Chest(enemy));
            yield return Tap(CentreOf(TouchButtonKind.Fire), 0.15f);
            yield return For(1.2f);
            Expect(lobs >= 1, $"a tap on FIRE lobs a {gun} shot ({lobs})");
            fingers[3] = CentreOf(TouchButtonKind.Fire);
            var holdUntil = Time.realtimeSinceStartup + 3.2f;
            while (Time.realtimeSinceStartup < holdUntil)
            {
                LobAt(you, arsenal, Chest(enemy));
                yield return null;
            }
            fingers.Remove(3);
            yield return For(1.5f);
            trace.Add($"{gun}: tap and hold FIRE lobbed {lobs} shots, {landed} landed");
            Expect(lobs >= 3, $"FIRE held keeps lobbing the {gun} ({lobs})");
            Expect(landed >= 1, $"the {gun} shots land ({landed})");
            // Auto-fire on the arc: aimed so the lob lands on an enemy it starts, on a teammate or at the sky it does not
            GuiSettings.AutoFire = true;
            yield return For(0.5f);
            var auto = false;
            if (enemy.State.Alive && you.State.Alive && Face(you, enemy, 12))
            {
                for (var i = 0; i < 90 && !auto && enemy.State.Alive && you.State.Alive; i++)
                {
                    LobAt(you, arsenal, Chest(enemy));
                    yield return null;
                    var predicted = combat.PredictLob(arsenal, arsenal.EyePosition, arsenal.AimDirection);
                    if (predicted.hit && predicted.player == enemy)
                        auto |= Controls.AutoFiring;
                }
                trace.Add($"{gun} auto-fire with the arc on an enemy: {auto}");
                Expect(auto || !enemy.State.Alive || !you.State.Alive, $"the {gun} auto-fires when its arc hits an enemy");
            }
            GuiSettings.AutoFire = true;
            yield return For(arsenal.Weapon.shotsPerSecond > 0 ? 1.5f : 0.5f);
            // A level aim at the enemy does not make the lob hit it: no fire (the crosshair is on the enemy, the arc falls short)
            if (enemy.State.Alive && you.State.Alive && mate.State.Alive && Face(you, mate, 8))
            {
                var checkedFrames = 0;
                for (var i = 0; i < 40 && checkedFrames < 5 && mate.State.Alive; i++)
                {
                    LobAt(you, arsenal, Chest(mate));
                    yield return null;
                    var predicted = combat.PredictLob(arsenal, arsenal.EyePosition, arsenal.AimDirection);
                    if (predicted.hit && predicted.player == mate)
                    {
                        checkedFrames++;
                        Expect(!Controls.AutoFiring, $"no {gun} auto-fire with the arc on a teammate");
                    }
                }
                trace.Add($"{gun} auto-fire: none with the arc on a teammate ({checkedFrames} frames)");
            }
            you.Controller.SetAim(you.Controller.Yaw, -80);
            yield return For(0.4f);
            Expect(!Controls.AutoFiring, $"no {gun} auto-fire at the sky");
            combat.Fx.Lobbed -= onLob;
            combat.State.AreaAdded -= onArea;
            GuiSettings.AutoFire = wasAuto;
        }

        private static void Finish()
        {
            if (finished)
                return;
            finished = true;
            EditorApplication.update -= Update;
            TouchControls.Feed = null;
            SessionState.EraseBool(ActiveKey);
            EditorPrefs.SetBool(SdfPlatform.Device.EditorMobileKey, SessionState.GetBool(ActiveKey + ".before", false));
            PlayerPrefs.DeleteKey("gui.touchLook");
            PlayerPrefs.DeleteKey("gui.autoFire");
            foreach (var line in trace)
                Debug.Log("Touch play test: " + line);
            foreach (var error in errors)
                Debug.Log("Touch play test error: " + error);
            Debug.Log($"Touch play test: {errors.Count} errors");
            EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
        }
    }
}
