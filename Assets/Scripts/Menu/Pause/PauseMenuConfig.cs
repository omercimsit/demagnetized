using UnityEngine;

namespace Menu.Pause
{
    // pause menu config - sizes, colors, animation speeds
    public static class PauseMenuConfig
    {
        // Font sizes
        public const float TitleFontSize = 72f;
        public const float HeaderFontSize = 28f;
        public const float ButtonFontSizeNormal = 26f;
        public const float ButtonFontSizeSelected = 32f;
        public const float TabFontSize = 13f;
        public const float LabelFontSize = 14f;
        public const float SubLabelFontSize = 10f;
        public const float SettingLabelFontSize = 11f;
        public const float ValueFontSize = 18f;

        // Button count
        public const int MaxButtonCount = 15;

        // Layout
        public const float PanelWidth = 500f;
        public const float PanelStartX = 120f;
        public const float ButtonHeight = 55f;
        public const float ButtonSpacing = 6f;
        public const float SliderHeight = 2f;
        public const float SliderHandleSize = 12f;
        public const float ToggleSwitchWidth = 40f;
        public const float ToggleSwitchHeight = 20f;
        public const float SettingRowSpacing = 55f;
        public const float CompactRowSpacing = 38f;

        // Animation
        public const float MenuFadeSpeed = 4f;
        public const float SettingsFadeSpeed = 6f;
        public const float TabTransitionSpeed = 8f;
        public const float ButtonScaleSpeed = 15f;
        public const float ButtonAnimSpeed = 20f;
        public const float TooltipFadeInSpeed = 6f;
        public const float TooltipFadeOutSpeed = 8f;
        public const float TooltipDelay = 0.3f;
        public const float FeedbackFadeThreshold = 0.5f;

        // VHS effects
        public const float ScanlineScrollSpeed = 50f;
        public const float GlitchProbability = 0.02f;
        public const float GlitchDuration = 0.05f;
        public const float TrackingScrollSpeed = 80f;
        public const float PausedFlickerSpeed = 3f;
        public const float ScanlineAlpha = 0.25f;
        public const float BlinkPeriod = 2f;

        // Colors
        // Accent colors delegate to MenuStyles canonical palette
        public static Color AccentColor   => MenuStyles.Amber;
        public static Color DangerColor   => MenuStyles.Danger;
        public static Color SuccessColor  => MenuStyles.MechanicGreen;
        public static Color TextPrimary   => MenuStyles.TextMain;
        public static Color TextSecondary => MenuStyles.TextMid;
        public static Color TextMuted     => MenuStyles.TextDim;
        // Pause-menu-specific colors (no MenuStyles equivalent)
        public static readonly Color PanelBackground = new Color(0.02f, 0.02f, 0.03f, 0.88f);
        public static readonly Color OverlayColor = new Color(0f, 0f, 0f, 0.12f);
        public static readonly Color HighlightBg = new Color(1f, 1f, 1f, 0.06f);
        public static readonly Color SeparatorColor = new Color(1f, 1f, 1f, 0.1f);

        // Shared texture (delegated to MenuStyles)
        public static Texture2D SharedTexture => MenuStyles.SolidTexture;
    }
}
