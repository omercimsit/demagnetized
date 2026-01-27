using UnityEngine;

// Style pooling alias
using S = MenuStyles;

/// <summary>
/// VHS Visual Effects System.
/// Provides retro VCR aesthetics: scanlines, tracking lines, pause symbol, noise.
/// Designed for IMGUI but can be adapted to Canvas with RawImage.
/// </summary>
public class VHSEffects : MonoBehaviour
{
    public static VHSEffects Instance { get; private set; }
    
    #region Settings
    
    [Header("Effect Settings")]
    [SerializeField] private bool enableScanlines = true;
    [SerializeField] private bool enableTrackingLines = true;
    [SerializeField] private bool enablePauseSymbol = true;
    [SerializeField] private bool enableNoise = true;
    
    [Header("Animation")]
    [SerializeField] private float scanlineSpeed = 50f;
    [SerializeField] private float trackingSpeed = 80f;
    [SerializeField] private float flickerRate = 3f;
    
    [Header("Colors")]
    [SerializeField] private Color warmWhite = new Color(0.96f, 0.94f, 0.88f, 1f);
    [SerializeField] private Color tapeOrange = new Color(0.85f, 0.45f, 0.15f, 1f);
    [SerializeField] private Color crtCyan = new Color(0.3f, 0.8f, 0.9f, 1f);
    [SerializeField] private Color vcrTracking = new Color(0.15f, 0.15f, 0.2f, 0.5f);
    
    #endregion
    
    #region State
    
    private float _scanlineOffset = 0f;
    private float _trackingOffset = 0f;
    private float _pauseSymbolAlpha = 0f;
    private bool _showPauseSymbol = true;
    private bool _isActive = false;
    private float _targetAlpha = 0f;
    private float _currentAlpha = 0f;
    
    // Textures from MenuStyles (shared, not owned)
    private Texture2D _solidTex => MenuStyles.SolidTexture;
    private Texture2D _noiseTex => MenuStyles.NoiseTexture;
    
    // UI Scale
    private float _uiScale = 1f;
    
    #endregion
    
