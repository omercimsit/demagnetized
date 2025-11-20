using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Simple Localization System - Supports Turkish, English, German
/// Usage: LocalizationManager.Instance.Get("key") or L.Get("key")
/// </summary>
public class LocalizationManager : Singleton<LocalizationManager>
{

    public enum Language { Turkish, English, German }

    [SerializeField] private Language currentLanguage = Language.Turkish;

    public Language CurrentLanguage => currentLanguage;
    private static readonly string[] _languageNames = { "TÜRKÇE", "ENGLISH", "DEUTSCH" };
    private static readonly string[] _languageCodes = { "tr", "en", "de" };
    public static string[] LanguageNames => _languageNames;
    public static string[] LanguageCodes => _languageCodes;

    private const string LANGUAGE_PREF_KEY = "Language";
    private static readonly int MaxLanguageIndex = Enum.GetValues(typeof(Language)).Length - 1;

    /// <summary>
    /// Fired when the active language changes. Subscribe to refresh cached localized strings.
    /// </summary>
    public static event Action<Language> OnLanguageChanged;

    private Dictionary<string, string[]> translations;

    // Quick access
    public static string Get(string key) => Instance?.GetText(key) ?? key;

    protected override void OnAwake()
    {
        LoadLanguage();
        InitializeTranslations();
    }

    private void LoadLanguage()
    {
        if (PlayerPrefs.HasKey(LANGUAGE_PREF_KEY))
        {
            // User already chose a language — respect it
            int saved = PlayerPrefs.GetInt(LANGUAGE_PREF_KEY, 0);
            currentLanguage = (Language)Mathf.Clamp(saved, 0, MaxLanguageIndex);
        }
        else
        {
            // First launch — auto-detect from system language
            currentLanguage = DetectSystemLanguage();
            PlayerPrefs.SetInt(LANGUAGE_PREF_KEY, (int)currentLanguage);
            PlayerPrefs.Save();
            Debug.Log($"[Localization] Auto-detected language: {currentLanguage} (system: {Application.systemLanguage})");
        }
    }

    private static Language DetectSystemLanguage()
    {
        switch (Application.systemLanguage)
        {
            case SystemLanguage.Turkish:
                return Language.Turkish;
            case SystemLanguage.German:
                return Language.German;
            default:
                return Language.English;
        }
    }

    public void SetLanguage(Language lang)
    {
        if (currentLanguage == lang) return;

        currentLanguage = lang;
        PlayerPrefs.SetInt(LANGUAGE_PREF_KEY, (int)lang);
        PlayerPrefs.Save();

        OnLanguageChanged?.Invoke(lang);
        Debug.Log($"[Localization] Language changed to: {lang}");
    }

    public void SetLanguage(int index)
    {
        SetLanguage((Language)Mathf.Clamp(index, 0, MaxLanguageIndex));
    }

    public string GetText(string key)
    {
        if (translations.TryGetValue(key, out string[] values))
        {
            int langIndex = (int)currentLanguage;
            if (langIndex < values.Length)
                return values[langIndex];
        }
        return key;
    }

