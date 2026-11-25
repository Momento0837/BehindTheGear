#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
[InitializeOnLoad]
public static class GarbageBallisticCheck
{
    const string Key="BTG.GarbageBallisticCheck", Folder="Temp/Nov19/";
    static int stage, samples;
    static double deadline, next;
    static PlayerMovement2D player;
    static Rigidbody2D body;
    static bool offscreen, launched;
    static float vx, previousYSpeed, previousX, launchTime;
    static GarbageBallisticCheck() { EditorApplication.update+=Tick; }
    static void Require(bool value,string message) { if(!value)throw new Exception(message); }
    static void Finish(string result) { File.WriteAllText(Folder+"garbage-fall-result.txt",result); SessionState.SetBool(Key,false); EditorApplication.isPlaying=false; stage=0; }
    static IEnumerator Travel()
    {
        bool done=false;
        SceneFadeTransition.LoadScene("Market",.3f,success=>{Require(success,"Market load");done=true;});
        while(!done)yield return null;
    }
    static void Tick()
    {
        if(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating && File.Exists(Folder+"check-garbage"))
        {
            File.Delete(Folder+"check-garbage");
            if(Application.unityVersion!="6000.3.23f1")throw new Exception("Wrong Unity version");
            for(int i=0;i<SceneManager.sceneCount;i++){var scene=SceneManager.GetSceneAt(i);if(scene.isDirty)EditorSceneManager.SaveScene(scene,Folder+"BeforeFallCheck-"+i+".unity",true);}
            SessionState.SetBool(Key,true); stage=0;samples=0;offscreen=launched=false;EditorApplication.isPlaying=true;
        }
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try
        {
            if(stage==0){deadline=EditorApplication.timeSinceStartup+60;EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/Main/Forest.unity",new LoadSceneParameters(LoadSceneMode.Single));stage=1;next=EditorApplication.timeSinceStartup+2;return;}
            Require(EditorApplication.timeSinceStartup<deadline,"Timeout stage "+stage);
            if(EditorApplication.timeSinceStartup<next)return;
            if(stage==1)
            {
                player=UnityEngine.Object.FindFirstObjectByType<PlayerMovement2D>();if(player==null)return;
                foreach(var flow in UnityEngine.Object.FindObjectsByType<StorySceneFlow>(FindObjectsSortMode.None))flow.StopAllCoroutines();
                foreach(var video in UnityEngine.Object.FindObjectsByType<CutsceneVideoSequence>(FindObjectsSortMode.None))video.enabled=false;
                body=player.GetComponent<Rigidbody2D>();
                var host=new GameObject("Test Exit Portal");host.AddComponent<BoxCollider2D>().isTrigger=true;
                var portal=host.AddComponent<ScenePortal2D>();
                player.StartCoroutine(PortalDeparture.Travel(player,portal,Travel));stage=2;return;
            }
            File.WriteAllText(Folder+"fall-trace.txt", "scene="+SceneManager.GetActiveScene().name+" owner="+StorySceneFlow.GarbageArrivalOwnsMotion+" fade="+SceneFadeTransition.IsRunning+" pos="+body.position+" vel="+body.linearVelocity+" simulated="+body.simulated+" offscreen="+offscreen+" launched="+launched+" samples="+samples+" expedition="+SaveGameSession.Expedition);
            if(!StorySceneFlow.GarbageArrivalOwnsMotion)return;
            var view=Camera.main.WorldToViewportPoint(player.transform.position);
            if(!launched && SceneFadeTransition.IsRunning)
            {Require(view.x>1 && view.y>1,"Player must start above and right of the viewport: "+view);offscreen=true;return;}
            if(SceneFadeTransition.IsRunning || !body.simulated)return;
            if(!launched)
            {
                if(body.linearVelocity.x>=-.01f)return;
                launched=true;launchTime=Time.time;vx=body.linearVelocity.x;previousYSpeed=body.linearVelocity.y;previousX=body.position.x;return;
            }
            if(Time.time-launchTime>.15f && player.IsGroundedForStory)
            {
                Require(offscreen,"Observed offscreen setup before fade reveal");
                Require(samples>=3,"Observed gravitational fall samples");
                Require(Mathf.Abs(body.position.x-23)<.35f,"Landing x remains original: "+body.position.x);
                Finish("PASS Unity "+Application.unityVersion+"; Forest departure -> Market fade -> offscreen upper-right start; constant horizontal launch velocity and gravity acceleration; no bounds clamp during flight; landing="+body.position+"; samples="+samples);return;
            }
            Require(!player.GetComponent<PlayerMapBounds2D>().enabled,"No bounds clamp during fall");
            Require(Mathf.Abs(body.linearVelocity.x-vx)<.02f,"Horizontal velocity preserved across portal cleanup");
            Require(body.linearVelocity.y<=previousYSpeed+.01f,"Gravity accelerates downward");
            Require(body.position.x<=previousX+.01f,"No rightward teleport midflight");
            previousX=body.position.x;previousYSpeed=body.linearVelocity.y;samples++;
        }
        catch(Exception e){Finish("FAIL\n"+e);Debug.LogException(e);}
    }
}
#endif
