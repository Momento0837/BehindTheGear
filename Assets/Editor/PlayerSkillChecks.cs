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

/// <summary>Explicit Play checks with isolated runtime actors, no scene or save-file writes.</summary>
[InitializeOnLoad]
public static class PlayerSkillChecks
{
    private const string Output = "Temp/PlayerSkillValidation/";
    private const string Armed = "BTG.PlayerSkills.Checks";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly StringBuilder Report = new();
    private static bool wasPaused, wasBackground;
    private static bool wasSchemeLoaded;
    private static PlayerControlScheme oldScheme;
    private static bool running;
    private static int errors, checks;
    private static double deadline;
    private static GameObject actor, keyboardObject, hud;
    private static Scene fixtureScene;

    static PlayerSkillChecks()
    {
        // An existing editor request watcher supplies the initial source refresh only.
        if (File.Exists(Output + "request.flag") && File.Exists("Temp/HideOutValidation/request-ui-checks.flag"))
            File.Delete("Temp/HideOutValidation/request-ui-checks.flag");
        EditorApplication.update += Tick;
    }

    [MenuItem("Tools/Checks/Player Q E Skills")]
    public static void Run()
    {
        if (Application.unityVersion != "6000.3.23f1") throw new InvalidOperationException("Unity 6000.3.23f1 required.");
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run outside Play mode.");
        Directory.CreateDirectory(Output);
        running = false;
        File.WriteAllText(Output + "progress.txt", "Starting explicit skill checks.\n");
        wasPaused = EditorApplication.isPaused;
        wasBackground = Application.runInBackground;
        SessionState.SetBool(Armed, true);
        EditorApplication.isPaused = false;
        Application.runInBackground = true;
        deadline = EditorApplication.timeSinceStartup + 90;
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (File.Exists(Output + "request.flag") && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(Output + "request.flag");
            Run();
        }
        if (!SessionState.GetBool(Armed, false)) return;
        EditorApplication.isPaused = false;
        Application.runInBackground = true;
        EditorApplication.QueuePlayerLoopUpdate();
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 90;
        if (EditorApplication.timeSinceStartup > deadline) { Finish("FAIL timeout", null); return; }
        if (!EditorApplication.isPlaying || running) return;
        running = true; checks = errors = 0; Report.Clear();
        File.WriteAllText(Output + "progress.txt", "Creating isolated runtime skill fixture.\n");
        Application.logMessageReceived += Observe;
        try
        {
            fixtureScene = SceneManager.CreateScene("Isolated Player Skill Checks");
            actor = new GameObject("Skill Check Player"); actor.SetActive(false);
            SceneManager.MoveGameObjectToScene(actor, fixtureScene);
            actor.transform.position = new Vector3(1000, 1000, 0);
            var body = actor.AddComponent<Rigidbody2D>(); body.gravityScale = 0; body.constraints = RigidbodyConstraints2D.FreezeAll;
            actor.AddComponent<BoxCollider2D>();
            actor.AddComponent<SpriteRenderer>();
            var input = actor.AddComponent<PlayerInputReader>();
            var controlLock = actor.AddComponent<PlayerControlLock>();
            JsonUtility.FromJsonOverwrite("{\"controlsToDisable\":[]}", controlLock);
            actor.AddComponent<PlayerHealth>();
            actor.AddComponent<PlayerMeleeAttack>();
            actor.AddComponent<PlayerTargetRegistry>();
            actor.AddComponent<PlayerAttackController>();
            actor.SetActive(true);
            actor.GetComponent<PlayerSkills>().StartCoroutine(Guard(Checks(actor.GetComponent<PlayerSkills>())));
        }
        catch (Exception error) { Finish("FAIL setup", error); }
    }

