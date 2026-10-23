using UnityEngine;

namespace SdfAudio
{
    /// <summary>
    /// The sound effects' player: a fixed pool of <see cref="AudioSource"/>s made once, so nothing is allocated or instantiated per shot.
    /// A play takes a free source (or the oldest one, when the pool is full), sets it up from the <see cref="Cue"/> and lets it go when
    /// the clip has ended. Each cue has a voice limit (<see cref="Cue.maxVoices"/>) and a wait between plays (<see cref="Cue.cooldown"/>);
    /// a 3D sound beyond its cue's range is not played at all. Looped sounds (an engine, the minigun's burst) return a
    /// <see cref="Handle"/> to stop or move them, and may follow a transform. Time here is unscaled, so a slowed match end sounds right.
    /// Made with the game's audio host (<see cref="Audio"/>); games call <see cref="Audio"/>, not this.
    /// </summary>
    public sealed class AudioPlayer : MonoBehaviour
    {
        /// <summary>The sources in the pool.</summary>
        public const int PoolSize = 40;

        private sealed class Voice
        {
            public AudioSource source;
            public Cue cue;
            public bool active;
            public int id;
            public float started;
            public float endTime;
            public float baseVolume;
            public float fade;
            public float fadeVolume = 1;
            public bool stopping;
            public Transform follow;
            public bool followed;
            public Vector3 offset;
        }

        /// <summary>A playing sound, to stop it or change it; a default handle (nothing played) is harmless to use.</summary>
        public struct Handle
        {
            internal AudioPlayer player;
            internal int index;
            internal int id;

            private Voice Voice => player != null && index >= 0 && index < player.voices.Length && player.voices[index].id == id && player.voices[index].active ? player.voices[index] : null;

            /// <summary>The sound is still playing (or looping).</summary>
            public bool Alive => Voice != null && !Voice.stopping;

            /// <summary>Fades the sound out over <paramref name="seconds"/> and frees its source.</summary>
            public void Stop(float seconds = 0.1f)
            {
                var voice = Voice;
                if (voice == null)
                    return;
                voice.stopping = true;
                voice.fade = seconds <= 0 ? 1000 : 1 / seconds;
            }

            /// <summary>Sets the volume, 0 to 1, relative to the cue's own.</summary>
            public void SetVolume(float volume)
            {
                var voice = Voice;
                if (voice != null)
                    voice.source.volume = voice.baseVolume * Mathf.Clamp01(volume) * voice.fadeVolume;
            }

            /// <summary>Sets the pitch as a multiple of the one picked for the cue.</summary>
            public void SetPitch(float pitch)
            {
                var voice = Voice;
                if (voice != null)
                    voice.source.pitch = pitch;
            }

            /// <summary>Moves the sound to <paramref name="position"/> (a sound that follows a transform needs no calls).</summary>
            public void MoveTo(Vector3 position)
            {
                var voice = Voice;
                if (voice != null)
                    voice.source.transform.position = position;
            }
        }

        private Voice[] voices;
        private int nextId = 1;
        private Transform listener;
        private float nextListenerSearch;

        /// <summary>The place sounds are heard from (the audio listener); the origin before there is one.</summary>
        public Vector3 ListenerPosition
        {
            get
            {
                FindListener();
                return listener != null ? listener.position : Vector3.zero;
            }
        }

        internal void Build()
        {
            voices = new Voice[PoolSize];
            for (var i = 0; i < voices.Length; i++)
            {
                var child = new GameObject("Voice " + i);
                child.transform.SetParent(transform, false);
                var source = child.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.dopplerLevel = 0;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                voices[i] = new Voice { source = source };
            }
        }

        private void FindListener()
        {
            if (listener != null || Time.unscaledTime < nextListenerSearch)
                return;
            nextListenerSearch = Time.unscaledTime + 1;
            var found = FindFirstObjectByType<AudioListener>();
            listener = found != null ? found.transform : null;
        }

        // ---------------------------------------------------------------- Playing

