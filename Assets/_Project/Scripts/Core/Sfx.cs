using System.Collections.Generic;
using UnityEngine;

namespace WheresMyLeftSock.Core
{
    /// <summary>
    /// Procedural sound effects + music (no audio files in the project). Clips are synthesised on first use and cached.
    /// Sfx.Play("honk") / Sfx.PlayAt("thud", pos). Names: pop, grab, whoosh, thud, honk, honk_long, engine, rev,
    /// screech, heartbeat, ping, step, slip, bonk, jump, land, drawer, door, alarm, squelch, wah, win, lose, music.
    /// </summary>
    public static class Sfx
    {
        private const int SR = 22050;
        private static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();
        private static System.Random _rng = new System.Random(1234);

        public static AudioClip Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (Cache.TryGetValue(name, out AudioClip c) && c != null) return c;
            float[] data = Generate(name);
            if (data == null)
            {
                Debug.LogWarning($"[Sfx] unknown sound '{name}'");
                return null;
            }

            c = AudioClip.Create("sfx_" + name, data.Length, 1, SR, false);
            c.SetData(data, 0);
            Cache[name] = c;
            return c;
        }

        public static void Play(string name, float volume = 1f, float pitch = 1f) => AudioHub.Instance.Play(Get(name), null, volume, pitch);

        public static void PlayAt(string name, Vector3 pos, float volume = 1f, float pitch = 1f) => AudioHub.Instance.Play(Get(name), pos, volume, pitch);

        // ------------------------------------------------------------------ synthesis

        private static float[] Generate(string n)
        {
            _rng = new System.Random(n.GetHashCode());
            switch (n)
            {
                case "pop": return Sweep(0.11f, 380f, 1150f, 0, 2.2f, 0.55f);
                case "grab": return Sweep(0.08f, 220f, 420f, 0, 2.5f, 0.35f);
                case "jump": return Sweep(0.14f, 260f, 640f, 3, 1.5f, 0.35f);
                case "whoosh": return Whoosh(0.28f);
                case "thud": return Thud(0.2f, 105f, 0.8f);
                case "land": return Thud(0.14f, 140f, 0.45f);
                case "honk": return Honk(0.36f);
                case "honk_long": return Honk(0.9f);
                case "engine": return Engine(1f);
                case "rev": return Rev(1.3f);
                case "screech": return Screech(1.4f);
                case "heartbeat": return Heartbeat();
                case "ping": return Ping();
                case "step": return Step();
                case "slip": return SlideWhistle(0.55f, 1500f, 260f);
                case "bonk": return Bonk();
                case "drawer": return Drawer();
                case "door": return Creak();
                case "alarm": return Alarm();
                case "squelch": return Squelch();
                case "wah": return Wah(new[] { 233f, 220f }, new[] { 0.18f, 0.32f });
                case "lose": return Wah(new[] { 196f, 185f, 175f, 165f }, new[] { 0.3f, 0.3f, 0.3f, 0.9f });
                case "win": return Arp(new[] { 523f, 659f, 784f, 1047f }, 0.09f, 0.4f);
                case "music": return Music();
            }

            return null;
        }

        private static float[] Buf(float seconds) => new float[Mathf.Max(1, (int)(SR * seconds))];

        private static float Noise() => (float)(_rng.NextDouble() * 2.0 - 1.0);

        private static float Wave(float phase, int type)
        {
            switch (type)
            {
                case 1: return phase < 0.5f ? 0.6f : -0.6f;                      // square
                case 2: return (2f * phase - 1f) * 0.7f;                         // saw
                case 3: return 1f - 4f * Mathf.Abs(phase - 0.5f);                // triangle
                case 4: return phase < 0.25f ? 0.6f : -0.6f;                     // pulse 25%
                default: return Mathf.Sin(phase * Mathf.PI * 2f);               // sine
            }
        }

