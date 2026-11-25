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
public static class SelenaDialogueRenderCheck
{
    const string Key="BTG.SelenaDialogueRenderCheck",Folder="Temp/Nov19/";
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static int stage;static double next,deadline;
    static PlayerMovement2D player;static StorySceneFlow flow;static ConversationDialogue dialogue;
    static object Read(object o,string n)=>o.GetType().GetField(n,Flags).GetValue(o);
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static SelenaDialogueRenderCheck(){EditorApplication.update+=Tick;}
    static void Finish(string message){File.WriteAllText(Folder+"selena-render-result.txt",message);SessionState.SetBool(Key,false);EditorApplication.isPlaying=false;stage=0;}
    static void Tick()
    {
        if(!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&File.Exists(Folder+"check-selena-render"))
        {
            File.Delete(Folder+"check-selena-render");if(Application.unityVersion!="6000.3.23f1")throw new Exception("Wrong Unity version");
            for(int i=0;i<SceneManager.sceneCount;i++){var scene=SceneManager.GetSceneAt(i);if(scene.isDirty)EditorSceneManager.SaveScene(scene,Folder+"BeforeSelenaRender-"+i+".unity",true);}
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
            if(stage==3){dialogue.PlayFromLine(Resources.Load<TextAsset>("Dialogue/Forest_4/selena"),0);stage=4;next=now+2;return;}
            if(stage==4){
                var label=dialogue.BodyText;label.ForceMeshUpdate();
                Require(label.font.name=="SelenaDialogue","Selena uses padded same-face font");
                Require(label.fontSharedMaterial.mainTexture==label.font.atlasTexture,"Matching glyph atlas");
                Require(!label.fontSharedMaterial.IsKeywordEnabled("UNDERLAY_ON"),"No text background underlay");
                Require(label.textInfo.characterCount>0,"Korean text generated");
                ScreenCapture.CaptureScreenshot(Folder+"selena-render.png");stage=5;next=now+.5;return;
            }
            if(stage==5)Finish("PASS Unity "+Application.unityVersion+"; Selena Korean dialogue mesh rendered with matching atlas, padding and transparent material.");
        }        catch(Exception e){Finish("FAIL\n"+e);Debug.LogException(e);}
    }
}
#endif
