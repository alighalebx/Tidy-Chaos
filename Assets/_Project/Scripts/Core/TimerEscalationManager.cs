using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using WheresMyLeftSock.Player;
using WheresMyLeftSock.World;

namespace WheresMyLeftSock.Core
{
    /// <summary>
    /// Spec 4.5: the ride gets more impatient as the timer drops. Honks, phone toasts, faster music, and a red
    /// heartbeat panic vignette + FOV squeeze + shake for the final seconds.
    /// </summary>
    [DisallowMultipleComponent]
    public class TimerEscalationManager : MonoBehaviour
    {
        [SerializeField] private float politeHonkAt = 40f;
        [SerializeField] private float impatientAt = 25f;
        [SerializeField] private float panicAt = 12f;
        [SerializeField] private float panicMusicPitch = 1.3f;
        [SerializeField] private float impatientMusicPitch = 1.15f;

        /// <summary>Phone notification text (UI listens).</summary>
        public static event Action<string> OnToast;

        public int Stage { get; private set; }

        private GameManager _gm;
        private Volume _panicVolume;
        private float _heartTimer;
        private ThirdPersonCamera _cam;

        public static void Toast(string msg)
        {
            OnToast?.Invoke(msg);
            Sfx.Play("ping", 0.7f);
        }

        private void Start()
        {
            _gm = GameManager.Instance;
            _cam = FindFirstObjectByType<ThirdPersonCamera>();
            BuildPanicVolume();
            if (_gm != null) _gm.OnGameStateChanged += HandleState;
        }

        private void OnDestroy()
        {
            if (_gm != null) _gm.OnGameStateChanged -= HandleState;
            OnToast = null;
        }

        private void HandleState(GameState s)
        {
            if (s == GameState.Playing)
            {
                Stage = 0;
                Toast($"Outside. Leaving in {Mathf.CeilToInt(_gm.RoundDuration)}s.");
            }
            else if (s == GameState.Win || s == GameState.Lose)
            {
                Stage = 4;
                if (_cam != null) _cam.SetExtraFov(0f);
            }
        }

        private void Update()
        {
            if (_gm == null || _gm.CurrentState != GameState.Playing)
            {
                FadePanic(0f);
                return;
            }

            float t = _gm.TimeRemaining;
            RideCar car = RideCar.Instance;

            if (Stage < 1 && t <= politeHonkAt && _gm.RoundDuration > politeHonkAt)
            {
                Stage = 1;
                if (car != null) car.Honk(1);
                Toast("Yo I'm outside.");
            }

            if (Stage < 2 && t <= impatientAt)
            {
                Stage = 2;
                if (car != null) car.Honk(2, 0.12f);
                GameAudio.SetMusicPitch(impatientMusicPitch);
                Toast("Seriously, I'm double-parked!");
            }

            if (Stage < 3 && t <= panicAt)
            {
                Stage = 3;
                if (car != null) { car.SetPanic(true); car.Rev(); }
                GameAudio.SetMusicPitch(panicMusicPitch);
                Toast("LEAVING IN 10 SECONDS!!");
            }

            if (Stage == 3)
            {
                float urgency = 1f - Mathf.Clamp01(t / panicAt);
                float beatInterval = Mathf.Lerp(0.8f, 0.42f, urgency);
                _heartTimer -= Time.deltaTime;
                if (_heartTimer <= 0f)
                {
                    _heartTimer = beatInterval;
                    Sfx.Play("heartbeat", 0.9f);
                    if (_cam != null) _cam.AddShake(0.12f + urgency * 0.18f);
                }

                float pulse = Mathf.Pow(Mathf.Clamp01(_heartTimer / beatInterval), 3f);
                FadePanic(Mathf.Lerp(0.55f, 1f, pulse));
                if (_cam != null) _cam.SetExtraFov(-4f - urgency * 4f);
            }
        }

        private void FadePanic(float target)
        {
            if (_panicVolume == null) return;
            _panicVolume.weight = Mathf.MoveTowards(_panicVolume.weight, target, Time.deltaTime * 4f);
        }

        private void BuildPanicVolume()
        {
            var go = new GameObject("PanicVolume");
            go.transform.SetParent(transform, false);
            _panicVolume = go.AddComponent<Volume>();
            _panicVolume.isGlobal = true;
            _panicVolume.priority = 50f;
            _panicVolume.weight = 0f;
            VolumeProfile p = ScriptableObject.CreateInstance<VolumeProfile>();
            Vignette v = p.Add<Vignette>(true);
            v.color.Override(new Color(0.75f, 0.02f, 0.05f));
            v.intensity.Override(0.48f);
            v.smoothness.Override(0.55f);
            ChromaticAberration ca = p.Add<ChromaticAberration>(true);
            ca.intensity.Override(0.35f);
            _panicVolume.sharedProfile = p;
        }
    }
}
