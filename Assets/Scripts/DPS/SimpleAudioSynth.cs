using System;
using System.Collections.Generic;
using UnityEngine;

namespace DPS
{
    /// <summary>Procedurally generated placeholder sounds used when no clip is assigned.</summary>
    public enum SynthSound
    {
        Whistle,
        BeepGreen,
        BeepRed,
        Click,
        Siren,
        Cash,
    }

    /// <summary>
    /// Generates lightweight procedural <see cref="AudioClip"/> fallbacks so every tool can make
    /// a sound even with an empty Inspector slot. Clips are built once and cached; when a designer
    /// assigns a real clip the custom one always wins.
    /// </summary>
    public static class SimpleAudioSynth
    {
        const int SampleRate = 22050;

        static readonly Dictionary<SynthSound, AudioClip> Cache = new Dictionary<SynthSound, AudioClip>();

        /// <summary>Clears cached clips so they are rebuilt after a domain reload / play exit.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache()
        {
            Cache.Clear();
        }

        public static AudioClip Get(SynthSound sound)
        {
            if (Cache.TryGetValue(sound, out var cached) && cached != null)
                return cached;

            var clip = Build(sound);
            Cache[sound] = clip;
            return clip;
        }

        /// <summary>
        /// Plays <paramref name="custom"/> when assigned, otherwise the procedural fallback.
        /// Never throws and never dereferences a null source.
        /// </summary>
        public static void Play(AudioSource source, AudioClip custom, SynthSound fallback,
                                float volume = 1f, float pitch = 1f)
        {
            if (source == null)
                return;

            var clip = custom != null ? custom : Get(fallback);
            if (clip == null)
                return;

            source.pitch = pitch;
            source.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        static AudioClip Build(SynthSound sound)
        {
            switch (sound)
            {
                case SynthSound.Whistle: return Whistle();
                case SynthSound.BeepGreen: return BeepGreen();
                case SynthSound.BeepRed: return BeepRed();
                case SynthSound.Click: return Click();
                case SynthSound.Siren: return Siren();
                case SynthSound.Cash: return Cash();
                default: return Click();
            }
        }

        // ------------------------------------------------------------------ sounds

        /// <summary>Police whistle trill: high warble plus breath noise.</summary>
        static AudioClip Whistle()
        {
            const float duration = 0.62f;
            var buffer = NewBuffer(duration);
            var noise = new System.Random(1234);

            for (var i = 0; i < buffer.Length; i++)
            {
                var t = i / (float)SampleRate;
                var warble = 1950f + 320f * Mathf.Sin(2f * Mathf.PI * 17f * t);
                var tone = Mathf.Sin(2f * Mathf.PI * warble * t);
                var breath = ((float)noise.NextDouble() * 2f - 1f) * 0.15f;
                buffer[i] = (tone + breath) * 0.8f;
            }

            ApplyEnvelope(buffer, 0.02f, 0.18f);
            return FromSamples("Synth_Whistle", Normalize(buffer, 0.85f));
        }

        /// <summary>Bright two-tone confirmation.</summary>
        static AudioClip BeepGreen()
        {
            const float duration = 0.22f;
            var buffer = NewBuffer(duration);

            for (var i = 0; i < buffer.Length; i++)
            {
                var t = i / (float)SampleRate;
                buffer[i] = Mathf.Sin(2f * Mathf.PI * 1046.5f * t) * 0.6f
                          + Mathf.Sin(2f * Mathf.PI * 1568f * t) * 0.4f;
            }

            ApplyEnvelope(buffer, 0.008f, 0.09f);
            return FromSamples("Synth_BeepGreen", Normalize(buffer, 0.8f));
        }

        /// <summary>Low buzzing rejection.</summary>
        static AudioClip BeepRed()
        {
            const float duration = 0.5f;
            var buffer = NewBuffer(duration);

            for (var i = 0; i < buffer.Length; i++)
            {
                var t = i / (float)SampleRate;
                // Slightly detuned pair gives the harsh, "wrong answer" beat.
                var a = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 196f * t));
                var b = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 203f * t));
                buffer[i] = (a + b) * 0.35f;
            }

            ApplyEnvelope(buffer, 0.01f, 0.22f);
            return FromSamples("Synth_BeepRed", Normalize(buffer, 0.75f));
        }

        /// <summary>Short tactile click for desk buttons.</summary>
        static AudioClip Click()
        {
            const float duration = 0.06f;
            var buffer = NewBuffer(duration);
            var noise = new System.Random(77);

            for (var i = 0; i < buffer.Length; i++)
            {
                var t = i / (float)SampleRate;
                var clack = ((float)noise.NextDouble() * 2f - 1f);
                var body = Mathf.Sin(2f * Mathf.PI * 820f * t) * 0.5f;
                buffer[i] = (clack * 0.6f + body) * Mathf.Exp(-48f * t);
            }

            return FromSamples("Synth_Click", Normalize(buffer, 0.7f));
        }

        /// <summary>Siren sweep used on arrest.</summary>
        static AudioClip Siren()
        {
            const float duration = 1.4f;
            var buffer = NewBuffer(duration);

            for (var i = 0; i < buffer.Length; i++)
            {
                var t = i / (float)SampleRate;
                var cycle = Mathf.Repeat(t, 0.7f) / 0.7f;
                var freq = Mathf.Lerp(620f, 980f, Mathf.Sin(cycle * Mathf.PI));
                var sine = Mathf.Sin(2f * Mathf.PI * freq * t);
                // Subtle square component so it cuts through on the Quest speaker.
                var square = Mathf.Sign(sine) * 0.18f;
                buffer[i] = sine * 0.6f + square;
            }

            ApplyEnvelope(buffer, 0.03f, 0.25f);
            return FromSamples("Synth_Siren", Normalize(buffer, 0.8f));
        }

        /// <summary>Rising arpeggio for money.</summary>
        static AudioClip Cash()
        {
            const float duration = 0.42f;
            var buffer = NewBuffer(duration);
            var notes = new[] { 1318.5f, 1760f, 2093f, 2637f };
            var step = buffer.Length / notes.Length;

            for (var i = 0; i < buffer.Length; i++)
            {
                var t = i / (float)SampleRate;
                var noteIndex = Mathf.Clamp(i / step, 0, notes.Length - 1);
                var localT = (i % step) / (float)SampleRate;
                buffer[i] = Mathf.Sin(2f * Mathf.PI * notes[noteIndex] * t) * Mathf.Exp(-22f * localT);
            }

            ApplyEnvelope(buffer, 0.005f, 0.12f);
            return FromSamples("Synth_Cash", Normalize(buffer, 0.7f));
        }

        // ----------------------------------------------------------------- helpers

        static float[] NewBuffer(float seconds)
        {
            return new float[Mathf.Max(1, Mathf.CeilToInt(seconds * SampleRate))];
        }

        /// <summary>Linear fade in/out so clips never start or end on a click.</summary>
        static void ApplyEnvelope(float[] buffer, float attack, float release)
        {
            var attackSamples = Mathf.Max(1, Mathf.CeilToInt(attack * SampleRate));
            var releaseSamples = Mathf.Max(1, Mathf.CeilToInt(release * SampleRate));

            for (var i = 0; i < buffer.Length; i++)
            {
                var gain = 1f;
                if (i < attackSamples)
                    gain = i / (float)attackSamples;
                else if (i > buffer.Length - releaseSamples)
                    gain = (buffer.Length - i) / (float)releaseSamples;

                buffer[i] *= Mathf.Clamp01(gain);
            }
        }

        static float[] Normalize(float[] buffer, float peak)
        {
            var max = 0f;
            foreach (var s in buffer)
                max = Mathf.Max(max, Mathf.Abs(s));

            if (max <= 0.0001f)
                return buffer;

            var scale = peak / max;
            for (var i = 0; i < buffer.Length; i++)
                buffer[i] *= scale;

            return buffer;
        }

        static AudioClip FromSamples(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
