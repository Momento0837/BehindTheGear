#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
[InitializeOnLoad]
public static class ControlHintsCheck
{
    const string Folder="Temp/ControlHintsValidation", Key="BTG.ControlHintsCheck";
    static double next, deadline;
    static int stage;
    static WorldInteractionHint hint;
    static PlayerMovement2D player;
    static float fixedY;
    static ControlTutorialUI tutorial;
    static ControlHintsCheck(){EditorApplication.update+=Tick;}
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static void Finish(string result){File.WriteAllText(Folder+"/result.txt",result);SessionState.SetBool(Key,false);EditorApplication.isPlaying=false;stage=0;}
    static void Tick()
    {
        if(!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&File.Exists(Folder+"/run"))
        {
            File.Delete(Folder+"/run");
            if(Application.unityVersion!="6000.3.23f1")return;
            for(int i=0;i<SceneManager.sceneCount;i++){var scene=SceneManager.GetSceneAt(i);if(scene.isDirty)EditorSceneManager.SaveScene(scene,Folder+"/Unsaved-"+scene.name+".unity",true);}
            SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
        }
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try
        {
            if(stage==0){deadline=EditorApplication.timeSinceStartup+30;EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/Main/Market.unity",new LoadSceneParameters(LoadSceneMode.Single));stage=1;next=EditorApplication.timeSinceStartup+1;return;}
            Require(EditorApplication.timeSinceStartup<deadline,"Timeout");
            if(EditorApplication.timeSinceStartup<next)return;
            if(stage==1)
            {
                foreach(var name in new[]{"F","MouseL","WASDSpace"})
                {
                    var clip=ControlHintAnimation.Load(name);
                    Require(clip.Frame(0)!=null&&clip.Frame(0)!=clip.Frame(.6f),name+" animated frames");
                    Require(clip.Frame(0).rect.size==clip.Size,name+" full resolution");
                    UnityEngine.Object.DestroyImmediate(clip.Frame(0));
                    clip=ControlHintAnimation.Load(name);
                    Require(clip.Frame(0)!=null && clip.Frame(0).texture!=null,name+" stale cached sprite rebuilt");
                    var pixels=clip.Frame(0).texture.GetPixels32();
                    Require(pixels.Any(p=>p.a>0 && p.r!=p.g),name+" atlas contains coloured pixels, not white");
                }
                player=UnityEngine.Object.FindFirstObjectByType<PlayerMovement2D>();
                Require(player!=null,"Player exists");
                hint=UnityEngine.Object.FindObjectsByType<WorldInteractionHint>(FindObjectsSortMode.None).FirstOrDefault(h=>h.name=="Interaction");
                Require(hint!=null,"Garbage interaction F installed");
                fixedY=hint.transform.Find("Animated F Hint").position.y;
                player.transform.position+=Vector3.up*3;
                tutorial=new GameObject("Tutorial Validation").AddComponent<ControlTutorialUI>();tutorial.Begin(player);
                stage=2;next=EditorApplication.timeSinceStartup+1.1;
            }
            else
            {
                Require(Mathf.Abs(hint.transform.Find("Animated F Hint").position.y-fixedY)<.001f,"Hint stays at fixed world height after jump");
                var image=tutorial.GetComponentInChildren<Image>();
                Require(image.enabled&&!image.raycastTarget,"Tutorial shown without blocking input");
                Require(image.rectTransform.sizeDelta==new Vector2(132,132),"Scaled WASD dimensions");
                Require(Mathf.Abs(image.rectTransform.anchoredPosition.y+24)<.01f,"Slide enters top centre");
                Finish("PASS Unity "+Application.unityVersion+"\nAll GIF clips animate with coloured pixels; destroyed cached sprites rebuilt; Garbage F created; fixed world height; nonblocking 132px WASD entrance. Input sequence and full Forest story require manual playthrough.");
            }
        }
        catch(Exception e){Finish("FAIL\n"+e);Debug.LogException(e);}
    }
}
#endif
