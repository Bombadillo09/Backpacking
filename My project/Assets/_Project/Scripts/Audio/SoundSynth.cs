using System;
using System.Collections.Generic;
using Backpacking.World;
using UnityEngine;
using Random = System.Random;

namespace Backpacking.Audio
{
    /// <summary>
    /// Placeholder sounds generated in code, so the game has audio before real recordings are added.
    /// Every component that uses one also has a clip slot; a recording put there replaces the generated sound.
    /// Clips are made on first use and cached.
    /// </summary>
    public static class SoundSynth
    {
        const int SampleRate = 44100;

        static readonly Dictionary<string, AudioClip> cache = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => cache.Clear();

        // ---------- Loops ----------

        /// <summary>Gusting wind, loopable.</summary>
        public static AudioClip Wind() => Cached("Wind", () =>
        {
            var random = new Random(11);
            float[] data = Loopable(8f, 1.5f, length =>
            {
                var samples = new float[length];
                var rumble = new OnePole();
                var whistle = new OnePole();
                var whistleFloor = new OnePole();
                for (int i = 0; i < length; i++)
                {
                    float t = (float)i / SampleRate;
                    float gust = Smooth01(0.5f + 0.3f * MathF.Sin(t * 0.7f) + 0.2f * MathF.Sin(t * 1.9f + 1f) + 0.15f * MathF.Sin(t * 0.31f + 2f));
                    float noise = Noise(random);
                    float low = rumble.LowPass(noise, 180f + 500f * gust);
                    float band = whistle.LowPass(noise, 900f + 900f * gust) - whistleFloor.LowPass(noise, 500f);
                    samples[i] = (low * 2.2f + band * 0.5f * gust) * (0.35f + 0.65f * gust);
                }
                return samples;
            });
            return Clip("Wind", Normalise(data, 0.8f));
        });

        /// <summary>Steady rain with individual drops, loopable.</summary>
        public static AudioClip Rain() => Cached("Rain", () =>
        {
            var random = new Random(23);
            float[] data = Loopable(6f, 1f, length =>
            {
                var samples = new float[length];
                var floor = new OnePole();
                var top = new OnePole();
                for (int i = 0; i < length; i++)
                {
                    float noise = Noise(random);
                    float hiss = top.LowPass(noise, 6500f) - floor.LowPass(noise, 500f);
                    samples[i] = hiss * 0.45f;
                }
                // Drops: tiny, bright ticks scattered densely.
                AddGrains(samples, random, 700f, 0.0015f, 0.004f, 0.05f, 0.35f, 2500f);
                return samples;
            });
            return Clip("Rain", Normalise(data, 0.7f));
        });

        /// <summary>A crackling campfire, loopable.</summary>
        public static AudioClip Fire() => Cached("Fire", () =>
        {
            var random = new Random(37);
            float[] data = Loopable(6f, 1f, length =>
            {
                var samples = new float[length];
                var roar = new OnePole();
                var hissLow = new OnePole();
                for (int i = 0; i < length; i++)
                {
                    float t = (float)i / SampleRate;
                    float noise = Noise(random);
                    float flicker = 0.7f + 0.3f * MathF.Sin(t * 3.1f) * MathF.Sin(t * 1.3f + 0.5f);
                    samples[i] = roar.LowPass(noise, 160f) * 2.5f * flicker + (noise - hissLow.LowPass(noise, 3000f)) * 0.025f;
                }
                // Crackles, then the occasional louder pop.
                AddGrains(samples, random, 14f, 0.001f, 0.006f, 0.1f, 0.9f, 1800f, cubeAmplitude: true);
                AddGrains(samples, random, 0.6f, 0.008f, 0.02f, 0.6f, 1f, 600f);
                return samples;
            });
            return Clip("Fire", Normalise(data, 0.8f));
        });

        /// <summary>Water lapping at a shore, loopable.</summary>
        public static AudioClip Water() => Cached("Water", () =>
        {
            var random = new Random(41);
            float[] data = Loopable(7f, 1.5f, length =>
            {
                var samples = new float[length];
                var body = new OnePole();
                var floor = new OnePole();
                for (int i = 0; i < length; i++)
                {
                    float t = (float)i / SampleRate;
                    float wave = MathF.Pow(MathF.Max(0f, MathF.Sin(t * 2.1f) * 0.6f + MathF.Sin(t * 0.83f + 1f) * 0.4f), 2f);
                    float noise = Noise(random);
                    float wash = body.LowPass(noise, 300f + 900f * wave) - floor.LowPass(noise, 120f);
                    samples[i] = wash * (0.25f + wave);
                }
                AddGrains(samples, random, 6f, 0.004f, 0.012f, 0.05f, 0.2f, 900f);
                return samples;
            });
            return Clip("Water", Normalise(data, 0.7f));
        });

