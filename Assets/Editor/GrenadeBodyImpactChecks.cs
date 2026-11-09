using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BehindTheGear.HideOut;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit checks in an isolated physics scene during an existing Play session. No asset/save writes.</summary>
[InitializeOnLoad]
public static class GrenadeBodyImpactChecks
{
    private const string Output = "Temp/GrenadeBodyImpactValidation/";
    private static readonly Vector2 Center = new(2000, 2000);
    private static readonly StringBuilder Report = new();
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static Scene fixture;
    private static GameObject owner;
    private static IEnumerator routine;
    private static Sprite square;
    private static double nextTick, deadline;
    private static int checks;
    private static bool oldBackground;

    static GrenadeBodyImpactChecks() => EditorApplication.update += Tick;

    [MenuItem("Tools/Checks/Grenade Body Impacts (during Play)")]
    public static void Run()
    {
        if (Application.unityVersion != "6000.3.23f1" || EditorApplication.isPaused)
            throw new InvalidOperationException("Use an unpaused Unity 6000.3.23f1 editor.");
        if (!Application.isPlaying)
        {
            SessionState.SetBool("BTG.GrenadeBodyImpact.Pending", true);
            SessionState.SetBool("BTG.GrenadeBodyImpact.StartedPlay", true);
            EditorApplication.isPlaying = true;
            return;
        }
        Directory.CreateDirectory(Output);
        oldBackground = Application.runInBackground;
        Application.runInBackground = true;
        Report.Clear(); checks = 0;
        fixture = SceneManager.CreateScene("Grenade isolated physics checks", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        owner = Create("Fixture owner", Center + Vector2.up * 100);
        square = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.one * .5f, 1);
        routine = Verify(); deadline = EditorApplication.timeSinceStartup + 40;
        File.WriteAllText(Output + "progress.txt", "RUNNING in isolated PhysicsScene2D; existing Play session retained\n");
    }

