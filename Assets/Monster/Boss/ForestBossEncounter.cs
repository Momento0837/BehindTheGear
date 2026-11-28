using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// The Forest boss encounter from start to finish. StorySceneFlow.FinishForestCombat runs it once the story monsters
/// are dead: it waits for the rest of the field (wildcats), plays the entrance, runs the fight and returns when the
/// boss has been defeated and its rewards have dropped. The closing monologue and the portal follow as before.
/// </summary>
public static class ForestBossEncounter
{
    private const int GroundLayer = 6;
    private static PlayerMapBounds2D fenced;
    private static bool fencedHadBounds;
    private static Vector2 fencedPrevious;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() // domain reload is off in this project
    {
        fenced = null;
        Engaged = Defeated = false;
        retryUntil = 0f;
        skipRequested = false;
    }

    private static bool skipRequested;

    /// <summary>
    /// Testing (editor key 0): ends the encounter that is running now, at any point: while it waits for the field,
    /// during the entrance or in the fight. The boss is removed without rewards and the story carries on as if it had
    /// been beaten. Does nothing when no encounter is running.
    /// </summary>
    public static bool RequestSkip()
    {
        bool running = BossGoliath.Active != null || Object.FindAnyObjectByType<BossEncounterRunner>() != null || storyRunActive;
        if (!running) return false;
        skipRequested = true;
        if (BossGoliath.Active != null) Object.Destroy(BossGoliath.Active.gameObject); // its OnDestroy hands back time, arena and camera
        return true;
    }

    private static bool storyRunActive;

    /// <summary>The boss has shown itself in this Forest run (its full entrance is not replayed).</summary>
    public static bool Engaged { get; private set; }
    /// <summary>The boss has been beaten in this Forest run (it does not come back).</summary>
    public static bool Defeated { get; private set; }
    private static float retryUntil;

    /// <summary>A new Forest run: the boss is waiting again.</summary>
    public static void ResetProgress() => Engaged = Defeated = false;

    /// <summary>Progress from a save (bed save or a visit to the base).</summary>
    public static void RestoreProgress(bool engaged, bool defeated) { Engaged = engaged; Defeated = defeated; }

    /// <summary>The player died to the boss: the Forest reload that follows should resume at the boss.</summary>
    public static void MarkRetry() => retryUntil = Time.realtimeSinceStartup + 45f; // only that reload, not a later new game

    /// <summary>The coming Forest load is a retry of the boss (asks without using it up).</summary>
    public static bool RetryPending => Engaged && !Defeated && Time.realtimeSinceStartup < retryUntil;

    /// <summary>True once, on the Forest reload after dying to the boss.</summary>
    public static bool ConsumeRetry()
    {
        bool retry = Engaged && !Defeated && Time.realtimeSinceStartup < retryUntil;
        retryUntil = 0f;
        return retry;
    }

    // Keeps the player inside the arena for the fight, remembering what to give back.
    private static void FenceArena(PlayerMapBounds2D bounds, Vector3 centre, float halfWidth)
    {
        if (bounds == null) return;
        if (fenced == null) { fenced = bounds; fencedHadBounds = bounds.HasHorizontalBounds; fencedPrevious = bounds.HorizontalBounds; }
        bounds.SetHorizontalBounds(new Vector2(centre.x - halfWidth, centre.x + halfWidth));
    }

    /// <summary>Re-centres the fence on the boss (it walks in phase 2). Never reaches past the map's own bounds.</summary>
    public static void MoveArena(float centreX, float halfWidth)
    {
        if (fenced == null) return;
        float left = centreX - halfWidth, right = centreX + halfWidth;
        if (fencedHadBounds) { left = Mathf.Max(left, fencedPrevious.x); right = Mathf.Min(right, fencedPrevious.y); }
        var current = fenced.HorizontalBounds;
        if (Mathf.Approximately(current.x, left) && Mathf.Approximately(current.y, right)) return;
        fenced.SetHorizontalBounds(new Vector2(left, right));
    }

