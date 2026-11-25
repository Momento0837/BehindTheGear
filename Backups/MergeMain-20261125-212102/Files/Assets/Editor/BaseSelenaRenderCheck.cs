using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BehindTheGear.HideOut;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
[InitializeOnLoad] public static class BaseSelenaRenderCheck {
 const string Dir="Temp/BaseSelenaRender/"; const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static int stage;static double deadline,next;
 static BaseSelenaRenderCheck(){EditorApplication.update+=Tick;}
 static void Check(bool b,string s){if(!b)throw new Exception(s);File.AppendAllText(Dir+"connection.txt","PASS "+s+"\n");}
 static void Tick(){if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
 if(File.Exists(Dir+"check")&&!EditorApplication.isPlayingOrWillChangePlaymode){File.Delete(Dir+"check");File.WriteAllText(Dir+"connection.txt","");SessionState.SetBool("BaseSelena.Render",true);stage=0;EditorApplication.isPlaying=true;return;}
 if(!SessionState.GetBool("BaseSelena.Render",false)||!EditorApplication.isPlaying)return;
 try{
 if(stage==0){deadline=EditorApplication.timeSinceStartup+90;stage=1;next=EditorApplication.timeSinceStartup+2;return;}
 if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Timeout "+stage);
 if(EditorApplication.timeSinceStartup<next||SceneFadeTransition.IsRunning)return;
 if(stage==1){EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/Main/Market.unity",new LoadSceneParameters(LoadSceneMode.Single));stage=2;next=EditorApplication.timeSinceStartup+2;return;}
 var player=UnityEngine.Object.FindFirstObjectByType<PlayerMovement2D>();if(player==null)return;
 if(stage==2){
 var progress=player.GetComponent<PrototypeTutorialState>()??player.gameObject.AddComponent<PrototypeTutorialState>();progress.SelenaArrived=true;
 var entrance=UnityEngine.Object.FindFirstObjectByType<HideOutEntrance>();Check(entrance!=null,"Neighborhood hideout entrance");
 Check(Application.CanStreamedLevelBeLoaded(entrance.settings.scenePath),"Hideout scene registered");
 entrance.GetComponent<Interactable2D>().Interact(player.gameObject);stage=3;next=EditorApplication.timeSinceStartup+2;return;}
 var view=UnityEngine.Object.FindFirstObjectByType<HideOutScene>();if(view==null)return;
 var bridge=view.TrainingPlayer;
 if(stage==3){Check(view.GetComponent<BaseArrivalTutorial>()!=null,"Base tutorial connected");stage=4;}
 if(stage==4){var dialogue=UnityEngine.Object.FindObjectsByType<ConversationDialogue>(FindObjectsSortMode.None).FirstOrDefault(d=>d.IsPlaying);
 if(dialogue==null)return;
 if(dialogue.CurrentSpeaker!="Selena")throw new Exception("Expected Selena");
 stage=5;next=EditorApplication.timeSinceStartup+2;return;}
 if(stage==5){var dialogue=UnityEngine.Object.FindObjectsByType<ConversationDialogue>(FindObjectsSortMode.None).First(d=>d.IsPlaying);
 Check(dialogue.gameObject.name=="Base Tutorial Dialogue","Cloned Hideout presentation");
 Check(dialogue.BodyText.font.name=="SelenaDialogue","Correct Selena font in Hideout");
 Check(dialogue.BodyText.fontSharedMaterial.mainTexture==dialogue.BodyText.font.atlasTexture,"Matching atlas in cloned UI");
 ScreenCapture.CaptureScreenshot(Dir+"selena.png");stage=6;next=EditorApplication.timeSinceStartup+.5;return;}
 if(stage==6){File.AppendAllText(Dir+"connection.txt","PASSED Unity "+Application.unityVersion);Finish();} }catch(Exception e){File.AppendAllText(Dir+"connection.txt","FAIL "+e);Finish();}}
 static void Finish(){SessionState.SetBool("BaseSelena.Render",false);EditorApplication.isPlaying=false;}
}