    #region Lifecycle
    
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
    }
    
    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        
        // Animate alpha
        _currentAlpha = Mathf.MoveTowards(_currentAlpha, _targetAlpha, dt * 4f);
        _pauseSymbolAlpha = _currentAlpha;
        
        // Scanline animation
        _scanlineOffset += dt * scanlineSpeed;
        if (_scanlineOffset > 100f) _scanlineOffset = 0f;
        
        // Tracking lines animation
        _trackingOffset += dt * trackingSpeed;
        if (_trackingOffset > 200f) _trackingOffset = 0f;
        
        // Pause symbol flicker
        if (_isActive)
        {
            _showPauseSymbol = Mathf.Sin(Time.unscaledTime * flickerRate) > -0.3f;
        }
    }
    
    private void OnDestroy()
    {
        // Textures are shared via MenuStyles — no cleanup needed here
        if (Instance == this) Instance = null;
    }
    
    #endregion
    
    #region Public API
    
    /// <summary>
    /// Activate VHS effects (call when pausing)
    /// </summary>
    public void Activate()
    {
        _isActive = true;
        _targetAlpha = 1f;
    }
    
    /// <summary>
    /// Deactivate VHS effects (call when resuming)
    /// </summary>
    public void Deactivate()
    {
        _isActive = false;
        _targetAlpha = 0f;
    }
    
    /// <summary>
    /// Set UI scale factor
    /// </summary>
    public void SetUIScale(float scale)
    {
        _uiScale = Mathf.Clamp(scale, 0.5f, 2f);
    }
    
    /// <summary>
    /// Returns true if effects should be drawn
    /// </summary>
    public bool ShouldDraw => _currentAlpha > 0.01f;
    
    /// <summary>
    /// Current effect alpha (0-1)
    /// </summary>
    public float CurrentAlpha => _currentAlpha;
    
    #endregion
    
    #region Drawing Methods
    
    /// <summary>
    /// Draw all VHS effects. Call from OnGUI.
    /// </summary>
    public void DrawAllEffects()
    {
        if (!ShouldDraw) return;
        
        float w = Screen.width;
        float h = Screen.height;
        
        if (enableTrackingLines) DrawTrackingLines(w, h);
        if (enablePauseSymbol) DrawPauseSymbol(w, h);
    }
    
    /// <summary>
    /// Draw scanlines on a specific panel. Call from OnGUI.
    /// </summary>
    public void DrawScanlines(Rect panelRect, float alpha = 1f)
    {
        if (!enableScanlines || _solidTex == null) return;
        
        GUI.color = new Color(0f, 0f, 0f, 0.05f * _currentAlpha * alpha);
        for (float yPos = panelRect.y + (_scanlineOffset % 3); yPos < panelRect.y + panelRect.height; yPos += 3)
        {
            GUI.DrawTexture(new Rect(panelRect.x, yPos, panelRect.width, 1), _solidTex);
        }
        GUI.color = Color.white;
    }
    
    /// <summary>
    /// Draw noise overlay on a specific panel. Call from OnGUI.
    /// </summary>
    public void DrawNoise(Rect panelRect, float alpha = 0.03f)
    {
        if (!enableNoise || _noiseTex == null) return;
        
        GUI.color = new Color(1f, 1f, 1f, alpha * _currentAlpha);
        GUI.DrawTexture(panelRect, _noiseTex);
        GUI.color = Color.white;
    }
    
    /// <summary>
    /// Draw VHS pause symbol (||) at specified position
    /// </summary>
    public void DrawPauseSymbol(float screenW, float screenH)
    {
        if (!_showPauseSymbol || _solidTex == null) return;
        
        float alpha = _pauseSymbolAlpha;
        float symbolX = screenW * 0.7f;
        float symbolY = screenH * 0.15f;
        float barWidth = 18 * _uiScale;
        float barHeight = 50 * _uiScale;
        float barGap = 12 * _uiScale;
        
        // Glow effect behind bars
        GUI.color = new Color(warmWhite.r, warmWhite.g, warmWhite.b, 0.1f * alpha);
        GUI.DrawTexture(new Rect(symbolX - 10, symbolY - 10, barWidth * 2 + barGap + 20, barHeight + 20), _solidTex);
        
        // Left bar
        GUI.color = new Color(warmWhite.r, warmWhite.g, warmWhite.b, 0.9f * alpha);
        GUI.DrawTexture(new Rect(symbolX, symbolY, barWidth, barHeight), _solidTex);
        
        // Right bar
        GUI.DrawTexture(new Rect(symbolX + barWidth + barGap, symbolY, barWidth, barHeight), _solidTex);
        
        // "PAUSE" text below (pooled style, localized)
        var pauseStyle = S.S(GUI.skin.label, Mathf.RoundToInt(14 * _uiScale),
            new Color(warmWhite.r, warmWhite.g, warmWhite.b, 0.7f * alpha), TextAnchor.MiddleCenter);
        pauseStyle.fontStyle = FontStyle.Bold;
        GUI.color = Color.white;
        GUI.Label(new Rect(symbolX - 20, symbolY + barHeight + 8, barWidth * 2 + barGap + 40, 25), L.Get("vhs_pause"), pauseStyle);
    }
    
    /// <summary>
    /// Draw VHS tracking lines effect (horizontal noise lines)
    /// </summary>
    public void DrawTrackingLines(float screenW, float screenH)
    {
        if (_solidTex == null) return;
        
        float alpha = _pauseSymbolAlpha * 0.15f;
        
        // Draw several tracking lines at different positions
        for (int i = 0; i < 3; i++)
        {
            float lineY = ((_trackingOffset + i * 70) % (screenH + 50)) - 25;
            float lineHeight = 2 + Random.Range(0f, 3f);
            float lineAlpha = alpha * (0.5f + Random.Range(0f, 0.5f));
            
            // Main tracking line
            GUI.color = new Color(vcrTracking.r, vcrTracking.g, vcrTracking.b, lineAlpha);
            GUI.DrawTexture(new Rect(0, lineY, screenW, lineHeight), _solidTex);
            
            // Slight color distortion above/below
            GUI.color = new Color(crtCyan.r, crtCyan.g, crtCyan.b, lineAlpha * 0.3f);
            GUI.DrawTexture(new Rect(0, lineY - 2, screenW, 1), _solidTex);
            
            GUI.color = new Color(tapeOrange.r, tapeOrange.g, tapeOrange.b, lineAlpha * 0.2f);
            GUI.DrawTexture(new Rect(0, lineY + lineHeight + 1, screenW, 1), _solidTex);
        }
        
        // Occasional horizontal jitter
        if (Random.value < 0.1f)
        {
            float jitterY = Random.Range(0f, screenH);
            float jitterOffset = Random.Range(-5f, 5f);
            GUI.color = new Color(1f, 1f, 1f, 0.05f * alpha);
            GUI.DrawTexture(new Rect(jitterOffset, jitterY, screenW, 1), _solidTex);
        }
        
        GUI.color = Color.white;
    }
    
    /// <summary>
    /// Draw dark vignette effect around screen edges (delegates to MenuStyles)
    /// </summary>
    public void DrawVignette(float screenW, float screenH, float intensity = 1f)
    {
        MenuStyles.DrawVignette(screenW, screenH, _currentAlpha * intensity, 15, 0.22f);
    }
    
    /// <summary>
    /// Get the noise texture for external use
    /// </summary>
    public Texture2D GetNoiseTexture() => _noiseTex;
    
    /// <summary>
    /// Get the solid texture for external use
    /// </summary>
    public Texture2D GetSolidTexture() => _solidTex;
    
    #endregion
}
