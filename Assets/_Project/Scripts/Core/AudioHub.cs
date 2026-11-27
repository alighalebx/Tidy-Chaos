using System.Collections.Generic;
using UnityEngine;

namespace WheresMyLeftSock.Core
{
    /// <summary>Pool of AudioSources for 2D + 3D one-shots, plus a music channel. Created on demand.</summary>
    public class AudioHub : MonoBehaviour
    {
        private const int MaxPoolSize = 32;

        private static AudioHub _instance;
        private readonly List<AudioSource> _pool = new List<AudioSource>();
        private readonly List<float> _busyUntil = new List<float>();
        private readonly List<int> _priorities = new List<int>();
        private int _next;
        private AudioSource _music;

        public static AudioHub Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new GameObject("AudioHub").AddComponent<AudioHub>();
                }

                return _instance;
            }
        }

        public AudioSource Music
        {
            get
            {
                if (_music == null)
                {
                    _music = gameObject.AddComponent<AudioSource>();
                    _music.loop = true;
                    _music.playOnAwake = false;
                    _music.spatialBlend = 0f;
                    _music.priority = 16;
                    _music.volume = 0.3f;
                }

                return _music;
            }
        }

        public AudioSource Play(AudioClip clip, Vector3? pos, float volume, float pitch)
            => Play(clip, pos, volume, pitch, 128, -1f, -1f);

        public AudioSource Play(
            AudioClip clip,
            Vector3? pos,
            float volume,
            float pitch,
            int priority = 128,
            float spatialBlendOverride = -1f,
            float minDistanceOverride = -1f)
        {
            if (clip == null) return null;

            float now = Time.unscaledTime;
            int clampedPriority = Mathf.Clamp(priority, 0, 255);
            int chosenIdx = -1;

            // 1. Prefer an idle voice whose pitch-adjusted clip window has finished.
            for (int i = 0; i < _pool.Count; i++)
            {
                int idx = (_next + i) % _pool.Count;
                AudioSource cand = _pool[idx];
                if (!cand.isPlaying && now >= _busyUntil[idx])
                {
                    chosenIdx = idx;
                    break;
                }
            }

            // 2. Grow pool up to MaxPoolSize before considering voice stealing.
            if (chosenIdx < 0 && _pool.Count < MaxPoolSize)
            {
                var go = new GameObject("sfx");
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 6f;
                s.maxDistance = 45f;
                chosenIdx = _pool.Count;
                _pool.Add(s);
                _busyUntil.Add(0f);
                _priorities.Add(255);
            }

            // 3. Only steal a voice of equal or lower importance (higher numeric priority);
            //    break ties by the slot closest to finishing.
            if (chosenIdx < 0)
            {
                int worstPriority = -1;
                float earliestFinish = float.MaxValue;
                for (int i = 0; i < _pool.Count; i++)
                {
                    if (_priorities[i] < clampedPriority) continue;
                    if (_priorities[i] > worstPriority ||
                        (_priorities[i] == worstPriority && _busyUntil[i] < earliestFinish))
                    {
                        worstPriority = _priorities[i];
                        earliestFinish = _busyUntil[i];
                        chosenIdx = i;
                    }
                }
            }

            if (chosenIdx < 0)
            {
                // All 32 voices are currently playing strictly higher-priority audio.
                return null;
            }

            AudioSource src = _pool[chosenIdx];
            src.transform.position = pos ?? Vector3.zero;
            src.spatialBlend = spatialBlendOverride >= 0f ? spatialBlendOverride : (pos.HasValue ? 0.6f : 0f);
            src.minDistance = minDistanceOverride > 0f ? minDistanceOverride : 6f;
            src.maxDistance = 45f;
            src.priority = clampedPriority;
            src.loop = false;
            src.clip = clip;
            src.volume = volume;
            src.pitch = pitch;
            src.Play();

            float pitchScale = Mathf.Max(0.1f, Mathf.Abs(pitch));
            _busyUntil[chosenIdx] = now + (clip.length / pitchScale) + 0.15f;
            _priorities[chosenIdx] = clampedPriority;
            _next = (chosenIdx + 1) % _pool.Count;
            return src;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
