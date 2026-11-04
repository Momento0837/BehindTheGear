using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Only the last departed room is retained. Never part of a save snapshot.</summary>
public static class RoomDropCache
{
    public sealed class Drop
    {
        public string itemId;
        public int amount;
        public Vector3 localPosition;
    }
    public sealed class Snapshot
    {
        public string roomId;
        public List<Drop> drops = new();
    }
    public sealed class Transition
    {
        public Snapshot departing, arriving;
        public string destination;
    }
    public static Snapshot Previous { get; private set; }
    public static string CurrentRoom { get; private set; }
    public static string RoomAt(Scene scene, Vector3 position) => scene.name == "Market"
        ? scene.path + (position.x < 44f ? "#garbage" : "#market") : scene.path;
    public static string SceneRoom(string scene) => scene == "Market" || scene.EndsWith("/Market.unity")
        ? "Assets/Scenes/Main/Market.unity" + (SaveGameSession.Expedition ? "#market" : "#garbage")
        : scene == "Forest" ? "Assets/Scenes/Main/Forest.unity" : scene;
    private static Vector3 Origin(string room) => room != null && room.EndsWith("#market") ? new Vector3(44.92f, 0, 0) : Vector3.zero;
    public static void SetCurrent(string room) => CurrentRoom = room;
    public static Transition Prepare(string destination)
    {
        string source = CurrentRoom ?? SceneManager.GetActiveScene().path;
        var snapshot = new Snapshot { roomId = source };
        foreach (var pickup in UnityEngine.Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None))
            if (pickup.Definition != null && pickup.Amount > 0 && RoomAt(pickup.gameObject.scene, pickup.transform.position) == source)
                snapshot.drops.Add(new Drop { itemId = pickup.Definition.ItemId, amount = pickup.Amount,
                    localPosition = pickup.transform.position - Origin(source) });
        // Reserve arrival before replacing Previous. Empty snapshots are intentionally meaningful.
        return new Transition { departing = snapshot, arriving = Previous?.roomId == destination ? Previous : null, destination = destination };
    }
    public static void Commit(Transition transition, Scene scene)
    {
        if (transition == null) return;
        var settings = SaveGameSession.Settings;
        if (transition.arriving != null)
        {
            // Hide synchronously: deferred Destroy must never permit collection or another capture.
            foreach (var pickup in UnityEngine.Object.FindObjectsByType<WorldItemPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (pickup.gameObject.scene == scene && RoomAt(scene, pickup.transform.position) == transition.destination)
                { pickup.gameObject.SetActive(false); UnityEngine.Object.Destroy(pickup.gameObject); }
            foreach (var drop in transition.arriving.drops)
            {
                var item = settings != null ? settings.Find(drop.itemId) : null;
                if (item == null) throw new InvalidOperationException("미등록 드롭 ID: " + drop.itemId);
                var root = new GameObject("Restored " + drop.itemId);
                SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.position = Origin(transition.destination) + drop.localPosition;
                root.AddComponent<WorldItemPickup>().Configure(item, drop.amount);
            }
        }
        // Same-scene rooms must not leave old live drops outside their cached snapshot.
        foreach (var pickup in UnityEngine.Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None))
            if (pickup.gameObject.scene == scene && RoomAt(scene, pickup.transform.position) == transition.departing.roomId
                && transition.destination != transition.departing.roomId)
            { pickup.gameObject.SetActive(false); UnityEngine.Object.Destroy(pickup.gameObject); }
        Previous = transition.departing; CurrentRoom = transition.destination;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Clear() { Previous = null; CurrentRoom = null; }
}
