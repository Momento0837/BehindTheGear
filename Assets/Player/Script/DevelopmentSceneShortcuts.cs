#if UNITY_EDITOR || DEVELOPMENT_BUILD
using BehindTheGear.HideOut;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>Play-only development navigation. Does not modify scene assets or save slots.</summary>
[DefaultExecutionOrder(-10000)]
public sealed class DevelopmentSceneShortcuts : MonoBehaviour
{
    public const string ForestPath = "Assets/Scenes/Main/Forest.unity";
    public const string MarketPath = "Assets/Scenes/Main/Market.unity";
    public const string HideOutPath = "Assets/Scenes/HideOutSystemScene/HideOutSystemScene.unity";
    private static DevelopmentSceneShortcuts instance;
    private bool switching;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        EnsureInitialized();
    }

    // Restore the keyboard service when scripts reload during an existing Play session.
    public static void EnsureInitialized()
    {
        if (!Application.isPlaying || instance != null) return;
        instance = FindFirstObjectByType<DevelopmentSceneShortcuts>();
        if (instance != null) return;
        var root = new GameObject("Development Scene Shortcuts");
        DontDestroyOnLoad(root);
        instance = root.AddComponent<DevelopmentSceneShortcuts>();
    }

    public static bool IsShortcutChord(Keyboard keyboard) => keyboard != null
        && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed)
        && (keyboard.digit1Key.isPressed || keyboard.digit2Key.isPressed || keyboard.digit3Key.isPressed
            || keyboard.numpad1Key.isPressed || keyboard.numpad2Key.isPressed || keyboard.numpad3Key.isPressed
            || keyboard.digit1Key.wasPressedThisFrame || keyboard.digit2Key.wasPressedThisFrame || keyboard.digit3Key.wasPressedThisFrame
            || keyboard.numpad1Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame);

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || !(keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed)) return;
        if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) Travel(ForestPath);
        else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) Travel(MarketPath);
        else if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) Travel(HideOutPath);
    }

    public static bool Travel(string path)
    {
        if (!Application.isPlaying) return false;
        if (path != ForestPath && path != MarketPath && path != HideOutPath) return false;
        EnsureInitialized();
        return instance.BeginTravel(path);
    }

    private bool BeginTravel(string path)
    {
        if (switching || SceneFadeTransition.IsRunning || HideOutTravel.IsTravelling) return false;
        if (SceneManager.GetActiveScene().path == path)
        { ShowNotice("Already in this scene"); return false; }
        if (!Application.CanStreamedLevelBeLoaded(path))
        { ShowNotice("Scene is missing from the build scene list"); return false; }
        // Death owns a separate asynchronous restart; do not race that existing sequence.
        var playerHealth = PlayerTargetRegistry.Current != null
            ? PlayerTargetRegistry.Current.GetComponent<PlayerHealth>() : FindFirstObjectByType<PlayerHealth>();
        if (playerHealth != null && playerHealth.IsDead)
        { ShowNotice("Wait for the death restart"); return false; }

        foreach (var menu in FindObjectsByType<PlayerSkillDevelopmentMenu>(FindObjectsSortMode.None)) menu.Close();
        foreach (var inventory in FindObjectsByType<InventoryUI>(FindObjectsSortMode.None)) inventory.CloseForDevelopmentSceneTravel();
        foreach (var input in FindObjectsByType<PlayerInputReader>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            input.SuppressGameplayForCurrentFrame();
        PlayerInputReader.SuppressPointerForCurrentFrame();
        switching = true;
        SceneFadeTransition.LoadScene(path, .2f, success =>
        {
            switching = false;
            if (success) HideOutTravel.ClearSession();
            else ShowNotice("Scene travel failed");
        });
        return true;
    }

    private void ShowNotice(string value) => Debug.Log("[Development Scene Shortcuts] " + value, this);
}
#endif