    /// <summary>Gives the player their own map bounds back. Safe to call more than once.</summary>
    public static void RestoreArena()
    {
        if (fenced == null) return;
        if (fencedHadBounds) fenced.SetHorizontalBounds(fencedPrevious); else fenced.ClearHorizontalBounds();
        fenced = null;
    }

    /// <summary>The whole encounter, for the story flow. Ends at once when the boss cannot be created.</summary>
    public static IEnumerator Run()
    {
        skipRequested = false; // a press from before this encounter does not count
        storyRunActive = true;
        try { yield return Interruptible(RunEncounter()); }
        finally { storyRunActive = false; }
        if (skipRequested)
        {
            skipRequested = false;
            if (BossGoliath.Active != null) Object.Destroy(BossGoliath.Active.gameObject);
            RestoreArena();
            Engaged = Defeated = true; // skipped = treated as beaten: the monologue and the portal follow
            Debug.Log("[Boss] Encounter skipped (testing key).");
        }
    }

    private static IEnumerator RunEncounter()
    {
        if (Defeated) yield break; // already beaten in this run (restored save): straight to the monologue
        if (Engaged)
        {
            // Seen before (died to it, or saved mid-fight): no second wait, no full entrance. Whatever else is
            // standing in the field (respawned wildcats) is cleared so the duel is not interrupted.
            ClearField();
            yield return new WaitForSeconds(.6f);
            ClearField(); // anything that was placed in the meantime
            yield return Play(EntranceKind.Short);
            yield break;
        }
        // Every other enemy first: the ambient wildcats are not story monsters, so the story flow does not wait for them.
        while (BehindTheGear.HideOut.HideOutTravel.IsVisiting || AnyMonsterAlive()) yield return new WaitForSeconds(.25f);
        yield return new WaitForSeconds(1f); // let the last drops land
        yield return Play(EntranceKind.Full);
    }

    public enum EntranceKind { None, Short, Full }

    /// <summary>Entrance + fight + defeat. Also used by the editor shortcut.</summary>
    public static IEnumerator Play(EntranceKind entrance)
    {
        var profile = Resources.Load<BossProfile>(BossProfile.ResourcePath);
        if (profile == null) { Debug.LogError("[Boss] Resources/" + BossProfile.ResourcePath + " is missing."); yield break; }
        if (!TryFindGround(profile.arenaCenterX, out float groundY))
        { Debug.LogError("[Boss] No Ground-layer collider under the arena centre (x = " + profile.arenaCenterX + ")."); yield break; }
        // The ground collider is a fraction of a pixel above the painted ground: lower the boss so its feet touch it.
        Vector3 centre = new(profile.arenaCenterX, groundY - profile.groundSinkPixels / BossSpriteLayout.PixelsPerUnit, 0f);

        var playerRoot = PlayerRoot();
        var controls = playerRoot != null ? playerRoot.GetComponent<PlayerControlLock>() : null;
        var bounds = playerRoot != null ? playerRoot.GetComponent<PlayerMapBounds2D>() : null;
        // Same as StorySceneFlow.PlacePlayer: the fence component is added when the player does not have one yet.
        if (bounds == null && playerRoot != null && playerRoot.GetComponent<BoxCollider2D>() != null && playerRoot.GetComponent<Rigidbody2D>() != null)
            bounds = playerRoot.gameObject.AddComponent<PlayerMapBounds2D>();

        BossGoliath boss;
        if (entrance != EntranceKind.None)
        {
            // The player only watches during the entrance. The lock is always given back, whatever happens.
            controls?.Acquire();
            try
            {
                if (playerRoot != null) playerRoot.GetComponent<InventoryUI>()?.CloseForStoryDialogue();
                bool full = entrance == EntranceKind.Full;
                boss = BossGoliath.Spawn(profile, full ? centre + Vector3.right * profile.entranceWalkDistance : centre);
                boss.HomeX = centre.x;
                Engaged = true;
                yield return full ? Entrance(boss, profile, centre, bounds) : ShortEntrance(boss, profile, centre, bounds);
            }
            finally { controls?.Release(); }
        }
        else
        {
            boss = BossGoliath.Spawn(profile, centre);
            FenceArena(bounds, centre, profile.arenaHalfWidth);
            boss.StartCoroutine(boss.CameraDirector.Hold(centre.x, .6f));
        }
        if (boss == null) yield break;

        var director = boss.CameraDirector;
        director.TrackX = () => boss != null ? boss.transform.position.x : centre.x; // phase 2 replaces this
        boss.ShowHealthBar();
        boss.Audio.StartMusic(1f);
        boss.BeginFight();
        Engaged = true;
        while (boss != null && !boss.DefeatFinished) yield return null;
        if (boss != null) Defeated = true;

        // Hand the field back: the player's own bounds and the follow camera.
        RestoreArena();
        if (boss != null)
        {
            // Back to the normal lens first (phase 2 widened it), then the follow camera takes over again.
            director.ZoomTo(1f, .8f);
            yield return new WaitForSecondsRealtime(.85f);
            director.Release();
        }
        yield return new WaitForSeconds(.4f);
    }

