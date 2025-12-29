using UnityEngine;

namespace Menu.Pause
{
    /// <summary>
    /// VHS Effects Renderer (Refined)
    /// Subtle analog imperfections to add texture without reducing readability.
    /// </summary>
    public static class PauseMenuVHSEffects
    {
        /// <summary>
        /// Noise texture delegated to MenuStyles (single canonical 128x128 texture).
        /// </summary>
        public static Texture2D NoiseTex => MenuStyles.NoiseTexture;
        
        private static Texture2D _scanlineTex;
        private static Texture2D ScanlineTex
        {
            get
            {
                if (_scanlineTex == null)
                {
                    _scanlineTex = new Texture2D(1, 2);
                    _scanlineTex.SetPixel(0, 0, new Color(0, 0, 0, 0f));
                    _scanlineTex.SetPixel(0, 1, new Color(0, 0, 0, 0.4f)); // Dark line
                    _scanlineTex.filterMode = FilterMode.Point;
                    _scanlineTex.Apply();
                }
                return _scanlineTex;
            }
        }

        public static void DrawTrackingLines(float screenWidth, float screenHeight, float offset, float alpha)
        {
            // Horizontal noise bar moving down
            float barHeight = 8;
            float y = offset % screenHeight;
            
            // Draw a few lines
            var tex = MenuStyles.SolidTexture;
            GUI.color = new Color(1f, 1f, 1f, 0.05f * alpha); // Very subtle
            GUI.DrawTexture(new Rect(0, y, screenWidth, barHeight), tex);
            GUI.DrawTexture(new Rect(0, y - 50, screenWidth, 2), tex);
            
            // Noise overlay on the bar
             GUI.color = new Color(1f, 1f, 1f, 0.08f * alpha);
             GUI.DrawTextureWithTexCoords(new Rect(0, y, screenWidth, barHeight), NoiseTex, new Rect(0, 0, screenWidth/128f, 1));
        }

        public static void DrawScanlines(Rect area, float alpha)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.1f * alpha);
            Rect texCoords = new Rect(0, 0, 1, area.height / 2f); // 1 line every 2 pixels
            GUI.DrawTextureWithTexCoords(area, ScanlineTex, texCoords);
        }

        public static void DrawGlitchEffect(float w, float intensity, float alpha)
        {
            if (intensity <= 0.01f) return;
            
            // Random blocks of color shift
            float h = Screen.height;
            float blockH = UnityEngine.Random.Range(10, 100);
            float y = UnityEngine.Random.Range(0, h);
            
            GUI.color = new Color(MenuStyles.CrtCyan.r, MenuStyles.CrtCyan.g, MenuStyles.CrtCyan.b, 0.2f * alpha * intensity);
            GUI.DrawTexture(new Rect(0, y, w, blockH), MenuStyles.SolidTexture);
            
            GUI.color = new Color(MenuStyles.RustRed.r, MenuStyles.RustRed.g, MenuStyles.RustRed.b, 0.2f * alpha * intensity);
            GUI.DrawTexture(new Rect(5, y + 2, w, blockH), MenuStyles.SolidTexture);
        }

        public static void Cleanup()
        {
            // Noise texture is managed by MenuStyles — don't destroy it here
            if (_scanlineTex != null) { UnityEngine.Object.Destroy(_scanlineTex); _scanlineTex = null; }
        }

    }
}
