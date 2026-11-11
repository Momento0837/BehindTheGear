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
public static class ControlSchemeStartupCheck
{
    const string Folder="Temp/control-startup-validation", Active="BTG.SingleControlStartup";
    static int phase;
    static double deadline, next;
    static ControlSchemeStartupCheck(){EditorApplication.update+=Tick;}
    [MenuItem("Tools/Checks/Control Scheme Startup")]
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
            CheckInput();
            Finish("PASS Unity "+Application.unityVersion+"\nMain menu -> save slot -> Forest without control selection. WASD/Q/E/Space input works; arrow and ZXCV gameplay input ignored; obsolete control preference cannot select another layout. Real save slot untouched.");
        }
        catch(Exception e){Finish("FAIL\n"+e);Debug.LogException(e);}
    }
    static void CheckInput()
    {
        var keyboard=InputSystem.AddDevice<Keyboard>();
        var root=new GameObject("Single Controls Input Validation");
        try
        {
            var input=root.AddComponent<PlayerInputReader>();
            int attacks=0, jumps=0, skills=0;float movement=0;bool crouch=false;
            input.BasicAttackPressed+=()=>attacks++;input.SecondaryAttackPressed+=()=>attacks++;
            input.SkillOnePressed+=()=>skills++;input.SkillTwoPressed+=()=>skills++;
            input.JumpPressed+=()=>jumps++;input.MoveChanged+=v=>movement=v;input.CrouchChanged+=v=>crouch=v;
            var update=typeof(PlayerInputReader).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic);
            PlayerControlSchemeSettings.Select(PlayerControlScheme.ArrowKeys);
            Require(PlayerControlSchemeSettings.Current==PlayerControlScheme.WasdMouseQe,"Old selection cannot change fixed layout");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.LeftArrow,Key.UpArrow,Key.DownArrow,Key.Z,Key.X,Key.C,Key.V));InputSystem.Update();keyboard.MakeCurrent();
            update.Invoke(input,null);
            Require(movement==0&&!crouch&&attacks==0&&skills==0&&jumps==0,"Arrow/ZXCV keys ignored");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.D,Key.Space,Key.Q,Key.E));InputSystem.Update();keyboard.MakeCurrent();
            update.Invoke(input,null);
            Require(movement==1&&jumps==1&&skills==2,$"WASD Space Q E active: movement={movement} jumps={jumps} skills={skills} suppressed={InventoryUI.GameplayInputSuppressedThisFrame} menu={PlayerSkillDevelopmentMenu.IsOpen}");
        }
        finally{UnityEngine.Object.Destroy(root);InputSystem.RemoveDevice(keyboard);}
    }
}
