#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Opt-in isolated batch physics regression check. Never saves scenes or save data.
[InitializeOnLoad]
public static class WorldItemPhysicsChecks
{
    private const string Folder = "Logs/Validation/WorldItemPhysics";
    private const string BatchKey = "BTG.WorldItemPhysicsChecks.BatchPending";
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo FixedTick = typeof(WorldItemPickup).GetMethod("FixedUpdate", Fields);
    static WorldItemPhysicsChecks() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (!Application.isBatchMode || !SessionState.GetBool(BatchKey, false)) return;
        if (EditorApplication.isCompiling || !EditorApplication.isPlaying) return;
        SessionState.EraseBool(BatchKey);
        bool success = RunChecks();
        EditorApplication.Exit(success ? 0 : 1);
    }

    // Invoke with -batchmode -executeMethod WorldItemPhysicsChecks.RunBatch (without -quit).
    // The batch process owns its disposable empty scene; the user's editor is never changed.
    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("World item physics checks require a separate Unity batch process.");
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start the batch check outside Play Mode.");
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/check-result.txt", "RUNNING: waiting for the isolated batch Play Mode check.\n");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(BatchKey, true);
        EditorApplication.EnterPlaymode();
    }

    // Compatibility entry point for existing batch commands.
    public static void Run() => RunBatch();

    private static bool RunChecks()
    {
        Directory.CreateDirectory(Folder);
        var report = new StringBuilder();
        Scene testScene = default;
        Scene originalActiveScene = SceneManager.GetActiveScene();
        bool success = false;
        InventoryItemDefinition quest = null;
        try
        {
            Require(Application.unityVersion == "6000.3.23f1", "Run in the project's Unity 6000.3.23f1 editor.");
            Require(!SceneFadeTransition.IsRunning, "A live scene fade must not be running.");
            testScene = SceneManager.CreateScene("World Item Physics Check", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            SceneManager.SetActiveScene(testScene);
            PhysicsScene2D physics = testScene.GetPhysicsScene2D();
            Require(physics.IsValid() && physics != Physics2D.defaultPhysicsScene, "Checks must use an isolated local physics scene.");
            var floor = NewObject(testScene, "Check Floor", new Vector2(0, -.5f)).AddComponent<BoxCollider2D>();
            floor.gameObject.layer = LayerMask.NameToLayer("Ground");
            floor.size = new Vector2(500, 1);
            string[] paths = { "Story/RedCloth", "Story/TutorialRecoveryPotion", "Story/TutorialGear" };
            var drops = new WorldItemPickup[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                var definition = Resources.Load<InventoryItemDefinition>(paths[i]);
                Require(definition != null, "Missing asset: " + paths[i]);
                drops[i] = NewObject(testScene, paths[i], new Vector2((i - 1) * 30, 20)).AddComponent<WorldItemPickup>();
                drops[i].Configure(definition, 1);
                var rigidbody = drops[i].GetComponent<Rigidbody2D>();
                var solid = drops[i].GetComponent<BoxCollider2D>();
                Require(rigidbody.bodyType == RigidbodyType2D.Dynamic && rigidbody.gravityScale > 0, paths[i] + " has gravity.");
                Require(solid != null && !solid.isTrigger && solid.enabled, paths[i] + " has a separate solid collider.");
                Require(drops[i].GetComponent<CircleCollider2D>().isTrigger, "Magnet range stays a trigger.");
                var artwork = definition.ItemId == "red_cloth" ? drops[i].transform.Find("Red Cloth Visual").GetComponent<SpriteRenderer>() : drops[i].GetComponent<SpriteRenderer>();
                Require(solid.size.x > 0 && solid.size.y > 0 && solid.bounds.size.x <= artwork.bounds.size.x + .025f && solid.bounds.size.y <= artwork.bounds.size.y + .025f,
                    paths[i] + " ground collider fits artwork, including the cloth child scale.");
            }
            var overlappingCloth = NewObject(testScene, "Overlapping Cloth Drop", new Vector2(-30, 22)).AddComponent<WorldItemPickup>();
            overlappingCloth.Configure(drops[0].Definition, 1);
            Physics2D.SyncTransforms();
            for (int step = 0; step < 250; step++) physics.Simulate(.02f);
            foreach (var drop in drops)
            {
                var solid = drop.GetComponent<BoxCollider2D>();
                Require(drop.GetComponent<Rigidbody2D>().position.y < 19, drop.Definition.ItemId + " falls from spawn height.");
                Require(Mathf.Abs(solid.bounds.min.y - floor.bounds.max.y) < .08f, drop.Definition.ItemId + " rests on the floor: bottom=" + solid.bounds.min.y);
                report.AppendLine("PASS gravity / landing / visual collider: " + drop.Definition.ItemId + ", size=" + solid.size);
            }

            Require(Mathf.Abs(overlappingCloth.GetComponent<BoxCollider2D>().bounds.min.y - floor.bounds.max.y) < .08f,
                "Overlapping loot settles on the floor instead of stacking on other loot.");
            report.AppendLine("PASS overlapping drops do not form floating piles");

            var inventory = NewObject(testScene, "Check Inventory", new Vector2(1000, 5)).AddComponent<PlayerInventory>();
            var playerColliderA = inventory.gameObject.AddComponent<BoxCollider2D>();
            var playerColliderB = inventory.gameObject.AddComponent<BoxCollider2D>();
            playerColliderA.isTrigger = playerColliderB.isTrigger = true;
            var track = typeof(WorldItemPickup).GetMethod("TrackTarget", Fields);
            var exit = typeof(WorldItemPickup).GetMethod("OnTriggerExit2D", Fields);
            track.Invoke(drops[0], new object[] { playerColliderA });
            track.Invoke(drops[0], new object[] { playerColliderB });
            exit.Invoke(drops[0], new object[] { playerColliderA });
            Require(ReferenceEquals(typeof(WorldItemPickup).GetField("target", Fields).GetValue(drops[0]), inventory),
                "One player collider leaving does not clear another collider still in range.");
            exit.Invoke(drops[0], new object[] { playerColliderB });
            Require(typeof(WorldItemPickup).GetField("target", Fields).GetValue(drops[0]) == null,
                "The last player collider leaving releases the target.");
            report.AppendLine("PASS multiple player colliders do not interrupt attraction");
            WorldItemPickup cloth = drops[0];
            var body = cloth.GetComponent<Rigidbody2D>();
            body.position = new Vector2(0, 5);
            Set(cloth, "target", inventory);
            float dt = Time.fixedDeltaTime;
            float firstSpeed = 0, lastSpeed = 0;
            for (int step = 0; step < Mathf.CeilToInt(1f / dt); step++)
            {
                Vector2 before = body.position;
                FixedTick.Invoke(cloth, null);
                physics.Simulate(dt);
                float speed = Vector2.Distance(before, body.position) / dt;
                if (step == 0) firstSpeed = speed;
                Require(speed >= lastSpeed - .002f && speed <= 8.402f, "Attraction accelerates monotonically without exceeding 1.2x: " + speed);
                lastSpeed = speed;
            }
            Require(Mathf.Abs(firstSpeed - 7f) < .005f && Mathf.Abs(lastSpeed - 8.4f) < .005f, "Attraction starts at 7 and reaches 8.4.");
            Require(body.bodyType == RigidbodyType2D.Kinematic && !cloth.GetComponent<BoxCollider2D>().enabled, "Attraction retains an unobstructed route to the player.");
            report.AppendLine("PASS magnet acceleration: " + firstSpeed + " -> " + lastSpeed + " units/s, capped at 1.2x");

            Set(cloth, "target", null);
            FixedTick.Invoke(cloth, null);
            float releasedY = body.position.y;
            for (int step = 0; step < 5; step++) physics.Simulate(dt);
            Require(body.bodyType == RigidbodyType2D.Dynamic && cloth.GetComponent<BoxCollider2D>().enabled && body.position.y < releasedY, "Lost target restores falling and ground collision.");
            report.AppendLine("PASS released magnet restores gravity and solid collider");

            Set(inventory, "capacity", 1);
            Set(inventory, "slotsPerAttackStat", 0);
            var fullSlot = inventory.GetSlot(0);
            Set(fullSlot, "item", drops[1].Definition);
            Set(fullSlot, "amount", drops[1].Definition.MaxStack);
            Require(!inventory.CanAddAny(cloth.Definition), "Test inventory is full.");
            Set(cloth, "target", inventory);
            FixedTick.Invoke(cloth, null);
            Require(body.bodyType == RigidbodyType2D.Dynamic && cloth.GetComponent<BoxCollider2D>().enabled, "Full inventory does not suspend drops in midair.");
            report.AppendLine("PASS full inventory keeps gravity active");

            quest = ScriptableObject.CreateInstance<InventoryItemDefinition>();
            Set(quest, "category", InventoryItemDefinition.ItemCategory.Quest);
            Set(quest, "icon", drops[0].Definition.Icon);
            var fixedDrop = NewObject(testScene, "Check Quest Pickup", new Vector2(-100, 7)).AddComponent<WorldItemPickup>();
            fixedDrop.Configure(quest, 1);
            var questBody = fixedDrop.GetComponent<Rigidbody2D>();
            Vector2 fixedPosition = questBody.position;
            for (int step = 0; step < 50; step++) physics.Simulate(.02f);
            Require(questBody.bodyType == RigidbodyType2D.Kinematic && Vector2.Distance(fixedPosition, questBody.position) < .001f && !fixedDrop.GetComponent<BoxCollider2D>().enabled,
                "F-interaction quest pickups stay fixed.");
            report.AppendLine("PASS quest interaction pickup stays fixed");
            report.AppendLine("RESULT: PASS");
            success = true;
        }
        catch (Exception error)
        {
            report.AppendLine("RESULT: FAIL\n" + error);
            Debug.LogException(error);
        }
        finally
        {
            if (originalActiveScene.IsValid()) SceneManager.SetActiveScene(originalActiveScene);
            if (testScene.IsValid()) SceneManager.UnloadSceneAsync(testScene);
            if (quest != null) UnityEngine.Object.DestroyImmediate(quest);
            File.WriteAllText(Folder + "/check-result.txt", report.ToString());
        }
        return success;
    }

    private static GameObject NewObject(Scene scene, string name, Vector2 position)
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.position = position;
        return go;
    }
    private static void Set(object value, string field, object data) => value.GetType().GetField(field, Fields).SetValue(value, data);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif