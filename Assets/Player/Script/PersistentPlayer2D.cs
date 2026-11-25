using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Keeps the prototype player alive and places it at a scene entry point after travel.</summary>
[DisallowMultipleComponent]
public sealed class PersistentPlayer2D : MonoBehaviour
{
    private static PersistentPlayer2D instance;
    private static string pendingSceneName;
    private static string pendingSpawnPointId;
    private static bool hasPendingTravel;

    private Rigidbody2D body;
    private bool isChangingScene;
    private PortalArrival portalArrival;
    private bool foundTravelSpawn;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        body = GetComponent<Rigidbody2D>();
        DontDestroyOnLoad(transform.root.gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    public bool TravelTo(string sceneName, string spawnPointId, bool animatePortalArrival = false)
    {
        if (isChangingScene || string.IsNullOrWhiteSpace(sceneName) || string.IsNullOrWhiteSpace(spawnPointId))
            return false;

        pendingSceneName = sceneName;
        pendingSpawnPointId = spawnPointId;
        hasPendingTravel = true;
        isChangingScene = true;

        foundTravelSpawn = false;
        SceneFadeTransition.LoadScene(sceneName, .8f, OnTravelLoadFinished, readyToReveal: () =>
        {
            if (animatePortalArrival && foundTravelSpawn && portalArrival == null)
                portalArrival = PortalArrival.Prepare(GetComponent<PlayerMovement2D>());
            return true;
        });
        return true;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!hasPendingTravel || scene.name != pendingSceneName) return;

        SceneSpawnPoint2D[] spawnPoints = FindObjectsByType<SceneSpawnPoint2D>(FindObjectsSortMode.None);
        SceneSpawnPoint2D destination = null;
        foreach (SceneSpawnPoint2D spawnPoint in spawnPoints)
        {
            if (spawnPoint.SpawnPointId == pendingSpawnPointId)
            {
                destination = spawnPoint;
                break;
            }
        }

        if (destination == null)
        {
            Debug.LogError($"[Portal] Scene '{scene.name}' has no spawn point named '{pendingSpawnPointId}'.", this);
        }
        else
        {
            foundTravelSpawn = true;
            transform.position = destination.transform.position;
            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.WakeUp();
            }
        }

        ClearPendingTravel();
    }

    private void OnTravelLoadFinished(bool succeeded)
    {
        if (!succeeded) { portalArrival?.Dispose(); portalArrival = null; ClearPendingTravel(); }
    }

    public IEnumerator CompletePortalArrival()
    {
        var arrival = portalArrival;
        try { if (arrival != null) yield return arrival.Enter(); }
        finally { arrival?.Dispose(); portalArrival = null; }
    }

    private void ClearPendingTravel()
    {
        hasPendingTravel = false;
        pendingSceneName = null;
        pendingSpawnPointId = null;
        isChangingScene = false;
    }

    private void OnDestroy()
    {
        portalArrival?.Dispose();
        if (instance != this) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        instance = null;
    }
}