    private void InitializeTranslations()
    {
        // Format: { "key", new string[] { "Turkish", "English", "German" } }
        translations = new Dictionary<string, string[]>
        {
            // ============ MAIN MENU ============
            { "paused", new[] { "D U R A K L A T I L D I", "P A U S E D", "P A U S I E R T" } },
            { "resume", new[] { "DEVAM ET", "RESUME", "FORTSETZEN" } },
            { "settings", new[] { "AYARLAR", "SETTINGS", "EINSTELLUNGEN" } },
            { "mainmenu", new[] { "ANA MENÜ", "MAIN MENU", "HAUPTMENÜ" } },
            { "quit", new[] { "ÇIKIŞ", "QUIT", "BEENDEN" } },
            { "back", new[] { "GERİ", "BACK", "ZURÜCK" } },
            { "clicktostart", new[] { "BAŞLAMAK İÇİN TIKLA", "CLICK TO START", "KLICKE ZUM STARTEN" } },

            // ============ SETTINGS TABS ============
            { "tab_game", new[] { "OYUN", "GAME", "SPIEL" } },
            { "tab_graphics", new[] { "GRAFİK", "GRAPHICS", "GRAFIK" } },
            { "tab_effects", new[] { "EFEKTLER", "EFFECTS", "EFFEKTE" } },

            // ============ GAME SETTINGS ============
            { "language", new[] { "DİL", "LANGUAGE", "SPRACHE" } },
            { "audio", new[] { "SES", "AUDIO", "AUDIO" } },
            { "sensitivity", new[] { "HASSASİYET", "SENSITIVITY", "EMPFINDLICHKEIT" } },

            // ============ GRAPHICS SETTINGS ============
            { "display", new[] { "EKRAN", "DISPLAY", "ANZEIGE" } },
            { "fullscreen", new[] { "TAM EKRAN", "FULLSCREEN", "VOLLBILD" } },
            { "windowed", new[] { "PENCERE", "WINDOWED", "FENSTER" } },
            { "vsync_on", new[] { "VSYNC AÇIK", "VSYNC ON", "VSYNC AN" } },
            { "vsync_off", new[] { "VSYNC KAPALI", "VSYNC OFF", "VSYNC AUS" } },
            { "quality", new[] { "KALİTE", "QUALITY", "QUALITÄT" } },
            { "quality_high", new[] { "YÜKSEK", "HIGH", "HOCH" } },
            { "quality_medium", new[] { "ORTA", "MEDIUM", "MITTEL" } },
            { "quality_low", new[] { "DÜŞÜK", "LOW", "NIEDRIG" } },
            { "antialiasing", new[] { "ANTİ-ALİASİNG", "ANTI-ALIASING", "KANTENGLÄTTUNG" } },
            { "sharpness", new[] { "Keskinlik:", "Sharpness:", "Schärfe:" } },

            // ============ DLSS DESCRIPTIONS ============
            { "dlss_off", new[] { "DLSS kapalı - TAA kullanılır", "DLSS off - using TAA", "DLSS aus - TAA wird verwendet" } },
            { "dlss_dlaa", new[] { "Native çözünürlük + AI AA", "Native resolution + AI AA", "Native Auflösung + AI AA" } },
            { "dlss_quality", new[] { "1.5x upscale - iyi kalite", "1.5x upscale - good quality", "1.5x Upscale - gute Qualität" } },
            { "dlss_balanced", new[] { "1.7x upscale - dengeli", "1.7x upscale - balanced", "1.7x Upscale - ausgewogen" } },
            { "dlss_performance", new[] { "2x upscale - yüksek FPS", "2x upscale - high FPS", "2x Upscale - hohe FPS" } },
            { "dlss_ultra", new[] { "3x upscale - max FPS", "3x upscale - max FPS", "3x Upscale - max FPS" } },

            // ============ EFFECTS SETTINGS ============
            { "postprocessing", new[] { "POST-PROCESSING", "POST-PROCESSING", "NACHBEARBEITUNG" } },
            { "lighting", new[] { "AYDINLATMA", "LIGHTING", "BELEUCHTUNG" } },
            { "bloom", new[] { "BLOOM", "BLOOM", "BLOOM" } },
            { "motionblur", new[] { "MOTION BLUR", "MOTION BLUR", "BEWEGUNGSUNSCHÄRFE" } },
            { "vignette", new[] { "VIGNETTE", "VIGNETTE", "VIGNETTE" } },
            { "filmgrain", new[] { "FILM GRAIN", "FILM GRAIN", "FILMKORN" } },
            { "ssao", new[] { "SSAO", "SSAO", "SSAO" } },
            { "ssr", new[] { "SSR", "SSR", "SSR" } },
            { "volumetricfog", new[] { "VOLUMETRIC FOG", "VOLUMETRIC FOG", "VOLUMETRISCHER NEBEL" } },

            // ============ SETTINGS TABS (Main Menu) ============
            { "tab_audio", new[] { "SES", "AUDIO", "AUDIO" } },
            { "tab_video", new[] { "VİDEO", "VIDEO", "VIDEO" } },
            { "tab_controls", new[] { "KONTROLLER", "CONTROLS", "STEUERUNG" } },
            { "tab_accessibility", new[] { "ERİŞİM", "ACCESSIBILITY", "BARRIEREFREIHEIT" } },

            // ============ VIDEO SETTINGS ============
            { "resolution", new[] { "ÇÖZÜNÜRLÜK", "RESOLUTION", "AUFLÖSUNG" } },
            { "vsync", new[] { "VSYNC", "VSYNC", "VSYNC" } },
            { "fps_limit", new[] { "FPS LİMİTİ", "FPS LIMIT", "FPS LIMIT" } },
            { "dlss_mode", new[] { "ANTİ-ALİASİNG / DLSS", "ANTI-ALIASING / DLSS", "KANTENGLÄTTUNG / DLSS" } },
            { "aa_taa_active", new[] { "ANTİ-ALİASİNG: TAA (AKTİF)", "ANTI-ALIASING: TAA (ACTIVE)", "KANTENGLÄTTUNG: TAA (AKTIV)" } },

            // ============ CONTROLS SETTINGS ============
            { "mouse_sensitivity", new[] { "FARE HASSASİYETİ", "MOUSE SENSITIVITY", "MAUSEMPFINDLICHKEIT" } },
            { "invert_y", new[] { "Y EKSENİ TERS", "INVERT Y", "Y UMKEHREN" } },
            { "fov", new[] { "GÖRÜŞ ALANI", "FIELD OF VIEW", "SICHTFELD" } },

            // ============ ACCESSIBILITY SETTINGS ============
            { "subtitle_size", new[] { "ALTYAZI BOYUTU", "SUBTITLE SIZE", "UNTERTITELGRÖSSE" } },
            { "colorblind_mode", new[] { "RENK KÖRLÜĞÜ", "COLORBLIND MODE", "FARBENBLINDMODUS" } },

            // ============ PAUSE MENU TABS ============
            { "system_suspended", new[] { "SİSTEM ASKIDA", "SYSTEM SUSPENDED", "SYSTEM ANGEHALTEN" } },
            { "configure_options", new[] { "Oyun ayarlarını yapılandır", "Configure game options", "Spieloptionen konfigurieren" } },
            { "camera", new[] { "KAMERA", "CAMERA", "KAMERA" } },
            { "visual_effects", new[] { "GÖRSEL EFEKTLER", "VISUAL EFFECTS", "VISUELLE EFFEKTE" } },
            { "performance_effects", new[] { "PERFORMANS EFEKTLERİ", "PERFORMANCE EFFECTS", "LEISTUNGSEFFEKTE" } },
            { "master_volume", new[] { "ANA SES", "MASTER VOLUME", "HAUPTLAUTSTÄRKE" } },
            { "music", new[] { "MÜZİK", "MUSIC", "MUSIK" } },
            { "sound_effects", new[] { "SES EFEKTLERİ", "SOUND EFFECTS", "SOUNDEFFEKTE" } },
            { "settings_saved", new[] { "Ayarlar Kaydedildi", "Settings Saved", "Einstellungen Gespeichert" } },
            { "defaults_restored", new[] { "Varsayılanlar Yüklendi", "Defaults Restored", "Standards Wiederhergestellt" } },

            // ============ COMMON ============
            { "on", new[] { "AÇIK", "ON", "AN" } },
            { "off", new[] { "KAPALI", "OFF", "AUS" } },
            { "apply", new[] { "UYGULA", "APPLY", "ANWENDEN" } },
            { "reset", new[] { "SIFIRLA", "RESET", "ZURÜCKSETZEN" } },

            // ============ AUDIO LABELS ============
            { "ambience", new[] { "ORTAM SESİ", "AMBIENCE", "UMGEBUNG" } },

            // ============ SUBTITLE / ACCESSIBILITY ============
            { "size_small", new[] { "KÜÇÜK", "SMALL", "KLEIN" } },
            { "size_medium", new[] { "ORTA", "MEDIUM", "MITTEL" } },
            { "size_large", new[] { "BÜYÜK", "LARGE", "GROSS" } },
            { "cb_none", new[] { "YOK", "NONE", "KEINE" } },
            { "cb_deuteranopia", new[] { "DEUTERANOPI", "DEUTERANOPIA", "DEUTERANOPIE" } },
            { "cb_protanopia", new[] { "PROTANOPI", "PROTANOPIA", "PROTANOPIE" } },
            { "cb_tritanopia", new[] { "TRİTANOPİ", "TRITANOPIA", "TRITANOPIE" } },

            // ============ WORKBENCH MENU ============
            { "insert_cassette", new[] { "[ KASET TAK ]", "[ INSERT CASSETTE ]", "[ KASSETTE EINLEGEN ]" } },
            { "press_play", new[] { "[ OYNAT ]", "[ PRESS PLAY ]", "[ PLAY DRÜCKEN ]" } },
            { "calibrate", new[] { "[ KALİBRE ET ]", "[ CALIBRATE ]", "[ KALIBRIEREN ]" } },
            { "power_off", new[] { "[ KAPAT ]", "[ POWER OFF ]", "[ AUSSCHALTEN ]" } },
            { "repair_station", new[] { "TAMİR İSTASYONU", "REPAIR STATION", "REPARATURSTATION" } },
            { "click_to_select", new[] { "[SEÇMEK İÇİN TIKLA]", "[CLICK TO SELECT]", "[KLICKEN ZUM AUSWÄHLEN]" } },

            // ============ CLONE SELECTION ============
            { "select_clone", new[] { "KLON SEÇ", "SELECT CLONE", "KLON WÄHLEN" } },
            { "clone_routine", new[] { "RUTİN", "ROUTINE", "ROUTINE" } },
            { "clone_shadow", new[] { "GÖLGE", "SHADOW", "SCHATTEN" } },
            { "clone_spark", new[] { "KIVILCIM", "SPARK", "FUNKE" } },
            { "release_to_confirm", new[] { "Seçimi onaylamak için TAB'ı bırakın", "Release TAB to confirm selection", "TAB loslassen zum Bestätigen" } },
            { "swap_cassette", new[] { "[ Kaset değiştirmek için TIKLA veya BIRAK ]", "[ CLICK or RELEASE to swap cassette ]", "[ KLICKEN oder LOSLASSEN zum Kassettenwechsel ]" } },

            // ============ RECORDING HUD ============
            { "recording", new[] { "KAYIT", "REC", "AUFN" } },
            { "tape_remaining", new[] { "KALAN KASET", "TAPE REMAINING", "BAND ÜBRIG" } },
            { "pause", new[] { "DURAKLAT", "PAUSE", "PAUSE" } },

            // ============ VHS PAUSE MENU ============
            { "play", new[] { "OYNAT", "PLAY", "ABSPIELEN" } },
            { "rewind", new[] { "GERİ SAR", "REWIND", "ZURÜCKSPULEN" } },
            { "eject", new[] { "ÇIKAR", "EJECT", "AUSWERFEN" } },
            { "checkpoint", new[] { "KONTROL NOKTASI", "CHECKPOINT", "KONTROLLPUNKT" } },

            // ============ PAUSE MENU UI ============
            { "clone_system_status", new[] { "KLON SİSTEMİ v2.0 // KARARLI", "CLONE SYSTEM v2.0 // STABLE", "KLONSYSTEM v2.0 // STABIL" } },

            // ============ CONFIRMATION DIALOGS ============
            { "confirm_mainmenu", new[] { "Ana menüye dönmek istediğinize emin misiniz?\nKaydedilmemiş ilerleme kaybolacak.", "Return to main menu?\nUnsaved progress will be lost.", "Zurück zum Hauptmenü?\nNicht gespeicherter Fortschritt geht verloren." } },
            { "confirm_mainmenu_title", new[] { "ANA MENÜ", "MAIN MENU", "HAUPTMENÜ" } },
            { "confirm_quit", new[] { "Oyundan çıkmak istediğinize emin misiniz?", "Are you sure you want to quit?", "Möchten Sie das Spiel wirklich beenden?" } },
            { "confirm_quit_title", new[] { "ÇIKIŞ", "QUIT", "BEENDEN" } },
            { "button_yes", new[] { "EVET", "YES", "JA" } },
            { "button_no", new[] { "HAYIR", "NO", "NEIN" } },

            // ============ CREDITS ============
            { "credits_subtitle", new[] { "BİR OYUN", "A GAME BY", "EIN SPIEL VON" } },
            { "credits_role_design_art", new[] { "Oyun Tasarımı • Sanat Yönetimi", "Game Design • Art Direction", "Spieldesign • Art Direction" } },
            { "credits_role_design_code", new[] { "Oyun Tasarımı • Programlama", "Game Design • Programming", "Spieldesign • Programmierung" } },
            { "credits_copyright", new[] { "© 2026 Tüm Hakları Saklıdır", "© 2026 All Rights Reserved", "© 2026 Alle Rechte vorbehalten" } },
            { "credits_close_hint", new[] { "[ Tıkla veya ESC ile kapat ]", "[ Click or ESC to close ]", "[ Klick oder ESC zum Schließen ]" } },

            // ============ CLONE SYSTEM UI ============
            { "clone_select_title", new[] { "◈  KLON TÜRÜ SEÇ  ◈", "◈  SELECT CLONE TYPE  ◈", "◈  KLON-TYP WÄHLEN  ◈" } },
            { "clone_select_subtitle", new[] { "Zamansal yankı kişiliğini seç", "Choose your temporal echo personality", "Wähle die Persönlichkeit deines Zeitechos" } },
            { "clone_recorded", new[] { "KAYITLI", "RECORDED", "AUFGENOMMEN" } },
            { "clone_empty", new[] { "BOŞ", "EMPTY", "LEER" } },
            { "clone_status", new[] { "Durum", "Status", "Status" } },
            { "clone_time", new[] { "Zaman", "Time", "Zeit" } },
            { "clone_clones", new[] { "Klonlar", "Clones", "Klone" } },
            { "hint_navigate", new[] { "Gezin", "Navigate", "Navigieren" } },
            { "hint_quick_select", new[] { "Hızlı Seç", "Quick Select", "Schnellwahl" } },
            { "hint_confirm", new[] { "Onayla", "Confirm", "Bestätigen" } },
            { "hint_select", new[] { "Seç", "Select", "Auswählen" } },
            { "hint_close", new[] { "Kapat", "Close", "Schließen" } },
            { "phase_ready", new[] { "Hazır", "Ready", "Bereit" } },
            { "phase_recording", new[] { "KAYIT", "RECORDING", "AUFNAHME" } },
            { "phase_rewinding", new[] { "Geri Sarılıyor", "Rewinding", "Zurückspulen" } },
            { "phase_review", new[] { "İnceleme Modu", "Review Mode", "Überprüfungsmodus" } },
            { "phase_paused", new[] { "DURDURULDU", "PAUSED", "PAUSIERT" } },
            { "phase_playing", new[] { "Oynatılıyor", "Playing", "Wiedergabe" } },

            // ============ VHS / LOADING ============
            { "vhs_pause", new[] { "DURAKLAT", "PAUSE", "PAUSE" } },
            { "loading", new[] { "YÜKLENİYOR", "LOADING", "LADEN" } },

            // ============ STATUS PANEL HINTS ============
            { "status_hint_idle", new[] { "[TAB] Seç   [R] Kayıt", "[TAB] Select   [R] Record", "[TAB] Auswählen   [R] Aufnahme" } },
            { "status_hint_recording", new[] { "[R] Kaydı Durdur", "[R] Stop Recording", "[R] Aufnahme Stoppen" } },
            { "status_hint_review", new[] { "[SPACE] Oynat   [TAB] Seç   [R] Kayıt", "[SPACE] Play   [TAB] Select   [R] Record", "[SPACE] Abspielen   [TAB] Auswählen   [R] Aufnahme" } },
            { "status_hint_playback", new[] { "[SPACE] Duraklat/Devam", "[SPACE] Pause/Resume", "[SPACE] Pause/Weiter" } },
            { "status_hint_rewinding", new[] { "Geri Sarılıyor...", "Rewinding...", "Zurückspulen..." } },

            // ============ CONTROL HINTS ============
            { "hint_mouse_speed", new[] { "Fare bakış hızı", "Mouse look speed", "Mausgeschwindigkeit" } },
            { "hint_fov", new[] { "Görüş açısı (geniş = daha görünür)", "Field of view (wider = more visible)", "Sichtfeld (breiter = mehr sichtbar)" } },

            // ============ FOV DESCRIPTIONS ============
            { "fov_narrow", new[] { "Dar (Sinematik)", "Narrow (Cinematic)", "Schmal (Cinematic)" } },
            { "fov_standard", new[] { "Standart", "Standard", "Standard" } },
            { "fov_wide", new[] { "Geniş (Rekabetçi)", "Wide (Competitive)", "Weit (Kompetitiv)" } },
            { "fov_ultrawide", new[] { "Ultra Geniş", "Ultra Wide", "Ultra Weit" } },

            // ============ DISPLAY HINTS ============
            { "hint_fullscreen", new[] { "Özel mod", "Exclusive mode", "Exklusiver Modus" } },
            { "hint_vsync", new[] { "FPS'i Hz'e sabitle", "Limit FPS to Hz", "FPS auf Hz begrenzen" } },

            // ============ QUALITY DESCRIPTIONS ============
            { "quality_desc_high", new[] { "En iyi görsel, DLSS Kalite (%66)", "Best visuals, DLSS Quality (66%)", "Beste Grafik, DLSS Qualität (66%)" } },
            { "quality_desc_balanced", new[] { "Dengeli, DLSS Dengeli (%58)", "Balanced, DLSS Balanced (58%)", "Ausgewogen, DLSS Ausgewogen (58%)" } },
            { "quality_desc_performance", new[] { "Maks FPS, DLSS Performans (%50)", "Max FPS, DLSS Performance (50%)", "Max FPS, DLSS Leistung (50%)" } },

            // ============ DLSS MODEL INFO ============
            { "dlss_model_m", new[] { "DLSS 4.5 Model M (RTX 40/50 Optimize)", "DLSS 4.5 Model M (RTX 40/50 Optimized)", "DLSS 4.5 Modell M (RTX 40/50 Optimiert)" } },
            { "dlss_model_k", new[] { "DLSS Model K (RTX 20/30)", "DLSS Model K (RTX 20/30)", "DLSS Modell K (RTX 20/30)" } },
            { "dlss_not_supported", new[] { "DLSS desteklenmiyor (RTX gerekli)", "DLSS not supported (RTX required)", "DLSS nicht unterstützt (RTX erforderlich)" } },

            // ============ EFFECTS HINTS ============
            { "hint_bloom", new[] { "Işıldama", "Glow effects", "Lichteffekte" } },
            { "hint_vignette", new[] { "Kenar karartma", "Edge darkening", "Randabdunklung" } },
            { "hint_filmgrain", new[] { "Sinematik gren", "Cinematic grain", "Filmkorn" } },
            { "hint_motionblur", new[] { "Hareket bulanıklığı", "Movement blur", "Bewegungsunschärfe" } },
            { "hint_perf_effects", new[] { "Kapatmak FPS artırabilir", "Disabling may improve FPS", "Deaktivieren kann FPS erhöhen" } },
            { "hint_ssao", new[] { "Ortam gölgeleri (GPU yoğun)", "Ambient shadows (GPU intensive)", "Umgebungsschatten (GPU intensiv)" } },
            { "hint_volumetricfog", new[] { "3D sis ışınları (GPU yoğun)", "3D fog rays (GPU intensive)", "3D Nebel (GPU intensiv)" } },

            // ============ SPLASH / LOADING ============
            { "splash_press_any_key", new[] { "Devam etmek için bir tuşa basın", "Press any key to continue", "Beliebige Taste zum Fortfahren drücken" } },

            // ============ DLSS DESCRIPTIONS ============
            { "dlss_desc_off", new[] { "DLSS kapalı, TAA kullanılır", "DLSS off, uses TAA", "DLSS aus, TAA wird verwendet" } },
            { "dlss_desc_dlaa", new[] { "Native çözünürlük + DLSS AA (en iyi kalite)", "Native resolution + DLSS AA (best quality)", "Native Auflösung + DLSS AA (beste Qualität)" } },
            { "dlss_desc_quality", new[] { "1.5x upscale - iyi kalite", "1.5x upscale - good quality", "1.5x Upscale - gute Qualität" } },
            { "dlss_desc_balanced", new[] { "1.7x upscale - dengeli", "1.7x upscale - balanced", "1.7x Upscale - ausgewogen" } },
            { "dlss_desc_performance", new[] { "2x upscale - yüksek FPS", "2x upscale - high FPS", "2x Upscale - hohe FPS" } },
            { "dlss_desc_ultraperf", new[] { "3x upscale - max FPS", "3x upscale - max FPS", "3x Upscale - max FPS" } },

            // ============ DEBUG UI ============
            { "debug_total", new[] { "Toplam", "Total", "Gesamt" } },
            { "debug_visible", new[] { "Görünen", "Visible", "Sichtbar" } },
            { "debug_status", new[] { "DURUM", "STATUS", "STATUS" } },
            { "debug_status_good", new[] { "✓ İYİ", "✓ GOOD", "✓ GUT" } },
            { "debug_status_medium", new[] { "◐ ORTA", "◐ MEDIUM", "◐ MITTEL" } },
            { "debug_status_poor", new[] { "✗ ZAYIF", "✗ POOR", "✗ SCHLECHT" } },
            { "debug_press_to_hide", new[] { "[{key}] ile gizle", "Press [{key}] to hide", "[{key}] zum Ausblenden" } },
            { "tagline", new[] { "Bir hafıza silinemez", "A memory cannot be erased", "Eine Erinnerung kann nicht gelöscht werden" } },
            { "liner_notes", new[] { "YAPIM EKİBİ", "LINER NOTES", "LINER NOTES" } },
            { "liner_notes_subtitle", new[] { "Kasetin arkasındaki insanlar", "The people behind the tape", "Die Menschen hinter dem Band" } },
            { "progress_lost_warning", new[] { "TÜM İLERLEME KAYBOLACAK", "ALL PROGRESS WILL BE LOST", "ALLER FORTSCHRITT GEHT VERLOREN" } },
            { "debug_overlay_controls", new[] { "F1: Paneli aç/kapat | ESC: Ayarlar", "F1: Toggle this overlay | ESC: Settings menu", "F1: Overlay umschalten | ESC: Einstellungen" } },

            // ============ MAIN MENU ITEMS ============
            { "menu_continue", new[] { "DEVAM", "CONTINUE", "FORTSETZEN" } },
            { "menu_side_a", new[] { "A YÜZÜ", "SIDE A", "SEITE A" } },
            { "menu_new_recording", new[] { "YENİ KAYIT", "NEW RECORDING", "NEUE AUFNAHME" } },
            { "menu_chapters", new[] { "BÖLÜMLER", "CHAPTERS", "KAPITEL" } },
            { "menu_locked", new[] { "[KİLİTLİ]", "[LOCKED]", "[GESPERRT]" } },
            { "menu_calibration", new[] { "KALİBRASYON", "CALIBRATION", "KALIBRIERUNG" } },
            { "confirm_new_recording", new[] { "Yeni bir kayıt başlatılsın mı?\nBu mevcut kaseti silecek.", "Begin a new recording?\nThis will erase the current tape.", "Neue Aufnahme starten?\nDas aktuelle Band wird gelöscht." } },
            { "confirm_eject", new[] { "Kaset çıkarılsın mı?", "Eject the tape?", "Band auswerfen?" } },
            { "btn_cancel", new[] { "İptal", "Cancel", "Abbrechen" } },
            { "btn_erase_record", new[] { "Sil ve Kaydet", "Erase & Record", "Löschen & Aufnehmen" } },

            // ============ CLONE TYPES ============
            { "clone_routine", new[] { "RUTİN", "ROUTINE", "ROUTINE" } },
            { "clone_routine_desc", new[] { "SUPERHOT Zaman", "SUPERHOT Time", "SUPERHOT-Zeit" } },
            { "clone_fear", new[] { "KORKU", "FEAR", "ANGST" } },
            { "clone_fear_desc", new[] { "Zaman Bozulması", "Time Distortion", "Zeitverzerrung" } },
            { "clone_brave", new[] { "CESARET", "BRAVE", "MUTIG" } },
            { "clone_brave_desc", new[] { "Hız Artışı", "Speed Boost", "Geschwindigkeitsschub" } },

            // ============ VHS LOADING ============
            { "vhs_rec", new[] { "KAY", "REC", "AUF" } },
            { "vhs_play", new[] { "▶ OYNAT", "▶ PLAY", "▶ ABSPIELEN" } },

            // ============ CREDITS ============
            { "credits_created_by", new[] { "YAPAN", "CREATED BY", "ERSTELLT VON" } },
            { "credits_game_design", new[] { "OYUN TASARIMI", "GAME DESIGN", "SPIELDESIGN" } },
            { "credits_programming", new[] { "PROGRAMLAMA", "PROGRAMMING", "PROGRAMMIERUNG" } },
            { "credits_narrative", new[] { "SENARYO", "NARRATIVE", "ERZÄHLUNG" } },
            { "credits_env_art", new[] { "ÇEVRE SANATI", "ENVIRONMENT ART", "UMGEBUNGSKUNST" } },
            { "credits_char_anim", new[] { "KARAKTER ANİMASYON", "CHARACTER ANIMATION", "CHARAKTERANIMATION" } },
            { "credits_audio_design", new[] { "SES TASARIMI", "AUDIO DESIGN", "AUDIODESIGN" } },
            { "credits_special_thanks", new[] { "ÖZEL TEŞEKKÜRLER", "SPECIAL THANKS", "BESONDERER DANK" } },
            { "credits_thanks_names", new[] { "Unity Technologies\nNVIDIA\nAçık kaynak topluluğu", "Unity Technologies\nNVIDIA\nThe open-source community", "Unity Technologies\nNVIDIA\nDie Open-Source-Community" } },
            { "credits_you_playing", new[] { "Oynadığın için.", "You, for playing.", "Dafür, dass du spielst." } },

            // ============ HUD LABELS ============
            { "hud_chapter", new[] { "BÖLÜM", "CHAPTER", "KAPITEL" } },
            { "hud_ch2_name", new[] { "Silme", "Erasure", "Löschung" } },
            { "hud_scene", new[] { "SAHNE", "SCENE", "SZENE" } },

            // ============ NAVIGATION HINTS ============
            { "nav_navigate", new[] { "GEZİN", "NAVIGATE", "NAVIGIEREN" } },
            { "nav_select", new[] { "SEÇ", "SELECT", "AUSWÄHLEN" } },

            // ============ TECHNICAL LABELS ============
            { "render_label", new[] { "Render", "Render", "Render" } },
            { "sample_rate", new[] { "Örnekleme Hızı", "Sample Rate", "Abtastrate" } },
            { "interact_prompt", new[] { "Etkileşmek için [E]", "Press [E] to interact", "[E] zum Interagieren" } },
        };
    }
}

/// <summary>
/// Shortcut class for localization: L.Get("key")
/// Tries Unity Localization (Loc) first, falls back to legacy LocalizationManager.
/// After migration is verified, the fallback can be removed.
/// </summary>
public static class L
{
    public static string Get(string key)
    {
        // Primary: Unity Localization package
        string result = Loc.Get(key);
        // If Loc returned the key itself (not found), try legacy fallback
        if (result == key)
        {
            string legacy = LocalizationManager.Get(key);
            if (legacy != key) return legacy;
        }
        return result;
    }
}
