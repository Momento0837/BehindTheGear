#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class MouseCombatCheck
{
    const string Folder="Temp/MouseCombatValidation", Key="BTG.MouseCombatCheck";
    static IEnumerator routine;
    static double deadline;
    static Mouse mouse;
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static MouseCombatCheck(){EditorApplication.update+=Tick;}
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static object Read(object o,string field)=>o.GetType().GetField(field,Flags).GetValue(o);
    static object Call(object o,string method,params object[] args)=>o.GetType().GetMethod(method,Flags).Invoke(o,args);
    [MenuItem("Tools/Checks/Mouse Combat")]
    public static void Run(){if(Application.unityVersion!="6000.3.23f1"||EditorApplication.isPlayingOrWillChangePlaymode)return;Directory.CreateDirectory(Folder);SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;}
    static void Tick()
    {
        if(!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&File.Exists(Folder+"/run")){File.Delete(Folder+"/run");Run();}
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try{
            if(routine==null){routine=Check();deadline=EditorApplication.timeSinceStartup+45;}
            Require(EditorApplication.timeSinceStartup<deadline,"Timeout");
            if(!routine.MoveNext())Finish("PASS Unity "+Application.unityVersion+"\nHalf HP start/restart; existing HP preserved on normal travel; authored cursor sprite reference and nonblocking canvas; left/right mouse aim aligns sprite and hitbox; movement cannot reverse pending swing.");
        }catch(Exception e){Finish("FAIL\n"+e);Debug.LogException(e);}
    }
    static void Finish(string result){if(mouse!=null){InputSystem.RemoveDevice(mouse);mouse=null;}File.WriteAllText(Folder+"/result.txt",result);routine=null;SessionState.SetBool(Key,false);EditorApplication.isPlaying=false;}
    static IEnumerator Check()
    {
        EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/Main/Market.unity",new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
        var player=UnityEngine.Object.FindFirstObjectByType<PlayerMeleeAttack>();
        Require(player!=null,"Player present");
        var health=player.GetComponent<PlayerHealth>();
        Require(health.CurrentHealth==Mathf.CeilToInt(health.MaxHealth*.5f),"Starts at half health");
        health.RestoreSavedHealth(0);health.ReviveAfterSceneRestart();
        Require(health.CurrentHealth==Mathf.CeilToInt(health.MaxHealth*.5f),"Restarts at half health");
        health.RestoreSavedHealth(1);health.ReviveAfterSceneRestart();Require(health.CurrentHealth==1,"Alive HP preserved");
        var style=Resources.Load<GameCursorStyle>("UI/GameCursorStyle");
        Require(style!=null&&style.sprite!=null&&AssetDatabase.GetAssetPath(style.sprite)=="Assets/!UI/Other/Curser.png","Original cursor sprite resolves");
        var cursor=UnityEngine.Object.FindFirstObjectByType<GameMouseCursor>();
        Require(cursor!=null,"Cursor created automatically");
        var image=cursor.GetComponentInChildren<Image>();
        Require(image!=null&&!image.raycastTarget&&cursor.GetComponent<GraphicRaycaster>()==null,"Cursor does not block UI");
        mouse=InputSystem.AddDevice<Mouse>();
        Vector2 screen=Camera.main.WorldToScreenPoint(player.transform.position);
        foreach(float direction in new[]{-1f,1f})
        {
            InputState.Change(mouse.position,screen+Vector2.right*direction*100f);
            mouse.MakeCurrent();
            player.BeginComboSwing();
            Require((float)Read(player,"facingDirection")==direction,"Mouse selects attack side");
            Require(player.GetComponent<SpriteRenderer>().flipX==(direction<0),"Sprite faces swing direction");
            Vector2 centre=(Vector2)Call(player,"GetAttackCenter");
            Require(Mathf.Sign(centre.x-player.GetComponent<BoxCollider2D>().bounds.center.x)==direction,"Hitbox matches direction");
            Call(player,"UpdateFacing",-direction);
            Require((float)Read(player,"facingDirection")==direction,"Movement cannot reverse swing");
            player.CancelComboSwing();
        }
        if(Application.isFocused)
        {
            InputState.Change(mouse.position,new Vector2(Screen.width*.5f,Screen.height*.5f));
            Call(cursor,"LateUpdate");
            Require(image.enabled&&Vector2.Distance(image.transform.position,mouse.position.ReadValue())<.01f&&!Cursor.visible,"Cursor follows pointer");
        }
    }
}
#endif
