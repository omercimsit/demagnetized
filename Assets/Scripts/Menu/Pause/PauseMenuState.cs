using UnityEngine;

namespace Menu.Pause
{
    /// <summary>
    /// Pause Menu State - Centralized state management for the pause system.
    /// Extracted from PauseMenuManager for modularity.
    /// Uses ScriptableObject-like pattern for cross-component access.
    /// </summary>
    public class PauseMenuState
    {
        #region Singleton
        private static PauseMenuState _instance;
        public static PauseMenuState Instance => _instance ??= new PauseMenuState();
        #endregion

        #region Core State
        public bool IsPaused { get; private set; }
        public bool IsSettingsOpen { get; set; }
        public int SettingsTab { get; set; }
        public float SavedTimeScale { get; set; } = 1f;
        public float LastPauseToggleTime { get; set; }
        #endregion

        #region Animation State
        public float MenuAlpha { get; set; }
        public float SettingsAlpha { get; set; }
        public float TabPosition { get; set; }
        public float GlitchTimer { get; set; }
        public float ScanlineOffset { get; set; }
        public float NoiseTime { get; set; }
        public float[] ButtonScales { get; } = new float[PauseMenuConfig.MaxButtonCount];
        public float[] ButtonAnimProgress { get; set; } = new float[PauseMenuConfig.MaxButtonCount];
        #endregion

        #region Navigation State
        public int SelectedIndex { get; set; }
        public int LastHoveredButton { get; set; } = -1;
        public int MaxMainButtons { get; set; } = 4;
        #endregion

        /// <summary>
        /// Resets the state to default values. Call this on game start.
        /// </summary>
        public void Reset()
        {
            IsPaused = false;
            IsSettingsOpen = false;
            SettingsTab = 0;
            LastPauseToggleTime = 0f;

            MenuAlpha = 0f;
            SettingsAlpha = 0f;
            TabPosition = 0f;
            GlitchTimer = 0f;
            ScanlineOffset = 0f;

            SelectedIndex = 0;
            LastHoveredButton = -1;

            for(int i=0; i<ButtonAnimProgress.Length; i++) ButtonAnimProgress[i] = 0f;
            for(int i=0; i<ButtonScales.Length; i++) ButtonScales[i] = 1f;
        }

        #region Tooltip & Feedback
        public string CurrentTooltip { get; set; } = "";
        public float TooltipAlpha { get; set; }
        public float TooltipTimer { get; set; }
        public string FeedbackMessage { get; set; } = "";
        public float FeedbackAlpha { get; set; }
        public float FeedbackTimer { get; set; }
        #endregion

        #region Methods

        public void SetPaused(bool paused)
        {
            IsPaused = paused;
        }

        public void UpdateAnimations(float deltaTime)
        {
            // Menu fade
            MenuAlpha = Mathf.MoveTowards(MenuAlpha, IsPaused ? 1f : 0f, deltaTime * PauseMenuConfig.MenuFadeSpeed);
            SettingsAlpha = Mathf.MoveTowards(SettingsAlpha, IsSettingsOpen ? 1f : 0f, deltaTime * PauseMenuConfig.SettingsFadeSpeed);
            TabPosition = Mathf.Lerp(TabPosition, SettingsTab, deltaTime * PauseMenuConfig.TabTransitionSpeed);

            // VHS effects
            ScanlineOffset += deltaTime * PauseMenuConfig.ScanlineScrollSpeed;
            NoiseTime += deltaTime;
            if (Random.value < PauseMenuConfig.GlitchProbability) GlitchTimer = PauseMenuConfig.GlitchDuration;
            GlitchTimer = Mathf.Max(0, GlitchTimer - deltaTime);

            // Button scales
            for (int i = 0; i < ButtonScales.Length; i++)
            {
                float target = (i == SelectedIndex && !IsSettingsOpen) ? 1.05f : 1f;
                ButtonScales[i] = Mathf.Lerp(ButtonScales[i], target, deltaTime * PauseMenuConfig.ButtonScaleSpeed);

                float animTarget = (i == SelectedIndex) ? 1f : 0f;
                ButtonAnimProgress[i] = Mathf.Lerp(ButtonAnimProgress[i], animTarget, deltaTime * PauseMenuConfig.ButtonAnimSpeed);
            }

            // Tooltip
            if (!string.IsNullOrEmpty(CurrentTooltip))
            {
                TooltipTimer += deltaTime;
                if (TooltipTimer > PauseMenuConfig.TooltipDelay)
                    TooltipAlpha = Mathf.MoveTowards(TooltipAlpha, 1f, deltaTime * PauseMenuConfig.TooltipFadeInSpeed);
            }
            else
            {
                TooltipAlpha = Mathf.MoveTowards(TooltipAlpha, 0f, deltaTime * PauseMenuConfig.TooltipFadeOutSpeed);
                TooltipTimer = 0f;
            }

            // Feedback
            if (FeedbackTimer > 0f)
            {
                FeedbackTimer -= deltaTime;
                FeedbackAlpha = Mathf.MoveTowards(FeedbackAlpha, FeedbackTimer > PauseMenuConfig.FeedbackFadeThreshold ? 1f : 0f, deltaTime * PauseMenuConfig.MenuFadeSpeed);
            }
            else
            {
                FeedbackAlpha = Mathf.MoveTowards(FeedbackAlpha, 0f, deltaTime * PauseMenuConfig.MenuFadeSpeed);
            }
        }

        public void ShowFeedback(string message, float duration = 2f)
        {
            FeedbackMessage = message;
            FeedbackTimer = duration;
            FeedbackAlpha = 0f;
        }

        public void SetTooltip(string tooltip)
        {
            if (CurrentTooltip != tooltip)
            {
                CurrentTooltip = tooltip;
                TooltipTimer = 0f;
            }
        }

        #endregion
    }
}
