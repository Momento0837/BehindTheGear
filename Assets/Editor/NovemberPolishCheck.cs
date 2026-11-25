#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class NovemberPolishCheck
{
    const string Folder = "Temp/Nov19", Key = "BTG.Nov19.Check";
    static int stage;
    static double next, deadline;
    static PlayerMovement2D player;
    static ControlTutorialUI tutorial;
    static ScenePortal2D portal;
    static Vector3 origin;
    static Image image;
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static NovemberPolishCheck() { EditorApplication.update += Tick; }
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static object Read(object value, string field) => value.GetType().GetField(field, Private).GetValue(value);
    static void Finish(string value)
    {
        File.WriteAllText(Folder + "/verification.txt", value);
        SessionState.SetBool(Key, false); EditorApplication.isPlaying = false; stage = 0;
    }
    static void Tick()
    {
        if (!EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode && File.Exists(Folder + "/check"))
        {
            File.Delete(Folder + "/check");
            if (Application.unityVersion != "6000.3.23f1") { File.WriteAllText(Folder + "/verification.txt", "FAIL wrong Unity version"); return; }
            for (int i=0;i<SceneManager.sceneCount;i++)
            {
                var scene=SceneManager.GetSceneAt(i);
                if(scene.isDirty) EditorSceneManager.SaveScene(scene, Folder + "/BeforeCheck-"+i+"-"+scene.name+".unity", true);
            }
            SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
        }
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if(stage==0) { deadline=EditorApplication.timeSinceStartup+60; EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/Main/Forest.unity", new LoadSceneParameters(LoadSceneMode.Single)); stage=1; next=EditorApplication.timeSinceStartup+3; return; }
            Require(EditorApplication.timeSinceStartup<deadline, "Runtime validation timeout");
            if(EditorApplication.timeSinceStartup<next) return;
            if(stage==1)
            {
                player=UnityEngine.Object.FindFirstObjectByType<PlayerMovement2D>();
                if(player==null)return;
                var w=ControlHintAnimation.Load("W"); var f=ControlHintAnimation.Load("F");
                Require(w.Frame(0)!=null && w.Frame(0)!=w.Frame(.2f), "W frames must animate");
                Require(w.Frame(0).texture!=f.Frame(0).texture && w.Size==f.Size, "W and F must be independent placeholder clips");
                var prefab=Resources.Load<GameObject>("ControlHints/Portal_W_Tutorial");
                Require(prefab!=null && prefab.GetComponent<PortalTutorialVisual>()!=null, "W prefab connection");
                var cloak=Resources.Load<InventoryItemDefinition>("Story/TutorialGear");
                Require(AssetDatabase.GetAssetPath(cloak.Icon)=="Assets/!UI/Other/Tattered_Cloak.png", "Cloak must not fall back to gear");
                Require(Resources.Load<InventoryItemDefinition>("Story/OldBallpointPen")!=null, "Pen reward exists");
                var shader=Shader.Find("BehindTheGear/World Chromatic Motion");
                Require(shader!=null && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "World edge haze shader");
                foreach(var monster in UnityEngine.Object.FindObjectsByType<MonsterFollowController>(FindObjectsSortMode.None))
                {
                    var range=(BoxCollider2D)Read(monster,"attackRange");
                    var sensor=monster.transform.Find("Attack Trigger").GetComponent<BoxCollider2D>();
                    Require(Mathf.Abs(sensor.size.y-range.size.y*.7f)<.001f, "All originals/clones use 70 percent vertical attack range");
                }
                var health=player.GetComponent<PlayerHealth>();
                health.RestoreHealth(health.MaxHealth);
                typeof(PlayerMovement2D).GetMethod("StartAttack",Private).Invoke(player,new object[]{1});
                Require(player.IsAttacking,"Attack started");
                health.TakeDamage(health.Defense+1);
                Require(player.IsAttacking,"Earlier attack survives nonlethal damage");
                player.enabled=false; player.GetComponent<Rigidbody2D>().simulated=false;
                var bounds=player.GetComponent<PlayerMapBounds2D>(); if(bounds!=null)bounds.enabled=false;
                origin=player.transform.position;
                var host=new GameObject("Portal Test");host.SetActive(false);
                host.transform.position=player.GetComponent<Collider2D>().bounds.center;
                host.AddComponent<BoxCollider2D>().size=new Vector2(6,8);
                host.GetComponent<BoxCollider2D>().isTrigger=true;
                portal=host.AddComponent<ScenePortal2D>();
                typeof(ScenePortal2D).GetField("hintUsed",Private).SetValue(portal,true);
                host.SetActive(true); Physics2D.SyncTransforms();
                Require(portal.ContainsPlayer(player.gameObject),"Portal overlap");
                tutorial=new GameObject("Portal UI Test").AddComponent<ControlTutorialUI>();
                tutorial.ArmPortal(host); tutorial.Begin(player,true);
                stage=2;next=EditorApplication.timeSinceStartup+1.2;
            }
            else if(stage==2)
            {
                image=tutorial.PresentationRoot.GetComponentsInChildren<Image>().First(i=>i.name.StartsWith("Portal_W_Tutorial"));
                Require(image.enabled && image.sprite!=null && Mathf.Abs(image.rectTransform.anchoredPosition.x-24)<.1f,"W prefab enters from left");
                player.transform.position=origin+Vector3.right*20; Physics2D.SyncTransforms();
                Require(!portal.ContainsPlayer(player.gameObject),"Outside portal range");
                stage=3;next=EditorApplication.timeSinceStartup+.15;
            }
            else if(stage==3)
            {
                Require(image.enabled && image.rectTransform.anchoredPosition.x<24,"Exit keeps animating");
                player.transform.position=origin; Physics2D.SyncTransforms();
                stage=4;next=EditorApplication.timeSinceStartup+1.5;
            }
            else if(stage==4)
            {
                Require(image.enabled && Mathf.Abs(image.rectTransform.anchoredPosition.x-24)<.1f,"Reentry completes after full exit");
                player.transform.position=origin+Vector3.right*20;Physics2D.SyncTransforms();
                stage=5;next=EditorApplication.timeSinceStartup+.7;
            }
            else if(stage==5)
            {
                Require(!image.enabled,"Portal UI hides outside range");
                typeof(ScenePortal2D).GetField("glowFacesRight",Private).SetValue(portal,false);
                player.transform.position=origin;Physics2D.SyncTransforms();
                stage=6;next=EditorApplication.timeSinceStartup+1.2;
            }
            else
            {
                Require(image.enabled && image.rectTransform.pivot.x==1 && Mathf.Abs(image.rectTransform.anchoredPosition.x+24)<.1f,"Right portal UI enters from right");
                Finish("PASS Unity "+Application.unityVersion+"\nW/F independent animated clips; W prefab left/right entrance, complete exit/reentry, outside-range hide; cloak icon and pen; world edge haze shader; monster vertical ranges including clones; nonlethal damage preserves earlier attack.\nManual checks remain: full story travel, garbage landing, dialogue appearance/choice timing, fog appearance and cinematic movement.");
            }
        }
        catch(Exception e) { Finish("FAIL\n"+e); UnityEngine.Debug.LogException(e); }
    }
}
#endif
