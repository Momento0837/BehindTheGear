using System;
using System.Linq;
using BehindTheGear.HideOut;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SaveGameSession
{
    public static HideOutSettings Settings { get; private set; }
    public static bool Expedition { get; private set; }
    public static string MenuMessage { get; private set; }
    private static HideOutSave pending;
    private static GameObject pendingPlayer;
    public static bool IsContinuing => pending != null;
    public static void Configure(HideOutSettings settings) => Settings = settings;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        Settings = null; pending = null; pendingPlayer = null; Expedition = false; MenuMessage = null;
        SceneManager.sceneLoaded -= SceneLoaded; SceneManager.sceneLoaded += SceneLoaded;
    }
    public static void BeginNewGame()
    {
        StorySceneFlow.RestartAtCombat = false;
        MenuMessage = null; DisposePlayer(); pending = null; pendingPlayer = null; Expedition = false;
        HideOutTravel.ClearSession(); RoomDropCache.Clear(); QuestTracker.Reset(); QuestJournal.Clear();
    }
    public static void Continue(HideOutSave save, HideOutSettings settings, GameObject playerPrefab)
    {
        if (SceneFadeTransition.IsRunning || pending != null) return;
        if (playerPrefab == null) throw new InvalidOperationException("이어하기 플레이어 프리팹이 연결되지 않았습니다.");
        HideOutPlayerBridge.ValidateSnapshot(save, settings);
        MenuMessage = null; Settings = settings; pending = save; pendingPlayer = playerPrefab;
        SceneFadeTransition.LoadScene(settings.scenePath, .4f, success =>
        {
            if (!success) { pending = null; pendingPlayer = null; MenuMessage = "기지 씬을 열지 못했습니다."; }
        });
    }
    private static void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (pending == null || Settings == null || scene.path != Settings.scenePath) return;
        var snapshot = pending; var prefab = pendingPlayer;
        pending = null; pendingPlayer = null; // Consume exactly once, before any callbacks.
        GameObject rig = null;
        try
        {
            DisposePlayer(); RoomDropCache.Clear(); HideOutTravel.ClearSession();
            rig = UnityEngine.Object.Instantiate(prefab);
            var bridge = rig.GetComponentInChildren<HideOutPlayerBridge>()
                ?? rig.GetComponentInChildren<PlayerInventory>().gameObject.AddComponent<HideOutPlayerBridge>();
            bridge.Initialize(Settings, false);
            bridge.RestoreSnapshot(snapshot);
            Expedition = true;
            bridge.SetMessage("이어하기 완료 · 기지 내부");
        }
        catch (Exception error)
        {
            if (rig != null) { rig.SetActive(false); UnityEngine.Object.Destroy(rig); }
            MenuMessage = "불러오기 실패 · 저장 파일 유지: " + error.Message;
            SceneManager.LoadScene("ControlSchemeSelect");
        }
    }
    public static void StartExpedition()
    {
        Expedition = true; RoomDropCache.Clear();
        SceneFadeTransition.LoadScene("Forest", .4f);
    }
    public static void ReturnToMenu()
    {
        if (SceneFadeTransition.IsRunning) return;
        RoomDropCache.Clear(); HideOutTravel.ClearSession(); pending = null; pendingPlayer = null;
        DisposePlayer(); QuestTracker.Reset(); QuestJournal.Clear(); Expedition = false;
        SceneFadeTransition.LoadScene("ControlSchemeSelect", .4f);
    }
    private static void DisposePlayer()
    {
        foreach (var player in UnityEngine.Object.FindObjectsByType<PersistentPlayer2D>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            // All roots in this game's persistent player scene belong to this play session.
            foreach (var root in player.gameObject.scene.GetRootGameObjects())
            {
                if (root.GetComponent<SceneFadeTransition>() != null || root.GetComponent<WindowsQuitOnEscape>() != null) continue;
                root.SetActive(false); UnityEngine.Object.Destroy(root);
            }
        }
    }
}
