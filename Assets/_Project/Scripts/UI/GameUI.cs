using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WheresMyLeftSock.Core;
using WheresMyLeftSock.Items;

namespace WheresMyLeftSock.UI
{
    /// <summary>
    /// Runtime-built game UI (no prefab wiring): outfit checklist, phone toasts, title card, and the results
    /// breakdown written into the existing HUD end panel (rank, outfit, dignity, time bonus, score, funny tags).
    /// </summary>
    [DisallowMultipleComponent]
    public class GameUI : MonoBehaviour
    {
        private static readonly Color Panel = new Color(0.06f, 0.07f, 0.1f, 0.72f);
        private static readonly Color Good = new Color(0.45f, 0.95f, 0.45f);
        private static readonly Color Meh = new Color(1f, 0.7f, 0.25f);
        private static readonly Color Empty = new Color(1f, 1f, 1f, 0.35f);

        public static GameUI Instance { get; private set; }

        private GameManager _gm;
        private RectTransform _checklist;
        private readonly Dictionary<WearSlot, (Image box, TMP_Text label)> _rows = new Dictionary<WearSlot, (Image, TMP_Text)>();
        private TMP_Text _dignity;
        private RectTransform _toast;
        private TMP_Text _toastText;
        private readonly Queue<string> _toastQueue = new Queue<string>();
        private bool _toastBusy;
        private GameObject _title;
        private TMP_Text _titlePrompt;

        private void Awake()
        {
            Instance = this;
            BuildCanvas();
        }

        private void Start()
        {
            _gm = GameManager.Instance;
            if (_gm != null) _gm.OnGameStateChanged += HandleState;
            if (OutfitManager.Instance != null) OutfitManager.Instance.OnItemCollected += HandleCollected;
            TimerEscalationManager.OnToast += QueueToast;
            RefreshChecklist();
            if (_gm != null) HandleState(_gm.CurrentState);
        }

        private void OnDestroy()
        {
            if (_gm != null) _gm.OnGameStateChanged -= HandleState;
            if (OutfitManager.Instance != null) OutfitManager.Instance.OnItemCollected -= HandleCollected;
            TimerEscalationManager.OnToast -= QueueToast;
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ build

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("GameUICanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            Transform root = canvasGo.transform;

            // --- checklist (top-left)
            _checklist = R("Checklist", root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -24), new Vector2(400, 330));
            Img(_checklist, Panel);
            T("Title", _checklist, "GET DRESSED!", 34, FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(22, -14), new Vector2(360, 44), new Color(1f, 0.85f, 0.3f));
            int row = 0;
            foreach (WearSlot s in OutfitManager.Slots)
            {
                float y = -68 - row * 44;
                RectTransform boxRt = R("Box", _checklist, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, y - 6), new Vector2(26, 26));
                Image box = Img(boxRt, Empty);
                TMP_Text label = T("Label", _checklist, s.ToString(), 26, FontStyles.Normal, TextAlignmentOptions.Left, new Vector2(64, y), new Vector2(330, 38), Color.white);
                _rows[s] = (box, label);
                row++;
            }

            _dignity = T("Dignity", _checklist, "Dignity 0/100", 22, FontStyles.Italic, TextAlignmentOptions.Left, new Vector2(24, -290), new Vector2(360, 32), new Color(1f, 1f, 1f, 0.75f));

            // --- phone toast (top-right, slides in)
            _toast = R("PhoneToast", root, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(520, -110), new Vector2(480, 118));
            Img(_toast, new Color(0.08f, 0.09f, 0.12f, 0.94f));
            RectTransform bar = R("Accent", _toast, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, new Vector2(10, 0));
            Img(bar, new Color(0.3f, 0.85f, 0.4f));
            T("From", _toast, "RIDE  ·  now", 20, FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(30, -12), new Vector2(420, 28), new Color(1f, 1f, 1f, 0.55f));
            _toastText = T("Msg", _toast, "", 30, FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(30, -42), new Vector2(430, 66), Color.white);

