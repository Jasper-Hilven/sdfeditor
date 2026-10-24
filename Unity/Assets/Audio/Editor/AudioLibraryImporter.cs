using UnityEditor;
using UnityEngine;

namespace SdfAudio.Editor
{
    /// <summary>
    /// The import settings of everything in Assets/Audio/Library, set by code on every import (so they are the same on every machine and a
    /// new file needs no clicking, and a change here is a re-import away):
    /// <list type="bullet">
    /// <item><b>Sound effects</b> (the default): mono (a 3D source is mixed to mono anyway and half the size), Vorbis at quality 0.6,
    /// <i>Decompress On Load</i> (short, played often, no decode cost while playing), 44.1 kHz (some of the library is 96 kHz).</item>
    /// <item><b>Stings and the applause</b>: as effects but stereo (they are on the screen, not in the world).</item>
    /// <item><b>The heartbeat</b>: <i>Compressed In Memory</i> (8 s, looped, would be 700 kB decompressed).</item>
    /// <item><b>Music</b> (Library/Music, but the stings): stereo, Vorbis at quality 0.4, <i>Compressed In Memory</i> and loaded in the
    /// background. WebGL does not stream audio, so Compressed In Memory is the choice there too: the compressed bytes stay in memory and
    /// are decoded as they play; <i>Decompress On Load</i> would be about 10 MB a 57 s layer.</item>
    /// </list>
    /// Everything in Assets/Audio/CC0 follows the same rules: its effects are forced to mono (they came as stereo files), decompressed on
    /// load, Vorbis at 0.6; its music (the menu loop) is stereo, Vorbis at 0.4, Compressed In Memory and loaded in the background.
    /// WebGL gets the same settings as an override, so the platform default never differs by accident.
    /// </summary>
    public sealed class AudioLibraryImporter : AssetPostprocessor
    {
        public const string Folder = "Assets/Audio/Library/";
        /// <summary>The CC0 sounds (Assets/Audio/CC0, see SOURCES.md there): same rules, the effects forced to mono, the music stereo.</summary>
        public const string Cc0Folder = "Assets/Audio/CC0/";

        /// <summary>Raised when the rules below change, so Unity imports every sound again (2: tiny clips as PCM, the heartbeat decompressed on load; 3: short Ogg clips as PCM too).</summary>
        public override uint GetVersion() => 3;

        /// <summary>
        /// WAV sources smaller than this (a few tens of milliseconds: the minigun's heads) are kept as PCM: FMOD, which Unity
        /// imports with, cannot open a Vorbis clip that short ("Couldn't perform seek operation"), and PCM that small costs nothing.
        /// </summary>
        public const long PcmBelowBytes = 16 * 1024;

        /// <summary>
        /// Ogg sources shorter than this (the hit-marker tick, the M4's shots, the enemies' footsteps) are kept as PCM too: Safari cannot
        /// decode the web build's compressed copy of a clip that short ("EncodingError: Decoding failed", then "Loading FSB failed").
        /// </summary>
        public const float PcmBelowSeconds = 0.2f;

        private void OnPreprocessAudio()
        {
            var cc0 = assetPath.StartsWith(Cc0Folder);
            if (!cc0 && !assetPath.StartsWith(Folder))
                return;
            var importer = (AudioImporter)assetImporter;
            var name = System.IO.Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            var inMusic = assetPath.StartsWith(cc0 ? Cc0Folder + "music/" : Folder + "Music/");
            var sting = !cc0 && name.Contains("sting");
            var music = inMusic && !sting;
            // The CC0 effects were encoded as stereo (the local ffmpeg has no mono Vorbis encoder), so they are made mono here; only the
            // music stays stereo
            var stereo = music || sting || name.Contains("applauding");

            importer.forceToMono = !stereo;
            importer.loadInBackground = music;
            importer.ambisonic = false;

            var tiny = assetPath.EndsWith(".wav") ? new System.IO.FileInfo(assetPath).Length < PcmBelowBytes
                : assetPath.EndsWith(".ogg") && OggSeconds(assetPath) is { } seconds && seconds < PcmBelowSeconds;
            var settings = new AudioImporterSampleSettings
            {
                // Only the music stays compressed in memory: FMOD would not open the heartbeat that way ("Can't use FMOD_OPENMEMORY_POINT
                // on non PCM source data"), and decompressed it is under a megabyte
                loadType = music ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad,
                compressionFormat = tiny ? AudioCompressionFormat.PCM : AudioCompressionFormat.Vorbis,
                quality = music ? 0.4f : 0.6f,
                sampleRateSetting = music ? AudioSampleRateSetting.PreserveSampleRate : AudioSampleRateSetting.OverrideSampleRate,
                sampleRateOverride = 44100,
                preloadAudioData = true,
            };
            importer.defaultSampleSettings = settings;
            importer.SetOverrideSampleSettings("WebGL", settings);
        }

        /// <summary>
        /// An Ogg Vorbis file's length in seconds, from its headers: the last page's granule position (samples) over the sample rate of the
        /// identification header ("\x01vorbis", then version, channels, rate); null when it is not one.
        /// </summary>
        public static double? OggSeconds(string path)
        {
            var bytes = System.IO.File.ReadAllBytes(path);
            var id = IndexOf(bytes, new byte[] { 1, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' }, 0, false);
            if (id < 0 || id + 16 > bytes.Length)
                return null;
            var rate = System.BitConverter.ToUInt32(bytes, id + 12);
            var last = IndexOf(bytes, new[] { (byte)'O', (byte)'g', (byte)'g', (byte)'S' }, bytes.Length - 4, true);
            if (rate == 0 || last < 0 || last + 14 > bytes.Length)
                return null;
            var samples = System.BitConverter.ToInt64(bytes, last + 6);
            return samples > 0 ? samples / (double)rate : null;
        }

        private static int IndexOf(byte[] bytes, byte[] pattern, int from, bool backwards)
        {
            for (var i = from; backwards ? i >= 0 : i <= bytes.Length - pattern.Length; i += backwards ? -1 : 1)
            {
                var match = true;
                for (var j = 0; j < pattern.Length && match; j++)
                    match = bytes[i + j] == pattern[j];
                if (match)
                    return i;
            }
            return -1;
        }
    }
}
