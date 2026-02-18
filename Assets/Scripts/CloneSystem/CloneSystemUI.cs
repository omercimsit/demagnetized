using UnityEngine;

namespace CloneSystem
{
    // handles all the OnGUI rendering for clone selection and status panel
    // extracted from AAACloneSystem to keep that file from getting even bigger
    public class CloneSystemUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private AAACloneSystem cloneSystem;

        [Header("Slow-Mo Visual Settings")]
        [SerializeField] private float vignetteIntensity = 0.25f;
        [SerializeField] private int vignetteEdgeWidth = 60;
        [SerializeField] private Color vignetteColor = new Color(0.05f, 0.15f, 0.35f, 1f);

        // cached GUI styles - initialized once, reused every frame to avoid GC
        private static GUIStyle _boxStyle;
        private static GUIStyle _titleStyle;
        private static GUIStyle _labelStyle;
        private static GUIStyle _hintStyle;
        private static GUIStyle _cardTitleStyle;
        private static GUIStyle _cardSubStyle;
        private static GUIStyle _cardNameStyle;
        private static GUIStyle _cardDescStyle;
        private static GUIStyle _badgeStyle;
        private static GUIStyle _statusStyle;
        private static GUIStyle _hintBarStyle;
        private static GUIStyle _iconStyle;
        private static Texture2D _bgTexture;
        private static bool _stylesInitialized = false;

        private float _selectionAnimTime = 0f;
        private float _selectionAlpha = 0f;
        private float _trackingOffset = 0f;
        private int _hoverIndex = -1;

        private static readonly string[] Icons = { "◉", "◈", "◆" };

        private void Awake()
        {
            if (cloneSystem == null)
                cloneSystem = GetComponent<AAACloneSystem>();

            if (cloneSystem == null)
                cloneSystem = AAACloneSystem.Instance;
        }

        private void OnEnable()
        {
            if (cloneSystem != null)
                cloneSystem.OnSelectionChanged += ResetSelectionAnimation;
        }

        private void OnDisable()
        {
            if (cloneSystem != null)
                cloneSystem.OnSelectionChanged -= ResetSelectionAnimation;
        }

        private void OnGUI()
        {
            if (cloneSystem == null) return;

            InitStyles();

            DrawSlowMoOverlay();

            if (cloneSystem.IsSelectionOpen)
            {
                DrawSelectionBar();
            }
            else
            {
                DrawStatusPanel();
            }
        }

        // init once and cache - creating GUIStyles every frame is a bad time
        private static void InitStyles()
        {
            if (_stylesInitialized && _boxStyle != null) return;

            if (_bgTexture == null)
            {
                _bgTexture = new Texture2D(1, 1);
                _bgTexture.SetPixel(0, 0, new Color(0.05f, 0.05f, 0.1f, 0.95f));
                _bgTexture.Apply();
            }

            _boxStyle = new GUIStyle(GUI.skin.box);
            _boxStyle.normal.background = _bgTexture;

            _titleStyle = new GUIStyle(GUI.skin.label);
            _titleStyle.fontSize = 22;
            _titleStyle.fontStyle = FontStyle.Bold;
            _titleStyle.alignment = TextAnchor.MiddleLeft;

            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.fontSize = 16;

            _hintStyle = new GUIStyle(GUI.skin.label);
            _hintStyle.fontSize = 14;

            _cardTitleStyle = new GUIStyle(GUI.skin.label);
            _cardTitleStyle.fontSize = 28;
            _cardTitleStyle.fontStyle = FontStyle.Bold;
            _cardTitleStyle.alignment = TextAnchor.MiddleCenter;

            _cardSubStyle = new GUIStyle(GUI.skin.label);
            _cardSubStyle.fontSize = 12;
            _cardSubStyle.alignment = TextAnchor.MiddleCenter;

            _cardNameStyle = new GUIStyle(GUI.skin.label);
            _cardNameStyle.fontStyle = FontStyle.Bold;
            _cardNameStyle.alignment = TextAnchor.MiddleCenter;

            _cardDescStyle = new GUIStyle(GUI.skin.label);
            _cardDescStyle.fontSize = 13;
            _cardDescStyle.alignment = TextAnchor.MiddleCenter;

            _badgeStyle = new GUIStyle(GUI.skin.label);
            _badgeStyle.fontSize = 13;
            _badgeStyle.fontStyle = FontStyle.Bold;
            _badgeStyle.alignment = TextAnchor.MiddleCenter;

            _statusStyle = new GUIStyle(GUI.skin.label);
            _statusStyle.fontSize = 12;
            _statusStyle.fontStyle = FontStyle.Bold;
            _statusStyle.alignment = TextAnchor.MiddleCenter;

            _hintBarStyle = new GUIStyle(GUI.skin.label);
            _hintBarStyle.fontSize = 13;
            _hintBarStyle.alignment = TextAnchor.MiddleCenter;

            _iconStyle = new GUIStyle(GUI.skin.label);
            _iconStyle.alignment = TextAnchor.MiddleCenter;

            _stylesInitialized = true;
        }

