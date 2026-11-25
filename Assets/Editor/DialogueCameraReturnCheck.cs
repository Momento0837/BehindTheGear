#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
[InitializeOnLoad]
public static class DialogueCameraReturnCheck
{
    const string Key="BTG.DialogueCameraReturnCheck",Folder="Temp/Nov19/";
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static int stage;static double next,deadline;
    static PlayerMovement2D player;static StorySceneFlow flow;static ConversationDialogue dialogue;
    static object Read(object o,string n)=>o.GetType().GetField(n,Flags).GetValue(o);
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static DialogueCameraReturnCheck(){EditorApplication.update+=Tick;}
    static void Finish(string message){File.WriteAllText(Folder+"dialogue-camera-return-result.txt",message);SessionState.SetBool(Key,false);EditorApplication.isPlaying=false;stage=0;}
    static void Tick()
    {
        if(!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&File.Exists(Folder+"check-dialogue-camera-return"))
        {
            File.Delete(Folder+"check-dialogue-camera-return");if(Application.unityVersion!="6000.3.23f1")throw new Exception("Wrong Unity version");
            for(int i=0;i<SceneManager.sceneCount;i++){var scene=SceneManager.GetSceneAt(i);if(scene.isDirty)EditorSceneManager.SaveScene(scene,Folder+"BeforeDialogueCameraReturn-"+i+".unity",true);}
            stage=-2;SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
        }
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try
        {
            double now=EditorApplication.timeSinceStartup;
            if(stage==-2){stage=-1;next=now+2;return;}if(now<next)return;
            if(stage==-1){SaveGameSession.BeginNewGame();stage=0;next=now+.2;return;}
            if(stage==0){deadline=now+65;SceneManager.LoadScene("Market");stage=1;next=now+1;return;}
            Require(now<deadline,"Timeout stage "+stage);
            if(stage==1)
            {
                player=UnityEngine.Object.FindFirstObjectByType<PlayerMovement2D>();flow=UnityEngine.Object.FindFirstObjectByType<StorySceneFlow>();if(player==null||flow==null)return;
                dialogue=(ConversationDialogue)Read(flow,"dialogue");if(dialogue==null)return;
                if(dialogue.IsPlaying){dialogue.GetType().GetMethod("RequestAdvance",Flags).Invoke(dialogue,null);return;}
                if((string)Read(flow,"currentGuide")!="garbage_search"||StorySceneFlow.GarbageArrivalOwnsMotion)return;
                var portal=((GameObject)Read(flow,"portal")).GetComponent<ScenePortal2D>();
                player.StartCoroutine(PortalDeparture.Travel(player,portal,()=> (IEnumerator)flow.GetType().GetMethod("EnterMarket",Flags).Invoke(flow,null)));
                stage=2;return;
            }
            if(stage==2)
            {
                if(SceneFadeTransition.IsRunning || PortalArrival.IsEntering || player.GetComponent<PlayerControlLock>().IsLocked)return;
                ((MarketNpcBubble)Read(flow,"marketBubble"))?.RestoreCompletedGreeting();
                flow.GetType().GetMethod("PlacePlayer",Flags).Invoke(flow,new object[]{new Vector3(70,-2.5f,0)});
                stage=3;next=now+3;return;
            }
            var camera=Camera.main;var follow=camera.GetComponent<PlayerCameraFollow2D>();
            string State()=>"player="+player.transform.position+" camera="+camera.transform.position+" viewport="+camera.WorldToViewportPoint(player.transform.position)+" lens="+camera.fieldOfView+" clamp="+follow.GetType().GetMethod("ClampHorizontalPosition",Flags).Invoke(follow,new object[]{player.transform.position.x})+" bounds="+Read(follow,"horizontalWorldBounds")+" target="+((Transform)Read(follow,"target")).name;
            if(stage==3){File.WriteAllText(Folder+"dialogue-camera-trace.txt","before "+State()+"\n");follow.BeginDialogueFocus(dialogue,player.transform);stage=4;next=now+4;return;}
            if(stage==4){File.AppendAllText(Folder+"dialogue-camera-trace.txt","focused "+State()+"\n");follow.EndDialogueFocus(dialogue);stage=5;next=now+1;return;}
            if(stage==5){Require(Mathf.Abs(camera.WorldToViewportPoint(player.transform.position).x-.5f)<.01f,"Player returns to screen center within one second while zooming out");File.AppendAllText(Folder+"dialogue-camera-trace.txt","after "+State()+"\n");Finish("PASS: Player returns to horizontal center within one second after dialogue focus ends.");}
        }        catch(Exception e){Finish("FAIL\n"+e);Debug.LogException(e);}
    }
}
#endif
