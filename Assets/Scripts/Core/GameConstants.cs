namespace CloneGame.Core
{
    /// <summary>
    /// Central location for game-wide constants.
    /// Prevents hardcoded strings scattered across the codebase.
    /// </summary>
    public static class GameConstants
    {
        // Player identifiers
        public const string PLAYER_TAG = "Player";
        public const string PLAYER_CHARACTER_NAME = "Banana Man";

        // Layer names
        public const string KINEMATION_LAYER = "KinemationCharacter";
        public const string PORTAL_LAYER = "Portal";

        // Common tags
        public const string MAIN_CAMERA_TAG = "MainCamera";
        public const string PORTAL_TAG = "Portal";
    }
}
