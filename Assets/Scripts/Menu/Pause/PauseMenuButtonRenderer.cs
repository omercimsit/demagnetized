using UnityEngine;
using System;

namespace Menu.Pause
{
    /// <summary>
    /// V20 RENDERER: LUXE BUTTONS (Clean, Orange Accent)
    /// Rich details: Index numbers, accent lines, hover effects.
    /// Uses shared texture from MenuStyles.
    /// </summary>
    public static class PauseMenuButtonRenderer
    {
        private static GUIStyle _buttonStyle;
        
        // Use shared texture from MenuStyles to avoid duplication
        private static Texture2D SolidTex => MenuStyles.SolidTexture;

        public static bool DrawButton(
            float x, ref float y, float width,
            string text, int index, int selectedIndex,
            float animProgress,
            float alpha, float uiScale,
            Font font, bool isDanger,
            Action onClick, Action onHover)
        {
            float height = PauseMenuConfig.ButtonHeight * uiScale;
            float spacing = PauseMenuConfig.ButtonSpacing * uiScale;
            Rect r = new Rect(x, y, width, height);
            bool isSelected = (index == selectedIndex);

            float eased = animProgress * animProgress * (3f - 2f * animProgress);

            // Interaction
            bool clicked = false;
            bool isHovered = r.Contains(Event.current.mousePosition);
            // Remove restricted event type check to allow hover even if mouse isn't moving but UI moves under it
            if(isHovered && !isSelected) onHover?.Invoke();
            if(isHovered && Event.current.type==EventType.MouseDown && Event.current.button==0) { onClick?.Invoke(); clicked=true; Event.current.Use(); }

            // === CLEAN LUXE VISUALS ===
            
            Color accent = isDanger ? MenuStyles.Danger : Color.white;

            // Background highlight for selected
            if (isSelected)
            {
                GUI.color = new Color(accent.r, accent.g, accent.b, 0.06f * alpha);
                GUI.DrawTexture(r, SolidTex);
                
                // Top line
                GUI.color = new Color(accent.r, accent.g, accent.b, 0.15f * alpha);
                GUI.DrawTexture(new Rect(r.x, r.y, width * eased, 1), SolidTex);
            }

            // Left indicator bar (animated)
            if (isSelected)
            {
                float barH = height * 0.5f * eased;
                float barY = r.y + (height - barH) / 2f;
                
                // Glow
                GUI.color = new Color(accent.r, accent.g, accent.b, 0.2f * alpha);
                GUI.DrawTexture(new Rect(r.x - 22 * uiScale, barY - 2, 6 * uiScale, barH + 4), SolidTex);
                
                // Core
                GUI.color = new Color(accent.r, accent.g, accent.b, alpha);
                GUI.DrawTexture(new Rect(r.x - 18 * uiScale, barY, 3 * uiScale, barH), SolidTex);
            }

            // Main text
            if (_buttonStyle == null) _buttonStyle = new GUIStyle(GUI.skin.label);
            _buttonStyle.font = font;
            _buttonStyle.alignment = TextAnchor.MiddleLeft;
            _buttonStyle.fontStyle = FontStyle.Bold;

            float baseFontSize = PauseMenuConfig.ButtonFontSizeNormal * uiScale;
            float targetFontSize = PauseMenuConfig.ButtonFontSizeSelected * uiScale;
            _buttonStyle.fontSize = Mathf.RoundToInt(Mathf.Lerp(baseFontSize, targetFontSize, eased));

            if (isSelected)
                _buttonStyle.normal.textColor = new Color(accent.r, accent.g, accent.b, alpha);
            else
                _buttonStyle.normal.textColor = MenuStyles.WithAlpha(MenuStyles.DustyGray, alpha);

            float xOffset = isSelected ? (12 * eased) : 0;
            
            GUI.Label(new Rect(r.x + xOffset, r.y, width, height), text, _buttonStyle);

            // Right-side decorator for selected
            if (isSelected && eased > 0.5f)
            {
                GUI.color = new Color(accent.r, accent.g, accent.b, 0.3f * alpha * (eased - 0.5f) * 2f);
                GUI.DrawTexture(new Rect(r.x + width - 30 * uiScale, r.y + height/2 - 1, 20 * uiScale, 2), SolidTex);
            }

            y += height + spacing;
            return clicked;
        }

        public static bool DrawSimpleButton(Rect rect, string text, float alpha, float uiScale, Font font)
        {
            bool hovered = rect.Contains(Event.current.mousePosition);

            var style = MenuStyles.S(MenuStyles.StyleBold, (int)(18 * uiScale),
                hovered ? Color.white : MenuStyles.WithAlpha(MenuStyles.DustyGray, alpha), TextAnchor.MiddleCenter);
            style.font = font;
            style.fontStyle = FontStyle.Bold;

            GUI.color = new Color(1, 1, 1, (hovered ? 0.2f : 0.08f) * alpha);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1), SolidTex);
            GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height - 1, rect.width, 1), SolidTex);

            if (hovered)
            {
                GUI.color = new Color(1, 1, 1, 0.06f * alpha);
                GUI.DrawTexture(rect, SolidTex);
            }

            GUI.Label(rect, text, style);

            if (hovered && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Event.current.Use();
                return true;
            }
            return false;
        }
        
        public static void Cleanup() 
        { 
            // Texture is managed by MenuStyles, don't destroy it here
            _buttonStyle = null;
        }
    }
}
