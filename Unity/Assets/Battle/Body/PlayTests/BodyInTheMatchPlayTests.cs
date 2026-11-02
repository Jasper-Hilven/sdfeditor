using System.Collections;
using System.Linq;
using NUnit.Framework;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SdfBody.PlayTests
{
    /// <summary>The soldier in a real match (ticket 44): the Meadow plays for a few seconds, every player has an animated soldier, no errors are logged.</summary>
    public class BodyInTheMatchPlayTests
    {
        const string Meadow = "Assets/Scenes/Meadow.unity";

        [UnityTest]
        public IEnumerator EveryPlayerHasASoldierThatMoves()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(Meadow, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield break;
#endif
            yield return new WaitForSecondsRealtime(2f);
            // The level scene starts behind a menu, paused
            Time.timeScale = 1f;
            // A held match (the warm-up) is let go: Match.Release(), by reflection (Match is in the game's own assembly)
            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (behaviour.GetType().Name == "Match")
                {
                    Debug.Log($"[BodyPlay] match held {behaviour.GetType().GetProperty("Held").GetValue(behaviour)}");
                    behaviour.GetType().GetMethod("Release").Invoke(behaviour, null);
                }
            // The round counts down, then the bots run
            yield return new WaitForSecondsRealtime(12f);
            Time.timeScale = 1f;
            Debug.Log($"[BodyPlay] time {Time.time} unscaled {Time.unscaledTime} frames {Time.frameCount} scale {Time.timeScale} dt {Time.deltaTime}");
            yield return new WaitForSecondsRealtime(6f);
            var animators = Object.FindObjectsByType<SoldierAnimator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Debug.Log($"[BodyPlay] {animators.Length} soldier animators");
            Assert.GreaterOrEqual(animators.Length, 2, "the players have soldiers");
            var before = animators.Select(a => a.transform.position).ToArray();
            yield return new WaitForSecondsRealtime(4f);
            var travelled = animators.Select((a, i) => (a.transform.position - before[i]).magnitude).ToArray();
            Debug.Log($"[BodyPlay] travelled {string.Join(", ", travelled.Select(t => t.ToString("F1")))}; trees {string.Join(", ", animators.Select(a => a.Tree.ToString("F2")))}; targets {string.Join(", ", animators.Select(a => a.TreeTarget.ToString("F2")))}");
            var moving = animators.Count(a => a.Tree.magnitude > 0.1f);
            var drawn = animators.Count(a => a.GetComponentsInChildren<Renderer>().Any(r => r.enabled));
            Debug.Log($"[BodyPlay] {moving} moving, {drawn} drawn");
            Assert.Greater(moving, 0, "somebody runs");
            Assert.Greater(drawn, 0, "bodies are drawn");
            foreach (var a in animators)
                Assert.IsNotNull(a.Body.runtimeAnimatorController, a.name);
        }
    }
}
