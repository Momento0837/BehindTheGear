using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BehindTheGear.HideOut;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
[InitializeOnLoad] public static class BaseConnectionBuildCheck {
 const string Dir="Temp/V004Build/"; const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static int stage;static double deadline,next;
 static BaseConnectionBuildCheck(){EditorApplication.update+=Tick;}
 static void Check(bool b,string s){if(!b)throw new Exception(s);File.AppendAllText(Dir+"connection.txt","PASS "+s+"\n");}
 static void Tick(){if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
 if(File.Exists(Dir+"check")&&!EditorApplication.isPlayingOrWillChangePlaymode){File.Delete(Dir+"check");File.WriteAllText(Dir+"connection.txt","");SessionState.SetBool("V004.Connection",true);stage=0;EditorApplication.isPlaying=true;return;}
 if(!SessionState.GetBool("V004.Connection",false)||!EditorApplication.isPlaying)return;
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
 if(dialogue!=null){if(dialogue.IsChoosing)typeof(ConversationDialogue).GetMethod("SelectChoice",Flags).Invoke(dialogue,new object[]{true});else typeof(ConversationDialogue).GetMethod("RequestAdvance",Flags).Invoke(dialogue,null);next=EditorApplication.timeSinceStartup+.25;return;}
 Check(bridge.State.baseTutorialComplete&&bridge.State.selenaRecruited,"Arrival dialogue completes and unlocks clinic");
 Check(!(bool)typeof(HideOutScene).GetMethod("IsLocked",Flags).Invoke(view,new object[]{Facility.Clinic}),"Clinic available through facility UI");
 bridge.Change(s=>{s.coins=100000;s.actionPoints=100000;HideOutModel.Add(s.inventory,"hideout_scrap",100,view.settings);return "isolated build check";});
 stage=5;next=EditorApplication.timeSinceStartup+.3;return;}
if(stage==5){int level=bridge.State.levels[(int)Facility.Clinic];bridge.Upgrade(Facility.Clinic);
 Check(bridge.State.levels[(int)Facility.Clinic]==level+1,"Clinic upgrade applies: "+bridge.Message);
 Check(view.gameObject.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0),"No missing scripts in hideout");
 File.AppendAllText(Dir+"connection.txt","PASSED Unity "+Application.unityVersion);Finish();}
 }catch(Exception e){File.AppendAllText(Dir+"connection.txt","FAIL "+e);Finish();}}
 static void Finish(){SessionState.SetBool("V004.Connection",false);EditorApplication.isPlaying=false;}
}
