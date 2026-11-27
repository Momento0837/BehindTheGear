using System;
using System.IO;
using BehindTheGear.HideOut;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// When the game is closed (or Play Mode is stopped) without saving at the bed, the progress at that moment is written to
/// a separate auto-save next to the manual slot. The start menu then asks whether to load it (예) or discard it (아니요).
/// The manual slot and the account (cloud) save are never touched. Needs the base state, i.e. after the first base visit.
/// </summary>
public static class QuitAutoSave
{
    public static string FilePath => Path.Combine(Path.GetDirectoryName(ManualSaveStore.SlotPath), "slot-1-autosave.json");
    public static bool Exists => File.Exists(FilePath);
    public static DateTime SavedAt => File.GetLastWriteTime(FilePath);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Hook()
    {
        // Domain reload is disabled in this project: unsubscribe first so the handler is never added twice.
        Application.quitting -= OnQuitting;
        Application.quitting += OnQuitting;
    }

    public static void Delete()
    {
        foreach (string suffix in new[] { "", ".tmp", ".bak" })
            if (File.Exists(FilePath + suffix)) File.Delete(FilePath + suffix);
    }

    private static void OnQuitting()
    {
        try { Write(); }
        catch (Exception error) { Debug.LogWarning("[AutoSave] 종료 자동 저장 실패 · " + error.Message); }
    }

    /// <summary>Writes the auto-save for the live player. Returns false when there is nothing (new) to keep.</summary>
    public static bool Write()
    {
        var bridge = FindPlayerBridge();
        if (bridge == null) return false; // menu, or before the base state exists
        var snapshot = bridge.CaptureSnapshot();
        // Closed in the field: leaving the base after loading puts the player back here.
        var scene = SceneManager.GetActiveScene();
        try
        {
            var here = FieldReturnState.Capture(bridge.gameObject, scene);
            here.Validate(); // only Forest / Market are field scenes; inside the base the entrance record stays
            snapshot.fieldReturn = here;
        }
        catch (Exception) { }
        if (SameAsManualSave(snapshot)) { Delete(); return false; }
        ManualSaveStore.Write(FilePath, snapshot);
        Debug.Log("[AutoSave] 저장하지 않은 진행을 자동 저장했습니다 · " + FilePath);
        return true;
    }

    private static HideOutPlayerBridge FindPlayerBridge()
    {
        string slot = Path.GetFullPath(ManualSaveStore.SlotPath);
        foreach (var bridge in Object.FindObjectsByType<HideOutPlayerBridge>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (bridge == null || !bridge.Initialized || bridge.Sandbox || bridge.IsSaving || bridge.Inventory == null || bridge.Health == null) continue;
            if (!string.Equals(Path.GetFullPath(bridge.SavePath), slot, StringComparison.OrdinalIgnoreCase)) continue;
            return bridge;
        }
        return null;
    }

    // Closing right after a bed save: nothing new, so no notice next time.
    private static bool SameAsManualSave(HideOutSave snapshot)
    {
        if (!ManualSaveStore.Exists) return false;
        try { return Comparable(ManualSaveStore.ReadSlot()) == Comparable(snapshot); }
        catch (Exception) { return false; }
    }

    private static string Comparable(HideOutSave save)
    {
        var copy = JsonUtility.FromJson<HideOutSave>(JsonUtility.ToJson(save));
        copy.activeClock = 0; // ticks every frame
        return JsonUtility.ToJson(copy);
    }
}
