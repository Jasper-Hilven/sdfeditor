using UnityEngine;

namespace SdfLevel
{
    /// <summary>
    /// The level's wind, for everything <see cref="InstancedScatterRenderer"/> draws: sets the shader global <c>_SdfWind</c> that
    /// SdfScatter.hlsl sways the scattered models by (direction, strength, and the phase it runs up at <see cref="speed"/>). Each draw
    /// group scales the sway by its own wind setting (from the level file), so grass and tree canopies share one wind. One per level
    /// scene; without it nothing sways.
    /// </summary>
    [ExecuteAlways]
    public sealed class Wind : MonoBehaviour
    {
        private static readonly int WindId = Shader.PropertyToID("_SdfWind");

        [Tooltip("Where the wind blows to, in degrees about +y (0 is +z, 90 is +x)")]
        public float direction = 60;
        [Tooltip("1 sways each draw group as far as the level file says, 0 is calm")]
        public float strength = 1;
        [Tooltip("How fast the sway runs, in radians per second")]
        public float speed = 1.6f;
        [Tooltip("Where the sway is: it runs up by speed each second; set it to hold the wind at one moment")]
        public float phase;

        private float lastTime = -1;

        private void OnEnable() => Apply();

        private void OnDisable() => Shader.SetGlobalVector(WindId, Vector4.zero);

        private void Update()
        {
            // In the editor outside play mode as well, so the scene view moves with it
            var now = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
            if (lastTime >= 0)
                phase += speed * Mathf.Clamp(now - lastTime, 0, 0.1f);
            lastTime = now;
            Apply();
        }

        /// <summary>Puts the wind as it is now on the shaders.</summary>
        public void Apply()
        {
            var radians = direction * Mathf.Deg2Rad;
            Shader.SetGlobalVector(WindId, new Vector4(Mathf.Sin(radians), Mathf.Cos(radians), enabled ? strength : 0, phase));
        }
    }
}