    // Distant footsteps -> letterbox and pan -> a dark shape walks in -> it lights up, whistles, pounds the ground ->
    // title card -> the bars leave. About nine seconds.
    private static IEnumerator Entrance(BossGoliath boss, BossProfile profile, Vector3 centre, PlayerMapBounds2D bounds)
    {
        boss.Invulnerable = true;
        boss.SetDormant(true);
        boss.Rig.SetTint(new Color(.2f, .2f, .2f, 1f)); // a silhouette until it steps into the clearing
        var view = Camera.main;

        // 1. Three footsteps from behind the trees on the right, each closer: dust kicks up at the right edge of the
        //    view, a little further in each time, and drifts away from where the boss is coming from.
        for (int i = 0; i < 3; i++)
        {
            boss.Audio.Play(profile.sfxThud, .45f + .2f * i);
            boss.CameraDirector.Shake(.05f + .05f * i, .4f);
            if (view != null)
            {
                float edge = view.transform.position.x + BossCameraDirector.VisibleHalfHeight(view) * view.aspect;
                float lift = BossSpriteLayout.FxDustSize.y * .5f / BossSpriteLayout.PixelsPerUnit;
                for (int d = 0; d <= i; d++)
                    BossFx.Play(boss.Rig, boss.Rig.Dust, new Vector3(edge - .6f - d * 1.1f, centre.y + lift, 0f), .14f, profile.fxSortingOrder,
                        new Vector2(-1.4f - .4f * d, .6f));
            }
            yield return new WaitForSeconds(.8f);
        }

        // 2. Letterbox, and the camera settles on the clearing where the fight will be.
        boss.StartCoroutine(boss.Overlay.Letterbox(true, .6f));
        FenceArena(bounds, centre, profile.arenaHalfWidth);
        yield return boss.CameraDirector.Hold(centre.x, 1f);

        // 3. The Goliath walks out of the trees: foot up, foot down, the boiler rocking over each planted foot.
        yield return boss.WalkTo(centre.x, profile.entranceWalkSpeed, footfall: .7f);
        boss.Footfall(1f);
        yield return new WaitForSeconds(.25f);

        // 4. In the open: the colours come back, the furnace lights, the gear starts turning.
        for (float t = 0f; t < .4f; t += Time.deltaTime)
        {
            float c = Mathf.Lerp(.2f, 1f, t / .4f);
            boss.Rig.SetTint(new Color(c, c, c, 1f));
            yield return null;
        }
        boss.Rig.SetTint(Color.white);
        boss.SetDormant(false);

        // 5. Steam whistle, then the fists run out of their drums and pound the ground.
        boss.Audio.Play(profile.sfxWhistle);
        boss.Audio.Play(profile.sfxSteam, .7f);
        yield return new WaitForSeconds(.6f);
        yield return boss.EntranceSlam();

        // 6. Title card, then the bars leave and the fight is on.
        yield return boss.Overlay.ShowTitle(profile.title, profile.displayName, profile.titleHoldSeconds);
        yield return boss.Overlay.Letterbox(false, .5f);
        boss.Invulnerable = false;
    }

