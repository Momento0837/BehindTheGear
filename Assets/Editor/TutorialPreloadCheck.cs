#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
[InitializeOnLoad] public static class TutorialPreloadCheck
{
 const string Dir="Temp/TutorialPreloadValidation/", Key="BTG.TutorialPreloadCheck";
 const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 static int stage, attacks; static double deadline, next; static float startX, maxDelta;
 static bool covered; static PlayerMovement2D player; static ControlTutorialUI tutorial;
 static ControlHintAnimation wasd, mouse, interaction;
 static TutorialPreloadCheck(){EditorApplication.update+=Tick;}
 static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Finish(string result){
 File.WriteAllText(Dir+"result.txt",result); SessionState.SetBool(Key,false);
 if(Keyboard.current!=null)InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());
 if(Mouse.current!=null)InputSystem.QueueStateEvent(Mouse.current,new MouseState());
 EditorApplication.isPlaying=false;stage=0;}
 static void Tick(){
 if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
 if(!EditorApplication.isPlayingOrWillChangePlaymode&&File.Exists(Dir+"run")){
 File.Delete(Dir+"run");Require(Application.unityVersion=="6000.3.23f1","Editor version");
 for(int i=0;i<SceneManager.sceneCount;i++){var s=SceneManager.GetSceneAt(i);if(s.isDirty)EditorSceneManager.SaveScene(s,Dir+s.name+".before.unity",true);}
 stage=0;attacks=0;covered=false;maxDelta=0;SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;return;}
 if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying)return;
 try{
 if(stage==0){deadline=EditorApplication.timeSinceStartup+90;stage=1;next=EditorApplication.timeSinceStartup+2;return;}
 Require(EditorApplication.timeSinceStartup<deadline,"Timeout stage "+stage);
 if(stage>=4)maxDelta=Mathf.Max(maxDelta,Time.unscaledDeltaTime);
 if(EditorApplication.timeSinceStartup<next)return;
 if(stage==1){
 typeof(ControlHintAnimation).GetMethod("ResetCache",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
 Require(!ControlHintAnimation.TutorialsLoaded,"Cold cache");
 EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/Main/Forest.unity",new LoadSceneParameters(LoadSceneMode.Single));
 EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Focus(); stage=2;next=EditorApplication.timeSinceStartup+2;return;}
 if(stage==2){var cut=UnityEngine.Object.FindFirstObjectByType<CutsceneVideoSequence>();Require(cut!=null&&cut.IsPlaying,"Opening movie");
 typeof(CutsceneVideoSequence).GetMethod("Finish",Private).Invoke(cut,new object[]{true});stage=3;return;}
 if(stage==3){
 var overlay=GameObject.Find("Opening Quest Black Screen");
 if(overlay!=null&&overlay.GetComponentInChildren<Image>().color.a>=.999f)covered=true;
 tutorial=UnityEngine.Object.FindFirstObjectByType<ControlTutorialUI>();if(tutorial==null)return;
 Require(covered,"Opaque opening cover before tutorials");Require(ControlHintAnimation.TutorialsLoaded,"All GIFs cached before Begin");
 wasd=ControlHintAnimation.Load("WASDSpace");mouse=ControlHintAnimation.Load("MouseL");interaction=ControlHintAnimation.Load("F");
 Require(!ControlHintAnimation.PreloadTutorials().MoveNext(),"Warm/death preload has no wait");
 player=UnityEngine.Object.FindFirstObjectByType<PlayerMovement2D>();
 Require(player.enabled&&!player.GetComponent<PlayerControlLock>().IsLocked&&Time.timeScale>0,"Controls/world released");
 var image=tutorial.GetComponentsInChildren<Image>().First(i=>i.name=="Animated Keys");Require(!image.raycastTarget,"No input-blocking tutorial image");
 Require(ReferenceEquals(typeof(ControlTutorialUI).GetField("clip",Private).GetValue(tutorial),wasd),"WASD reuses preloaded clip");
 player.GetComponent<PlayerInputReader>().BasicAttackPressed+=()=>attacks++;
 startX=player.transform.position.x;InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(UnityEngine.InputSystem.Key.D));stage=4;next=EditorApplication.timeSinceStartup+.6;return;}
 if(stage==4){Require(Mathf.Abs(player.transform.position.x-startX)>.1f,"Movement works during WASD entrance: delta="+(player.transform.position.x-startX)+" key="+Keyboard.current.dKey.isPressed+" reader="+player.GetComponent<PlayerInputReader>().enabled+" fade="+SceneFadeTransition.IsRunning+" inventory="+InventoryUI.GameplayInputSuppressedThisFrame);InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(UnityEngine.InputSystem.Key.A));stage=5;next=EditorApplication.timeSinceStartup+.3;return;}
 if(stage==5){InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(UnityEngine.InputSystem.Key.W));stage=6;next=EditorApplication.timeSinceStartup+.3;return;}
 if(stage==6){InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());stage=7;next=EditorApplication.timeSinceStartup+1.8;return;}
 if(stage==7){Require(ReferenceEquals(typeof(ControlTutorialUI).GetField("clip",Private).GetValue(tutorial),mouse),"MouseL uses preloaded clip after three keys");
 Require(player.enabled&&!player.GetComponent<PlayerControlLock>().IsLocked&&Time.timeScale>0,"Mouse hint does not lock input");
 InputSystem.QueueStateEvent(Mouse.current,new MouseState{position=new Vector2(Screen.width*.5f,Screen.height*.5f)}.WithButton(MouseButton.Left));stage=8;next=EditorApplication.timeSinceStartup+.2;return;}
 if(stage==8){InputSystem.QueueStateEvent(Mouse.current,new MouseState());Require(attacks>0,"Left click reaches attack input");stage=9;next=EditorApplication.timeSinceStartup+mouse.Duration+1;return;}
 if(stage==9){Require(!tutorial.GetComponentsInChildren<Image>().First(i=>i.name=="Animated Keys").enabled,"Mouse hint completes after click and cycle");
 Require(ReferenceEquals(ControlHintAnimation.Load("F"),interaction),"F reuses cache");
 Finish($"PASS Unity {Application.unityVersion}\nCold Forest opening/skip: opaque cover precedes preload; all 3 clips cached before tutorial.\nWASD: movement during entrance; 3 keys advance. MouseL: cached clip, unlocked controls, attack input received, exits after cycle.\nWarm preload yields no frames; F cache reused. Largest observed tutorial delta: {maxDelta*1000:F1} ms.\n");}
 }catch(Exception e){Finish("FAIL "+e);}
 }
}
#endif
