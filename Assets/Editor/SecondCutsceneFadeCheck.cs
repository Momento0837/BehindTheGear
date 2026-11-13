using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

[InitializeOnLoad]
public static class SecondCutsceneFadeCheck
{
    const string Root="Temp/SecondCutsceneFadeFix/";
    const string Key="SecondCutsceneFadeCheck.Active";
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static int stage;
    static double started,since;
    static CutsceneVideoSequence movie;
    static bool arrived;
    static string Mode=>SessionState.GetString(Key+".Mode","skip");
    static string Output=>Root+Mode+"-results.txt";
    static SecondCutsceneFadeCheck()=>EditorApplication.update+=Tick;
    static object Get(object target,string name)=>target.GetType().GetField(name,Flags).GetValue(target);
    static Image Cover()=> (Image)Get(UnityEngine.Object.FindFirstObjectByType<SceneFadeTransition>(),"fadeImage");
    static void Check(bool ok,string text)
    { if(!ok)throw new Exception(text); File.AppendAllText(Output,"PASS "+text+"\n"); }
    static void Next(){stage++;since=EditorApplication.timeSinceStartup;}
    static void Stop(Exception error=null)
    {
        File.AppendAllText(Output,error==null?"COMPLETE\n":"FAIL "+error+"\n");
        SessionState.SetBool(Key,false);SceneManager.sceneLoaded-=Loaded;EditorApplication.isPlaying=false;
    }
    static void Loaded(Scene scene,LoadSceneMode mode)
    {
        if(scene.name!="Forest")return;
        try {Check(Cover().color==Color.black,"Destination activates only under opaque black");arrived=true;}
        catch(Exception e){Stop(e);}
    }
    static void Tick()
    {
        if(!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&!EditorApplication.isPlayingOrWillChangePlaymode&&File.Exists(Root+"run-check"))
        {
            if(Application.unityVersion!="6000.3.23f1")return;
            SessionState.SetString(Key+".Mode",File.ReadAllText(Root+"run-check").Trim());File.Delete(Root+"run-check");
            File.WriteAllText(Output,"Unity "+Application.unityVersion+"\n");
            started=since=0;stage=0;arrived=false;SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;return;
        }
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying)return;
        try
        {
            if(started==0)started=since=EditorApplication.timeSinceStartup;
            double elapsed=EditorApplication.timeSinceStartup-since;
            if(EditorApplication.timeSinceStartup-started>60)throw new Exception("Timeout stage "+stage);
            if(stage==0&&elapsed>2&&SceneManager.GetActiveScene().name=="ControlSchemeSelect")
            {SaveGameSession.BeginNewGame();SaveGameSession.StartExpedition("Market");Next();}
            else if(stage==1&&SceneManager.GetActiveScene().name=="Market"&&!SceneFadeTransition.IsRunning)
            {
                var flow=UnityEngine.Object.FindFirstObjectByType<StorySceneFlow>();
                movie=UnityEngine.Object.FindFirstObjectByType<CutsceneVideoSequence>();
                flow.StartCoroutine((IEnumerator)typeof(StorySceneFlow).GetMethod("FinalCutscene",Flags).Invoke(flow,null));Next();
            }
            else if(stage==2&&elapsed>1&&((VideoPlayer)Get(movie,"videoPlayer")).isPlaying)
            {
                SceneManager.sceneLoaded+=Loaded;
                if(Mode=="normal")typeof(CutsceneVideoSequence).GetMethod("OnVideoFinished",Flags).Invoke(movie,new object[]{null});
                else typeof(CutsceneVideoSequence).GetMethod("Finish",Flags).Invoke(movie,new object[]{true});
                Check(((GameObject)Get(movie,"videoRoot")).activeSelf,"Video surface stays visible when ending starts");
                Check(!((VideoPlayer)Get(movie,"videoPlayer")).isPlaying,"Video frame pauses during fade");Next();
            }
            else if(stage==3&&elapsed>.25)
            {
                Check(SceneManager.GetActiveScene().name=="Market","Outgoing scene retained while darkening");
                Check(movie.IsPlaying&&((GameObject)Get(movie,"videoRoot")).activeSelf,"Video remains visible throughout fade to black");
                Check(Cover().color.a>0&&Cover().color.a<1,"Black overlay fades over video");Next();
            }
            else if(stage==4&&arrived&&!SceneFadeTransition.IsRunning)
            {
                Check(!CutsceneVideoSequence.IsAnyVisible,"Outgoing video closes under black");
                Check(Cover().color.a==0&&Time.timeScale==1,"New scene reveals and world resumes");Stop();
            }
        }
        catch(Exception e){Stop(e);}
    }
}
