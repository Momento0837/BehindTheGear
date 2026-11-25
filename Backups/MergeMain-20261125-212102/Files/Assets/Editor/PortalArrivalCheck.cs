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
public static class PortalArrivalCheck
{
    const string Key="BTG.PortalArrivalCheck",Folder="Temp/Nov19/";
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static int stage;static double next,deadline;static bool sawOffscreen,sawWalking;
    static PlayerMovement2D player;static StorySceneFlow flow;static ConversationDialogue dialogue;
    static object Read(object o,string n)=>o.GetType().GetField(n,Flags).GetValue(o);
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static PortalArrivalCheck(){EditorApplication.update+=Tick;}
    static void Finish(string message){File.WriteAllText(Folder+"portal-arrival-result.txt",message);SessionState.SetBool(Key,false);EditorApplication.isPlaying=false;stage=0;}
    static void Tick()
    {
        if(!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&File.Exists(Folder+"check-portal-arrival"))
        {
            File.Delete(Folder+"check-portal-arrival");if(Application.unityVersion!="6000.3.23f1")throw new Exception("Wrong Unity version");
            for(int i=0;i<SceneManager.sceneCount;i++){var scene=SceneManager.GetSceneAt(i);if(scene.isDirty)EditorSceneManager.SaveScene(scene,Folder+"BeforePortalArrival-"+i+".unity",true);}
            stage=-2;sawOffscreen=sawWalking=false;SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
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
            var body=player.GetComponent<Rigidbody2D>();var controls=player.GetComponent<PlayerControlLock>();
            if(stage==2)
            {
                if(!(bool)Read(flow,"marketMap"))return;
                if(SceneFadeTransition.IsRunning)
                {
                    Require(Camera.main.WorldToViewportPoint(player.transform.position).x<0,"Prepared outside left edge under black fade");sawOffscreen=true;return;
                }
                if(player.transform.position.x<48.99f)
                {
                    Require(sawOffscreen&&controls.IsLocked&&!body.simulated,"Entry remains cinematic and locked");
                    Require(!player.GetComponent<PlayerMapBounds2D>().enabled,"Entry bypasses bounds until inside");sawWalking=true;return;
                }
                Require(sawWalking&&Mathf.Abs(player.transform.position.x-49)<.02f,"Left entry reaches Market spawn");
                if(!body.simulated||controls.IsLocked)return;
                // Isolate the right-side motion from the NPC greeting that normally starts at x70.
                ((MarketNpcBubble)Read(flow,"marketBubble"))?.RestoreCompletedGreeting();
                flow.GetType().GetMethod("PlacePlayer",Flags).Invoke(flow,new object[]{new Vector3(93,-2.5f,0)});
                var arrival=PortalArrival.Prepare(player);
                Require(arrival!=null && Camera.main.WorldToViewportPoint(player.transform.position).x>1,"Right-side destination starts beyond right edge");
                player.StartCoroutine(arrival.Enter());stage=3;return;
            }
            if(stage==3 && body.simulated)
            {
                Require(Mathf.Abs(player.transform.position.x-93)<.02f,"Right entry reaches original spawn");
                Require(!controls.IsLocked&&player.GetComponent<InventoryUI>().enabled&&player.GetComponent<PlayerMapBounds2D>().enabled,"Input and bounds restored after arrival");
                Finish("PASS Unity "+Application.unityVersion+"; Garbage-to-Market portal prepares player left of camera during fade; inward cinematic walk reaches x49 with controls locked; right-side destination enters from outside right to x93; input/bounds restored. Special Garbage fall and cutscene/automatic travel have no arrival hook.");
            }
        }
        catch(Exception e){Finish("FAIL\n"+e);Debug.LogException(e);}
    }
}
#endif