    // Retry / continue: the boss is already in the clearing. Bars in, one pound of the fists, bars out (about 2.5 s).
    private static IEnumerator ShortEntrance(BossGoliath boss, BossProfile profile, Vector3 centre, PlayerMapBounds2D bounds)
    {
        boss.Invulnerable = true;
        FenceArena(bounds, centre, profile.arenaHalfWidth);
        boss.StartCoroutine(boss.Overlay.Letterbox(true, .4f));
        yield return boss.CameraDirector.Hold(centre.x, .6f);
        boss.Audio.Play(profile.sfxWhistle, .8f);
        yield return boss.EntranceSlam();
        yield return boss.Overlay.Letterbox(false, .4f);
        boss.Invulnerable = false;
    }

    /// <summary>Editor/testing: runs the encounter on its own object (no story flow needed).</summary>
    public static void PlayStandalone(EntranceKind entrance)
    {
        if (!Application.isPlaying) return;
        if (BossGoliath.Active != null) Object.Destroy(BossGoliath.Active.gameObject);
        foreach (var old in Object.FindObjectsByType<BossEncounterRunner>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
        skipRequested = false;
        var runner = new GameObject("Boss Encounter Runner").AddComponent<BossEncounterRunner>();
        runner.StartCoroutine(runner.RunThenRemove(Interruptible(Play(entrance))));
    }

    // Runs `root` and every coroutine nested in it (yield return IEnumerator) one step at a time, so a skip can stop
    // it between any two steps, even in the middle of the entrance. Stopping disposes the iterators, which runs their
    // finally blocks (the player's control lock is given back).
    private static IEnumerator Interruptible(IEnumerator root)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(root);
        try
        {
            while (stack.Count > 0)
            {
                if (skipRequested) yield break;
                var top = stack.Peek();
                if (!top.MoveNext()) { stack.Pop(); continue; }
                if (top.Current is IEnumerator nested) { stack.Push(nested); continue; }
                yield return top.Current;
            }
        }
        finally
        {
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
        }
    }

    private static Transform PlayerRoot()
    {
        var player = PlayerTargetRegistry.Current;
        if (player == null) return null;
        var health = player.GetComponentInParent<PlayerHealth>();
        if (health == null) health = player.GetComponentInChildren<PlayerHealth>();
        return health != null ? health.transform : player;
    }

    private static void ClearField()
    {
        foreach (var monster in Object.FindObjectsByType<MonsterFollowController>(FindObjectsSortMode.None))
            if (monster.CurrentHealth > 0 && !monster.IsDevelopmentSpawn) monster.gameObject.SetActive(false);
    }

    private static bool AnyMonsterAlive()
    {
        foreach (var monster in Object.FindObjectsByType<MonsterFollowController>(FindObjectsSortMode.None))
            if (monster.CurrentHealth > 0 && !monster.IsDevelopmentSpawn) return true;
        return false;
    }

    // The ground line is measured, not assumed: cast down from above the playfield onto the Ground layer.
    private static bool TryFindGround(float x, out float y)
    {
        var hit = Physics2D.Raycast(new Vector2(x, 20f), Vector2.down, 60f, 1 << GroundLayer);
        y = hit.collider != null ? hit.point.y : 0f;
        return hit.collider != null;
    }
}

/// <summary>Coroutine host for an encounter started outside the story flow (editor shortcut).</summary>
public sealed class BossEncounterRunner : MonoBehaviour
{
    public IEnumerator RunThenRemove(IEnumerator encounter)
    {
        yield return encounter;
        Destroy(gameObject);
    }
}