        /// <summary>A few crickets chirping out of step, loopable (every pattern repeats exactly in the clip).</summary>
        public static AudioClip Crickets() => Cached("Crickets", () =>
        {
            const float seconds = 4f;
            int length = (int)(seconds * SampleRate);
            var samples = new float[length];
            // Carrier pitch, chirps per loop, chirp offset, loudness.
            (float pitch, int chirps, float offset, float level)[] crickets =
            {
                (4400f, 7, 0.05f, 1f),
                (4850f, 6, 0.21f, 0.6f),
                (5200f, 9, 0.12f, 0.35f),
            };
            foreach ((float pitch, int chirps, float offset, float level) in crickets)
            {
                float period = seconds / chirps;
                for (int chirp = 0; chirp < chirps; chirp++)
                for (int pulse = 0; pulse < 3; pulse++)
                {
                    float start = chirp * period + offset + pulse * 0.022f;
                    AddTone(samples, start, 0.013f, pitch, pitch, level);
                }
            }
            return Clip("Crickets", Normalise(samples, 0.6f));
        });

        // ---------- One-shots ----------

        /// <summary>One of several short bird songs.</summary>
        public static AudioClip BirdCall(int variant) => Cached($"Bird{variant}", () =>
        {
            var random = new Random(100 + variant);
            int notes = random.Next(2, 7);
            bool trill = random.NextDouble() < 0.35;
            float time = 0.02f;
            var samples = new float[(int)(2f * SampleRate)];
            float basePitch = Range(random, 2400f, 4200f);
            for (int note = 0; note < notes; note++)
            {
                float length = trill ? Range(random, 0.025f, 0.045f) : Range(random, 0.06f, 0.16f);
                float from = basePitch * Range(random, 0.85f, 1.25f);
                float to = from * Range(random, 0.6f, 1.6f);
                AddTone(samples, time, length, from, to, Range(random, 0.6f, 1f), vibrato: trill ? 0f : Range(random, 0f, 250f));
                time += length + (trill ? 0.012f : Range(random, 0.04f, 0.14f));
            }
            Array.Resize(ref samples, Mathf.Min(samples.Length, (int)((time + 0.05f) * SampleRate)));
            return Clip($"Bird{variant}", Normalise(samples, 0.8f));
        });

        public const int BirdVariants = 6;
        public const int FootstepVariants = 4;

        /// <summary>One footstep on a surface. Several variants per surface, so steps don't repeat.</summary>
        public static AudioClip Footstep(Surface surface, int variant) => Cached($"Step{surface}{variant}", () =>
        {
            var random = new Random(200 + (int)surface * 10 + variant);
            var samples = new float[(int)(0.32f * SampleRate)];
            switch (surface)
            {
                case Surface.Soft:
                    AddThud(samples, random, 0f, 0.05f, 140f, 1f);
                    AddGrainsAt(samples, random, 0.005f, 0.09f, 220f, 0.15f, 0.4f, 1800f);
                    break;
                case Surface.Leaves:
                    AddThud(samples, random, 0f, 0.04f, 160f, 0.7f);
                    AddGrainsAt(samples, random, 0f, 0.16f, 700f, 0.2f, 0.7f, 2400f);
                    break;
                case Surface.Hard:
                    AddThud(samples, random, 0f, 0.03f, 300f, 0.8f);
                    AddGrainsAt(samples, random, 0f, 0.012f, 900f, 0.6f, 1f, 3000f);
                    AddGrainsAt(samples, random, 0.01f, 0.08f, 120f, 0.15f, 0.45f, 2500f);
                    break;
                case Surface.Snow:
                    AddThud(samples, random, 0.01f, 0.06f, 120f, 0.6f);
                    AddGrainsAt(samples, random, 0f, 0.22f, 1400f, 0.15f, 0.5f, 1200f, swell: true);
                    break;
                case Surface.Water:
                    AddSplash(samples, random);
                    break;
            }
            return Clip($"Step{surface}{variant}", Normalise(samples, 0.9f));
        });

