using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
[InitializeOnLoad]
public static class V004BuildRequest
{
 static V004BuildRequest(){EditorApplication.update+=Poll;}
 static void Poll(){
 if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
 const string root="Temp/V004Build/";
 if(File.Exists(root+"prepare")){
 File.Delete(root+"prepare");
 try{
 if(Application.unityVersion!="6000.3.23f1")throw new Exception("Wrong editor version");
 var setup=EditorSceneManager.GetSceneManagerSetup();
 string backup=root+"SceneBackup-"+DateTime.Now.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(backup);
 for(int i=0;i<SceneManager.sceneCount;i++){var scene=SceneManager.GetSceneAt(i);EditorSceneManager.SaveScene(scene,backup+"/"+i+"-"+scene.name+".unity",true);}
 // Retain live scene copies, then build/test the current authored files on disk.
 EditorSceneManager.OpenScene("Assets/Scenes/Main/Forest.unity",OpenSceneMode.Single);
 string[] required={"Assets/Scenes/Controls/ControlSchemeSelect.unity","Assets/Scenes/Main/Forest.unity","Assets/Scenes/Main/Market.unity","Assets/Scenes/HideOutSystemScene/HideOutSystemScene.unity","Assets/Scenes/HideOutSystemScene/ScareCrowScene/ScareCrowScene.unity"};
 foreach(var path in required)if(!EditorBuildSettings.scenes.Any(s=>s.enabled&&s.path==path)||!File.Exists(path))throw new Exception("Missing enabled scene: "+path);
 File.WriteAllText(root+"prepared.txt","PASS Unity "+Application.unityVersion+"; required scenes enabled; live scene copies: "+backup);
 }catch(Exception e){File.WriteAllText(root+"prepared.txt","FAIL "+e);} }
 if(!File.Exists(root+"build"))return;
 File.Delete(root+"build");
 var previous=Environment.GetEnvironmentVariable("BTG_BUILD_OUTPUT");
 try{Environment.SetEnvironmentVariable("BTG_BUILD_OUTPUT",Path.GetFullPath(root+"v0.0.4"));WindowsStoryBuild.Build();}
 finally{Environment.SetEnvironmentVariable("BTG_BUILD_OUTPUT",previous);}
 }
}
