using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ReviveAnimationCheck
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Checks/Revive Animation")]
    public static void Run()
    {
        const string result = "Temp/ReviveAnimationValidation/result.txt";
        Directory.CreateDirectory(Path.GetDirectoryName(result));
        Scene preview = default;
        PlayerMovement2D movement = null;
        try
        {
            Require(Application.unityVersion == "6000.3.23f1" && !EditorApplication.isPlayingOrWillChangePlaymode, "Requires edit mode in Unity 6000.3.23f1");
            preview = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Revive Check Player");
            SceneManager.MoveGameObjectToScene(root, preview);
            root.AddComponent<SpriteRenderer>();
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/!Animation/Player/Player.controller");
            var health = root.AddComponent<PlayerHealth>();
            var control = root.GetComponent<PlayerControlLock>();
            movement = root.AddComponent<PlayerMovement2D>();
            Set(control, "controlsToDisable", new Behaviour[] { movement });
            Set(control, "playerBody", root.GetComponent<Rigidbody2D>());
            Call(control, "Awake");
            Call(health, "Awake");
            Call(movement, "Awake");
            Set(health, "invincibilityDuration", 0f);
            health.SetMaxHealthBonus("ReviveCheck", 5);
            animator.Rebind();
            animator.Update(0f);

            for (int attempt = 0; attempt < 3; attempt++)
            {
                health.TakeDamage(1000);
                Require(health.IsDead && !movement.enabled, "Death locks movement");
                // Both a pending Dead trigger and an already-playing Dead state must reset.
                if (attempt > 0)
                {
                    animator.Play("Base Layer.Dead", 0, .4f);
                    animator.Update(0f);
                    Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Dead"), "Death animation entered");
                }
                health.ReviveAfterSceneRestart();
                Require(!health.IsDead && health.CurrentHealth == health.MaxHealth && movement.enabled, "Revive restores HP including bonuses and controls");
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Idle"), "Revive immediately evaluates Idle");
                animator.Update(.1f);
                animator.Update(3f);
                Require(!animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Dead"), "Pending trigger did not replay death");
            }
            File.WriteAllText(result, "PASS Unity " + Application.unityVersion + "\nThree death/revive cycles, pending Dead trigger, already-playing Dead, immediate Idle, no replay after 3 seconds, HP bonuses and control restore. Isolated preview; no scenes saved.\n");
            Debug.Log("REVIVE_ANIMATION_CHECK_PASS");
        }
        catch (Exception error)
        {
            File.WriteAllText(result, "FAIL\n" + error);
            Debug.LogException(error);
        }
        finally
        {
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static void Call(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
