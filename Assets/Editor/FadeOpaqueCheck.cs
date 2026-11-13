using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class FadeOpaqueCheck
{
    const string Key="FadeOpaqueCheck.Active";
    const string Output="Temp/FadeOpaqueFix/results.txt";
    static double started;
    static int stage;
    static bool loaded;
    static bool captured;
    static FadeOpaqueCheck() { EditorApplication.update+=Tick; }
    public static void Run()
    {
        if(Application.unityVersion!="6000.3.23f1") throw new Exception("Wrong editor version");
        started=0; stage=0; loaded=false; captured=false;
        Directory.CreateDirectory("Temp/FadeOpaqueFix");
        File.WriteAllText(Output,"Unity "+Application.unityVersion+"\n");
        SessionState.SetBool(Key,true); EditorApplication.isPlaying=true;
    }
    static Image Cover()
    {
        var fade=UnityEngine.Object.FindFirstObjectByType<SceneFadeTransition>();
        return fade==null?null:(Image)typeof(SceneFadeTransition).GetField("fadeImage",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(fade);
    }
    static void Check(bool ok,string message)
    {
        if(!ok) throw new Exception(message);
        File.AppendAllText(Output,"PASS "+message+"\n");
    }
    static void OnLoaded(Scene scene,LoadSceneMode mode)
    {
        if(scene.name!="Market") return;
        try
        {
            var cover=Cover();
            Check(cover!=null && cover.color==Color.black,"Scene activation occurs behind 100% opaque black");
            Check(cover.canvas.renderMode==RenderMode.ScreenSpaceOverlay && cover.canvas.sortingOrder==short.MaxValue,
                "Screen overlay remains above new scene");
            Check(cover.rectTransform.anchorMin==Vector2.zero && cover.rectTransform.anchorMax==Vector2.one
                && cover.rectTransform.offsetMin==Vector2.zero && cover.rectTransform.offsetMax==Vector2.zero,"Black cover fills entire canvas");
            loaded=true;
            UnityEngine.Object.FindFirstObjectByType<SceneFadeTransition>().StartCoroutine(CheckBlackFrame());
        }
        catch(Exception e) { Fail(e); }
    }
    static void Fail(Exception e)
    {
        File.AppendAllText(Output,"FAIL "+e+"\n"); SessionState.SetBool(Key,false); Finish(1);
    }
    static void Finish(int code) { if(Application.isBatchMode) EditorApplication.Exit(code); else EditorApplication.isPlaying=false; }
    static System.Collections.IEnumerator CheckBlackFrame()
    {
        yield return new WaitForEndOfFrame();
        var texture=ScreenCapture.CaptureScreenshotAsTexture();
        try
        {
            bool black=true;
            var pixels=texture.GetPixels32();
            for(int i=0;i<pixels.Length;i+=97)
                if(pixels[i].r>1 || pixels[i].g>1 || pixels[i].b>1) { black=false; break; }
            Check(black,"Rendered scene-change frame is entirely black (pixel samples)");
            captured=true;
        }
        catch(Exception e) { Fail(e); }
        finally { UnityEngine.Object.Destroy(texture); }
    }
    static void Tick()
    {
        if(!EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode && File.Exists("Temp/FadeOpaqueFix/run-check"))
        { File.Delete("Temp/FadeOpaqueFix/run-check"); Run(); return; }
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying) return;
        try
        {
            if(started==0) started=EditorApplication.timeSinceStartup;
            if(EditorApplication.timeSinceStartup-started>55) throw new Exception("Timeout stage "+stage);
            if(stage==0 && EditorApplication.timeSinceStartup-started>2 && SceneManager.GetActiveScene().name=="ControlSchemeSelect")
            {
                SaveGameSession.BeginNewGame(); SceneManager.sceneLoaded+=OnLoaded;
                SaveGameSession.StartExpedition("Market"); stage=1;
            }
            else if(stage==1 && loaded && captured && !SceneFadeTransition.IsRunning)
            {
                Check(Cover().color.a==0,"Black fades away only after scene activation completes");
                Check(Time.timeScale==1,"World pause restored");
                File.AppendAllText(Output,"COMPLETE\n"); SessionState.SetBool(Key,false);
                SceneManager.sceneLoaded-=OnLoaded; Finish(0);
            }
        }
        catch(Exception e) { Fail(e); }
    }
}
