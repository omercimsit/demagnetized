using UnityEngine;

namespace Menu.Pause
{
    // draws the dark panel background and decorations
    public static class PauseMenuPanelRenderer
    {
        // Use shared texture from MenuStyles to avoid duplication
        private static Texture2D SolidTex => MenuStyles.SolidTexture;

        public static Rect GetPanelRect(float w, float h, float uiScale)
        {
            float width = PauseMenuConfig.PanelWidth * uiScale;
            float startX = PauseMenuConfig.PanelStartX * uiScale;
            return new Rect(startX, 0, width, h);
        }

        public static void DrawDarkOverlay(float w, float h, float alpha)
        {
            float time = Time.unscaledTime;

            // 1. Draw Base Field (Deep Dark Blue)
            GUI.color = new Color(0.01f, 0.012f, 0.02f, alpha * 0.95f);
            GUI.DrawTexture(new Rect(0, 0, w, h), SolidTex);

            // 2. Moving Fog / Smoke Layers
            Texture2D noise = PauseMenuVHSEffects.NoiseTex;
            if (noise != null)
            {
                
                // Layer 1: Slow drift right-down
                float offX1 = (time * 0.02f) % 1.0f;
                float offY1 = (time * 0.01f) % 1.0f;
                
                GUI.color = new Color(0.4f, 0.45f, 0.5f, 0.04f * alpha); // Very faint blue-grey smoke
                GUI.DrawTextureWithTexCoords(new Rect(0, 0, w, h), noise, new Rect(offX1, offY1, 1, 1));
                
                // Layer 2: Slower drift left-up (Create interference pattern)
                float offX2 = (time * -0.015f) % 1.0f;
                float offY2 = (time * 0.005f) % 1.0f;
                
                GUI.color = new Color(0.3f, 0.3f, 0.35f, 0.03f * alpha);
                GUI.DrawTextureWithTexCoords(new Rect(0, 0, w, h), noise, new Rect(offX2, offY2, 2, 2)); // 2x tiling
            }

            // 3. The "Solid" Left Panel Gradient (for text readability)
            // Draw cached gradient ON TOP of the fog to keep text clear
            GUI.color = new Color(0.01f, 0.012f, 0.02f, alpha);
            GUI.DrawTexture(new Rect(0, 0, w * 0.85f, h), MenuStyles.GradientTexture);

            // 4. Subtle Vignette Pulse
            float pulse = 1f + Mathf.Sin(time * 0.5f) * 0.05f; // Breathing effect
            GUI.color = new Color(0, 0, 0, 0.3f * alpha * pulse);
            GUI.DrawTexture(new Rect(0, 0, w, h), SolidTex);
            
            // 5. Cinematic Borders
            GUI.color = new Color(0,0,0, 0.8f * alpha);
            GUI.DrawTexture(new Rect(0,0,w,2), SolidTex);
            GUI.DrawTexture(new Rect(0,h-2,w,2), SolidTex);
        }

        public static void DrawPanel(Rect r, float alpha, float time, float scanlineOffset)
        {
            float uiScale = r.width / 500f;
            
            // Main vertical line
            GUI.color = new Color(1, 1, 1, 0.25f * alpha);
            GUI.DrawTexture(new Rect(r.x - 25, 0, 1, Screen.height), SolidTex);
            
            // Corner brackets (Top)
            float bracketY = Screen.height * 0.1f;
            DrawCornerBracket(r.x - 40, bracketY, alpha, true);
            
            // Corner brackets (Bottom)
            float bracketY2 = Screen.height * 0.85f;
            DrawCornerBracket(r.x - 40, bracketY2, alpha, false);
            
            // Floating dots (Animated)
            float pulse1 = (Mathf.Sin(time * 2.5f) + 1f) / 2f;
            float pulse2 = (Mathf.Sin(time * 3.2f + 1f) + 1f) / 2f;
            
            GUI.color = new Color(1, 1, 1, (0.3f + pulse1 * 0.4f) * alpha);
            GUI.DrawTexture(new Rect(r.x - 30, Screen.height * 0.25f + pulse1 * 5, 4, 4), SolidTex);
            
            GUI.color = new Color(1, 1, 1, (0.2f + pulse2 * 0.3f) * alpha);
            GUI.DrawTexture(new Rect(r.x - 35, Screen.height * 0.5f + pulse2 * 8, 3, 3), SolidTex);
        }
        
