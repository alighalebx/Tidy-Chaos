using UnityEngine;
using WheresMyLeftSock.Player;
using WheresMyLeftSock.World;

namespace WheresMyLeftSock.Core
{
    /// <summary>
    /// Hooks the procedural SFX into gameplay: music, footsteps, jump/land, bonks, grab/yeet, prop impacts,
    /// win/lose stingers. All sounds come from Sfx (no audio files needed).
    /// </summary>
    [DisallowMultipleComponent]
    public class GameAudio : MonoBehaviour
    {
        [SerializeField] private float musicVolume = 0.45f;
        [SerializeField] private float footstepVolume = 0.5f;

        private GameManager _gm;
        private PlayerController _player;
        private InteractionManager _interaction;
        private float _stepTimer;
        private bool _leftFoot;
        private static float _musicPitchTarget = 1f;

        public static void SetMusicPitch(float p) => _musicPitchTarget = p;

        private void Start()
        {
            _musicPitchTarget = 1f;
            _gm = GameManager.Instance;
            _player = FindFirstObjectByType<PlayerController>();
            _interaction = _player != null ? _player.GetComponent<InteractionManager>() : FindFirstObjectByType<InteractionManager>();

            if (_gm != null) _gm.OnGameStateChanged += HandleState;
            if (_player != null)
            {
                _player.Jumped += OnJump;
                _player.Landed += OnLand;
                _player.KnockedDown += OnKnocked;
            }

            if (_interaction != null)
            {
                _interaction.OnObjectGrabbed += OnGrab;
                _interaction.OnObjectReleased += OnRelease;
            }

            foreach (PhysicsClutter c in FindObjectsByType<PhysicsClutter>(FindObjectsSortMode.None))
            {
                if (c.GetComponent<ImpactSound>() == null) c.gameObject.AddComponent<ImpactSound>();
            }

            // pre-bake the music so the first frame of play doesn't hitch
            Sfx.Get("music");
            if (_gm != null && _gm.CurrentState == GameState.Playing) StartMusic();
        }

        private void OnDestroy()
        {
            if (_gm != null) _gm.OnGameStateChanged -= HandleState;
            if (_player != null)
            {
                _player.Jumped -= OnJump;
                _player.Landed -= OnLand;
                _player.KnockedDown -= OnKnocked;
            }

            if (_interaction != null)
            {
                _interaction.OnObjectGrabbed -= OnGrab;
                _interaction.OnObjectReleased -= OnRelease;
            }
        }

        private void HandleState(GameState s)
        {
            switch (s)
            {
                case GameState.Playing:
                    StartMusic();
                    break;
                case GameState.Win:
                    AudioHub.Instance.Music.Stop();
                    Sfx.Play("win", 0.8f);
                    break;
                case GameState.Lose:
                    AudioHub.Instance.Music.Stop();
                    Sfx.Play("lose", 0.8f);
                    break;
            }
        }

        private void StartMusic()
        {
            AudioSource m = AudioHub.Instance.Music;
            m.clip = Sfx.Get("music");
            m.volume = musicVolume;
            m.pitch = 1f;
            _musicPitchTarget = 1f;
            if (!m.isPlaying) m.Play();
        }

        private void Update()
        {
            AudioSource m = AudioHub.Instance.Music;
            m.pitch = Mathf.MoveTowards(m.pitch, _musicPitchTarget, Time.deltaTime * 0.5f);

            if (_player == null || _gm == null || _gm.CurrentState != GameState.Playing) return;
            float speed = _player.CurrentHorizontalSpeed;
            if (!_player.IsGrounded || _player.IsKnockedDown || speed < 0.8f)
            {
                _stepTimer = 0.05f;
                return;
            }

            _stepTimer -= Time.deltaTime;
            if (_stepTimer <= 0f)
            {
                _stepTimer = Mathf.Lerp(0.45f, 0.24f, Mathf.InverseLerp(2f, 7.5f, speed));
                _leftFoot = !_leftFoot;
                Sfx.PlayAt("step", _player.transform.position, footstepVolume * Mathf.Clamp01(speed / 4f + 0.3f),
                    (_leftFoot ? 1f : 1.12f) * Random.Range(0.92f, 1.08f));
            }
        }

        private void OnJump() => Sfx.PlayAt("jump", _player.transform.position, 0.45f, Random.Range(0.95f, 1.1f));

        private void OnLand(float fallSpeed)
        {
            if (fallSpeed > 2.5f) Sfx.PlayAt("land", _player.transform.position, Mathf.Clamp01(fallSpeed / 8f) * 0.8f);
        }

        private void OnKnocked() => Sfx.PlayAt("bonk", _player.transform.position, 0.8f, Random.Range(0.9f, 1.1f));

        private void OnGrab(PhysicsClutter c) => Sfx.PlayAt("grab", c.transform.position, 0.5f, Random.Range(0.9f, 1.15f));

        private void OnRelease(PhysicsClutter c, bool thrown)
        {
            if (thrown) Sfx.PlayAt("whoosh", c.transform.position, 0.7f, Random.Range(0.9f, 1.15f));
        }
    }
}