    private static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (routine == null && Application.isPlaying && SessionState.GetBool("BTG.GrenadeBodyImpact.Pending", false))
        {
            SessionState.SetBool("BTG.GrenadeBodyImpact.Pending", false);
            Run();
        }
        if (routine == null && File.Exists(Output + "request.flag"))
        {
            File.Delete(Output + "request.flag");
            try { Run(); } catch (Exception error) { Directory.CreateDirectory(Output); File.WriteAllText(Output + "result.txt", "FAILED " + error); }
        }
        if (routine == null || EditorApplication.timeSinceStartup < nextTick) return;
        nextTick = EditorApplication.timeSinceStartup + Time.fixedDeltaTime;
        try
        {
            if (!Application.isPlaying || EditorApplication.timeSinceStartup > deadline) throw new Exception("Play ended or checks timed out.");
            foreach (var grenade in Object.FindObjectsByType<PlayerGrenadeProjectile>(FindObjectsSortMode.None))
                if (grenade.gameObject.scene == fixture) typeof(PlayerGrenadeProjectile).GetMethod("FixedUpdate", Private).Invoke(grenade, null);
            fixture.GetPhysicsScene2D().Simulate(Time.fixedDeltaTime);
            CollectEffects();
            if (!routine.MoveNext()) Finish(null);
        }
        catch (Exception error) { Finish(error); }
        EditorApplication.QueuePlayerLoopUpdate();
    }

    private static void CollectEffects()
    {
        foreach (var pulse in Object.FindObjectsByType<PlayerSkillPulse>(FindObjectsSortMode.None))
            if (Vector2.Distance(pulse.transform.position, Center) < 50 && pulse.gameObject.scene != fixture)
                SceneManager.MoveGameObjectToScene(pulse.gameObject, fixture);
    }

    private static void Finish(Exception error)
    {
        routine = null;
        if (error != null) Report.AppendLine("FAIL " + error);
        Report.AppendLine($"Unity {Application.unityVersion}; {checks} checks; {(error == null ? "PASSED" : "FAILED")}");
        File.WriteAllText(Output + "result.txt", Report.ToString());
        if (fixture.IsValid() && fixture.isLoaded) SceneManager.UnloadSceneAsync(fixture);
        if (square != null) Object.Destroy(square);
        Application.runInBackground = oldBackground;
        if (SessionState.GetBool("BTG.GrenadeBodyImpact.StartedPlay", false))
        {
            SessionState.SetBool("BTG.GrenadeBodyImpact.StartedPlay", false);
            EditorApplication.isPlaying = false;
        }
    }

    private static GameObject Create(string label, Vector2 position)
    {
        var go = new GameObject(label);
        SceneManager.MoveGameObjectToScene(go, fixture);
        go.transform.position = position;
        return go;
    }
    private static void Set(object instance, string field, object value) => instance.GetType().GetField(field, Private).SetValue(instance, value);
    private static bool Exploded(PlayerGrenadeProjectile grenade) => grenade == null || (bool)typeof(PlayerGrenadeProjectile).GetField("exploded", Private).GetValue(grenade);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++; Report.AppendLine("PASS " + message);
        File.WriteAllText(Output + "progress.txt", Report.ToString());
    }
    private static PlayerGrenadeProjectile Throw(Vector2 offset, Vector2 velocity, float fuse = 3, GameObject thrower = null)
        => PlayerGrenadeProjectile.Launch(Center + offset, velocity, 0, fuse, 24, 2.4f, 15, 1.6f, thrower != null ? thrower : owner);
    private static BoxCollider2D Box(GameObject go, Vector2 size)
    {
        var box = go.AddComponent<BoxCollider2D>(); box.size = size; return box;
    }
    private static void Clear()
    {
        CollectEffects();
        foreach (var root in fixture.GetRootGameObjects()) if (root != owner)
        {
            root.SetActive(false);
            Object.Destroy(root);
        }
    }

    private static IEnumerator Verify()
    {
        var gang = AssetDatabase.LoadAllAssetsAtPath("Assets/!Animation/Monster/SpriteSheet/GangB_Idle.png").OfType<Sprite>().First();
        Check(gang.GetPhysicsShapeCount() > 0, "Real monster sprite has an imported physics outline (no texture pixel reads).");
        var monsterGo = Create("Monster parent", Center); monsterGo.SetActive(false);
        monsterGo.layer = LayerMask.NameToLayer("Monster");
        var rb = monsterGo.AddComponent<Rigidbody2D>(); rb.gravityScale = 0; rb.constraints = RigidbodyConstraints2D.FreezeAll;
        monsterGo.AddComponent<SpriteRenderer>().sprite = gang;
        var child = Create("Movement child", Center); child.transform.SetParent(monsterGo.transform, true); child.layer = LayerMask.NameToLayer("Ground");
        var movementBox = Box(child, Vector2.one * 12);
        var monster = monsterGo.AddComponent<MonsterFollowController>(); Set(monster, "maxHealth", 100); Set(monster, "moveSpeed", 0f); Set(monster, "patrolRadius", 0f);
        monsterGo.SetActive(true);
        var duplicate = Box(monsterGo, Vector2.one * 12);
        var sensor = Create("Recognition child", Center); sensor.transform.SetParent(monsterGo.transform, true);
        Box(sensor, Vector2.one * 30).isTrigger = true; sensor.AddComponent<MonsterRangeTrigger2D>();
        var otherWeapon = Create("Other weapon collider", Center + Vector2.up * 20).AddComponent<CircleCollider2D>();
        Physics2D.SyncTransforms();
        var grenade = Throw(new Vector2(-8, 0), Vector2.right * 50);
        yield return null; yield return null;
        Check(!Exploded(grenade) && grenade.GetComponent<Rigidbody2D>().linearVelocity.x > 0,
            "Oversized movement BoxCollider and recognition sensor neither bounce nor prematurely detonate the grenade.");
        for (int i = 0; i < 25 && !Exploded(grenade); i++) yield return null;
        Check(Exploded(grenade) && monster.CurrentHealth == 76, "Real monster sprite body detonates; child Ground collider resolves the parent; duplicate colliders deal exactly 24 once.");
        Check(movementBox.size == Vector2.one * 12 && duplicate.enabled && !Physics2D.GetIgnoreCollision(movementBox, otherWeapon),
            "Movement boxes, monster component and unrelated weapon collision pairs remain intact.");
        Clear(); yield return null; yield return null;

        var prop = Create("Interactable box parent", Center); prop.AddComponent<Interactable2D>();
        prop.AddComponent<SpriteRenderer>().sprite = square;
        var box = Box(prop, Vector2.one * 8);
        var receiver = prop.AddComponent<HideOutDummy>();
        var second = Create("Duplicate child", Center); second.transform.SetParent(prop.transform, true); Box(second, Vector2.one * 8);
        grenade = Throw(new Vector2(-3, 1.5f), Vector2.right * 50, .3f);
        for (int i = 0; i < 4; i++) yield return null;
        Check(!Exploded(grenade) && receiver.Hits == 0, "A path through transparent space inside an oversized BoxCollider misses the visible sprite.");
        Object.Destroy(grenade.gameObject); yield return null;
        var beforeEffects = new HashSet<PlayerSkillPulse>(Object.FindObjectsByType<PlayerSkillPulse>(FindObjectsSortMode.None));
        grenade = Throw(new Vector2(-3, 0), Vector2.right * 250);
        typeof(PlayerGrenadeProjectile).GetMethod("FixedUpdate", Private).Invoke(grenade, null);
        Check(Exploded(grenade), "A fast swept impact detonates before any explicit explosion/timer call or physics reflection.");
        grenade.Explode();
        typeof(PlayerGrenadeProjectile).GetMethod("Update", Private).Invoke(grenade, null);
        CollectEffects();
        Check(Exploded(grenade) && receiver.Hits == 1 && receiver.LastDamage == 24,
            "Fast direct prop impact uses existing explosion damage once despite repeated explosion and timer calls.");
        int createdEffects = Object.FindObjectsByType<PlayerSkillPulse>(FindObjectsSortMode.None).Count(pulse => !beforeEffects.Contains(pulse));
        Check(createdEffects == 2,
            "One explosion creates exactly the existing two pulse effects, without duplicate effects.");
        Clear(); yield return null; yield return null;

        prop = Create("Authored polygon prop", Center); prop.AddComponent<Interactable2D>();
        prop.AddComponent<SpriteRenderer>().sprite = square; prop.transform.localScale = Vector3.one * 3;
        Box(prop, Vector2.one * 8);
        var polygon = prop.AddComponent<PolygonCollider2D>(); polygon.points = new[] { new Vector2(-.1f, -.1f), new Vector2(.1f, -.1f), new Vector2(0, .1f) };
        grenade = Throw(new Vector2(-3, 1), Vector2.right * 50);
        for (int i = 0; i < 4; i++) yield return null;
        Check(!Exploded(grenade), "Existing authored PolygonCollider2D takes priority over movement boxes and sprite fallback.");
        Clear(); yield return null; yield return null;

        var player = Create("Player body", Center); player.SetActive(false);
        player.layer = LayerMask.NameToLayer("Player");
        player.AddComponent<SpriteRenderer>().sprite = square;
        Box(player, Vector2.one * 4);
        var control = player.AddComponent<PlayerControlLock>(); JsonUtility.FromJsonOverwrite("{\"controlsToDisable\":[]}", control);
        var health = player.AddComponent<PlayerHealth>(); Set(health, "maxHealth", 100);
        player.SetActive(true);
        int damaged = 0; health.Damaged += () => damaged++;
        grenade = Throw(new Vector2(-3, 0), Vector2.right * 100);
        for (int i = 0; i < 10 && !Exploded(grenade); i++) yield return null;
        Check(Exploded(grenade) && health.CurrentHealth == 96 && damaged == 1,
            "Player sprite impact detonates once and preserves existing self/friendly damage (15 power minus 11 defense).");
        Clear(); yield return null; yield return null;

        player = Create("Thrower", Center); player.layer = LayerMask.NameToLayer("Player");
        player.AddComponent<SpriteRenderer>().sprite = square; Box(player, Vector2.one);
        grenade = Throw(Vector2.zero, Vector2.right * 30, 3, player);
        var ownerCollider = player.GetComponent<Collider2D>();
        Check(grenade.GetComponent<Collider2D>().Distance(ownerCollider).isOverlapped,
            "New grenade physics position matches its spawn Transform before the first physics step.");
        yield return null;
        Check(!Exploded(grenade), "Spawn overlap with the thrower does not immediately explode. " +
            (grenade == null ? "Grenade destroyed" : $"body={grenade.GetComponent<Rigidbody2D>().position}, owner distance={grenade.GetComponent<Collider2D>().Distance(ownerCollider).distance}"));
        for (int i = 0; i < 3; i++) yield return null;
        grenade.GetComponent<Rigidbody2D>().linearVelocity = Vector2.left * 30;
        for (int i = 0; i < 20 && !Exploded(grenade); i++) yield return null;
        Check(Exploded(grenade), "Returning to the thrower after separation detonates against the visible body.");
        Clear(); yield return null; yield return null;

        player = Create("Narrow movement box thrower", Center); player.SetActive(false);
        player.layer = LayerMask.NameToLayer("Player");
        Box(player, new Vector2(.9f, 2.15f));
        control = player.AddComponent<PlayerControlLock>(); JsonUtility.FromJsonOverwrite("{\"controlsToDisable\":[]}", control);
        health = player.AddComponent<PlayerHealth>(); Set(health, "maxHealth", 100);
        var playerVisual = Create("Wider child sprite", Center); playerVisual.transform.SetParent(player.transform, true);
        playerVisual.transform.localScale = Vector3.one * 3;
        playerVisual.AddComponent<SpriteRenderer>().sprite = square;
        player.SetActive(true);
        grenade = Throw(new Vector2(.7f, .35f), Vector2.right * 10.5f, 3, player);
        typeof(PlayerGrenadeProjectile).GetMethod("FixedUpdate", Private).Invoke(grenade, null);
        Check(!grenade.GetComponent<Collider2D>().Distance(player.GetComponent<Collider2D>()).isOverlapped && !Exploded(grenade),
            "Clearing a narrow movement box while still inside the thrower's wider sprite does not cause self-detonation.");
        for (int i = 0; i < 9 && !Exploded(grenade); i++) yield return null;
        Check(!Exploded(grenade) && health.CurrentHealth == 100,
            "Normal throw speed exits the wider thrower sprite without an early explosion or self damage.");
        grenade.GetComponent<Rigidbody2D>().linearVelocity = Vector2.left * 30;
        for (int i = 0; i < 20 && !Exploded(grenade); i++) yield return null;
        Check(Exploded(grenade), "The wider sprite becomes a direct-hit target after full separation and return.");
        yield return null;
        grenade = Throw(new Vector2(.7f, .35f), Vector2.right * 10.5f, 3, playerVisual);
        typeof(PlayerGrenadeProjectile).GetMethod("FixedUpdate", Private).Invoke(grenade, null);
        Check(!Exploded(grenade), "A child object passed as the thrower still excludes its parent PlayerHealth and body.");
        Clear(); yield return null; yield return null;

        var playerSprite = AssetDatabase.LoadAllAssetsAtPath("Assets/!Animation/Player/SpriteSheet/Player_Idle.png").OfType<Sprite>().First();
        foreach (float facing in new[] { -1f, 1f }) foreach (float angle in new[] { 20f, 75f })
        {
            player = Create("Actual player sprite thrower", Center); player.SetActive(false);
            player.layer = LayerMask.NameToLayer("Player");
            var renderer = player.AddComponent<SpriteRenderer>(); renderer.sprite = playerSprite; renderer.flipX = facing < 0;
            Box(player, new Vector2(.9f, 2.15f));
            control = player.AddComponent<PlayerControlLock>(); JsonUtility.FromJsonOverwrite("{\"controlsToDisable\":[]}", control);
            health = player.AddComponent<PlayerHealth>(); Set(health, "maxHealth", 100); player.SetActive(true);
            float radians = angle * Mathf.Deg2Rad;
            grenade = Throw(Vector2.up * .35f, new Vector2(Mathf.Cos(radians) * facing, Mathf.Sin(radians)) * 10.5f, 1.6f, player);
            grenade.GetComponent<Rigidbody2D>().gravityScale = 1.6f;
            for (int i = 0; i < 10 && !Exploded(grenade); i++) yield return null;
            Check(!Exploded(grenade) && health.CurrentHealth == 100,
                $"Real player sprite and original throw origin/speed/gravity: facing {facing}, angle {angle} exits without self-detonation.");
            Clear(); yield return null; yield return null;
        }

        var terrain = Create("Terrain wall", Center); terrain.layer = LayerMask.NameToLayer("Ground");
        Box(terrain, new Vector2(.4f, 8));
        grenade = Throw(new Vector2(-2, 0), Vector2.right * 20, .5f);
        for (int i = 0; i < 7 && !Exploded(grenade); i++) yield return null;
        Check(!Exploded(grenade) && grenade.GetComponent<Rigidbody2D>().linearVelocity.x < 0, "Ground wall reflects the physical grenade without impact detonation.");
        for (int i = 0; i < 60 && !Exploded(grenade); i++) yield return null;
        Check(Exploded(grenade), "The original fuse still detonates after a terrain bounce.");
        Clear(); yield return null; yield return null;

        terrain = Create("Terrain floor", Center + Vector2.down * 2); terrain.layer = LayerMask.NameToLayer("Ground");
        Box(terrain, new Vector2(20, .4f));
        grenade = Throw(Vector2.zero, Vector2.down * 20, .5f); grenade.GetComponent<Rigidbody2D>().gravityScale = 1.6f;
        for (int i = 0; i < 7 && !Exploded(grenade); i++) yield return null;
        Check(!Exploded(grenade) && grenade.GetComponent<Rigidbody2D>().linearVelocity.y > 0, "Ground floor bounces the falling grenade upward without impact detonation.");
        for (int i = 0; i < 60 && !Exploded(grenade); i++) yield return null;
        Check(Exploded(grenade), "The original fuse still detonates after a floor bounce.");
        Clear(); yield return null; yield return null;

        terrain = Create("Blocking terrain wall", Center); terrain.layer = LayerMask.NameToLayer("Ground"); Box(terrain, new Vector2(.4f, 8));
        prop = Create("Target behind wall", Center + Vector2.right * 2); prop.AddComponent<Interactable2D>();
        prop.AddComponent<SpriteRenderer>().sprite = square; Box(prop, Vector2.one);
        grenade = Throw(new Vector2(-2, 0), Vector2.right * 250);
        typeof(PlayerGrenadeProjectile).GetMethod("FixedUpdate", Private).Invoke(grenade, null);
        Check(!Exploded(grenade), "A high speed sweep does not detonate against a target behind blocking terrain.");
        Clear(); yield return null; yield return null;

        terrain = Create("Floor under moving grenade", Center + Vector2.down * .34f); terrain.layer = LayerMask.NameToLayer("Ground"); Box(terrain, new Vector2(20, .4f));
        prop = Create("Target beside floor", Center + Vector2.right * 2); prop.AddComponent<Interactable2D>();
        prop.AddComponent<SpriteRenderer>().sprite = square; Box(prop, Vector2.one);
        grenade = Throw(Vector2.zero, Vector2.right * 100); grenade.GetComponent<Rigidbody2D>().gravityScale = 1.6f;
        typeof(PlayerGrenadeProjectile).GetMethod("FixedUpdate", Private).Invoke(grenade, null);
        Check(Exploded(grenade), "Touching the floor does not hide a direct target beside the grenade.");
        Clear(); yield return null; yield return null;

        grenade = Throw(Vector2.zero, Vector2.zero, .15f);
        for (int i = 0; i < 40 && !Exploded(grenade); i++) yield return null;
        Check(Exploded(grenade), "An airborne grenade with no contact detonates on its original fuse.");
    }
}
