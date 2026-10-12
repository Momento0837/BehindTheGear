using UnityEngine;

public enum PlayerControlScheme
{
    WasdMouseQe = 0,
    ArrowKeys = 1
}

/// <summary>Stores the control layout chosen on the control selection scene.</summary>
public static class PlayerControlSchemeSettings
{
    private const string PreferenceKey = "BehindTheGear.PlayerControlScheme";
    private static bool loaded;
    private static PlayerControlScheme current;

    public static PlayerControlScheme Current
    {
        get
        {
            if (!loaded)
            {
                current = PlayerPrefs.GetInt(PreferenceKey, (int)PlayerControlScheme.WasdMouseQe) == (int)PlayerControlScheme.ArrowKeys
                    ? PlayerControlScheme.ArrowKeys
                    : PlayerControlScheme.WasdMouseQe;
                loaded = true;
            }

            return current;
        }
    }

    public static void Select(PlayerControlScheme scheme)
    {
        current = scheme == PlayerControlScheme.ArrowKeys
            ? PlayerControlScheme.ArrowKeys
            : PlayerControlScheme.WasdMouseQe;
        loaded = true;
        PlayerPrefs.SetInt(PreferenceKey, (int)current);
        PlayerPrefs.Save();
    }
}
