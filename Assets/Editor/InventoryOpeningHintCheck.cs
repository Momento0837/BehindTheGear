#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
[InitializeOnLoad]
public static class InventoryOpeningHintCheck
{
    const string Folder="Temp/InventoryOpeningHintValidation", Key="BTG.InventoryOpeningHintCheck";
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static int stage; static double next, deadline;
    static InventoryUI ui; static InventoryOpeningHint hint;
    static InventoryOpeningHintCheck(){EditorApplication.update+=Tick;}
    static object Read(object o,string name)=>o.GetType().GetField(name,Flags).GetValue(o);
    static void Call(object o,string name)=>o.GetType().GetMethod(name,Flags).Invoke(o,null);
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static void Finish(string result){File.WriteAllText(Folder+"/result.txt",result);SessionState.SetBool(Key,false);EditorApplication.isPlaying=false;stage=0;}
    static void Tick()
    {
        if(!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&File.Exists(Folder+"/run"))
        {
            File.Delete(Folder+"/run");if(Application.unityVersion!="6000.3.23f1")return;
            for(int i=0;i<SceneManager.sceneCount;i++){var scene=SceneManager.GetSceneAt(i);if(scene.isDirty)EditorSceneManager.SaveScene(scene,Folder+"/Unsaved-"+scene.name+".unity",true);}
            SessionState.SetBool(Key,true);stage=0;EditorApplication.isPlaying=true;
        }
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try
        {
            double now=EditorApplication.timeSinceStartup;
            if(stage==0){deadline=now+30;EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/Main/Market.unity",new LoadSceneParameters(LoadSceneMode.Single));stage=1;next=now+1;return;}
            Require(now<deadline,"Timeout");if(now<next)return;
            if(stage==1)
            {
                ui=UnityEngine.Object.FindFirstObjectByType<InventoryUI>();Require(ui!=null,"Inventory exists");
                Call(ui,"CloseInventory");
                ui.BeginEquipmentHint(Resources.Load<InventoryItemDefinition>("Story/TutorialGear"));
                hint=ui.GetComponent<InventoryOpeningHint>();Require(hint!=null,"Hint created");
                Require(Resources.Load<InventoryOpeningHintStyle>("UI/InventoryOpeningHintStyle").border!=null,"White_Border resolves");
                Require(Resources.Load<GameCursorStyle>("UI/GameCursorStyle").size==new Vector2(64,64),"Cursor doubled");
                stage=2;next=now+.7;return;
            }
            if(stage==2)
            {
                var dim=(Image)Read(hint,"dim");var border=(Image)Read(hint,"border");
                Require(dim!=null&&Mathf.Abs(dim.color.a-.3f)<.001f&&!dim.raycastTarget,"30 percent nonblocking dimmer");
                Require(border.enabled&&border.rectTransform.localScale.x>1&&border.rectTransform.localScale.x<1.5f,"Border enters from 150 percent");
                typeof(InventoryOpeningHint).GetField("elapsed",Flags).SetValue(hint,3.9f);Call(hint,"LateUpdate");Require(!border.enabled,"Two pulses then rest");
                typeof(InventoryOpeningHint).GetField("elapsed",Flags).SetValue(hint,4.9f);Call(hint,"LateUpdate");Require(border.enabled,"Pulse repeats after rest");
                ui.OpenInventory();Require(ui.IsOpen&&!border.enabled,"Opening immediately removes border");
                var hud=UnityEngine.Object.FindFirstObjectByType<InGameQuestHUD>();
                Require(hud!=null,"Quest HUD exists");
                Call(hud,"ShowDim");
                var questCanvas=(Canvas)Read(hud,"questForegroundCanvas");
                var questDim=((Image)Read(hud,"backgroundDim")).GetComponent<Canvas>();
                Require(questCanvas.sortingLayerID==ui.SceneCanvas.sortingLayerID
                    && questCanvas.sortingOrder<ui.SceneCanvas.sortingOrder
                    && questDim.sortingOrder<ui.SceneCanvas.sortingOrder,"Quest transition stays below inventory despite cursor/tutorial overlays");
                Call(hud,"HideDim");
                var cursor=UnityEngine.Object.FindFirstObjectByType<GameMouseCursor>();Call(cursor,"LateUpdate");
                Require(Cursor.visible&&!cursor.GetComponentInChildren<Image>().enabled,"Inventory uses Windows cursor");
                stage=3;next=now+.6;return;
            }
            Require(Read(hint,"overlay")==null || (GameObject)Read(hint,"overlay")==null,"Tutorial dim fades and cleans up");
            Finish("PASS Unity "+Application.unityVersion+"\nQuest text and transition dim remain below open inventory despite high-sorting cursor/tutorial canvases. Opening hint loop/dismissal and Windows cursor also verified.");
        }
        catch(Exception e){Finish("FAIL\n"+e);Debug.LogException(e);}
    }
}
#endif
