using System.Collections.Generic;
using UnityEngine;

namespace WheresMyLeftSock.Core
{
    /// <summary>Pool of AudioSources for 2D + 3D one-shots, plus a music channel. Created on demand.</summary>
    public class AudioHub : MonoBehaviour
    {
        private static AudioHub _instance;
        private readonly List<AudioSource> _pool = new List<AudioSource>();
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
                    _music.volume = 0.3f;
                }

                return _music;
            }
        }

        public AudioSource Play(AudioClip clip, Vector3? pos, float volume, float pitch)
        {
            if (clip == null) return null;
            if (_pool.Count < 24)
            {
                var go = new GameObject("sfx");
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 4f;
                s.maxDistance = 40f;
                _pool.Add(s);
            }

            AudioSource src = null;
            for (int i = 0; i < _pool.Count; i++)
            {
                AudioSource cand = _pool[(_next + i) % _pool.Count];
                if (!cand.isPlaying) { src = cand; _next = (_next + i + 1) % _pool.Count; break; }
            }

            if (src == null)
            {
                src = _pool[_next];
                _next = (_next + 1) % _pool.Count;
            }

            src.transform.position = pos ?? Vector3.zero;
            src.spatialBlend = pos.HasValue ? 0.7f : 0f;
            src.loop = false;
            src.clip = clip;
            src.volume = volume;
            src.pitch = pitch;
            src.Play();
            return src;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
