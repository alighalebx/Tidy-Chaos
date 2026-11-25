using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using WheresMyLeftSock.Core;
using WheresMyLeftSock.Player;

namespace WheresMyLeftSock.UI
{
    /// <summary>
    /// Runtime-built Esc Pause Menu, persisted settings (Master Volume + Mouse Sensitivity),
    /// Controls Card, and a subtle bottom-right Controls Hint strip during gameplay (zero scene edits).
    /// </summary>
    [DisallowMultipleComponent]
    public class PauseMenuUI : MonoBehaviour
    {
        public const string MasterVolumePrefKey = "WMLS_MasterVolume";

        private const float MinSensitivity = 0.03f;
        private const float MaxSensitivity = 0.35f;

        private static readonly Color StripBg = new Color(0.06f, 0.07f, 0.10f, 0.65f);
        private static readonly Color BackdropCol = new Color(0.04f, 0.05f, 0.08f, 0.78f);
        private static readonly Color CardBg = new Color(0.09f, 0.10f, 0.14f, 0.96f);
        private static readonly Color SubCardBg = new Color(0.05f, 0.06f, 0.09f, 0.88f);
        private static readonly Color AccentGold = new Color(1f, 0.85f, 0.30f, 1f);
        private static readonly Color SliderFillCol = new Color(1f, 0.78f, 0.22f, 1f);

        private GameManager _gm;
        private ThirdPersonCamera _camera;

        private GameObject _controlsHint;
        private GameObject _pausePanel;
        private UnityEngine.UI.Slider _volumeSlider;
        private TMP_Text _volumeValueText;
        private UnityEngine.UI.Slider _sensitivitySlider;
        private TMP_Text _sensitivityValueText;
        private UnityEngine.UI.Button _resumeButton;
        private UnityEngine.UI.Button _restartButton;
        private UnityEngine.UI.Button _quitButton;

        private bool _prefsDirty;

        private void Awake()
        {
            BuildCanvas();
        }

        private void Start()
        {
            _gm = GameManager.Instance != null ? GameManager.Instance : FindFirstObjectByType<GameManager>();
            _camera = FindFirstObjectByType<ThirdPersonCamera>();

            LoadAndSyncSettings();

            if (_gm != null)
            {
                _gm.OnGameStateChanged += HandleGameStateChanged;
                _gm.OnPauseChanged += HandlePauseChanged;
            }

            RefreshVisibility();
        }

        private void Update()
        {
            if (_gm == null || _gm.CurrentState != GameState.Playing)
            {
                return;
            }

            bool pausePressed =
                (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
                (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);

            if (pausePressed)
            {
                _gm.TogglePause();
            }
        }

        private void OnDestroy()
        {
            FlushPrefs();

            if (_gm != null)
            {
                _gm.OnGameStateChanged -= HandleGameStateChanged;
                _gm.OnPauseChanged -= HandlePauseChanged;
            }

            if (_volumeSlider != null) _volumeSlider.onValueChanged.RemoveListener(HandleVolumeChanged);
            if (_sensitivitySlider != null) _sensitivitySlider.onValueChanged.RemoveListener(HandleSensitivityChanged);
            if (_resumeButton != null) _resumeButton.onClick.RemoveListener(HandleResumeClicked);
            if (_restartButton != null) _restartButton.onClick.RemoveListener(HandleRestartClicked);
            if (_quitButton != null) _quitButton.onClick.RemoveListener(HandleQuitClicked);
        }

        // ------------------------------------------------------------------ UI construction

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("PauseMenuCanvas");
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;

            var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            Transform root = canvasGo.transform;

            BuildControlsHint(root);
            BuildPauseOverlay(root);
        }

        private void BuildControlsHint(Transform root)
        {
            RectTransform hintRt = CreateRect(
                "ControlsHint",
                root,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-24f, 20f),
                new Vector2(690f, 66f));

            AddImage(hintRt, StripBg, raycastTarget: false);