        /// <summary>A distant thunderclap rolling away.</summary>
        public static AudioClip Thunder() => Cached("Thunder", () =>
        {
            var random = new Random(61);
            int length = (int)(7f * SampleRate);
            var samples = new float[length];
            var rumble = new OnePole();
            var crack = new OnePole();
            // Rolls: overlapping swells that fade out over the clip.
            (float centre, float width, float level)[] rolls = { (0.4f, 0.3f, 1f), (1.3f, 0.6f, 0.8f), (2.6f, 1f, 0.6f), (4.2f, 1.4f, 0.35f) };
            for (int i = 0; i < length; i++)
            {
                float t = (float)i / SampleRate;
                float envelope = 0f;
                foreach ((float centre, float width, float level) in rolls)
                {
                    float d = (t - centre) / width;
                    envelope += level * MathF.Exp(-d * d);
                }
                float noise = Noise(random);
                float low = rumble.LowPass(noise, 110f + 80f * envelope);
                float snap = (noise - crack.LowPass(noise, 1200f)) * MathF.Exp(-t / 0.08f) * 0.25f;
                samples[i] = low * 4f * envelope + snap;
            }
            return Clip("Thunder", Normalise(samples, 0.95f));
        });

        /// <summary>A deer's alarm snort: a sharp, breathy blast through the nose.</summary>
        public static AudioClip Snort() => Cached("Snort", () =>
        {
            var random = new Random(71);
            var samples = new float[(int)(0.45f * SampleRate)];
            var body = new OnePole();
            var floor = new OnePole();
            for (int i = 0; i < samples.Length; i++)
            {
                float t = (float)i / SampleRate;
                float noise = Noise(random);
                float breath = body.LowPass(noise, 1400f - 900f * MathF.Min(1f, t / 0.3f)) - floor.LowPass(noise, 250f);
                samples[i] = breath * MathF.Min(1f, t / 0.012f) * MathF.Exp(-t / 0.11f);
            }
            return Clip("Snort", Normalise(samples, 0.9f));
        });

        /// <summary>A small flock bursting into flight: a flurry of wingbeats that thins out.</summary>
        public static AudioClip Flutter() => Cached("Flutter", () =>
        {
            var random = new Random(83);
            var samples = new float[(int)(1.4f * SampleRate)];
            for (int bird = 0; bird < 5; bird++)
            {
                float start = Range(random, 0f, 0.15f);
                float beatsPerSecond = Range(random, 13f, 18f);
                for (float t = start; t < samples.Length / (float)SampleRate - 0.05f; t += 1f / beatsPerSecond)
                {
                    // Each wingbeat is a soft whump; they fade as the bird gets away.
                    float level = MathF.Exp(-(t - start) / 0.45f) * Range(random, 0.6f, 1f);
                    AddGrain(samples, random, (int)(t * SampleRate), Range(random, 0.012f, 0.02f), level, 400f);
                }
            }
            return Clip("Flutter", Normalise(samples, 0.8f));
        });

        /// <summary>A heartbeat: lub, then dub.</summary>
        public static AudioClip Heartbeat() => Cached("Heartbeat", () =>
        {
            var samples = new float[(int)(0.5f * SampleRate)];
            AddBeat(samples, 0f, 58f, 1f);
            AddBeat(samples, 0.24f, 68f, 0.65f);
            return Clip("Heartbeat", Normalise(samples, 0.95f));
        });

        // ---------- Building blocks ----------

        /// <summary>A sine sweep with a smooth envelope, added into the buffer.</summary>
        static void AddTone(float[] samples, float start, float length, float fromHz, float toHz, float level, float vibrato = 0f)
        {
            int first = (int)(start * SampleRate);
            int count = (int)(length * SampleRate);
            float phase = 0f;
            for (int i = 0; i < count && first + i < samples.Length; i++)
            {
                float t = (float)i / count;
                float hz = Mathf.Lerp(fromHz, toHz, t) + vibrato * MathF.Sin(i / (float)SampleRate * 2f * MathF.PI * 40f);
                phase += 2f * MathF.PI * hz / SampleRate;
                samples[first + i] += MathF.Sin(phase) * MathF.Sin(t * MathF.PI) * level;
            }
        }

        /// <summary>A low, dull impact: the heel landing.</summary>
        static void AddThud(float[] samples, Random random, float start, float decay, float cutoff, float level)
        {
            var filter = new OnePole();
            int first = (int)(start * SampleRate);
            for (int i = first; i < samples.Length; i++)
            {
                float t = (float)(i - first) / SampleRate;
                samples[i] += filter.LowPass(Noise(random), cutoff) * 6f * MathF.Exp(-t / decay) * level;
            }
        }

        static void AddBeat(float[] samples, float start, float hz, float level)
        {
            int first = (int)(start * SampleRate);
            float phase = 0f;
            for (int i = first; i < samples.Length; i++)
            {
                float t = (float)(i - first) / SampleRate;
                phase += 2f * MathF.PI * hz * (1f - 0.35f * MathF.Min(1f, t / 0.1f)) / SampleRate;
                float envelope = MathF.Min(1f, t / 0.008f) * MathF.Exp(-t / 0.055f);
                samples[i] += MathF.Sin(phase) * envelope * level;
            }
        }