        private static void DrawCornerBracket(float x, float y, float alpha, bool isTop)
        {
            float dir = isTop ? 1 : -1;
            GUI.color = new Color(1, 1, 1, 0.4f * alpha);
            
            GUI.DrawTexture(new Rect(x, y, 25, 1), SolidTex);
            GUI.DrawTexture(new Rect(x, y, 1, 25 * dir), SolidTex);
            
            GUI.color = new Color(1, 1, 1, 0.6f * alpha);
            GUI.DrawTexture(new Rect(x + 28, y - 1, 3, 3), SolidTex);
        }

        public static void DrawDecals(Rect r, float alpha, float uiScale, Font font)
        {
            if(alpha < 0.5f) return;
            
            var style = MenuStyles.S(MenuStyles.StyleBody, (int)(9 * uiScale),
                new Color(1, 1, 1, 0.2f * alpha), TextAnchor.LowerLeft);
            style.font = font;

            GUI.Label(new Rect(r.x, Screen.height - 35, 300, 20), L.Get("clone_system_status"), style);

            // Status indicator
            float blink = (Time.unscaledTime % PauseMenuConfig.BlinkPeriod) > 1f ? 1f : 0.3f;
            GUI.color = new Color(PauseMenuConfig.SuccessColor.r, PauseMenuConfig.SuccessColor.g, PauseMenuConfig.SuccessColor.b, blink * alpha);
            GUI.DrawTexture(new Rect(r.x + 200, Screen.height - 28, 6, 6), SolidTex);
        }
        // title indicator with localization
        public static void DrawPausedIndicator(float w, float alpha, float uiScale, Font font, 
            string pausedText = "DEMAGNETIZED", string subtitleText = "SYSTEM SUSPENDED")
        {
            float xPos = 120f * uiScale;
            float yPos = Screen.height * 0.13f;
            
            // Title style - pooled, no allocation
            var style = MenuStyles.S(MenuStyles.StyleTitle, Mathf.RoundToInt(56 * uiScale),
                Color.white, TextAnchor.MiddleLeft);
            style.font = font;
            style.fontStyle = FontStyle.Bold;
            style.clipping = TextClipping.Overflow;
            
            // Calculate actual width needed
            float textWidth = 700 * uiScale; // Wide enough for any title
            
            // Shadow (subtle depth)
            style.normal.textColor = new Color(0, 0, 0, 0.5f * alpha);
            GUI.Label(new Rect(xPos + 3, yPos + 3, textWidth, 80), pausedText, style);
            
            // Main text
            style.normal.textColor = new Color(1, 1, 1, alpha);
            GUI.Label(new Rect(xPos, yPos, textWidth, 80), pausedText, style);
            
            // Amber accent underline (canonical palette)
            var accent = MenuStyles.Amber;
            GUI.color = new Color(accent.r, accent.g, accent.b, 0.8f * alpha);
            float lineWidth = pausedText.Length * 28f * uiScale; // Adjusted for new font size
            GUI.DrawTexture(new Rect(xPos, yPos + 72, Mathf.Min(lineWidth, 380), 3), SolidTex);
            
            // Decorative dot
            GUI.color = new Color(accent.r, accent.g, accent.b, 0.9f * alpha);
            GUI.DrawTexture(new Rect(xPos + Mathf.Min(lineWidth, 380) + 8, yPos + 69, 6, 9), SolidTex);
            
            // Subtitle — pooled style, uses canonical palette
            var subStyle = MenuStyles.S(MenuStyles.StyleBody, Mathf.RoundToInt(13 * uiScale),
                MenuStyles.WithAlpha(MenuStyles.DustyGray, alpha * 0.8f), TextAnchor.MiddleLeft);
            subStyle.font = font;
            GUI.Label(new Rect(xPos + 2, yPos + 85, 400, 30), subtitleText, subStyle);
            
            GUI.color = Color.white;
        }
        
        public static void Cleanup() { /* Gradient texture managed by MenuStyles.Cleanup() */ }
    }
}