            // --- title card (menu)
            _title = new GameObject("TitleCard", typeof(RectTransform));
            _title.transform.SetParent(root, false);
            var trt = (RectTransform)_title.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            Img(trt, new Color(0.05f, 0.04f, 0.1f, 0.45f));
            TMP_Text big = T("Big", trt, "WHERE'S MY\nLEFT SOCK?!", 128, FontStyles.Bold, TextAlignmentOptions.Center, Vector2.zero, new Vector2(1500, 360), new Color(1f, 0.85f, 0.25f), centered: true, y: 120);
            big.rectTransform.localRotation = Quaternion.Euler(0, 0, 4f);
            big.outlineWidth = 0.18f;
            big.outlineColor = new Color32(120, 40, 20, 255);
            T("Sub", trt, "You overslept. Your ride is outside. You're still in pajamas.", 34, FontStyles.Italic, TextAlignmentOptions.Center, Vector2.zero, new Vector2(1400, 60), Color.white, centered: true, y: -110);
            _titlePrompt = T("Prompt", trt, "CLICK OR PRESS ANY KEY TO WAKE UP", 38, FontStyles.Bold, TextAlignmentOptions.Center, Vector2.zero, new Vector2(1200, 60), new Color(0.6f, 1f, 0.7f), centered: true, y: -230);
            _title.SetActive(false);
        }