        static void AddSplash(float[] samples, Random random)
        {
            var low = new OnePole();
            var high = new OnePole();
            for (int i = 0; i < samples.Length; i++)
            {
                float t = (float)i / SampleRate;
                float noise = Noise(random);
                float band = high.LowPass(noise, 2500f) - low.LowPass(noise, 300f);
                samples[i] += band * MathF.Min(1f, t / 0.01f) * MathF.Exp(-t / 0.09f) * 1.5f;
            }
            // Bubbles: short rising blips.
            for (int bubble = 0; bubble < 5; bubble++)
            {
                float hz = Range(random, 500f, 1100f);
                AddTone(samples, Range(random, 0.02f, 0.2f), Range(random, 0.015f, 0.03f), hz, hz * 1.6f, Range(random, 0.15f, 0.35f));
            }
        }

        /// <summary>
        /// Scatters short noise bursts through the whole buffer: raindrops, crackles, grit underfoot.
        /// <paramref name="perSecond"/> on average, each lasting between the given lengths.
        /// </summary>
        static void AddGrains(float[] samples, Random random, float perSecond, float minLength, float maxLength,
            float minLevel, float maxLevel, float highPassHz, bool cubeAmplitude = false)
        {
            int count = (int)(perSecond * samples.Length / SampleRate);
            for (int g = 0; g < count; g++)
            {
                float level = (float)random.NextDouble();
                if (cubeAmplitude)
                    level = level * level * level;
                AddGrain(samples, random, random.Next(samples.Length), Range(random, minLength, maxLength),
                    Mathf.Lerp(minLevel, maxLevel, level), highPassHz);
            }
        }

        /// <summary>Like <see cref="AddGrains"/> but only between <paramref name="start"/> and start + <paramref name="span"/> seconds, fading out.</summary>
        static void AddGrainsAt(float[] samples, Random random, float start, float span, float perSecond,
            float minLevel, float maxLevel, float highPassHz, bool swell = false)
        {
            int count = Mathf.Max(1, (int)(perSecond * span));
            for (int g = 0; g < count; g++)
            {
                float at = (float)random.NextDouble();
                // Fade out across the span; a swell rises first, like snow compressing.
                float shape = swell ? MathF.Sin(at * MathF.PI) : 1f - at;
                AddGrain(samples, random, (int)((start + at * span) * SampleRate), Range(random, 0.001f, 0.004f),
                    Range(random, minLevel, maxLevel) * shape, highPassHz);
            }
        }

        static void AddGrain(float[] samples, Random random, int first, float length, float level, float highPassHz)
        {
            var filter = new OnePole();
            int count = (int)(length * SampleRate);
            for (int i = 0; i < count && first + i < samples.Length; i++)
            {
                float noise = Noise(random);
                float bright = noise - filter.LowPass(noise, highPassHz);
                samples[first + i] += bright * MathF.Exp(-4f * i / count) * level;
            }
        }

        /// <summary>
        /// Generates <paramref name="seconds"/> plus <paramref name="fade"/> of sound, then folds the extra onto the
        /// start with a crossfade, so the clip loops without a click.
        /// </summary>
        static float[] Loopable(float seconds, float fade, Func<int, float[]> generate)
        {
            int length = (int)(seconds * SampleRate);
            int overlap = (int)(fade * SampleRate);
            float[] raw = generate(length + overlap);
            var result = new float[length];
            Array.Copy(raw, result, length);
            for (int i = 0; i < overlap; i++)
            {
                float t = (float)i / overlap;
                result[i] = raw[i] * t + raw[length + i] * (1f - t);
            }
            return result;
        }

        static float[] Normalise(float[] samples, float peak)
        {
            float max = 0.0001f;
            foreach (float sample in samples)
                max = MathF.Max(max, MathF.Abs(sample));
            float scale = peak / max;
            for (int i = 0; i < samples.Length; i++)
                samples[i] *= scale;
            return samples;
        }

        static AudioClip Clip(string clipName, float[] samples)
        {
            AudioClip clip = AudioClip.Create(clipName, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        static AudioClip Cached(string key, Func<AudioClip> make)
        {
            if (!cache.TryGetValue(key, out AudioClip clip) || clip == null)
                cache[key] = clip = make();
            return clip;
        }

        static float Noise(Random random) => (float)random.NextDouble() * 2f - 1f;
        static float Range(Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);
        static float Smooth01(float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x));

        /// <summary>A one-pole low-pass filter. Subtract its output from the input for a high-pass.</summary>
        struct OnePole
        {
            float state;

            public float LowPass(float input, float cutoffHz)
            {
                float a = 1f - MathF.Exp(-2f * MathF.PI * cutoffHz / SampleRate);
                state += a * (input - state);
                return state;
            }
        }
    }
}