        private static float[] Sweep(float dur, float f0, float f1, int wave, float curve, float vol)
        {
            float[] b = Buf(dur);
            float ph = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)b.Length;
                float f = Mathf.Lerp(f0, f1, 1f - Mathf.Pow(1f - t, 2f));
                ph = (ph + f / SR) % 1f;
                float env = Mathf.Pow(1f - t, curve) * Mathf.Min(1f, i / (SR * 0.004f));
                b[i] = Wave(ph, wave) * env * vol;
            }

            return b;
        }

        private static float[] Whoosh(float dur)
        {
            float[] b = Buf(dur);
            float lp = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)b.Length;
                float cutoff = Mathf.Lerp(0.03f, 0.35f, Mathf.Sin(t * Mathf.PI));
                lp += (Noise() - lp) * cutoff;
                b[i] = lp * Mathf.Pow(Mathf.Sin(t * Mathf.PI), 1.5f) * 1.4f;
            }

            return b;
        }

        private static float[] Thud(float dur, float f0, float vol)
        {
            float[] b = Buf(dur);
            float ph = 0f, lp = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float s = i / (float)SR;
                float f = Mathf.Lerp(f0, f0 * 0.45f, s / dur);
                ph = (ph + f / SR) % 1f;
                lp += (Noise() - lp) * 0.15f;
                float click = s < 0.02f ? lp * (1f - s / 0.02f) : 0f;
                b[i] = (Mathf.Sin(ph * Mathf.PI * 2f) * Mathf.Exp(-s * 16f) + click * 0.8f) * vol;
            }

            return b;
        }

        private static float[] Honk(float dur)
        {
            float[] b = Buf(dur);
            float p1 = 0f, p2 = 0f, lp = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float s = i / (float)SR;
                float vib = 1f + 0.006f * Mathf.Sin(s * 2f * Mathf.PI * 6f);
                p1 = (p1 + 370f * vib / SR) % 1f;
                p2 = (p2 + 466f * vib / SR) % 1f;
                float raw = (Wave(p1, 1) + Wave(p2, 1)) * 0.5f;
                lp += (raw - lp) * 0.35f;
                float env = Mathf.Min(1f, s / 0.012f) * Mathf.Min(1f, (dur - s) / 0.04f);
                b[i] = lp * env * 0.9f;
            }

            return b;
        }

        private static float[] Engine(float dur)
        {
            // seamless 1s loop: every component has a whole number of cycles per second
            float[] b = Buf(dur);
            float lp = 0f, nlp = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float s = i / (float)SR;
                float saw = Wave((s * 40f) % 1f, 2) + 0.5f * Wave((s * 80f) % 1f, 2);
                float am = 0.65f + 0.35f * Mathf.Sin(s * 2f * Mathf.PI * 10f);
                lp += (saw * am - lp) * 0.06f;
                nlp += (Noise() - nlp) * 0.02f;
                b[i] = (lp * 1.4f + nlp * 0.6f) * 0.7f;
            }

            return b;
        }

        private static float[] Rev(float dur)
        {
            float[] b = Buf(dur);
            float ph = 0f, lp = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)b.Length;
                float f = Mathf.Lerp(40f, 130f, Mathf.Sin(t * Mathf.PI * 0.8f));
                ph = (ph + f / SR) % 1f;
                float am = 0.6f + 0.4f * Mathf.Sin(i / (float)SR * 2f * Mathf.PI * 14f);
                lp += (Wave(ph, 2) * am - lp) * Mathf.Lerp(0.05f, 0.2f, t);
                b[i] = lp * 1.6f * Mathf.Min(1f, (1f - t) * 4f);
            }

            return b;
        }

        private static float[] Screech(float dur)
        {
            float[] b = Buf(dur);
            float p1 = 0f, p2 = 0f, bp = 0f, lp = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float s = i / (float)SR;
                float t = s / dur;
                float wob = Mathf.Sin(s * 2f * Mathf.PI * 9f) * 90f;
                p1 = (p1 + (2100f + wob) / SR) % 1f;
                p2 = (p2 + (2650f - wob) / SR) % 1f;
                lp += (Noise() - lp) * 0.5f;
                bp = lp - bp * 0.3f;
                float env = Mathf.Min(1f, s / 0.03f) * Mathf.Pow(1f - t, 0.7f);
                b[i] = ((Mathf.Sin(p1 * 6.2832f) + Mathf.Sin(p2 * 6.2832f)) * 0.25f + bp * 0.25f) * env;
            }

            return b;
        }

        private static float[] Heartbeat()
        {
            float[] b = Buf(0.7f);
            AddThump(b, 0f, 1f);
            AddThump(b, 0.2f, 0.7f);
            return b;
        }

        private static void AddThump(float[] b, float at, float vol)
        {
            int start = (int)(at * SR);
            float ph = 0f;
            for (int i = start; i < b.Length; i++)
            {
                float s = (i - start) / (float)SR;
                ph = (ph + Mathf.Lerp(62f, 38f, Mathf.Min(1f, s / 0.15f)) / SR) % 1f;
                b[i] += Mathf.Sin(ph * 6.2832f) * Mathf.Exp(-s * 22f) * vol * Mathf.Min(1f, s / 0.004f);
            }
        }

        private static float[] Ping()
        {
            float[] b = Buf(0.36f);
            Note(b, 0f, 0.14f, 1320f, 0, 0.45f, 18f);
            Note(b, 0.11f, 0.25f, 1760f, 0, 0.45f, 12f);
            return b;
        }

        private static void Note(float[] b, float at, float dur, float f, int wave, float vol, float decay)
        {
            int start = (int)(at * SR);
            int len = (int)(dur * SR);
            float ph = 0f;
            for (int i = 0; i < len && start + i < b.Length; i++)
            {
                float s = i / (float)SR;
                ph = (ph + f / SR) % 1f;
                float env = Mathf.Min(1f, s / 0.005f) * Mathf.Exp(-s * decay) * Mathf.Min(1f, (dur - s) / 0.01f);
                b[start + i] += Wave(ph, wave) * env * vol;
            }
        }

        private static float[] Step()
        {
            float[] b = Buf(0.07f);
            float lp = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float s = i / (float)SR;
                lp += (Noise() - lp) * 0.12f;
                b[i] = lp * Mathf.Exp(-s * 55f) * 1.6f;
            }

            return b;
        }

        private static float[] SlideWhistle(float dur, float f0, float f1)
        {
            float[] b = Buf(dur);
            float ph = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)b.Length;
                float f = Mathf.Lerp(f0, f1, t) * (1f + 0.02f * Mathf.Sin(i / (float)SR * 6.2832f * 7f));
                ph = (ph + f / SR) % 1f;
                b[i] = Mathf.Sin(ph * 6.2832f) * 0.4f * Mathf.Min(1f, t * 20f) * Mathf.Min(1f, (1f - t) * 8f);
            }

            return b;
        }

        private static float[] Bonk()
        {
            float[] b = Buf(0.55f);
            float ph = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float s = i / (float)SR;
                float f = 210f + 130f * Mathf.Sin(s * 6.2832f * 13f) * Mathf.Exp(-s * 5f);
                ph = (ph + f / SR) % 1f;
                b[i] = Mathf.Sin(ph * 6.2832f) * Mathf.Exp(-s * 5.5f) * 0.55f;
            }

            Note(b, 0f, 0.06f, 900f, 0, 0.5f, 70f); // woodblock
            return b;
        }

        private static float[] Drawer()
        {
            float[] b = Buf(0.3f);
            float lp = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float s = i / (float)SR;
                float t = s / 0.3f;
                lp += (Noise() - lp) * 0.08f;
                float rumble = 0.6f + 0.4f * Mathf.Sin(s * 6.2832f * 31f);
                b[i] = lp * rumble * Mathf.Sin(Mathf.Min(1f, t * 1.2f) * Mathf.PI) * 1.3f;
            }

            AddThump(b, 0.24f, 0.35f);
            return b;
        }

        private static float[] Creak()
        {
            float[] b = Buf(0.45f);
            float ph = 0f, lp = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float s = i / (float)SR;
                float t = s / 0.45f;
                float f = 170f + 70f * Mathf.Sin(s * 6.2832f * 6f);
                ph = (ph + f / SR) % 1f;
                lp += (Wave(ph, 2) - lp) * 0.25f;
                b[i] = lp * Mathf.Sin(t * Mathf.PI) * 0.35f;
            }

            return b;
        }

        private static float[] Alarm()
        {
            float[] b = Buf(1f);
            for (int k = 0; k < 4; k++)
            {
                Note(b, k * 0.14f, 0.09f, 1050f, 1, 0.35f, 2f);
            }

            return b;
        }

        private static float[] Squelch()
        {
            float[] b = Buf(0.32f);
            float ph = 0f, lp = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float s = i / (float)SR;
                float f = 160f - 80f * s / 0.32f + 40f * Mathf.Sin(s * 6.2832f * 25f);
                ph = (ph + f / SR) % 1f;
                lp += (Noise() - lp) * 0.2f;
                b[i] = (Mathf.Sin(ph * 6.2832f) * 0.5f + lp * 0.4f) * Mathf.Exp(-s * 7f);
            }

            return b;
        }

        private static float[] Wah(float[] notes, float[] durs)
        {
            float total = 0f;
            foreach (float d in durs) total += d;
            float[] b = Buf(total + 0.05f);
            float at = 0f;
            for (int n = 0; n < notes.Length; n++)
            {
                int start = (int)(at * SR);
                int len = (int)(durs[n] * SR);
                float ph = 0f, lp = 0f;
                for (int i = 0; i < len && start + i < b.Length; i++)
                {
                    float s = i / (float)SR;
                    float vib = n == notes.Length - 1 ? 1f + 0.025f * Mathf.Sin(s * 6.2832f * 6f) * Mathf.Min(1f, s * 3f) : 1f;
                    ph = (ph + notes[n] * vib / SR) % 1f;
                    float cutoff = 0.05f + 0.25f * Mathf.Sin(Mathf.Min(1f, s / durs[n]) * Mathf.PI); // the "wah"
                    lp += (Wave(ph, 2) - lp) * cutoff;
                    float env = Mathf.Min(1f, s / 0.02f) * Mathf.Min(1f, (durs[n] - s) / 0.04f);
                    b[start + i] += lp * env * 0.9f;
                }

                at += durs[n];
            }

            return b;
        }

        private static float[] Arp(float[] notes, float step, float last)
        {
            float[] b = Buf(step * (notes.Length - 1) + last + 0.05f);
            for (int n = 0; n < notes.Length; n++)
            {
                bool isLast = n == notes.Length - 1;
                Note(b, n * step, isLast ? last : step * 1.2f, notes[n], 4, 0.3f, isLast ? 4f : 10f);
                Note(b, n * step, isLast ? last : step * 1.2f, notes[n] * 2f, 0, 0.12f, isLast ? 4f : 10f);
            }

            return b;
        }

        /// <summary>Bouncy 8-bar chiptune loop in C (C - G - Am - F), 150 bpm, seamless.</summary>
        private static float[] Music()
        {
            const float bpm = 150f;
            float beat = 60f / bpm;
            float eighth = beat * 0.5f;
            int bars = 8;
            float[] b = Buf(bars * 4 * beat);

            // chord roots (Hz) for each bar, 2 bars per chord
            float[] roots = { 130.81f, 130.81f, 98.00f, 98.00f, 110.00f, 110.00f, 87.31f, 87.31f };
            int[][] chordTones =
            {
                new[] { 0, 4, 7 }, new[] { 0, 4, 7 }, new[] { 0, 4, 7 }, new[] { 0, 4, 7 },
                new[] { 0, 3, 7 }, new[] { 0, 3, 7 }, new[] { 0, 4, 7 }, new[] { 0, 4, 7 },
            };

            // C major pentatonic melody pattern (semitones above C5), -1 = rest; 8 eighths per bar
            int[] melody =
            {
                0, -1, 4, 7, 9, 7, 4, -1,   2, 4, -1, 2, 0, -1, -1, -1,
                7, -1, 7, 9, 7, 4, 2, -1,   2, -1, 4, 2, -5, -1, -1, -1,
                9, -1, 12, 9, 7, -1, 4, 7,  9, 7, 4, -1, 2, 4, -1, -1,
                5, -1, 9, 12, 9, 5, 4, -1,  2, 4, 7, -1, 0, -1, -1, -1,
            };

            for (int bar = 0; bar < bars; bar++)
            {
                for (int e = 0; e < 8; e++)
                {
                    float at = (bar * 8 + e) * eighth;
                    // bass: root / octave bounce
                    float bf = roots[bar] * (e % 2 == 0 ? 1f : 2f);
                    Note(b, at, eighth * 0.9f, bf, 3, 0.32f, 3f);

                    // arpeggio sparkle on off-beats
                    if (e % 2 == 1)
                    {
                        int tone = chordTones[bar][(e / 2) % 3];
                        Note(b, at, eighth * 0.5f, roots[bar] * 4f * Mathf.Pow(2f, tone / 12f), 4, 0.06f, 20f);
                    }

                    // melody
                    int m = melody[bar * 8 + e];
                    if (m > -10 && m != -1)
                    {
                        Note(b, at, eighth * 0.95f, 523.25f * Mathf.Pow(2f, m / 12f), 4, 0.14f, 3f);
                    }

                    // drums: kick on beats 1 & 3, hat on off-beats, snare on 2 & 4
                    if (e == 0 || e == 4) AddKick(b, at);
                    if (e == 2 || e == 6) AddNoiseHit(b, at, 0.09f, 0.18f, 0.25f);
                    if (e % 2 == 1) AddNoiseHit(b, at, 0.03f, 0.06f, 0.7f);
                }
            }

            // soft clip
            for (int i = 0; i < b.Length; i++)
            {
                b[i] = (float)System.Math.Tanh(b[i] * 1.2f) * 0.8f;
            }

            return b;
        }

        private static void AddKick(float[] b, float at)
        {
            int start = (int)(at * SR);
            float ph = 0f;
            for (int i = 0; i < SR * 0.15f && start + i < b.Length; i++)
            {
                float s = i / (float)SR;
                ph = (ph + Mathf.Lerp(150f, 45f, Mathf.Min(1f, s / 0.08f)) / SR) % 1f;
                b[start + i] += Mathf.Sin(ph * 6.2832f) * Mathf.Exp(-s * 25f) * 0.5f;
            }
        }

        private static void AddNoiseHit(float[] b, float at, float dur, float vol, float bright)
        {
            int start = (int)(at * SR);
            float lp = 0f, prev = 0f;
            for (int i = 0; i < SR * dur && start + i < b.Length; i++)
            {
                float s = i / (float)SR;
                float n = Noise();
                lp += (n - lp) * 0.5f;
                float hp = n - prev * bright; // crude brightening
                prev = n;
                b[start + i] += Mathf.Lerp(lp, hp, bright) * Mathf.Exp(-s * (8f / dur)) * vol;
            }
        }
    }
}