            TMP_Text txt = CreateText(
                "ControlsHintText",
                hintRt,
                "[WASD] Move  ·  [Shift] Sprint  ·  [C / Ctrl] Crouch  ·  [Space] Jump\n[Hold LMB] Grab  ·  [RMB] Yeet  ·  [E] Open / Equip / Exit  ·  [Esc] Pause",
                18f,
                FontStyles.Normal,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(670f, 56f),
                new Color(0.95f, 0.96f, 1f, 0.90f));
            txt.lineSpacing = 6f;

            _controlsHint = hintRt.gameObject;
            _controlsHint.SetActive(false);
        }

        private void BuildPauseOverlay(Transform root)
        {
            var panelGo = new GameObject("PausePanel", typeof(RectTransform));
            panelGo.transform.SetParent(root, false);
            var panelRt = (RectTransform)panelGo.transform;
            panelRt.anchorMin = Vector2.zero;
            panelRt.anchorMax = Vector2.one;
            panelRt.offsetMin = Vector2.zero;
            panelRt.offsetMax = Vector2.zero;
            AddImage(panelRt, BackdropCol, raycastTarget: true);

            RectTransform card = CreateRect(
                "PauseCard",
                panelRt,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(760f, 700f));
            AddImage(card, CardBg, raycastTarget: true);

            RectTransform topBar = CreateRect(
                "AccentBar",
                card,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                Vector2.zero,
                new Vector2(0f, 8f));
            AddImage(topBar, AccentGold, raycastTarget: false);

            CreateText(
                "PauseTitle",
                card,
                "PAUSED",
                52f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -28f),
                new Vector2(600f, 68f),
                AccentGold);

            BuildSliderRow(
                card,
                "VolumeRow",
                "Master Volume",
                -112f,
                0f,
                1f,
                out _volumeSlider,
                out _volumeValueText);
            _volumeSlider.onValueChanged.AddListener(HandleVolumeChanged);

            BuildSliderRow(
                card,
                "SensitivityRow",
                "Mouse Sensitivity",
                -176f,
                MinSensitivity,
                MaxSensitivity,
                out _sensitivitySlider,
                out _sensitivityValueText);
            _sensitivitySlider.onValueChanged.AddListener(HandleSensitivityChanged);

            BuildControlsCard(card, -248f);

            _resumeButton = CreateButton(
                "ResumeButton",
                card,
                "RESUME",
                new Vector2(-220f, -595f),
                new Vector2(205f, 60f),
                new Color(0.20f, 0.65f, 0.34f, 1f));
            _resumeButton.onClick.AddListener(HandleResumeClicked);

            _restartButton = CreateButton(
                "RestartButton",
                card,
                "RESTART RUN",
                new Vector2(0f, -595f),
                new Vector2(215f, 60f),
                new Color(0.22f, 0.45f, 0.78f, 1f));
            _restartButton.onClick.AddListener(HandleRestartClicked);

            _quitButton = CreateButton(
                "QuitButton",
                card,
                "QUIT",
                new Vector2(220f, -595f),
                new Vector2(205f, 60f),
                new Color(0.75f, 0.24f, 0.24f, 1f));
            _quitButton.onClick.AddListener(HandleQuitClicked);