        /// <summary>
        /// Plays a cue at a place in the world. <paramref name="twoD"/> plays a 3D cue as a sound on the screen (your own weapon, your
        /// own steps). <paramref name="delay"/> seconds later when above 0. <paramref name="follow"/>, when given, is a transform the
        /// sound stays at (its position plus <paramref name="position"/> as an offset). Returns the handle, or a default one when the cue
        /// is empty, unknown, out of range, waiting, or over its voice limit.
        /// </summary>
        public Handle Play(Cue cue, Vector3 position, float volume = 1, float delay = 0, bool twoD = false, Transform follow = null, float pitch = 1)
        {
            if (cue == null || voices == null)
                return default;
            var clip = cue.Pick();
            if (clip == null)
                return default;
            var now = Time.unscaledTime;
            if (cue.cooldown > 0 && now - cue.lastPlayed < cue.cooldown)
                return default;
            var spatial = cue.spatial && !twoD;
            if (spatial)
            {
                var reach = cue.maxDistance;
                var at = follow != null ? follow.position + position : position;
                if ((at - ListenerPosition).sqrMagnitude > reach * reach)
                    return default;
            }

            var index = Take(cue, now);
            if (index < 0)
                return default;
            var voice = voices[index];
            var source = voice.source;
            voice.cue = cue;
            voice.active = true;
            voice.id = nextId++;
            voice.started = now;
            voice.stopping = false;
            voice.fade = 0;
            voice.fadeVolume = 1;
            voice.follow = follow;
            voice.followed = follow != null;
            voice.offset = position;
            voice.baseVolume = cue.volume * volume * SdfGui.GuiSettings.SoundVolume;
            cue.voices++;
            cue.lastPlayed = now;

            var pitched = cue.PickPitch() * pitch;
            source.clip = clip;
            source.loop = cue.loop;
            source.volume = voice.baseVolume;
            source.pitch = pitched;
            source.priority = cue.priority;
            source.spatialBlend = spatial ? 1 : 0;
            source.minDistance = cue.minDistance;
            source.maxDistance = cue.maxDistance;
            source.transform.position = follow != null ? follow.position + position : position;
            voice.endTime = cue.loop ? float.MaxValue : now + delay + clip.length / Mathf.Max(0.1f, Mathf.Abs(pitched)) + 0.1f;
            if (delay > 0)
                source.PlayDelayed(delay);
            else
                source.Play();
            return new Handle { player = this, index = index, id = voice.id };
        }

        /// <summary>A free source; when none is, the oldest one of a lower or equal priority. Also enforces the cue's voice limit. -1 when nothing may be taken.</summary>
        private int Take(Cue cue, float now)
        {
            // The cue's own limit: the oldest copy goes, unless it only just started
            if (cue.voices >= Mathf.Max(1, cue.maxVoices))
            {
                var oldest = -1;
                for (var i = 0; i < voices.Length; i++)
                    if (voices[i].active && voices[i].cue == cue && !voices[i].stopping && (oldest < 0 || voices[i].started < voices[oldest].started))
                        oldest = i;
                if (oldest < 0 || now - voices[oldest].started < 0.05f)
                    return -1;
                Free(oldest);
                return oldest;
            }
            for (var i = 0; i < voices.Length; i++)
                if (!voices[i].active)
                    return i;
            // The pool is full: the oldest sound that is not more important than this one
            var steal = -1;
            for (var i = 0; i < voices.Length; i++)
            {
                if (voices[i].cue.priority < cue.priority)
                    continue;
                if (steal < 0 || voices[i].started < voices[steal].started)
                    steal = i;
            }
            if (steal < 0)
                return -1;
            Free(steal);
            return steal;
        }

        private void Free(int index)
        {
            var voice = voices[index];
            if (!voice.active)
                return;
            voice.source.Stop();
            voice.source.clip = null;
            voice.active = false;
            voice.follow = null;
            voice.followed = false;
            if (voice.cue != null && voice.cue.voices > 0)
                voice.cue.voices--;
            voice.id = 0;
        }

        /// <summary>Stops every sound at once (a scene is left).</summary>
        public void StopAll()
        {
            if (voices == null)
                return;
            for (var i = 0; i < voices.Length; i++)
                Free(i);
        }

        // ---------------------------------------------------------------- Every frame

        private void Update()
        {
            if (voices == null)
                return;
            var now = Time.unscaledTime;
            var dt = Time.unscaledDeltaTime;
            for (var i = 0; i < voices.Length; i++)
            {
                var voice = voices[i];
                if (!voice.active)
                    continue;
                if (voice.followed)
                {
                    // A followed transform that is gone (its object destroyed) ends the sound
                    if (voice.follow == null)
                    {
                        Free(i);
                        continue;
                    }
                    voice.source.transform.position = voice.follow.position + voice.offset;
                }
                if (voice.stopping)
                {
                    voice.fadeVolume = Mathf.MoveTowards(voice.fadeVolume, 0, voice.fade * dt);
                    voice.source.volume = voice.baseVolume * voice.fadeVolume;
                    if (voice.fadeVolume <= 0)
                        Free(i);
                    continue;
                }
                if (now >= voice.endTime)
                    Free(i);
            }
        }
    }
}
