using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Places wildcats (Resources/Monsters/Wildcat) in the Forest together with the story monsters, before the opening
/// cutscene ends, so they are already standing there like the gang. Their AI stays off until the story switches its own
/// monsters on (combat start), exactly like the gang. They are placed far from the player around the story monsters.
/// StorySceneFlow collects its monster list on frame 2 after the load, so wildcats are created on frame 3 and never join
/// that list: they are extra field mobs, not part of the portal condition, and left out of field saves
/// (a fresh random set is placed on every Forest visit). Later Forest chapters hide the story monsters, so none appear.
/// </summary>
public sealed class WildcatFieldSpawner : MonoBehaviour
{
    public const string ProfilePath = "Monsters/Wildcat";
    private const string ForestScene = "Forest";
    // PlayerMapBounds2D keeps the player inside -26.18..26.18 in Forest; keep spawns a little inside that.
    private static readonly Vector2 ForestRange = new(-25.5f, 25.5f);

    private readonly List<MonsterFollowController> wildcats = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetHook() => SceneManager.sceneLoaded -= OnSceneLoaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InstallHook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded; // Enter Play Mode runs with domain reload disabled
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // One spawner per Forest load, living in the Forest scene so it is removed together with the scene.
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!Application.isPlaying || scene.name != ForestScene) return;
        var host = new GameObject("Wildcat Field Spawner");
        SceneManager.MoveGameObjectToScene(host, scene);
        host.AddComponent<WildcatFieldSpawner>();
    }

    private IEnumerator Start()
    {
        var profile = Resources.Load<MonsterProfile>(ProfilePath);
        if (profile == null)
        {
            Debug.LogWarning("[Wildcat] Resources/" + ProfilePath + " 프로필이 없어 살쾡이를 배치하지 않습니다.", this);
            yield break;
        }
        if (profile.fieldCount <= 0) yield break;

        // Frame 2: StorySceneFlow collects its monsters, clones the gang, places the player and starts the cutscene.
        yield return null;
        yield return null;
        yield return null;

        var anchors = StoryMonsters(false);
        Transform player = FindPlayer();
        if (anchors.Count == 0 || player == null) yield break; // later chapters: story monsters are hidden
        Spawn(profile, anchors, player);

        // Switch the wildcats on when the story switches its monsters on (end of the opening / tutorial hold).
        while (wildcats.Count > 0 && !StoryMonsters(false).Any(m => m.enabled)) yield return new WaitForSeconds(.1f);
        foreach (var cat in wildcats)
            if (cat != null && cat.CurrentHealth > 0) cat.enabled = true;
    }

    private static Transform FindPlayer()
    {
        Transform player = PlayerTargetRegistry.Current;
        if (player != null) return player;
        var health = FindFirstObjectByType<PlayerHealth>();
        return health != null ? health.transform : null;
    }

    // The Forest's own (story) monsters that are present and alive; optionally only those whose AI is on.
    private List<MonsterFollowController> StoryMonsters(bool enabledOnly) => gameObject.scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<MonsterFollowController>(false))
        .Where(m => !m.IsAmbientSpawn && !m.IsDevelopmentSpawn && m.CurrentHealth > 0 && (!enabledOnly || m.enabled))
        .ToList();

    private void Spawn(MonsterProfile profile, List<MonsterFollowController> anchors, Transform player)
    {
        // Build clones under an inactive holder so the profile is applied before their Awake.
        var holder = new GameObject("Wildcat Build (inactive)");
        holder.SetActive(false);
        SceneManager.MoveGameObjectToScene(holder, gameObject.scene);
        var source = anchors[0];
        bool aiOn = anchors.Any(m => m.enabled);
        var used = new List<float>();
        try
        {
            for (int i = 0; i < profile.fieldCount; i++)
            {
                var anchor = anchors[i % anchors.Count];
                float x = PickX(anchor.transform.position.x, player.position.x, profile, used);
                used.Add(x);
                var position = new Vector3(x, anchor.transform.position.y, 0f);
                var cat = Instantiate(source, position, Quaternion.identity, holder.transform);
                cat.name = "Wildcat " + (i + 1);
                // The source already built its hit flash / HP bar children at runtime; each wildcat builds its own.
                foreach (Transform child in cat.transform.Cast<Transform>().ToArray())
                    if (child.name == "White Hit Flash" || child.name == "HP Bar") DestroyImmediate(child.gameObject);
                cat.ApplyProfile(profile, 1f + Random.Range(-profile.speedJitter, profile.speedJitter));
                cat.transform.SetParent(null, true);
                SceneManager.MoveGameObjectToScene(cat.gameObject, gameObject.scene);
                cat.MarkAmbientSpawn();
                cat.BindPlayer(player);
                cat.enabled = aiOn; // idle in place during the cutscene, like the gang
                wildcats.Add(cat);
            }
        }
        finally
        {
            Destroy(holder);
        }
        Debug.Log("[Wildcat] Forest에 " + profile.displayName + " " + wildcats.Count + "마리 배치 (AI " + (aiOn ? "켜짐" : "컷신 후 켜짐") + ")", this);
    }

    // Random spot around a story monster, far from the player and not on top of another wildcat.
    private static float PickX(float anchorX, float playerX, MonsterProfile profile, List<float> used)
    {
        float best = anchorX;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            float x = anchorX + Random.Range(-profile.fieldSpread, profile.fieldSpread);
            float fromPlayer = x - playerX;
            if (Mathf.Abs(fromPlayer) < profile.fieldMinPlayerDistance)
            {
                float side = Mathf.Abs(fromPlayer) > .01f ? Mathf.Sign(fromPlayer) : (anchorX >= playerX ? 1f : -1f);
                x = playerX + side * (profile.fieldMinPlayerDistance + Random.Range(0f, profile.fieldSpread));
            }
            // Too close to the map edge on that side: use the far side of the player instead.
            if (x < ForestRange.x || x > ForestRange.y)
                x = playerX - Mathf.Sign(x - playerX) * (profile.fieldMinPlayerDistance + Random.Range(0f, profile.fieldSpread));
            best = Mathf.Clamp(x, ForestRange.x, ForestRange.y);
            if (used.All(u => Mathf.Abs(u - best) >= profile.spacing)) break;
        }
        return best;
    }
}
