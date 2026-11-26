using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using WheresMyLeftSock.UI;

namespace WheresMyLeftSock.Core
{
    /// <summary>
    /// Wake-up start (spec 3.1): alarm beeping + title card in Menu state; any key/click starts the run.
    /// After the first run, restarts skip straight into play.
    /// </summary>
    [DisallowMultipleComponent]
    public class RunIntro : MonoBehaviour
    {
        public static bool HasPlayedOnce;

        private GameManager _gm;
        private AudioSource _alarm;
        private bool _waiting;
        private float _readyAt;
        private bool _introCarHonked;

        public static void ResetForNewSession() => HasPlayedOnce = false;

        private IEnumerator Start()
        {
            yield return null; // after GameManager.Start picked Menu/Playing
            _gm = GameManager.Instance;
            if (_gm == null) yield break;

            if (HasPlayedOnce)
            {
                if (_gm.CurrentState != GameState.Playing) _gm.StartRun();
                yield break;
            }

            if (_gm.CurrentState != GameState.Menu) yield break;
            _waiting = true;
            _readyAt = Time.unscaledTime + 0.4f;
            if (GameUI.Instance != null) GameUI.Instance.ShowTitle(true);
            _alarm = AudioHub.Instance.Play(Sfx.Get("alarm"), null, 0.5f, 1f);
            if (_alarm != null) _alarm.loop = true;
        }

        private void OnDestroy()
        {
            if (_alarm != null)
            {
                _alarm.loop = false;
                _alarm.Stop();
            }
        }

        private void Update()
        {
            if (!_waiting) return;
            if (!_introCarHonked && Time.unscaledTime >= _readyAt + 0.6f)
            {
                _introCarHonked = true;
                World.RideCar.Instance?.Honk(1);
            }

            if (Time.unscaledTime < _readyAt) return;
            bool pressed = (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) ||
                           (Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame)) ||
                           (Gamepad.current != null && (Gamepad.current.buttonSouth.wasPressedThisFrame || Gamepad.current.startButton.wasPressedThisFrame));
            if (!pressed) return;

            _waiting = false;
            HasPlayedOnce = true;
            if (_alarm != null) { _alarm.loop = false; _alarm.Stop(); }
            if (GameUI.Instance != null) GameUI.Instance.ShowTitle(false);
            Sfx.Play("pop", 0.6f, 0.7f);
            _gm.StartRun();
        }
    }
}
