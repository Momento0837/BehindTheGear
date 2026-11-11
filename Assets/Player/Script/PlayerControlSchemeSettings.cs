using UnityEngine;

// Numeric legacy value retained for old editor fixtures; it is not a supported input layout.
public enum PlayerControlScheme { WasdMouseQe = 0, ArrowKeys = 1 }

/// <summary>Compatibility facade. Gameplay always uses WASD, mouse and Q/E.</summary>
public static class PlayerControlSchemeSettings
{
    // Retained for existing editor fixtures that restore these fields by reflection.
    private static bool loaded;
    private static PlayerControlScheme current;
    public static PlayerControlScheme Current => PlayerControlScheme.WasdMouseQe;
    public static void Select(PlayerControlScheme scheme)
    {
        loaded = true;
        current = PlayerControlScheme.WasdMouseQe;
    }
}