            _pausePanel = panelGo;
            _pausePanel.SetActive(false);
        }

        private static void BuildSliderRow(
            Transform parent,
            string name,
            string label,
            float yOffset,
            float min,
            float max,
            out UnityEngine.UI.Slider slider,
            out TMP_Text valueText)
        {
            RectTransform row = CreateRect(
                name,
                parent,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, yOffset),
                new Vector2(660f, 52f));

            CreateText(
                "Label",
                row,
                label,
                25f,
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0f),
                new Vector2(230f, 44f),
                Color.white);

            RectTransform sliderRt = CreateRect(
                "Slider",
                row,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(240f, 0f),
                new Vector2(325f, 34f));

            RectTransform bgRt = CreateRect(
                "Background",
                sliderRt,
                new Vector2(0f, 0.25f),
                new Vector2(1f, 0.75f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);
            AddImage(bgRt, new Color(0.22f, 0.24f, 0.30f, 1f), raycastTarget: true);

            RectTransform fillArea = CreateRect(
                "Fill Area",
                sliderRt,
                new Vector2(0f, 0.25f),
                new Vector2(1f, 0.75f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(-16f, 0f));

            RectTransform fillRt = CreateRect(
                "Fill",
                fillArea,
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(16f, 0f));
            AddImage(fillRt, SliderFillCol, raycastTarget: false);

            RectTransform handleArea = CreateRect(
                "Handle Slide Area",
                sliderRt,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(-20f, 0f));

            RectTransform handleRt = CreateRect(
                "Handle",
                handleArea,
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(24f, 0f));
            UnityEngine.UI.Image handleImg = AddImage(handleRt, Color.white, raycastTarget: true);

            slider = sliderRt.gameObject.AddComponent<UnityEngine.UI.Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };

            valueText = CreateText(
                "Value",
                row,
                "",
                23f,
                FontStyles.Normal,
                TextAlignmentOptions.Right,
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                Vector2.zero,
                new Vector2(80f, 44f),
                new Color(0.85f, 0.9f, 1f, 0.9f));
        }

        private static void BuildControlsCard(Transform parent, float yOffset)
        {
            RectTransform box = CreateRect(
                "ControlsCard",
                parent,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, yOffset),
                new Vector2(660f, 305f));
            AddImage(box, SubCardBg, raycastTarget: false);

            CreateText(
                "ControlsHeader",
                box,
                "CONTROLS",
                24f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -14f),
                new Vector2(580f, 34f),
                AccentGold);

            const string leftCol =
                "<b>Move</b>\n<b>Sprint</b>\n<b>Crouch</b>\n<b>Jump</b>";
            const string leftKeys =
                "W A S D / Arrows\nLeft Shift\nC or Left Ctrl\nSpace";

            const string rightCol =
                "<b>Grab / Carry</b>\n<b>Yeet / Throw</b>\n<b>Open / Equip / Exit</b>\n<b>Pause</b>";
            const string rightKeys =
                "Hold Left Click\nRight Click (while holding)\nE (or Left Click item)\nEsc / Start";

            TMP_Text lNames = CreateText(
                "LeftNames",
                box,
                leftCol,
                21f,
                FontStyles.Normal,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(26f, -64f),
                new Vector2(130f, 220f),
                Color.white);
            lNames.lineSpacing = 22f;

            TMP_Text lVals = CreateText(
                "LeftKeys",
                box,
                leftKeys,
                21f,
                FontStyles.Normal,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(142f, -64f),
                new Vector2(175f, 220f),
                new Color(0.8f, 0.9f, 1f, 0.9f));
            lVals.lineSpacing = 22f;

            TMP_Text rNames = CreateText(
                "RightNames",
                box,
                rightCol,
                21f,
                FontStyles.Normal,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(334f, -64f),
                new Vector2(185f, 220f),
                Color.white);
            rNames.lineSpacing = 22f;

            TMP_Text rVals = CreateText(
                "RightKeys",
                box,
                rightKeys,
                21f,
                FontStyles.Normal,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(510f, -64f),
                new Vector2(140f, 220f),
                new Color(0.8f, 0.9f, 1f, 0.9f));
            rVals.lineSpacing = 22f;
        }

        private static UnityEngine.UI.Button CreateButton(
            string name,
            Transform parent,
            string label,
            Vector2 pos,
            Vector2 size,
            Color baseColor)
        {
            RectTransform rt = CreateRect(
                name,
                parent,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 0.5f),
                pos,
                size);

            UnityEngine.UI.Image img = AddImage(rt, Color.white, raycastTarget: true);
            var btn = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
            btn.targetGraphic = img;
            btn.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };

            var colors = btn.colors;
            colors.normalColor = baseColor;
            colors.highlightedColor = Color.Lerp(baseColor, Color.white, 0.22f);
            colors.pressedColor = Color.Lerp(baseColor, Color.black, 0.25f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            CreateText(
                "Label",
                rt,
                label,
                24f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero,
                Color.white);

            return btn;
        }

        private static RectTransform CreateRect(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPos,
            Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
            return rt;
        }

        private static UnityEngine.UI.Image AddImage(RectTransform rt, Color color, bool raycastTarget)
        {
            var img = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            img.color = color;
            img.raycastTarget = raycastTarget;
            return img;
        }

        private static TMP_Text CreateText(
            string name,
            Transform parent,
            string text,
            float size,
            FontStyles style,
            TextAlignmentOptions align,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPos,
            Vector2 sizeDelta,
            Color color)
        {
            RectTransform rt = CreateRect(name, parent, anchorMin, anchorMax, pivot, anchoredPos, sizeDelta);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.alignment = align;
            tmp.color = color;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            return tmp;
        }

        // ------------------------------------------------------------------ state & settings

        private void LoadAndSyncSettings()
        {
            float savedVol = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumePrefKey, 1f));
            AudioListener.volume = savedVol;
            if (_volumeSlider != null) _volumeSlider.SetValueWithoutNotify(savedVol);
            UpdateVolumeLabel(savedVol);

            float savedSens = _camera != null
                ? _camera.MouseSensitivity
                : PlayerPrefs.GetFloat(ThirdPersonCamera.MouseSensitivityPrefKey, ThirdPersonCamera.DefaultMouseSensitivity);
            savedSens = Mathf.Clamp(savedSens, MinSensitivity, MaxSensitivity);
            if (_camera != null) _camera.MouseSensitivity = savedSens;
            if (_sensitivitySlider != null) _sensitivitySlider.SetValueWithoutNotify(savedSens);
            UpdateSensitivityLabel(savedSens);
        }

        private void HandleVolumeChanged(float value)
        {
            float clamped = Mathf.Clamp01(value);
            AudioListener.volume = clamped;
            PlayerPrefs.SetFloat(MasterVolumePrefKey, clamped);
            _prefsDirty = true;
            UpdateVolumeLabel(clamped);
        }

        private void HandleSensitivityChanged(float value)
        {
            float clamped = Mathf.Clamp(value, MinSensitivity, MaxSensitivity);
            if (_camera == null) _camera = FindFirstObjectByType<ThirdPersonCamera>();
            if (_camera != null) _camera.MouseSensitivity = clamped;
            PlayerPrefs.SetFloat(ThirdPersonCamera.MouseSensitivityPrefKey, clamped);
            _prefsDirty = true;
            UpdateSensitivityLabel(clamped);
        }

        private void UpdateVolumeLabel(float vol)
        {
            if (_volumeValueText != null)
            {
                _volumeValueText.text = $"{Mathf.RoundToInt(vol * 100f)}%";
            }
        }

        private void UpdateSensitivityLabel(float sens)
        {
            if (_sensitivityValueText != null)
            {
                _sensitivityValueText.text = sens.ToString("0.00");
            }
        }

        private void HandleGameStateChanged(GameState state) => RefreshVisibility();

        private void HandlePauseChanged(bool paused)
        {
            RefreshVisibility();
            if (!paused)
            {
                FlushPrefs();
                if (EventSystem.current != null)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }
            }
        }

        private void RefreshVisibility()
        {
            bool playing = _gm != null && _gm.CurrentState == GameState.Playing;
            bool paused = playing && _gm.IsPaused;

            if (_pausePanel != null) _pausePanel.SetActive(paused);
            if (_controlsHint != null) _controlsHint.SetActive(playing && !paused);
        }

        private void HandleResumeClicked()
        {
            FlushPrefs();
            _gm?.SetPaused(false);
        }

        private void HandleRestartClicked()
        {
            FlushPrefs();
            _gm?.RestartRun();
        }

        private void HandleQuitClicked()
        {
            FlushPrefs();
            Time.timeScale = 1f;

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void FlushPrefs()
        {
            if (!_prefsDirty)
            {
                return;
            }

            PlayerPrefs.Save();
            _prefsDirty = false;
        }
    }
}