        private void DrawSlowMoOverlay()
        {
            if (cloneSystem.CurrentPhase != AAACloneSystem.Phase.Playback) return;
            if (Time.timeScale >= 0.8f) return;

            float slowAmount = 1f - Time.timeScale;
            float alpha = slowAmount * vignetteIntensity;

            DrawSubtleVignette(alpha);
        }

        private void DrawSubtleVignette(float alpha)
        {
            Color edgeColor = new Color(vignetteColor.r, vignetteColor.g, vignetteColor.b, alpha);
            GUI.color = edgeColor;

            // four edge quads - crude but effective
            GUI.DrawTexture(new Rect(0, 0, Screen.width, vignetteEdgeWidth), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, Screen.height - vignetteEdgeWidth, Screen.width, vignetteEdgeWidth), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, 0, vignetteEdgeWidth, Screen.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(Screen.width - vignetteEdgeWidth, 0, vignetteEdgeWidth, Screen.height), Texture2D.whiteTexture);

            GUI.color = Color.white;
        }

        private void DrawSelectionBar()
        {
            var tex = MenuStyles.SolidTexture;
            float w = Screen.width;
            float h = Screen.height;

            _selectionAlpha = Mathf.MoveTowards(_selectionAlpha, 1f, Time.unscaledDeltaTime * 4f);
            _selectionAnimTime += Time.unscaledDeltaTime;
            _trackingOffset += Time.unscaledDeltaTime * 30f;
            float alpha = _selectionAlpha;

            // background layers
            GUI.color = new Color(MenuStyles.DeepBlack.r, MenuStyles.DeepBlack.g, MenuStyles.DeepBlack.b, 0.97f * alpha);
            GUI.DrawTexture(new Rect(0, 0, w, h), tex);

            GUI.color = new Color(MenuStyles.FilmBrown.r, MenuStyles.FilmBrown.g, MenuStyles.FilmBrown.b, 0.4f * alpha);
            GUI.DrawTexture(new Rect(0, h * 0.25f, w, h * 0.5f), tex);

            float pulse = (Mathf.Sin(Time.unscaledTime * 1.5f) + 1f) * 0.5f;
            GUI.color = new Color(MenuStyles.TapeOrange.r * 0.3f, MenuStyles.TapeOrange.g * 0.3f, MenuStyles.TapeOrange.b * 0.3f, 0.08f * pulse * alpha);
            GUI.DrawTexture(new Rect(0, h * 0.35f, w, h * 0.3f), tex);

            MenuStyles.DrawTrackingLines(w, h, _trackingOffset, alpha * 0.6f);
            MenuStyles.DrawScanlines(w, h, Time.unscaledTime * 20f, alpha * 0.5f);
            MenuStyles.DrawCornerBrackets(40, 60, 3, alpha);

            float centerY = h / 2f;

            float reelRotation = Time.unscaledTime * 30f;
            MenuStyles.DrawTapeReel(80, centerY, 45, reelRotation, alpha * 0.4f);
            MenuStyles.DrawTapeReel(w - 80, centerY, 45, -reelRotation, alpha * 0.4f);

            // title with shadow offset
            GUI.color = new Color(MenuStyles.TapeOrange.r, MenuStyles.TapeOrange.g, MenuStyles.TapeOrange.b, 0.5f * alpha);
            _cardTitleStyle.normal.textColor = GUI.color;
            GUI.Label(new Rect(2, centerY - 162, w, 40), L.Get("clone_select_title"), _cardTitleStyle);

            GUI.color = new Color(MenuStyles.WarmWhite.r, MenuStyles.WarmWhite.g, MenuStyles.WarmWhite.b, alpha);
            _cardTitleStyle.normal.textColor = GUI.color;
            GUI.Label(new Rect(0, centerY - 160, w, 40), L.Get("clone_select_title"), _cardTitleStyle);

            GUI.color = new Color(MenuStyles.DustyGray.r, MenuStyles.DustyGray.g, MenuStyles.DustyGray.b, 0.7f * alpha);
            _cardSubStyle.normal.textColor = GUI.color;
            GUI.Label(new Rect(0, centerY - 125, w, 20), L.Get("clone_select_subtitle"), _cardSubStyle);

            DrawCloneCards(w, h, centerY, alpha, tex);
            DrawControlsHintBar(w, h, centerY, alpha, tex);

            GUI.color = Color.white;
        }

        private void DrawCloneCards(float w, float h, float centerY, float alpha, Texture2D tex)
        {
            float cardWidth = 220;
            float cardHeight = 260;
            float cardSpacing = 35;
            float totalWidth = cardWidth * 3 + cardSpacing * 2;
            float startX = (w - totalWidth) / 2f;
            float cardY = centerY - cardHeight / 2f + 30;

            _hoverIndex = -1;

            var cloneTypes = cloneSystem.CloneTypes;
            int selectedIndex = cloneSystem.SelectedIndex;

            for (int i = 0; i < 3; i++)
            {
                var data = cloneTypes[i];
                bool isSelected = i == selectedIndex;
                bool isRecorded = cloneSystem.HasRecording((AAACloneSystem.CloneType)i);

                float cardX = startX + i * (cardWidth + cardSpacing);

                float targetScale = isSelected ? 1.08f : 0.92f;
                float animT = Mathf.Clamp01(_selectionAnimTime * 5f);
                float scale = Mathf.Lerp(1f, targetScale, MenuStyles.EaseOut(0f, 1f, animT));

                float scaledWidth = cardWidth * scale;
                float scaledHeight = cardHeight * scale;
                float offsetX = (cardWidth - scaledWidth) / 2f;
                float offsetY = (cardHeight - scaledHeight) / 2f;

                Rect cardRect = new Rect(cardX + offsetX, cardY + offsetY, scaledWidth, scaledHeight);

                bool isHovered = cardRect.Contains(Event.current.mousePosition);
                if (isHovered) _hoverIndex = i;

                if (isHovered && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    cloneSystem.SetSelectedIndex(i);
                    _selectionAnimTime = 0f;
                    Event.current.Use();
                }

                // glow behind selected/hovered card
                if (isSelected || isHovered)
                {
                    float glowAlpha = isSelected ? 0.35f : 0.15f;
                    float glowSize = isSelected ? 12f : 6f;
                    GUI.color = new Color(data.color.r, data.color.g, data.color.b, glowAlpha * alpha);
                    GUI.DrawTexture(new Rect(cardRect.x - glowSize, cardRect.y - glowSize,
                                             cardRect.width + glowSize * 2, cardRect.height + glowSize * 2), tex);
                }

                // card bg
                GUI.color = new Color(MenuStyles.DarkFilmBrown.r, MenuStyles.DarkFilmBrown.g, MenuStyles.DarkFilmBrown.b, 0.95f * alpha);
                GUI.DrawTexture(cardRect, tex);

                // top gradient tint
                GUI.color = new Color(data.color.r * 0.1f, data.color.g * 0.1f, data.color.b * 0.1f, 0.3f * alpha);
                GUI.DrawTexture(new Rect(cardRect.x, cardRect.y, cardRect.width, cardRect.height * 0.4f), tex);

                float barHeight = isSelected ? 5f : 3f;
                GUI.color = isSelected ? new Color(data.color.r, data.color.g, data.color.b, 0.9f * alpha)
                                        : new Color(data.color.r * 0.5f, data.color.g * 0.5f, data.color.b * 0.5f, 0.6f * alpha);
                GUI.DrawTexture(new Rect(cardRect.x, cardRect.y, cardRect.width, barHeight), tex);

                if (isSelected)
                {
                    GUI.color = new Color(data.color.r, data.color.g, data.color.b, 0.4f * alpha);
                    GUI.DrawTexture(new Rect(cardRect.x, cardRect.y, 2, cardRect.height), tex);
                }

                DrawCardContent(cardRect, data, isSelected, isRecorded, i, alpha, tex);
            }
        }

        private void DrawCardContent(Rect cardRect, AAACloneSystem.CloneTypeData data, bool isSelected, bool isRecorded, int index, float alpha, Texture2D tex)
        {
            float contentY = cardRect.y + 25;

            // keyboard shortcut badge in the corner
            Rect badgeRect = new Rect(cardRect.x + 10, cardRect.y + 10, 28, 22);
            GUI.color = new Color(MenuStyles.MetalGray.r, MenuStyles.MetalGray.g, MenuStyles.MetalGray.b, 0.6f * alpha);
            GUI.DrawTexture(badgeRect, tex);

            GUI.color = new Color(MenuStyles.WarmWhite.r, MenuStyles.WarmWhite.g, MenuStyles.WarmWhite.b, 0.8f * alpha);
            _badgeStyle.normal.textColor = GUI.color;
            GUI.Label(badgeRect, $"{index + 1}", _badgeStyle);

            // icon with pulse on selected
            float iconPulse = isSelected ? (Mathf.Sin(Time.unscaledTime * 3f) * 0.1f + 1f) : 1f;
            int iconSize = Mathf.RoundToInt((isSelected ? 56 : 44) * iconPulse);
            _iconStyle.fontSize = iconSize;
            GUI.color = isSelected ? new Color(data.color.r, data.color.g, data.color.b, alpha)
                                    : new Color(data.color.r, data.color.g, data.color.b, 0.5f * alpha);
            _iconStyle.normal.textColor = GUI.color;
            GUI.Label(new Rect(cardRect.x, contentY, cardRect.width, 60), Icons[index], _iconStyle);
            contentY += 65;

            // name
            _cardNameStyle.fontSize = isSelected ? 24 : 20;
            GUI.color = new Color(MenuStyles.WarmWhite.r, MenuStyles.WarmWhite.g, MenuStyles.WarmWhite.b, alpha);
            _cardNameStyle.normal.textColor = GUI.color;
            GUI.Label(new Rect(cardRect.x, contentY, cardRect.width, 30), L.Get(data.displayName), _cardNameStyle);
            contentY += 35;

            // description
            GUI.color = new Color(MenuStyles.DustyGray.r, MenuStyles.DustyGray.g, MenuStyles.DustyGray.b, 0.8f * alpha);
            _cardDescStyle.normal.textColor = GUI.color;
            GUI.Label(new Rect(cardRect.x, contentY, cardRect.width, 22), L.Get(data.description), _cardDescStyle);
            contentY += 35;

            // divider
            GUI.color = new Color(MenuStyles.MetalGray.r, MenuStyles.MetalGray.g, MenuStyles.MetalGray.b, 0.3f * alpha);
            GUI.DrawTexture(new Rect(cardRect.x + 20, contentY, cardRect.width - 40, 1), tex);
            contentY += 15;

            // recorded/empty status badge at the bottom of the card
            float statusBadgeW = 120;
            float statusBadgeH = 28;
            Rect statusRect = new Rect(cardRect.x + (cardRect.width - statusBadgeW) / 2, cardRect.y + cardRect.height - 45, statusBadgeW, statusBadgeH);

            Color statusBgColor = isRecorded ? new Color(MenuStyles.MechanicGreen.r * 0.3f, MenuStyles.MechanicGreen.g * 0.3f, MenuStyles.MechanicGreen.b * 0.3f)
                                              : new Color(MenuStyles.MetalGray.r * 0.3f, MenuStyles.MetalGray.g * 0.3f, MenuStyles.MetalGray.b * 0.3f);
            GUI.color = new Color(statusBgColor.r, statusBgColor.g, statusBgColor.b, 0.8f * alpha);
            GUI.DrawTexture(statusRect, tex);

            string statusIcon = isRecorded ? "●" : "○";
            string statusText = isRecorded ? L.Get("clone_recorded") : L.Get("clone_empty");
            Color statusColor = isRecorded ? MenuStyles.MechanicGreen : MenuStyles.DustyGray;

            GUI.color = new Color(statusColor.r, statusColor.g, statusColor.b, alpha);
            _statusStyle.normal.textColor = GUI.color;
            GUI.Label(statusRect, $"{statusIcon} {statusText}", _statusStyle);

            if (isSelected)
            {
                MenuStyles.DrawCornerAccent(cardRect.x + 5, cardRect.y + cardRect.height - 20, 15, alpha);
            }
        }

        private void DrawControlsHintBar(float w, float h, float centerY, float alpha, Texture2D tex)
        {
            float cardHeight = 260;
            float cardY = centerY - cardHeight / 2f + 30;

            float hintY = cardY + cardHeight + 50;
            float hintW = 700;
            float hintH = 45;
            float hintX = (w - hintW) / 2f;

            GUI.color = new Color(MenuStyles.DeepBlack.r, MenuStyles.DeepBlack.g, MenuStyles.DeepBlack.b, 0.85f * alpha);
            GUI.DrawTexture(new Rect(hintX, hintY, hintW, hintH), tex);

            GUI.color = new Color(MenuStyles.TapeOrange.r, MenuStyles.TapeOrange.g, MenuStyles.TapeOrange.b, 0.5f * alpha);
            GUI.DrawTexture(new Rect(hintX, hintY, hintW, 2), tex);

            float hintTextY = hintY + (hintH - 20) / 2;
            float spacing = hintW / 5f;

            DrawHintItem(hintX, hintTextY, spacing, "[ A  D ]", L.Get("hint_navigate"), MenuStyles.CrtCyan, alpha);
            DrawHintItem(hintX + spacing, hintTextY, spacing, "[ 1  2  3 ]", L.Get("hint_quick_select"), MenuStyles.CrtCyan, alpha);
            DrawHintItem(hintX + spacing * 2, hintTextY, spacing, "[ ENTER ]", L.Get("hint_confirm"), MenuStyles.MechanicGreen, alpha);
            DrawHintItem(hintX + spacing * 3, hintTextY, spacing, "[ CLICK ]", L.Get("hint_select"), MenuStyles.CrtCyan, alpha);
            DrawHintItem(hintX + spacing * 4, hintTextY, spacing, "[ TAB ]", L.Get("hint_close"), MenuStyles.TapeOrange, alpha);
        }

        private void DrawHintItem(float x, float y, float width, string key, string action, Color keyColor, float alpha)
        {
            GUI.color = new Color(keyColor.r, keyColor.g, keyColor.b, 0.9f * alpha);
            _hintBarStyle.normal.textColor = GUI.color;
            GUI.Label(new Rect(x, y, width, 20), key, _hintBarStyle);

            GUI.color = new Color(MenuStyles.DustyGray.r, MenuStyles.DustyGray.g, MenuStyles.DustyGray.b, 0.7f * alpha);
            _hintBarStyle.normal.textColor = GUI.color;
            GUI.Label(new Rect(x, y + 15, width, 16), action, _hintBarStyle);
        }

        private void DrawStatusPanel()
        {
            float width = 320;
            float height = 130;
            float x = 20;
            float y = Screen.height - height - 20;

            GUI.color = new Color(0.05f, 0.05f, 0.1f, 0.9f);
            GUI.Box(new Rect(x, y, width, height), "", _boxStyle);
            GUI.color = Color.white;

            float padding = 15;
            float innerWidth = width - padding * 2;
            float yPos = y + 12;

            var selectedData = cloneSystem.CloneTypes[cloneSystem.SelectedIndex];
            GUI.color = selectedData.color;
            GUI.Label(new Rect(x + padding, yPos, innerWidth, 28), $"◆ {L.Get(selectedData.displayName)}", _titleStyle);
            GUI.color = Color.white;
            yPos += 32;

            string phaseText = cloneSystem.CurrentPhase switch
            {
                AAACloneSystem.Phase.Idle => L.Get("phase_ready"),
                AAACloneSystem.Phase.Recording => "● " + L.Get("phase_recording"),
                AAACloneSystem.Phase.Rewinding => "◀◀ " + L.Get("phase_rewinding"),
                AAACloneSystem.Phase.Review => L.Get("phase_review"),
                AAACloneSystem.Phase.Playback => cloneSystem.IsClonesPaused ? "▐▐ " + L.Get("phase_paused") : "▶ " + L.Get("phase_playing"),
                _ => cloneSystem.CurrentPhase.ToString()
            };

            Color phaseColor = cloneSystem.CurrentPhase switch
            {
                AAACloneSystem.Phase.Recording => Color.red,
                AAACloneSystem.Phase.Playback => cloneSystem.IsClonesPaused ? Color.yellow : Color.green,
                _ => Color.white
            };

            GUI.color = phaseColor;
            GUI.Label(new Rect(x + padding, yPos, innerWidth, 22), $"{L.Get("clone_status")}: {phaseText}", _labelStyle);
            GUI.color = Color.white;
            yPos += 24;

            string timeInfo = cloneSystem.CurrentPhase == AAACloneSystem.Phase.Playback ? $"{L.Get("clone_time")}: {Time.timeScale:F1}x" : "";
            GUI.Label(new Rect(x + padding, yPos, innerWidth, 22), $"{L.Get("clone_clones")}: {cloneSystem.RecordingCount}/3   {timeInfo}", _labelStyle);
            yPos += 28;

            GUI.color = Color.yellow;
            string hint = cloneSystem.CurrentPhase switch
            {
                AAACloneSystem.Phase.Idle => L.Get("status_hint_idle"),
                AAACloneSystem.Phase.Recording => L.Get("status_hint_recording"),
                AAACloneSystem.Phase.Review => L.Get("status_hint_review"),
                AAACloneSystem.Phase.Playback => L.Get("status_hint_playback"),
                AAACloneSystem.Phase.Rewinding => L.Get("status_hint_rewinding"),
                _ => ""
            };
            GUI.Label(new Rect(x + padding, yPos, innerWidth, 22), hint, _hintStyle);
            GUI.color = Color.white;
        }

        public void ResetSelectionAnimation()
        {
            _selectionAnimTime = 0f;
        }

        public void OnSelectionClosed()
        {
            _selectionAlpha = 0f;
        }

        private void OnDestroy()
        {
            // static texture cleanup - only if this was the last instance
            if (_bgTexture != null)
            {
                Destroy(_bgTexture);
                _bgTexture = null;
            }
            _stylesInitialized = false;
        }
    }
}
