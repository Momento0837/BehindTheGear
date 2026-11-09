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
using Object = UnityEngine.Object;

/// <summary>Real Input System and Rigidbody2D checks in a disposable local physics scene; no saves or asset changes.</summary>
[InitializeOnLoad]
public static class PlayerDashChecks
{
    private const string Output = "Temp/PlayerDashValidation/";
    private const string Armed = "BTG.PlayerDashChecks";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly StringBuilder Report = new();
    private static readonly Vector2 Center = new(1000, 1000);
    private static readonly WaitForFixedUpdate Fixed = new();
    private static Scene fixture;
    private static GameObject actor, keyboardObject;
    private static PlayerMovement2D movement;
    private static PlayerInputReader input;
    private static PlayerSpriteFacing facing;
    private static PlayerHealth health;
    private static Rigidbody2D body;
    private static bool running, oldBackground, oldLoaded;
    private static PlayerControlScheme oldScheme;
    private static Keyboard oldKeyboard;
    private static int checks, errors;
    private static double deadline;

    static PlayerDashChecks() => EditorApplication.update += Tick;

    [MenuItem("Tools/Checks/Player Ctrl Walk and Shift Dash")]
    public static void Run()
    {
        if (Application.unityVersion != "6000.3.23f1" || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run from edit mode in Unity 6000.3.23f1.");
        Directory.CreateDirectory(Output);
        oldBackground = Application.runInBackground;
        SessionState.SetBool(Armed, true);
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (!SessionState.GetBool(Armed, false))
        {
            if (File.Exists(Output + "request.flag") && !EditorApplication.isPlayingOrWillChangePlaymode)
            { File.Delete(Output + "request.flag"); Run(); }
            return;
        }
        if (!Application.isPlaying || running) return;
        running = true; checks = errors = 0; Report.Clear();
        deadline = EditorApplication.timeSinceStartup + 45;
        Application.runInBackground = true;
        Application.logMessageReceived += Observe;
        try
        {
            oldKeyboard = Keyboard.current;
            oldLoaded = (bool)typeof(PlayerControlSchemeSettings).GetField("loaded", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            oldScheme = (PlayerControlScheme)typeof(PlayerControlSchemeSettings).GetField("current", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            SetScheme(PlayerControlScheme.WasdMouseQe);
            keyboardObject = new GameObject("Dash checks keyboard"); keyboardObject.AddComponent<PlayReviewKeyboard>();
            fixture = SceneManager.CreateScene("Isolated dash checks", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            actor = new GameObject("Dash check player"); actor.SetActive(false);
            SceneManager.MoveGameObjectToScene(actor, fixture);
            actor.transform.position = Center;
            body = actor.AddComponent<Rigidbody2D>(); body.gravityScale = 0;
            body.constraints = RigidbodyConstraints2D.FreezeRotation | RigidbodyConstraints2D.FreezePositionY;
            actor.AddComponent<BoxCollider2D>().size = new Vector2(.9f, 2.15f);
            actor.AddComponent<SpriteRenderer>().sprite = AssetDatabase.LoadAllAssetsAtPath("Assets/!Animation/Player/SpriteSheet/Player_Idle.png").OfType<Sprite>().First();
            actor.AddComponent<Animator>().runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/!Animation/Player/Player.controller");
            input = actor.AddComponent<PlayerInputReader>();
            var control = actor.AddComponent<PlayerControlLock>(); JsonUtility.FromJsonOverwrite("{\"controlsToDisable\":[]}", control);
            Set(control, "playerBody", body);
            health = actor.AddComponent<PlayerHealth>(); Set(health, "maxHealth", 100);
            movement = actor.AddComponent<PlayerMovement2D>();
            facing = actor.AddComponent<PlayerSpriteFacing>();
            actor.AddComponent<PlayerMeleeAttack>();
            actor.AddComponent<PlayerAttackController>();
            actor.SetActive(true);
            Physics2D.SyncTransforms();
            movement.StartCoroutine(Simulate());
            movement.StartCoroutine(Guard(Verify()));
            EditorApplication.update += Watchdog;
        }
        catch (Exception error) { Finish(error); }
    }

    private static void Watchdog()
    {
        if (running && EditorApplication.timeSinceStartup > deadline) Finish(new Exception("Dash checks timed out."));
        if (running) EditorApplication.QueuePlayerLoopUpdate();
    }
    private static IEnumerator Simulate()
    {
        while (running)
        {
            yield return Fixed;
            if (fixture.IsValid() && fixture.isLoaded) fixture.GetPhysicsScene2D().Simulate(Time.fixedDeltaTime);
        }
    }
    private static IEnumerator Guard(IEnumerator routine)
    {
        while (true)
        {
            bool more;
            try { more = routine.MoveNext(); }
            catch (Exception error) { Finish(error); yield break; }
            if (!more) break;
            yield return routine.Current;
        }
        Finish(null);
    }
    private static void Observe(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception) return;
        errors++; Report.AppendLine("RUNTIME " + message + "\n" + stack);
    }
    private static void Set(object value, string field, object data) => value.GetType().GetField(field, Private).SetValue(value, data);
    private static float ReadFloat(object value, string field) => (float)value.GetType().GetField(field, Private).GetValue(value);
    private static void SetScheme(PlayerControlScheme scheme)
    {
        typeof(PlayerControlSchemeSettings).GetField("loaded", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, true);
        typeof(PlayerControlSchemeSettings).GetField("current", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, scheme);
    }
    private static void Keys(params Key[] keys) => PlayReviewKeyboard.Held = keys;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message + $" [position={body.position}; velocity={body.linearVelocity}; dashing={movement.IsDashing}]");
        checks++; Report.AppendLine("PASS " + message);
        File.WriteAllText(Output + "progress.txt", Report.ToString());
    }
    private static void ResetDash(float distance = 3, float duration = .15f, float cooldown = 1)
    {
        Set(movement, "dashDistance", distance); Set(movement, "dashDuration", duration);
        Set(movement, "dashCooldown", cooldown); Set(movement, "dashReadyAt", 0f);
        body.position = Center; body.linearVelocity = Vector2.zero; Physics2D.SyncTransforms();
    }
    private static void Face(float direction)
    {
        typeof(PlayerSpriteFacing).GetField("<FacingDirection>k__BackingField", Private).SetValue(facing, direction);
        actor.GetComponent<SpriteRenderer>().flipX = direction < 0;
    }
    private static PlayerDashAfterimage[] Images() => Object.FindObjectsByType<PlayerDashAfterimage>(FindObjectsSortMode.None)
        .Where(effect => effect.gameObject.scene == fixture).ToArray();

    private static void CaptureAfterimages()
    {
        var cameraObject = new GameObject("Dash preview camera");
        SceneManager.MoveGameObjectToScene(cameraObject, fixture);
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
        camera.orthographic = true; camera.orthographicSize = 2.3f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f, .055f, .085f);
        camera.transform.position = new Vector3((Center.x + body.position.x) * .5f, Center.y, -10);
        var target = new RenderTexture(900, 450, 24);
        var pixels = new Texture2D(900, 450, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target; camera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 900, 450), 0, 0); pixels.Apply();
            File.WriteAllBytes(Output + "afterimage-preview.png", pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null;
            target.Release(); Object.Destroy(target); Object.Destroy(pixels); Object.Destroy(cameraObject);
        }
    }

    private static IEnumerator Verify()
    {
        foreach (var scheme in new[] { PlayerControlScheme.WasdMouseQe, PlayerControlScheme.ArrowKeys })
        {
            SetScheme(scheme); Keys(); yield return new WaitForSeconds(.05f);
            Key right = scheme == PlayerControlScheme.WasdMouseQe ? Key.D : Key.RightArrow;
            Keys(right); yield return new WaitForSeconds(.06f);
            Check(Mathf.Abs(body.linearVelocity.x - 7) < .01f, scheme + ": existing default run speed is preserved.");
            Keys(right, Key.LeftCtrl); yield return new WaitForSeconds(.06f);
            Check(input.WalkHeld && Mathf.Abs(body.linearVelocity.x - 3.5f) < .01f, scheme + ": holding Ctrl walks at 3.5.");
            Check(actor.GetComponent<BoxCollider2D>().size.y == 2.15f, "Ctrl walking does not change the crouch collider.");
            Keys(right); yield return new WaitForSeconds(.04f);
            Check(!input.WalkHeld && Mathf.Abs(body.linearVelocity.x - 7) < .01f, "Releasing Ctrl immediately resumes running.");
            Keys(Key.RightCtrl); yield return new WaitForSeconds(.04f);
            Check(input.WalkHeld && Mathf.Abs(body.linearVelocity.x) < .01f, "Ctrl alone leaves the player stationary (including right Ctrl).");
        }
        SetScheme(PlayerControlScheme.WasdMouseQe); Keys(); yield return new WaitForSeconds(.06f);
        ResetDash(); Face(-1);
        float start = body.position.x;
        Keys(Key.LeftShift); yield return new WaitUntil(() => movement.IsDashing);
        float readyAt = ReadFloat(movement, "dashReadyAt");
        Check(facing.FacingDirection == -1 && body.collisionDetectionMode == CollisionDetectionMode2D.Continuous,
            "Idle Shift dash uses the existing facing direction and continuous physics collision detection.");
        Check(!movement.TryStartDash(), "A second dash request during an active dash is discarded.");
        yield return new WaitForSeconds(.06f);
        Check(Images().Length >= 2, "Dashing emits multiple sprite snapshots along the path.");
        var sample = Images().First(); float oldAlpha = sample.GetComponent<SpriteRenderer>().color.a;
        Check(sample.GetComponent<Collider2D>() == null && sample.GetComponent<SpriteRenderer>().sortingOrder < actor.GetComponent<SpriteRenderer>().sortingOrder,
            "Afterimages have no collider and render behind the player.");
        yield return new WaitForSeconds(.03f);
        Check(sample != null && sample.GetComponent<SpriteRenderer>().color.a < oldAlpha, "Afterimages fade over time.");
        yield return new WaitUntil(() => !movement.IsDashing);
        Check(Mathf.Abs((start - body.position.x) - 3) < .025f, "An unobstructed default dash travels 3 units, including the partial final physics step.");
        Check(body.collisionDetectionMode == CollisionDetectionMode2D.Discrete && Mathf.Abs(body.linearVelocity.x) < .01f,
            "Dash completion restores the previous collision mode and stationary input.");
        Check(movement.DashCooldownRemaining > .6f, "The one-second cooldown starts at dash start rather than at dash end.");
        Check(!movement.TryStartDash(), "A request during cooldown is rejected without buffering.");
        float heldPosition = body.position.x;
        yield return new WaitForSeconds(1.05f);
        Check(!movement.IsDashing && Mathf.Abs(body.position.x - heldPosition) < .01f && ReadFloat(movement, "dashReadyAt") == readyAt,
            "Holding Shift beyond cooldown does not repeat or replay a rejected request.");
        Check(Images().Length == 0, "Expired afterimages clean themselves up.");
        Keys(); yield return new WaitForSeconds(.04f);
        Keys(Key.RightShift); yield return new WaitUntil(() => movement.IsDashing);
        Check(movement.IsDashing, "Releasing and pressing Shift again starts a fresh dash (right Shift supported).");
        yield return new WaitUntil(() => !movement.IsDashing);

        Keys(Key.D, Key.LeftCtrl); yield return new WaitForSeconds(.06f);
        Set(movement, "dashReadyAt", 0f);
        Keys(Key.D, Key.LeftCtrl, Key.LeftShift); yield return new WaitUntil(() => movement.IsDashing);
        yield return Fixed;
        Check(Mathf.Abs(body.linearVelocity.x - 20) < .02f, "Walking can start a full-speed dash.");
        Keys(Key.A, Key.LeftCtrl, Key.LeftShift); yield return new WaitForSeconds(.04f);
        Check(movement.IsDashing && facing.FacingDirection == 1 && body.linearVelocity.x > 19.9f,
            "Opposite movement and Ctrl during a dash cannot change its facing, direction or speed.");
        yield return new WaitUntil(() => !movement.IsDashing);
        Check(facing.FacingDirection == -1 && Mathf.Abs(body.linearVelocity.x + 3.5f) < .01f,
            "Dash completion applies the currently held opposite walk input and facing.");
        Keys(Key.D); yield return new WaitForSeconds(.05f); Set(movement, "dashReadyAt", 0f); Face(-1);
        Keys(Key.D, Key.LeftShift); yield return new WaitUntil(() => movement.IsDashing); yield return Fixed;
        Check(body.linearVelocity.x < -19.9f, "If movement input and facing disagree, the dash follows facing rather than movement input.");
        yield return new WaitUntil(() => !movement.IsDashing);
        Check(Mathf.Abs(body.linearVelocity.x - 7) < .01f, "Running resumes from the current input after the dash.");

        Keys(); yield return new WaitForSeconds(.05f);
        ResetDash(4.25f, .23f, .37f); Face(1); start = body.position.x;
        Keys(Key.LeftShift); yield return new WaitUntil(() => movement.IsDashing);
        float startedAt = Time.time;
        yield return Fixed;
        Check(Mathf.Abs(body.linearVelocity.x - 4.25f / .23f) < .02f, "Decimal distance and duration change actual dash speed.");
        yield return new WaitUntil(() => !movement.IsDashing);
        Check(Mathf.Abs(body.position.x - start - 4.25f) < .025f && Mathf.Abs(Time.time - startedAt - .23f) < .05f,
            "Inspector-equivalent 4.25 distance / 0.23 duration produce the configured distance and duration.");
        Check(movement.DashCooldownRemaining > .04f && movement.DashCooldownRemaining < .2f,
            "Inspector-equivalent 0.37 cooldown is applied from dash start.");

        Keys(); yield return new WaitForSeconds(.05f);
        ResetDash(6.75f, .09f); Face(1);
        var wall = new GameObject("Thin dash obstacle"); SceneManager.MoveGameObjectToScene(wall, fixture);
        wall.transform.position = Center + Vector2.right * 1.5f; wall.layer = LayerMask.NameToLayer("Ground");
        wall.AddComponent<BoxCollider2D>().size = new Vector2(.05f, 8); Physics2D.SyncTransforms();
        Keys(Key.LeftShift); yield return new WaitUntil(() => movement.IsDashing); yield return new WaitUntil(() => !movement.IsDashing);
        Check(body.position.x < Center.x + 1.1f && body.position.x > Center.x + .2f,
            "A fast dash hits a thin wall instead of tunneling; obstacles shorten actual travel.");
        wall.SetActive(false); Object.Destroy(wall);

        Keys(); yield return new WaitForSeconds(.05f); ResetDash(); Face(1);
        Keys(Key.LeftShift); yield return new WaitUntil(() => movement.IsDashing);
        var control = actor.GetComponent<PlayerControlLock>();
        Set(control, "controlsToDisable", new Behaviour[] { movement, input, facing });
        control.Acquire();
        Check(!movement.IsDashing && body.linearVelocity.x == 0 && !movement.TryStartDash(), "Existing dialogue/control lock cancels and blocks dashes.");
        control.Release(); Set(movement, "dashReadyAt", 0f);
        yield return new WaitForSeconds(.08f);
        Check(!movement.IsDashing, "Re-enabling input while Shift remains held does not trigger an automatic dash.");
        Keys(); yield return new WaitForSeconds(.04f);
        control.Acquire();
        Keys(Key.LeftShift); yield return new WaitForSeconds(.04f);
        Set(movement, "dashReadyAt", 0f); control.Release();
        yield return new WaitForSeconds(.06f);
        Check(!movement.IsDashing, "Shift first pressed while controls are disabled is discarded rather than replayed on unlock.");

        Keys(); yield return new WaitForSeconds(.05f); ResetDash();
        var floor = new GameObject("Existing jump ground"); SceneManager.MoveGameObjectToScene(floor, fixture);
        floor.transform.position = Center + Vector2.down * 1.2f; floor.layer = LayerMask.NameToLayer("Ground");
        floor.AddComponent<BoxCollider2D>().size = new Vector2(40, .25f);
        body.constraints = RigidbodyConstraints2D.FreezeRotation; body.gravityScale = 1.6f; Physics2D.SyncTransforms();
        yield return new WaitForSeconds(.08f);
        Keys(Key.Space); yield return new WaitForSeconds(.08f);
        Check(body.linearVelocity.y > 0 && body.position.y > Center.y, "Existing grounded jump input and vertical velocity still work.");
        Keys(Key.LeftShift); yield return new WaitUntil(() => movement.IsDashing);
        float beforeY = body.position.y;
        yield return new WaitUntil(() => !movement.IsDashing);
        Check(body.gravityScale == 1.6f && body.position.y > beforeY, "Air dashing preserves existing vertical jump motion and gravity.");

        Keys(); yield return new WaitForSeconds(.1f); body.constraints |= RigidbodyConstraints2D.FreezePositionY; body.gravityScale = 0;
        SetScheme(PlayerControlScheme.ArrowKeys);
        Keys(Key.DownArrow); yield return new WaitForSeconds(.05f);
        Check(Mathf.Abs(actor.GetComponent<BoxCollider2D>().size.y - 1.075f) < .001f, "Existing crouch input still adjusts the collider.");
        Keys(); yield return new WaitForSeconds(.05f);
        Check(Mathf.Abs(actor.GetComponent<BoxCollider2D>().size.y - 2.15f) < .001f, "Releasing crouch restores the collider.");
        Keys(Key.Z); yield return new WaitForSeconds(.08f);
        Check(movement.IsAttacking, "Existing arrow-layout basic attack still starts its real animation.");
        Keys(); yield return new WaitForSeconds(.75f);
        SetScheme(PlayerControlScheme.WasdMouseQe);
        var skills = actor.GetComponent<PlayerSkills>();
        Keys(Key.Q); yield return new WaitForSeconds(.08f);
        Check(skills.IsStealthed, "Existing Q skill input still activates stealth.");
        Keys(); yield return new WaitForSeconds(.04f); Set(movement, "dashReadyAt", 0f);
        Keys(Key.LeftShift); yield return new WaitUntil(() => movement.IsDashing);
        int hp = health.CurrentHealth; health.TakeDamage(15);
        Check(health.CurrentHealth == hp - 4, "Dash adds no invulnerability and preserves existing damage/defense rules.");
        health.TakeDamage(1000); // Clear normal damage invulnerability before testing death.
        Set(health, "invincibilityEndTime", 0f); health.TakeDamage(1000);
        Check(health.IsDead && !movement.IsDashing && !movement.TryStartDash(), "Death cancels the dash and prevents further dashes.");

        // Render the preview after timing-sensitive checks, so shader warmup/readback cannot skew them.
        health.ReviveAfterSceneRestart(); Keys(); yield return new WaitForSeconds(.06f);
        ResetDash(); Face(1);
        Keys(Key.LeftShift); yield return new WaitUntil(() => movement.IsDashing);
        yield return new WaitForSeconds(.09f);
        CaptureAfterimages();
    }

    private static void Finish(Exception error)
    {
        if (!running) return;
        running = false; SessionState.SetBool(Armed, false);
        EditorApplication.update -= Watchdog;
        Application.logMessageReceived -= Observe;
        if (error != null) Report.AppendLine("FAIL " + error);
        Report.AppendLine($"Unity {Application.unityVersion}; {checks} checks; {errors} runtime errors; {(error == null && errors == 0 ? "PASSED" : "FAILED")}");
        File.WriteAllText(Output + "result.txt", Report.ToString());
        typeof(PlayerControlSchemeSettings).GetField("loaded", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, oldLoaded);
        typeof(PlayerControlSchemeSettings).GetField("current", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, oldScheme);
        if (keyboardObject != null) Object.DestroyImmediate(keyboardObject);
        if (oldKeyboard != null && oldKeyboard.added) oldKeyboard.MakeCurrent();
        EditorApplication.isPlaying = false;
        Application.runInBackground = oldBackground;
    }
}
#endif