        private static RectTransform R(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        private static Image Img(RectTransform rt, Color c)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        private static TMP_Text T(string name, Transform parent, string text, float size, FontStyles style, TextAlignmentOptions align,
            Vector2 pos, Vector2 box, Color color, bool centered = false, float y = 0f)
        {
            RectTransform rt = centered
                ? R(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, y), box)
                : R(name, parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), pos, box);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = align;
            t.color = color;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            return t;
        }

        // ------------------------------------------------------------------ state

        public void ShowTitle(bool on)
        {
            if (_title != null) _title.SetActive(on);
        }

        private void Update()
        {
            if (_title != null && _title.activeSelf && _titlePrompt != null)
            {
                _titlePrompt.alpha = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 4f);
            }
        }

        private void HandleState(GameState s)
        {
            _checklist.gameObject.SetActive(s == GameState.Playing);
            if (s == GameState.Win || s == GameState.Lose)
            {
                StartCoroutine(FillEndScreen(s == GameState.Lose));
            }
        }

        private void HandleCollected(WearSlot slot, ItemData item, bool accepted)
        {
            RefreshChecklist();
            if (_rows.TryGetValue(slot, out var r)) StartCoroutine(PunchUI(r.label.rectTransform));
            if (!accepted) QueueToast($"(already wearing the good {RequiredName(slot)}. {item.displayName} goes in your pocket)", false);
        }

        private string RequiredName(WearSlot s)
        {
            ItemData d = OutfitManager.Instance != null ? OutfitManager.Instance.RequiredFor(s) : null;
            return d != null ? d.displayName : s.ToString();
        }

        private void RefreshChecklist()
        {
            OutfitManager om = OutfitManager.Instance;
            foreach (WearSlot s in OutfitManager.Slots)
            {
                if (!_rows.TryGetValue(s, out var r)) continue;
                ItemData eq = om != null ? om.Equipped(s) : null;
                if (eq == null)
                {
                    r.box.color = Empty;
                    r.label.text = RequiredName(s);
                    r.label.color = Color.white;
                }
                else if (eq.IsTrap)
                {
                    r.box.color = Meh;
                    r.label.text = $"{eq.displayName} <size=75%><i>({eq.funnyTag})</i></size>";
                    r.label.color = Meh;
                }
                else
                {
                    r.box.color = Good;
                    r.label.text = $"<s>{eq.displayName}</s>";
                    r.label.color = Good;
                }
            }

            if (om != null) _dignity.text = $"Dignity {om.DignityScore}/{om.MaxDignity}";
        }

        private static IEnumerator PunchUI(RectTransform rt)
        {
            for (float k = 0f; k < 1f; k += Time.unscaledDeltaTime / 0.3f)
            {
                rt.localScale = Vector3.one * (1f + Mathf.Sin(k * Mathf.PI) * 0.25f);
                yield return null;
            }

            rt.localScale = Vector3.one;
        }

        // ------------------------------------------------------------------ toasts

        private void QueueToast(string msg) => QueueToast(msg, true);

        private void QueueToast(string msg, bool fromRide)
        {
            _toastQueue.Enqueue(msg);
            if (!_toastBusy) StartCoroutine(ToastRoutine());
        }

        private IEnumerator ToastRoutine()
        {
            _toastBusy = true;
            while (_toastQueue.Count > 0)
            {
                _toastText.text = _toastQueue.Dequeue();
                const float hidden = 520f, shown = -24f;
                for (float k = 0f; k < 1f; k += Time.unscaledDeltaTime / 0.28f)
                {
                    float e = 1f + 2.2f * Mathf.Pow(k - 1f, 3f) + 1.2f * Mathf.Pow(k - 1f, 2f); // ease-out-back
                    _toast.anchoredPosition = new Vector2(Mathf.LerpUnclamped(hidden, shown, e), -110f);
                    yield return null;
                }

                _toast.anchoredPosition = new Vector2(shown, -110f);
                yield return new WaitForSecondsRealtime(2.6f);
                for (float k = 0f; k < 1f; k += Time.unscaledDeltaTime / 0.22f)
                {
                    _toast.anchoredPosition = new Vector2(Mathf.Lerp(shown, hidden, k * k), -110f);
                    yield return null;
                }

                _toast.anchoredPosition = new Vector2(hidden, -110f);
            }

            _toastBusy = false;
        }

        // ------------------------------------------------------------------ results

        private IEnumerator FillEndScreen(bool lost)
        {
            yield return null; // let the HUD set its own texts first, then overwrite
            GameObject hud = GameObject.Find("HUDCanvas");
            Transform panel = hud != null ? hud.transform.Find("EndScreenPanel") : null;
            if (panel == null || OutfitManager.Instance == null) yield break;

            OutfitManager om = OutfitManager.Instance;
            float t = _gm != null ? _gm.TimeRemaining : 0f;
            om.Rank(lost, t, out string letter, out string rankTitle);
            int dignity = om.DignityScore;
            int timeBonus = Mathf.FloorToInt(Mathf.Max(0f, t) * 2f);
            int total = OutfitManager.TotalScore(dignity, t);

            var title = panel.Find("EndTitleText") != null ? panel.Find("EndTitleText").GetComponent<TMP_Text>() : null;
            var sub = panel.Find("EndSubtitleText") != null ? panel.Find("EndSubtitleText").GetComponent<TMP_Text>() : null;
            var btn = panel.Find("RestartButton") as RectTransform;

            string rankCol = letter == "S" ? "#FFD54A" : letter == "A" ? "#7CFC7C" : letter == "B" ? "#6EC6FF" : letter == "C" ? "#FFB347" : "#FF6B6B";
            if (title != null)
            {
                title.rectTransform.anchoredPosition = new Vector2(0, 230);
                title.rectTransform.sizeDelta = new Vector2(1600, 110);
                title.text = $"<color={rankCol}>{letter}</color>  {rankTitle.ToUpperInvariant()}";
            }

            if (sub != null)
            {
                sub.rectTransform.anchoredPosition = new Vector2(0, -10);
                sub.rectTransform.sizeDelta = new Vector2(1300, 380);
                sub.fontSize = 30;
                sub.alignment = TextAlignmentOptions.Center;
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(lost ? "Time ran out. Your ride drove off without you!" : $"Escaped with {t:0.0}s to spare!");
                sb.AppendLine();
                foreach (WearSlot s in OutfitManager.Slots)
                {
                    ItemData eq = om.Equipped(s);
                    if (eq == null) sb.AppendLine($"<color=#FF7B7B>{RequiredName(s)}: nope</color>   <color=#999>+0</color>");
                    else if (eq.IsTrap) sb.AppendLine($"<color=#FFB347>{eq.displayName}</color> <i><size=80%>({eq.funnyTag})</size></i>   +{eq.dignityPoints}");
                    else sb.AppendLine($"<color=#7CFC7C>{eq.displayName}</color>   +{eq.dignityPoints}");
                }

                sb.AppendLine();
                sb.AppendLine($"Dignity {dignity}/{om.MaxDignity}   +   Time bonus {timeBonus}   =   <b><size=120%>SCORE {total}</size></b>");
                List<string> tags = om.FunnyTags();
                if (tags.Count > 0) sb.Append($"<size=85%><i>{string.Join("  ·  ", tags)}</i></size>");
                sub.text = sb.ToString();
            }

            if (btn != null) btn.anchoredPosition = new Vector2(0, -260);
        }
    }
}
