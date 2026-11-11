using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ForestSpawnCheck
{
    const string Folder="Temp/forest-spawn-validation", Active="BTG.ForestSpawnCheck";
    static int phase;
    static double deadline, next;
    static ForestSpawnCheck(){EditorApplication.update+=Tick;}
    [MenuItem("Tools/Checks/Forest Spawn")]
    public static void Run()
    {
        if(Application.unityVersion!="6000.3.23f1"||EditorApplication.isPlayingOrWillChangePlaymode)return;
        Directory.CreateDirectory(Folder);
        SessionState.SetBool(Active,true);phase=0;EditorApplication.isPlaying=true;
    }
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static void Finish(string result)
    {
        File.WriteAllText(Folder+"/result.txt",result);
        ManualSaveStore.UseValidationDirectory(null);
        SessionState.SetBool(Active,false);EditorApplication.isPlaying=false;phase=0;
    }
    static void Tick()
    {
        if(!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&File.Exists(Folder+"/run")){File.Delete(Folder+"/run");Run();}
        if(!SessionState.GetBool(Active,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try
        {
            double now=EditorApplication.timeSinceStartup;
            if(phase==0)
            {
                deadline=now+40; next=now+.5;
                ManualSaveStore.UseValidationDirectory(Path.GetFullPath(Folder+"/single-"+Guid.NewGuid().ToString("N")));
                SceneManager.LoadScene("ControlSchemeSelect");phase=1;return;
            }
            Require(now<deadline,"Startup timed out at "+phase);
            if(now<next||SceneFadeTransition.IsRunning)return;
            Require(!UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include,FindObjectsSortMode.None).Any(c=>c.name=="Control Selection Canvas"),"Control selection canvas removed");
            if(phase==1)
            {
                var main=UnityEngine.Object.FindFirstObjectByType<MainMenuController>();Require(main!=null,"Main menu remains");
                main.StartGame();phase=2;next=now+2;return;
            }
            if(phase==2)
            {
                var menu=UnityEngine.Object.FindFirstObjectByType<ManualSaveMenu>();Require(menu!=null,"Save menu remains");
                menu.NewGame();phase=3;next=now+2;return;
            }
            Require(SceneManager.GetActiveScene().name=="Forest","New game directly enters Forest");
            var player=UnityEngine.Object.FindFirstObjectByType<PlayerMovement2D>();
            Require(player!=null,"Forest player exists");
            var grounded=typeof(PlayerMovement2D).GetMethod("IsGrounded",BindingFlags.Instance|BindingFlags.NonPublic);
            Require((bool)grounded.Invoke(player,null),"Forest starts on the floor before cinematic physics resumes");
            float standingY=player.transform.position.y;
            player.transform.position+=Vector3.up*2;
            Require(player.SettleOnGround() && (bool)grounded.Invoke(player,null),"Ground placement also works while physics is paused");
            Require(Mathf.Abs(player.transform.position.y-standingY)<.02f,"Ground placement restores same standing height");

            Finish("PASS Unity "+Application.unityVersion+"\nForest new-game spawn grounded before cinematic; elevated player returns to same floor height without physics simulation. Main menu -> save slot -> Forest. Real save slot untouched.");
        }
        catch(Exception e){Finish("FAIL\n"+e);Debug.LogException(e);}
    }
}