    private static void Observe(string message, string trace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception) return;
        errors++; Report.AppendLine("RUNTIME ERROR " + message);
    }

    private static IEnumerator Guard(IEnumerator routine)
    {
        while (true)
        {
            bool more;
            try { more = routine.MoveNext(); }
            catch (Exception error) { Finish("FAIL", error); yield break; }
            if (!more) break;
            yield return routine.Current;
        }
        Finish(errors == 0 ? "PASS" : "FAIL runtime errors", null);
    }

    private static object Read(object value, string field) => value.GetType().GetField(field, Private).GetValue(value);
    private static void Set(object value, string field, object data) => value.GetType().GetField(field, Private).SetValue(value, data);
    private static void Check(bool result, string message)
    {
        if (!result) throw new InvalidOperationException(message);
        checks++; Report.AppendLine("PASS " + message);
        File.WriteAllText(Output + "progress.txt", Report.ToString());
    }

    private static MonsterFollowController Monster(Vector2 offset)
    {
        var root = new GameObject("Skill Check Monster"); root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, fixtureScene);
        root.transform.position = actor.transform.position + (Vector3)offset;
        var body = root.AddComponent<Rigidbody2D>(); body.gravityScale = 0f;
        body.constraints = RigidbodyConstraints2D.FreezePositionY | RigidbodyConstraints2D.FreezeRotation;
        var box = root.AddComponent<BoxCollider2D>(); box.size = new Vector2(.6f, 1f);
        var range = new GameObject("Authored Attack Range", typeof(BoxCollider2D)); range.transform.SetParent(root.transform, false);
        range.GetComponent<BoxCollider2D>().size = Vector2.one * 3f;
        var monster = root.AddComponent<MonsterFollowController>();
        Set(monster, "maxHealth", 100); Set(monster, "moveSpeed", 0f); Set(monster, "patrolRadius", 0f);
        Set(monster, "attackRange", range.GetComponent<BoxCollider2D>());
        root.SetActive(true); monster.BindPlayer(actor.transform);
        return monster;
    }

    private static void ArmAttack(MonsterFollowController monster)
    {
        Set(monster, "animationState", "Attack"); Set(monster, "attackPending", true);
        Set(monster, "actionUntil", Time.time + 2f);
    }

    private static void ResetCast(PlayerSkills skills)
    {
        skills.BreakStealth(); Set(skills, "stealthReadyAt", 0f); Set(skills, "shockwaveReadyAt", 0f);
        skills.GetComponent<PlayerMana>().RestoreMana(1000);
    }

    private static IEnumerator Checks(PlayerSkills skills)
    {
        var health = actor.GetComponent<PlayerHealth>();
        var mana = actor.GetComponent<PlayerMana>();
        var renderer = actor.GetComponent<SpriteRenderer>();
        var controlLock = actor.GetComponent<PlayerControlLock>();
        var near = Monster(new Vector2(.7f, 0f));
        var left = Monster(new Vector2(-1.5f, 0f));
        var far = Monster(new Vector2(5f, 0f));
        // A second hurtbox proves a shockwave cannot deal duplicate damage to one monster.
        near.gameObject.AddComponent<CircleCollider2D>().radius = .25f;
        Physics2D.SyncTransforms();
        yield return null;
        var sensor = near.GetComponentInChildren<MonsterRangeTrigger2D>();
        Check(skills.CanUseSkills && sensor.HasPlayer, "Runtime auto-wiring and visible player recognition");
        ArmAttack(near); near.ApplyAttackHit();
        Check(health.CurrentHealth == 9, "Visible player can be hit by an armed monster attack");
        Check(skills.TryUseStealth(), "Q can activate");
        Check(mana.CurrentMana == 85 && skills.IsStealthed && Mathf.Abs(renderer.color.a - .28f) < .01f,
            "Q costs 15 MP and makes player translucent");
        Check(skills.StealthCooldownRemaining > 11.8f && skills.StealthRemaining > 2.3f && skills.StealthRemaining <= 2.5f, "Q starts 12-second cooldown and 2.5-second duration");
        Check(!sensor.HasPlayer, "Attack sensor excludes invisible player");
        ArmAttack(near); near.ApplyAttackHit();
        Check(health.CurrentHealth == 9 && !(bool)Read(near, "attackPending"), "Wind-up attack cannot hit after Q and consumes pending hit");
        yield return new WaitForFixedUpdate();
        Check(!(bool)Read(near, "wasChasing"), "Already chasing monster returns to patrol during stealth");
        Check(!skills.TryUseStealth() && mana.CurrentMana == 85, "Repeated Q is rejected without spending MP");
        yield return new WaitForSeconds(2.6f);
        Check(!skills.IsStealthed && Mathf.Abs(renderer.color.a - 1f) < .01f && sensor.HasPlayer,
            "2.5-second expiry restores alpha and recognition");
        Check(skills.StealthCooldownRemaining > 6f && !skills.TryUseStealth(), "Cooldown continues after stealth expires");

        ResetCast(skills); Check(skills.TryUseStealth(), "Stealth reset fixture activates");
        yield return new WaitForSeconds(.25f);
        health.TakeDamage(1);
        Check(!skills.IsStealthed && renderer.color.a == 1f, "Taking damage breaks stealth; Q does not grant invincibility");
        ResetCast(skills); skills.TryUseStealth(); skills.enabled = false;
        Check(!PlayerSkills.IsHiddenFromMonsters(health) && renderer.color.a == 1f, "Disabling skills restores visibility");
        skills.enabled = true;
        Check(skills.StealthCooldownRemaining > 11f, "Disable/enable cannot reset cooldown");

        ResetCast(skills); skills.TryUseStealth();
        int nearHp = near.CurrentHealth, leftHp = left.CurrentHealth, farHp = far.CurrentHealth;
        ArmAttack(near);
        Check(skills.TryUseShockwave(), "Archived shockwave can still be called independently");
        Check(!skills.IsStealthed && mana.CurrentMana == 60, "Archived shockwave retains its 25 MP cost");
        Check(near.CurrentHealth == nearHp - 15 && left.CurrentHealth == leftHp - 15 && far.CurrentHealth == farHp,
            "E deals 150% melee damage once per nearby monster and excludes distant enemies");
        Check(!(bool)Read(near, "attackPending") && (float)Read(near, "skillStaggerUntil") > Time.time + .5f,
            "E interrupts attack wind-up and staggers for 0.6 seconds");
        near.ApplyAttackHit();
        yield return new WaitForFixedUpdate();
        Check(near.GetComponent<Rigidbody2D>().linearVelocity.x > 7.9f && left.GetComponent<Rigidbody2D>().linearVelocity.x < -7.9f,
            "Shockwave pushes enemies away on both sides at speed 8");
        Check(!skills.TryUseShockwave() && mana.CurrentMana == 60 && skills.ShockwaveCooldownRemaining > 7f,
            "Repeated E is rejected without spending MP; independent 8-second cooldown");

        ResetCast(skills); mana.TrySpend(100);
        Check(!skills.TryUseStealth() && !skills.TryUseShockwave() && skills.StealthCooldownRemaining == 0f && skills.ShockwaveCooldownRemaining == 0f,
            "Insufficient MP does not start cooldown");
        mana.RestoreMana(100);
        controlLock.Acquire();
        Check(!skills.TryUseStealth() && !skills.TryUseShockwave() && mana.CurrentMana == 100, "Dialogue/inventory lock blocks both skills");
        controlLock.Release();
        actor.GetComponent<Rigidbody2D>().simulated = false;
        Check(!skills.TryUseStealth() && !skills.TryUseShockwave(), "Hideout physics suspension blocks both skills");
        actor.GetComponent<Rigidbody2D>().simulated = true;

        // Feed real Input System key state through the production reader and attack controller.
        var schemeType = typeof(PlayerControlSchemeSettings);
        wasSchemeLoaded = (bool)schemeType.GetField("loaded", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        oldScheme = (PlayerControlScheme)schemeType.GetField("current", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        schemeType.GetField("loaded", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, true);
        schemeType.GetField("current", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, PlayerControlScheme.WasdMouseQe);
        keyboardObject = new GameObject("Skill Check Keyboard", typeof(PlayReviewKeyboard));
        ResetCast(skills); PlayReviewKeyboard.Held = new[] { Key.Q };
        yield return new WaitForSeconds(.15f); PlayReviewKeyboard.Held = Array.Empty<Key>();
        Check(skills.IsStealthed && mana.CurrentMana == 85, "Real Q key reaches stealth through the existing input reader");
        yield return new WaitForSeconds(.1f);
        var grenades = actor.GetComponent<PlayerGrenadeSkill>();
        Check(grenades.Remaining == 5, "Existing player auto-wires five grenades without replacing the rig");
        PlayReviewKeyboard.Held = new[] { Key.E };
        yield return new WaitForSeconds(.15f);
        float firstAngle = grenades.AimAngle;
        Check(grenades.IsAiming && grenades.Remaining == 5, "Holding real E aims without spending ammunition");
        yield return new WaitForSeconds(.2f);
        Check(grenades.AimAngle > firstAngle && actor.GetComponentInChildren<LineRenderer>().enabled, "Holding E adjusts the angle and displays the ballistic trajectory");
        PlayReviewKeyboard.Held = Array.Empty<Key>(); yield return new WaitForSeconds(.1f);
        Check(!grenades.IsAiming && grenades.Remaining == 4 && !skills.IsStealthed && mana.CurrentMana == 85
            && skills.ShockwaveCooldownRemaining == 0, "Releasing E throws one grenade, breaks stealth, and spends no shockwave MP");
        schemeType.GetField("current", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, PlayerControlScheme.ArrowKeys);
        ResetCast(skills); yield return new WaitForSeconds(.1f); PlayReviewKeyboard.Held = new[] { Key.C };
        yield return new WaitForSeconds(.15f); PlayReviewKeyboard.Held = Array.Empty<Key>();
        Check(skills.IsStealthed, "Arrow scheme maps stealth to C");
        yield return new WaitForSeconds(.1f); PlayReviewKeyboard.Held = new[] { Key.Z };
        yield return new WaitForSeconds(.15f); PlayReviewKeyboard.Held = Array.Empty<Key>();
        Check(!skills.IsStealthed, "Basic attack input breaks stealth");
        yield return new WaitForSeconds(.1f); PlayReviewKeyboard.Held = new[] { Key.V };
        yield return new WaitForSeconds(.15f); PlayReviewKeyboard.Held = Array.Empty<Key>();
        yield return new WaitForSeconds(.1f);
        Check(grenades.Remaining == 3 && skills.ShockwaveCooldownRemaining == 0, "Arrow scheme maps hold and release grenade throwing to V");

        hud = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/!UI/Ingame/InGame_Canvas.prefab"));
        var skillHud = hud.GetComponent<InGameSkillHUD>() ?? hud.AddComponent<InGameSkillHUD>();
        yield return new WaitForSeconds(.3f);
        Check(hud.transform.Find("Skill_ZQ/Skill Name").GetComponent<TMPro.TMP_Text>().text == "C 은신"
            && hud.transform.Find("Skill_EX/Skill Name").GetComponent<TMPro.TMP_Text>().text == "V 수류탄", "HUD labels follow selected control scheme");
        Check(hud.transform.Find("Skill_EX/Skill MP").GetComponent<TMPro.TMP_Text>().text == "수류탄 3/5", "Grenade HUD shows real carried ammunition instead of an MP cost");

        // A real accepted swing reserves its stealth bonus before restoring visibility.
        ResetCast(skills); Check(skills.TryUseStealth(), "Stealth can prepare the ambush fixture");
        var dummyObject = new GameObject("Ambush damage receiver");
        dummyObject.transform.position = actor.transform.position + Vector3.right;
        dummyObject.layer = LayerMask.NameToLayer("Monster"); dummyObject.AddComponent<BoxCollider2D>().size = new Vector2(.4f, .8f);
        var dummy = dummyObject.AddComponent<BehindTheGear.HideOut.HideOutDummy>();
        var melee = actor.GetComponent<PlayerMeleeAttack>();
        Set(melee, "monsterFilter", new ContactFilter2D { useLayerMask = true, layerMask = 1 << LayerMask.NameToLayer("Monster"), useTriggers = false });
        Physics2D.SyncTransforms(); melee.BeginComboSwing(); melee.ResolveComboHit(1);
        Check(!skills.IsStealthed && dummy.LastDamage == 20 && melee.ResolvedDamage == 10,
            "First stealth swing deals 20 final damage while baseline melee stays 10");
        melee.BeginComboSwing(); melee.ResolveComboHit(2);
        Check(dummy.LastDamage == 10 && dummy.Hits == 2, "The next swing receives no lingering stealth bonus");
        Object.Destroy(dummyObject);

        // Move the isolated actor away from the combat fixtures to observe unimpeded ballistics.
        actor.transform.position += Vector3.right * 50; Physics2D.SyncTransforms();
        Set(grenades, "readyAt", 0f);
        int ammo = grenades.Remaining;
        Check(grenades.BeginAim(), "Grenade can begin a fresh aimed throw");
        grenades.CancelAim();
        Check(grenades.Remaining == ammo && !grenades.TryThrow(), "Cancelling aim consumes nothing and a release without aiming throws nothing");
        Check(grenades.BeginAim(), "Grenade can aim again after cancellation");
        var beforeThrow = Object.FindObjectsByType<PlayerGrenadeProjectile>(FindObjectsSortMode.None);
        Check(grenades.TryThrow(), "Releasing an accepted aim launches a physical grenade");
        var projectile = Object.FindObjectsByType<PlayerGrenadeProjectile>(FindObjectsSortMode.None).Except(beforeThrow).Single();
        var projectileBody = projectile.GetComponent<Rigidbody2D>();
        Vector2 start = projectileBody.position, velocity = projectileBody.linearVelocity;
        Check(Physics2D.GetIgnoreCollision(projectile.GetComponent<CircleCollider2D>(), actor.GetComponent<BoxCollider2D>()), "Thrown body ignores the thrower's collider while its explosion can still hurt the player");
        yield return new WaitForSeconds(.15f);
        Check(projectileBody.position.x > start.x + .3f && projectileBody.linearVelocity.y < velocity.y,
            "Actual Rigidbody2D projectile travels forward and bends downward under gravity");
        yield return new WaitForSeconds(1.6f);
        Check(projectile == null, "Grenade detonates and cleans up after its real fuse");

        // Use real monster receivers, including duplicate colliders, for one simultaneous blast.
        var blastLeft = Monster(new Vector2(11.3f, 0));
        var blastRight = Monster(new Vector2(12.7f, 0));
        var blastOutside = Monster(new Vector2(17, 0));
        blastLeft.gameObject.AddComponent<CircleCollider2D>().radius = .3f;
        Physics2D.SyncTransforms(); yield return null;
        var blast = PlayerGrenadeProjectile.Launch((Vector2)actor.transform.position + Vector2.right * 12, Vector2.zero, 0, 5, 24, 2.4f, 15, 1.6f, actor);
        blast.Explode(); blast.Explode();
        Check(blastLeft.CurrentHealth == 76 && blastRight.CurrentHealth == 76 && blastOutside.CurrentHealth == 100,
            "One grenade deals 24 to every nearby monster once, excluding distant enemies and duplicate colliders");
        yield return new WaitForSeconds(.25f);
        health.RestoreHealth(100); Set(health, "invincibilityEndTime", 0f);
        PlayerGrenadeProjectile.Launch(actor.transform.position, Vector2.zero, 0, .08f, 24, 2.4f, 15, 1.6f, actor);
        yield return new WaitForSeconds(.15f);
        Check(health.CurrentHealth == 6, "Close explosion uses existing PlayerHealth and default defense for 4 self damage");
        PlayerGrenadeProjectile.Launch((Vector2)actor.transform.position + Vector2.right * 4, Vector2.zero, 0, .08f, 24, 2.4f, 15, 1.6f, actor);
        yield return new WaitForSeconds(.15f);
        Check(health.CurrentHealth == 6, "Player outside the danger radius takes no self damage");
        while (grenades.Remaining > 0)
        {
            Set(grenades, "readyAt", 0f);
            Check(grenades.BeginAim() && grenades.TryThrow(), "A remaining carried grenade can be consumed exactly once");
        }
        Check(grenades.Notice == "수류탄을 모두 소진했습니다." && grenades.NoticeRemaining > 0,
            "Fifth throw immediately publishes the exhausted ammunition message");
        Check(!grenades.BeginAim() && !grenades.TryThrow() && grenades.Remaining == 0, "Empty supply cannot create a sixth grenade");
        yield return null;
        Check(hud.transform.Find("Grenade Notice").GetComponent<TMPro.TMP_Text>().text == grenades.Notice
            && hud.transform.Find("Skill_EX/Skill MP").GetComponent<TMPro.TMP_Text>().text == "수류탄 0/5", "HUD displays exhausted message and zero remaining grenades");
        grenades.enabled = false; grenades.enabled = true;
        Check(grenades.Remaining == 0, "Disable and enable never refills ammunition");
        string emptySave = grenades.CaptureJson();
        bool invalidRejected = false;
        try { grenades.RestoreJson("{\"version\":1,\"remaining\":6}"); } catch (ArgumentException) { invalidRejected = true; }
        Check(invalidRejected && grenades.Remaining == 0, "Invalid saved ammunition is rejected without mutating the current supply");
        grenades.RestoreJson(emptySave);
        Check(grenades.Remaining == 0, "Existing save participant restores empty ammunition without an automatic refill");
        ResetCast(skills); health.TakeDamage(1000);
        Check(health.IsDead && !skills.TryUseStealth() && !skills.TryUseShockwave() && !grenades.BeginAim(), "Dead player cannot cast skills or aim grenades");
    }

    private static void Finish(string result, Exception error)
    {
        Application.logMessageReceived -= Observe;
        if (error != null) Report.AppendLine(error.ToString());
        Report.AppendLine(result + "; Unity " + Application.unityVersion + "; " + checks + " checks; " + errors + " runtime errors.");
        Directory.CreateDirectory(Output); File.WriteAllText(Output + "result.txt", Report.ToString());
        if (keyboardObject != null)
        {
            var schemeType = typeof(PlayerControlSchemeSettings);
            schemeType.GetField("loaded", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, wasSchemeLoaded);
            schemeType.GetField("current", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, oldScheme);
            Object.DestroyImmediate(keyboardObject);
        }
        if (hud != null) Object.Destroy(hud);
        if (actor != null) Object.Destroy(actor);
        SessionState.SetBool(Armed, false); running = false;
        EditorApplication.isPlaying = false;
        EditorApplication.isPaused = wasPaused;
        Application.runInBackground = wasBackground;
    }
}
#endif
