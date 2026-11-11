#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Live-play checks in a disposable local physics scene. Never reloads the user's scene or writes saves.</summary>
[InitializeOnLoad]
public static class PoisonDaggerChecks
{
    private const string Output = "Temp/PoisonDaggerValidation/";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Vector2 Center = new(1000, 1000);
    private static readonly StringBuilder Report = new();
    private static bool running, oldPaused, oldBackground, oldLoaded;
    private static float oldTimeScale;
    private static PlayerControlScheme oldScheme;
    private static Keyboard oldKeyboard;
    private static UnityEngine.Random.State oldRandom;
    private static Scene fixture;
    private static GameObject actor, keyboardObject;
    private static PlayerPoisonDaggerSkill skill;
    private static PoisonDaggerSettings settings;
    private static PlayerInputReader input;
    private static PlayerAttackController actions;
    private static Camera camera;
    private static RenderTexture target;
    private static int checks, errors;
    private static double deadline;
    private static EditorWindow oldFocus;
    static PoisonDaggerChecks() => EditorApplication.update += Tick;
    private static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || running || !File.Exists(Output + "request.flag")) return;
        File.Delete(Output + "request.flag");
        if (!Application.isPlaying || Application.unityVersion != "6000.3.23f1")
        { File.WriteAllText(Output + "result.txt", "FAIL Requires existing live Unity 6000.3.23f1 Play session."); return; }
        if (Object.FindObjectsByType<PlayerInputReader>(FindObjectsSortMode.None).Any(p => p.GetComponent<Rigidbody2D>()?.simulated == true))
        { File.WriteAllText(Output + "result.txt", "FAIL Run while the actual player is suspended in HideOut, so test input cannot move the user's player."); return; }
        running = true; checks = errors = 0; Report.Clear(); deadline = EditorApplication.timeSinceStartup + 100;
        oldPaused = EditorApplication.isPaused; oldBackground = Application.runInBackground; oldTimeScale = Time.timeScale;
        oldFocus = EditorWindow.focusedWindow;
        EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView")).Focus();
        oldKeyboard = Keyboard.current; oldRandom = UnityEngine.Random.state;
        oldLoaded = (bool)typeof(PlayerControlSchemeSettings).GetField("loaded", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        oldScheme = PlayerControlSchemeSettings.Current;
        Application.runInBackground = true; Time.timeScale = 1;
        Application.logMessageReceived += Observe; EditorApplication.update += Watchdog;
        try
        {
            settings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PoisonDaggerSettings>(PoisonDaggerAssetSetup.SettingsPath));
            if (settings == null || settings.daggerSprite == null || settings.projectilePrefab == null) throw new Exception("Missing authored skill assets.");
            fixture = SceneManager.CreateScene("Isolated poison dagger checks", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            keyboardObject = new GameObject("Poison checks keyboard"); keyboardObject.AddComponent<PlayReviewKeyboard>();
            Scheme(PlayerControlScheme.WasdMouseQe);
            actor = new GameObject("Poison checks actor"); actor.SetActive(false); SceneManager.MoveGameObjectToScene(actor, fixture);
            actor.transform.position = Center; actor.layer = LayerMask.NameToLayer("Player");
            var body = actor.AddComponent<Rigidbody2D>(); body.gravityScale = 0; body.constraints = RigidbodyConstraints2D.FreezeAll;
            actor.AddComponent<BoxCollider2D>().size = new Vector2(.9f, 2.15f);
            actor.AddComponent<SpriteRenderer>().sprite = AssetDatabase.LoadAllAssetsAtPath("Assets/!Animation/Player/SpriteSheet/Player_Idle.png").OfType<Sprite>().First();
            input = actor.AddComponent<PlayerInputReader>();
            var control = actor.AddComponent<PlayerControlLock>(); JsonUtility.FromJsonOverwrite("{\"controlsToDisable\":[]}", control); Set(control, "playerBody", body);
            actor.AddComponent<PlayerHealth>(); actor.AddComponent<PlayerMeleeAttack>(); actor.AddComponent<PlayerSpriteFacing>();
            actions = actor.AddComponent<PlayerAttackController>(); actor.SetActive(true);
            skill = actor.GetComponent<PlayerPoisonDaggerSkill>(); skill.Settings = settings;
            input.SkillTwoPressed += FollowCamera;
            var cameraObject = new GameObject("Poison checks camera"); SceneManager.MoveGameObjectToScene(cameraObject, fixture);
            camera = cameraObject.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 2; camera.aspect = 1;
            camera.transform.position = new Vector3(Center.x, Center.y, -10); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .055f, .085f); target = new RenderTexture(900, 600, 24); camera.targetTexture = target;
            Physics2D.SyncTransforms();
            skill.StartCoroutine(Simulate()); skill.StartCoroutine(Guard(Verify()));
            EditorApplication.isPaused = false;
        }
        catch (Exception error) { Finish(error); }
    }
    private static void Watchdog()
    {
        if (running && errors > 0) { Finish(new Exception("Runtime error during verification.")); return; }
        if (running && EditorApplication.timeSinceStartup > deadline) Finish(new Exception("Timed out."));
        if (running) EditorApplication.QueuePlayerLoopUpdate();
    }
    private static IEnumerator Simulate()
    {
        while (running) { yield return new WaitForFixedUpdate(); if (fixture.IsValid() && fixture.isLoaded) fixture.GetPhysicsScene2D().Simulate(Time.fixedDeltaTime); }
    }
    private static IEnumerator Guard(IEnumerator routine)
    {
        while (true)
        {
            bool more; try { more = routine.MoveNext(); } catch (Exception error) { Finish(error); yield break; }
            if (!more) break; yield return routine.Current;
        }
        Finish(null);
    }
    private static void Observe(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception) { errors++; Report.AppendLine("RUNTIME " + message + "\n" + stack); } }
    private static void Set(object instance, string field, object value) => instance.GetType().GetField(field, Private).SetValue(instance, value);
    private static void Scheme(PlayerControlScheme scheme)
    {
        typeof(PlayerControlSchemeSettings).GetField("loaded", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, true);
        typeof(PlayerControlSchemeSettings).GetField("current", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, scheme);
    }
    private static void Keys(params Key[] keys) => PlayReviewKeyboard.Held = keys;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Report.AppendLine("PASS " + message); checks++; File.WriteAllText(Output + "progress.txt", Report.ToString());
    }
    private static PlayerPoisonDaggerProjectile[] Shots() => Object.FindObjectsByType<PlayerPoisonDaggerProjectile>(FindObjectsSortMode.None)
        .Where(p => p.gameObject.scene == fixture && !p.IsConsumed).ToArray();
    private static void ClearShots() { foreach (var shot in Shots()) Object.DestroyImmediate(shot.gameObject); }
    private static void Prepare() { ClearShots(); Set(skill, "readyAt", 0f); }
    private static void FollowCamera() { foreach (var shot in Shots()) Set(shot, "gameplayCamera", camera); }
    private static PlayerPoisonDaggerProjectile Launch(Vector2 point, Vector2 direction, int direct = 4, int poison = 1)
    { var shot = PlayerPoisonDaggerProjectile.Launch(point, direction, actor, settings, direct, poison); Set(shot, "gameplayCamera", camera); return shot; }
    private static GameObject Box(string name, Vector2 point, Vector2 size, int layer, bool trigger = false)
    {
        var go = new GameObject(name); go.layer = layer; SceneManager.MoveGameObjectToScene(go, fixture); go.transform.position = point;
        var collider = go.AddComponent<BoxCollider2D>(); collider.size = size; collider.isTrigger = trigger; return go;
    }
    private static MonsterFollowController Monster(Vector2 point, Vector2 size, bool children = false)
    {
        var go = new GameObject("Poison checks monster"); go.SetActive(false); go.layer = 9; SceneManager.MoveGameObjectToScene(go, fixture); go.transform.position = point;
        var body = go.AddComponent<Rigidbody2D>(); body.gravityScale = 0; body.constraints = RigidbodyConstraints2D.FreezeAll;
        var enemy = go.AddComponent<MonsterFollowController>(); Set(enemy, "maxHealth", 1000); Set(enemy, "moveSpeed", 0f);
        if (children)
            for (int i = 0; i < 3; i++) { var child = new GameObject("Hitbox " + i); child.transform.SetParent(go.transform, false); child.layer = 9; child.AddComponent<BoxCollider2D>().size = size; }
        else go.AddComponent<BoxCollider2D>().size = size;
        go.SetActive(true); Physics2D.SyncTransforms(); return enemy;
    }
    private static IEnumerator Verify()
    {
        yield return new WaitForSeconds(.08f);
        Check(settings.cooldown == 7 && settings.projectileSpeed == 12 && settings.maxLifetime == 5, "Authored settings use requested cooldown, speed and lifetime.");
        Check(settings.daggerSprite.texture.width == 1292 && settings.daggerSprite.texture.height == 1217, "Projectile uses the attached original image.");
        var headings = new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down, new Vector2(1,1).normalized, new Vector2(-1,1).normalized, new Vector2(1,-1).normalized, new Vector2(-1,-1).normalized };
        var keySets = new[] { new[]{Key.D}, new[]{Key.A}, new[]{Key.W}, new[]{Key.S}, new[]{Key.D,Key.W}, new[]{Key.A,Key.W}, new[]{Key.D,Key.S}, new[]{Key.A,Key.S} };
        for (int h = 0; h < headings.Length; h++)
        {
            Prepare(); Keys(); yield return new WaitForSeconds(.04f);
            // Direction and E arrive in the same native input frame.
            Keys(keySets[h].Concat(new[]{Key.E}).ToArray()); yield return null; yield return null;
            var shots = Shots(); FollowCamera();
            Check(shots.Length == 3 && shots.All(p => Vector2.Distance(p.Direction, headings[h]) < .001f), "Keyboard direction + E: three normalized parallel shots " + headings[h]
                + $" [count={shots.Length}, aim={input.AimDirection}, canUse={skill.CanUse}, cooldown={skill.CooldownRemaining}, E={Keyboard.current?.eKey.isPressed}, body={actor.GetComponent<Rigidbody2D>().simulated}]");
            var side = new Vector2(-headings[h].y, headings[h].x);
            var middle = shots.OrderByDescending(p => Vector2.Dot((Vector2)p.transform.position - Center, headings[h])).First();
            var flanks = shots.Except(new[]{middle}).ToArray();
            Check(flanks.All(p => Mathf.Abs(Vector2.Dot(middle.transform.position - p.transform.position, headings[h]) - .4f) < .002f)
                && Mathf.Abs(Mathf.Abs(Vector2.Dot(flanks[0].transform.position - flanks[1].transform.position, side)) - .7f) < .002f,
                "Lead .4 / flank spacing .7 rotates with heading " + headings[h]);
            var offsets = shots.Select(p => p.transform.position - middle.transform.position).ToArray();
            var start = middle.transform.position;
            Keys(Key.D); yield return new WaitForSeconds(.12f);
            Check(shots.All(p => p != null) && shots.Select((p,i) => Vector3.Distance(p.transform.position - middle.transform.position, offsets[i]) < .002f).All(v => v)
                && Vector2.Dot(middle.transform.position - start, headings[h]) > .9f, "Flight maintains formation and launch heading after player turns " + headings[h]);
            Check(shots.All(p => Mathf.Abs(Mathf.DeltaAngle(p.transform.Find("Visual").eulerAngles.z + 45, Mathf.Atan2(headings[h].y, headings[h].x)*Mathf.Rad2Deg)) < .01f), "Sprite tip rotation follows heading " + headings[h]);
        }
        Prepare(); Keys(Key.W, Key.A); yield return new WaitForSeconds(.04f); Keys(); yield return new WaitForSeconds(.04f);
        Keys(Key.E); yield return null; yield return null; FollowCamera();
        Check(Shots().Length == 3 && Shots().All(p => Vector2.Distance(p.Direction, new Vector2(-1,1).normalized) < .001f), "Stationary throw remembers last diagonal keyboard facing.");
        Check(skill.CooldownRemaining > 6.8f && !skill.TryThrow(), "Cooldown starts on empty-space launch and rejects immediate reuse.");
        ClearShots(); yield return new WaitForSeconds(7.05f);
        Check(Shots().Length == 0, "Holding E past the full cooldown never repeats or queues a throw.");
        Keys(); yield return new WaitForSeconds(.04f); Keys(Key.E); yield return null; yield return null;
        Check(Shots().Length == 3, "Releasing and pressing E again fires after cooldown.");
        Keys(); Prepare();
        Scheme(PlayerControlScheme.ArrowKeys); yield return new WaitForSeconds(.04f);
        Keys(Key.UpArrow, Key.RightArrow, Key.E); yield return null; yield return null;
        Check(Shots().Length == 3 && Shots().All(p => p.Direction.x > .7f && p.Direction.y > .7f), "Arrow layout supports E and diagonal keyboard aiming.");
        Prepare(); Keys(); yield return new WaitForSeconds(.04f); Keys(Key.V); yield return null; yield return null;
        Check(Shots().Length == 3, "Arrow layout retains V as a single non-duplicated skill alias.");
        Keys(); Prepare(); Scheme(PlayerControlScheme.WasdMouseQe); yield return new WaitForSeconds(.04f);
        Keys(Key.D); yield return new WaitForSeconds(.04f); Keys(); yield return new WaitForSeconds(.04f);

        var enemy = Monster(Center + Vector2.right*2, new Vector2(.15f,.15f), true);
        Check(skill.TryThrow(), "Independent-collision fixture launches all three."); FollowCamera();
        yield return new WaitForSeconds(.12f);
        Check(enemy.CurrentHealth == 996 && Shots().Length == 2, "One dagger hits child colliders once; the two flanks continue independently.");
        ClearShots(); Object.DestroyImmediate(enemy.gameObject);

        enemy = Monster(Center + Vector2.right, new Vector2(4,4));
        var tightSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0,0,Texture2D.whiteTexture.width,Texture2D.whiteTexture.height), Vector2.one*.5f, Texture2D.whiteTexture.width);
        enemy.gameObject.AddComponent<SpriteRenderer>().sprite = tightSprite;
        var outsideBody = Launch(Center + Vector2.up*.9f, Vector2.right);
        yield return new WaitForSeconds(.15f);
        Check(outsideBody != null && enemy.CurrentHealth == 1000, "Oversized movement BoxCollider does not register hits outside the visible sprite body.");
        Launch(Center, Vector2.right); yield return new WaitForSeconds(.05f);
        Check(enemy.CurrentHealth == 996, "The same large BoxCollider target takes damage when the dagger reaches its visible body.");
        ClearShots(); Object.DestroyImmediate(enemy.gameObject); Object.DestroyImmediate(tightSprite);

        enemy = Monster(Center + Vector2.right, new Vector2(.5f, 1.2f), true);
        Launch(Center, Vector2.right); yield return new WaitForSeconds(.1f);
        var poison = enemy.GetComponent<PoisonStatus2D>();
        Check(enemy.CurrentHealth == 996 && poison != null && poison.IsActive, "Direct hit deals four damage once and starts one poison clock.");
        yield return new WaitForSeconds(.6f);
        Check(enemy.CurrentHealth == 996, "No immediate poison tick before one second.");
        yield return new WaitForSeconds(.5f);
        Check(enemy.CurrentHealth == 995, "First poison tick occurs one second after impact.");
        yield return new WaitForSeconds(3.05f);
        Check(enemy.CurrentHealth == 992 && !poison.IsActive, "Single hit delivers exactly four one-second poison ticks and stops.");
        Object.DestroyImmediate(enemy.gameObject);

        enemy = Monster(Center + Vector2.right, new Vector2(.5f,1.2f), true);
        Launch(Center, Vector2.right); yield return new WaitForSeconds(.12f); poison = enemy.GetComponent<PoisonStatus2D>();
        float next = poison.NextTickAt;
        yield return new WaitForSeconds(.45f);
        Launch(Center + Vector2.right, Vector2.right); Launch(Center + Vector2.right, Vector2.right);
        yield return new WaitForSeconds(.06f);
        Check(enemy.CurrentHealth == 988 && enemy.GetComponents<PoisonStatus2D>().Length == 1 && Mathf.Abs(poison.NextTickAt-next) < .001f && poison.Remaining > 3.8f,
            "Three independent direct hits apply; poison refreshes to four seconds with one unchanged tick clock.");
        yield return new WaitForSeconds(.53f);
        Check(enemy.CurrentHealth == 987, "Refreshing poison does not delay its pending tick or stack tick damage.");
        Set(actor.GetComponent<PlayerMeleeAttack>(), "attackDamage", 100);
        yield return new WaitForSeconds(3.55f);
        Check(enemy.CurrentHealth == 984 && !poison.IsActive, "Four original snapshot ticks remain unchanged after attack changes; refreshed poison stops.");
        Object.DestroyImmediate(enemy.gameObject);
        enemy = Monster(Center + Vector2.right, new Vector2(.5f,1.2f));
        Prepare(); Check(skill.TryThrow(), "Attack-snapshot fixture launches."); FollowCamera();
        Set(actor.GetComponent<PlayerMeleeAttack>(), "attackDamage", 10);
        yield return new WaitForSeconds(.13f);
        Check(enemy.CurrentHealth == 880, "All three daggers retain launch attack 100 at 40% despite later attack change.");
        yield return new WaitForSeconds(1.05f);
        Check(enemy.CurrentHealth == 870, "Poison retains launch attack 100 at 10% without stacking.");
        poison = enemy.GetComponent<PoisonStatus2D>(); enemy.gameObject.SetActive(false); yield return null;
        Check(!poison.IsActive, "Deactivating the target stops poison."); Object.DestroyImmediate(enemy.gameObject);
        enemy = Monster(Center + Vector2.right, new Vector2(.5f,1.2f)); Launch(Center, Vector2.right); yield return new WaitForSeconds(.12f);
        poison = enemy.GetComponent<PoisonStatus2D>();
        // Do not invoke real quest/drop death side effects in the user's session.
        Set(enemy, "<CurrentHealth>k__BackingField", 0); yield return null; yield return null;
        Check(!poison.IsActive, "Poison terminates when the existing target health reaches zero."); Object.DestroyImmediate(enemy.gameObject);

        var friendly = Box("Player collider", Center + Vector2.right, new Vector2(.8f,2), 7);
        var sensor = Box("Item sensor", Center + Vector2.right*1.5f, new Vector2(.8f,2), 8, true);
        var shot = Launch(Center, Vector2.right); yield return new WaitForSeconds(.18f);
        Check(shot != null && shot.transform.position.x > Center.x+2, "Player, thrower overlap and item triggers neither take damage nor consume daggers.");
        ClearShots(); Object.DestroyImmediate(friendly); Object.DestroyImmediate(sensor);
        var floor = Box("Ground floor", Center + Vector2.down*1.2f, new Vector2(20,.25f), 6);
        shot = Launch(Center, Vector2.down); yield return new WaitForSeconds(.18f);
        Check(shot != null && shot.transform.position.y < Center.y-2, "Shared Ground layer walkable floor is traversed without a bounce.");
        ClearShots(); Object.DestroyImmediate(floor);
        var wall = Box("Solid wall", Center + Vector2.right, new Vector2(.04f,3), 6); settings.projectileSpeed = 200;
        shot = Launch(Center, Vector2.right); yield return new WaitForSeconds(.05f);
        Check(shot == null, "Swept query hits thin walls even at 200 units per second."); Object.DestroyImmediate(wall);
        enemy = Monster(Center + Vector2.right, new Vector2(.04f,2)); shot = Launch(Center, Vector2.right);
        yield return new WaitForSeconds(.05f); Check(shot == null && enemy.CurrentHealth == 996, "High-speed sweep hits a thin enemy exactly once."); Object.DestroyImmediate(enemy.gameObject);
        settings.projectileSpeed = 12;
        wall = Box("Marked floor", Center + Vector2.right, new Vector2(.2f,2), 8);
        wall.AddComponent<PoisonDaggerSurface2D>().kind = PoisonDaggerSurface2D.SurfaceKind.Floor;
        shot = Launch(Center, Vector2.right); yield return new WaitForSeconds(.13f);
        Check(shot != null, "Explicit floor marker overrides shared obstacle layers."); ClearShots(); Object.DestroyImmediate(wall);

        shot = Launch(Center, Vector2.right); yield return new WaitForSeconds(.25f);
        Check(shot != null && shot.transform.position.x > Center.x+2, "Dagger remains alive just outside camera visibility.");
        yield return new WaitForSeconds(.25f); Check(shot == null, "Camera visibility plus three-unit margin removes a missed dagger.");
        settings.maxLifetime = .15f; shot = Launch(Center, Vector2.right); Set(shot, "gameplayCamera", null);
        yield return new WaitForSeconds(.22f); Check(shot == null, "Lifetime cleanup works without camera or map bounds."); settings.maxLifetime = 5;
        var bounds = actor.AddComponent<PlayerMapBounds2D>(); bounds.SetHorizontalBounds(new Vector2(Center.x-2,Center.x+2));
        shot = Launch(Center, Vector2.right); Set(shot,"gameplayCamera",null); yield return new WaitForSeconds(.25f);
        Check(shot == null, "Existing horizontal map bounds remove missed daggers."); Object.DestroyImmediate(bounds);

        Prepare(); settings.cooldown = .25f; settings.projectileSpeed = 6; settings.spawnOffset = 1;
        settings.centerLeadDistance = .55f; settings.sideOffset = .5f;
        Check(skill.TryThrow(), "Modified inspector-style settings launch successfully."); FollowCamera();
        var configured = Shots();
        Check(configured.Length == 3 && configured.All(p=>p.Speed==6) && skill.CooldownRemaining > .24f && skill.CooldownRemaining < .26f
            && configured.Any(p=>Mathf.Abs(p.transform.position.x-Center.x-1.55f)<.002f)
            && configured.Count(p=>Mathf.Abs(Mathf.Abs(p.transform.position.y-Center.y)-.5f)<.002f)==2,
            "Float cooldown, speed, spawn offset, lead and side spacing all affect the next launch.");
        settings.projectileSpeed = 60;
        yield return new WaitForSeconds(.3f);
        Check(configured.All(p=>p!=null && p.Speed==6) && skill.CooldownRemaining == 0, "In-flight shots keep their launch snapshot while the configured fractional cooldown expires.");
        Prepare(); settings.cooldown=7; settings.projectileSpeed=12; settings.spawnOffset=.6f; settings.centerLeadDistance=.4f; settings.sideOffset=.35f;
        settings.hitDamageMultiplier=.5f; settings.poisonDamageMultiplier=.2f; settings.poisonDuration=.4f; settings.poisonTickInterval=.1f;
        enemy=Monster(Center+Vector2.right,new Vector2(.5f,1.2f));
        Check(skill.TryThrow(), "Modified damage and poison settings launch successfully."); FollowCamera();
        yield return new WaitForSeconds(.08f);
        Check(enemy.CurrentHealth==985, "Configured direct damage multiplier applies independently to all three daggers.");
        yield return new WaitForSeconds(.45f);
        Check(enemy.CurrentHealth==977 && !enemy.GetComponent<PoisonStatus2D>().IsActive, "Configured poison multiplier, fractional duration and interval apply four non-stacking ticks.");
        Object.DestroyImmediate(enemy.gameObject); settings.hitDamageMultiplier=.4f; settings.poisonDamageMultiplier=.1f; settings.poisonDuration=4; settings.poisonTickInterval=1;

        Keys(); Prepare(); var grenades = actor.GetComponent<PlayerGrenadeSkill>(); int ammunition = grenades.Remaining;
        actions.SelectThrowSkill(PlayerAttackController.ThrowSkill.Grenade);
        Keys(Key.E); yield return new WaitForSeconds(.06f);
        Check(grenades.IsAiming && Shots().Length == 0 && grenades.Remaining == ammunition, "Selecting retained grenade routes E exclusively to its original aim behavior.");
        Keys(); yield return new WaitForSeconds(.06f);
        var grenadeShots = Object.FindObjectsByType<PlayerGrenadeProjectile>(FindObjectsSortMode.None).Where(p => p.gameObject.scene == fixture).ToArray();
        Check(grenades.Remaining == ammunition-1 && grenadeShots.Length == 1, "Release creates one original grenade and spends one saved ammunition.");
        foreach(var g in grenadeShots) Object.DestroyImmediate(g.gameObject);
        actions.SelectThrowSkill(PlayerAttackController.ThrowSkill.PoisonDaggers); actions.SelectThrowSkill(PlayerAttackController.ThrowSkill.Grenade);
        Check(grenades.Remaining == ammunition-1 && grenades.CooldownRemaining > 0, "Switching skills preserves grenade ammunition and cooldown.");
        actions.SelectThrowSkill(PlayerAttackController.ThrowSkill.PoisonDaggers);
        Keys(Key.E); yield return null; yield return null; FollowCamera();
        Check(Shots().Length == 3 && !grenades.IsAiming && grenades.Remaining == ammunition-1, "Switching back routes E only to daggers.");
        ClearShots(); Keys(); yield return new WaitForSeconds(.05f);
        var menu = actor.GetComponent<PlayerSkillDevelopmentMenu>();
        Keys(Key.K); yield return new WaitForSeconds(.08f);
        Check(PlayerSkillDevelopmentMenu.IsOpen && actor.GetComponent<PlayerControlLock>().IsLocked, "K opens the development workbench and locks gameplay input.");
        var buttons = actor.GetComponentsInChildren<Button>(true);
        buttons.Single(b=>b.name=="Equip grenade").onClick.Invoke();
        Check(actions.SelectedThrowSkill == PlayerAttackController.ThrowSkill.Grenade, "Workbench grenade button equips the preserved skill.");
        buttons.Single(b=>b.name=="Equip poison daggers").onClick.Invoke();
        buttons.Single(b=>b.name=="Primary category").onClick.Invoke();
        buttons.Single(b=>b.name=="Equip shockwave").onClick.Invoke();
        Check(actions.SelectedThrowSkill==PlayerAttackController.ThrowSkill.PoisonDaggers && actions.SelectedPrimarySkill==PlayerAttackController.PrimarySkill.Shockwave, "Workbench categories equip independent primary and throw skills.");
        buttons.Single(b=>b.name=="Equip stealth").onClick.Invoke();
        ScreenCapture.CaptureScreenshot(Path.GetFullPath(Output+"workbench-preview.png")); yield return null;
        Keys(); yield return new WaitForSeconds(.04f); Keys(Key.K); yield return new WaitForSeconds(.05f);
        Check(!PlayerSkillDevelopmentMenu.IsOpen && !actor.GetComponent<PlayerControlLock>().IsLocked, "K closes workbench and releases only its gameplay lock.");
        Keys(); yield return new WaitForSeconds(.04f);
        var movement = actor.AddComponent<PlayerMovement2D>(); var playerBody = actor.GetComponent<Rigidbody2D>();
        playerBody.constraints = RigidbodyConstraints2D.FreezeRotation | RigidbodyConstraints2D.FreezePositionY;
        Keys(Key.D); yield return new WaitForSeconds(.08f);
        Check(Mathf.Abs(playerBody.linearVelocity.x-7)<.01f, "Existing movement still runs at seven with the updated input reader.");
        Keys(Key.D,Key.LeftCtrl); yield return new WaitForSeconds(.08f);
        Check(input.WalkHeld && Mathf.Abs(playerBody.linearVelocity.x-3.5f)<.01f, "Existing Ctrl hold still walks at three and a half.");
        Keys(Key.D); yield return new WaitForSeconds(.06f);
        Check(!input.WalkHeld && Mathf.Abs(playerBody.linearVelocity.x-7)<.01f, "Releasing Ctrl returns immediately to the existing run speed.");
        Keys(Key.LeftShift); yield return new WaitForSeconds(.04f);
        Check(movement.IsDashing, "Existing Shift dash still activates without movement input.");
        yield return new WaitForSeconds(1.1f);
        Check(!movement.IsDashing, "Holding Shift does not repeat the existing dash after cooldown.");
        Keys(); yield return new WaitForSeconds(.04f);
        var baseSkills = actor.GetComponent<PlayerSkills>(); Keys(Key.Q); yield return new WaitForSeconds(.05f);
        Check(baseSkills.IsStealthed, "Existing Q input still activates stealth after weapon switching."); baseSkills.BreakStealth();
        Scheme(PlayerControlScheme.ArrowKeys); Keys(); yield return new WaitForSeconds(.04f);
        int basicInputs=0; input.BasicAttackPressed += () => basicInputs++;
        Keys(Key.Z); yield return new WaitForSeconds(.05f);
        Check(basicInputs==1, "Existing basic attack input still fires once.");
        Keys(); yield return new WaitForSeconds(.3f); playerBody.position=Center; playerBody.linearVelocity=Vector2.zero; Physics2D.SyncTransforms();
        CaptureFormation();
    }
    private static void CaptureFormation()
    {
        ClearShots(); camera.orthographicSize = 3; camera.aspect = 1.5f;
        for(int i=0;i<8;i++)
        {
            Vector2 heading = new Vector2(Mathf.Cos(i*45*Mathf.Deg2Rad),Mathf.Sin(i*45*Mathf.Deg2Rad));
            Vector2 side=new(-heading.y,heading.x); Vector2 point=Center+new Vector2((i%4-1.5f)*2,(i/4==0?1:-1)*1.4f);
            Launch(point+heading*.4f,heading); Launch(point+side*.35f,heading); Launch(point-side*.35f,heading);
        }
        camera.Render(); var old = RenderTexture.active; var pixels = new Texture2D(900,600,TextureFormat.RGB24,false);
        try { RenderTexture.active=target; pixels.ReadPixels(new Rect(0,0,900,600),0,0); pixels.Apply(); File.WriteAllBytes(Output+"formation-preview.png",pixels.EncodeToPNG()); }
        finally { RenderTexture.active=old; Object.Destroy(pixels); }
    }
    private static void Finish(Exception error)
    {
        if (!running) return; running = false;
        EditorApplication.update -= Watchdog; Application.logMessageReceived -= Observe;
        if(error!=null) Report.AppendLine("FAIL "+error);
        Report.AppendLine($"Unity {Application.unityVersion}; {checks} checks; {errors} runtime errors; {(error==null&&errors==0?"PASSED":"FAILED")}");
        File.WriteAllText(Output+"result.txt",Report.ToString());
        if(actor!=null) actor.GetComponent<PlayerSkillDevelopmentMenu>()?.Close();
        if(fixture.IsValid()&&fixture.isLoaded) { foreach(var root in fixture.GetRootGameObjects()) Object.DestroyImmediate(root); SceneManager.UnloadSceneAsync(fixture); }
        if(keyboardObject!=null) Object.DestroyImmediate(keyboardObject);
        if(settings!=null) Object.DestroyImmediate(settings);
        if(target!=null) { target.Release(); Object.DestroyImmediate(target); }
        typeof(PlayerControlSchemeSettings).GetField("loaded",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,oldLoaded);
        typeof(PlayerControlSchemeSettings).GetField("current",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,oldScheme);
        if(oldKeyboard!=null&&oldKeyboard.added) oldKeyboard.MakeCurrent();
        UnityEngine.Random.state=oldRandom; Time.timeScale=oldTimeScale; Application.runInBackground=oldBackground; EditorApplication.isPaused=oldPaused;
        if(oldFocus!=null) oldFocus.Focus();
    }
}
#endif
