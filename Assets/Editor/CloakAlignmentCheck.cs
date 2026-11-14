using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
[InitializeOnLoad] public static class CloakAlignmentCheck {
 static double start; static int phase; static CloakAlignmentCheck(){EditorApplication.update+=Tick;}
 static void Require(bool b,string m){if(!b)throw new Exception(m);}
 static void Tick(){
 if(EditorApplication.isCompiling || EditorApplication.isUpdating)return;
 const string dir="Temp/CloakAlignment/";
 if(File.Exists(dir+"run") && !EditorApplication.isPlayingOrWillChangePlaymode){
 File.Delete(dir+"run"); if(Application.unityVersion!="6000.3.23f1")return;
 for(int i=0;i<SceneManager.sceneCount;i++){var scene=SceneManager.GetSceneAt(i);if(scene.isDirty)EditorSceneManager.SaveScene(scene,dir+scene.name+".before.unity",true);}
 start=0;phase=0;SessionState.SetBool("CloakAlignment",true); EditorApplication.isPlaying=true;return;}
 if(!SessionState.GetBool("CloakAlignment",false)||!EditorApplication.isPlaying)return;
 if(start==0){start=EditorApplication.timeSinceStartup;return;}
 if(EditorApplication.timeSinceStartup-start<3)return;
 if(phase==0){phase=1;start=EditorApplication.timeSinceStartup;EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/Main/Forest.unity",new LoadSceneParameters(LoadSceneMode.Single));return;}
 try {
 var p=UnityEngine.Object.FindFirstObjectByType<PlayerCloakVisual>(); Require(p!=null,"No player");
 var inv=p.GetComponent<PlayerInventory>(); var gear=Resources.Load<InventoryItemDefinition>("Story/TutorialGear"); inv.Add(gear,1);
 for(int i=0;i<inv.Capacity;i++)if(inv.GetSlot(i).Item==gear){Require(inv.EquipFromSlot(i),"Equip");break;}
 Require(p.Equipped,"Cloak equipped");
 var library=Resources.Load<PlayerCloakLibrary>("PlayerSkills/PlayerCloakLibrary");
 var body=p.GetComponent<SpriteRenderer>(); var cloak=p.transform.Find("Cloak").GetComponent<SpriteRenderer>();
 var update=typeof(PlayerCloakVisual).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
 var report=new StringBuilder($"Unity {Application.unityVersion}\nReference rect={library.authoredBodyReference.rect} pivot={library.authoredBodyReference.pivot}\nAnimation rect={library.animatedBodyReference.rect} pivot={library.animatedBodyReference.pivot}\nCorrection={library.AnimationOffset:F6}\n");
 Require(Mathf.Abs(library.AnimationOffset.y+1.5f/11)<.00001f,"Expected 1.5 pixel reference correction");
 foreach(var sprite in library.bodySprites){
 body.sprite=sprite;
 foreach(bool flip in new[]{false,true}){
 body.flipX=flip;update.Invoke(p,null);
 Require(cloak.sprite==library.Frame(sprite)&&cloak.sprite!=null,"Frame mapping "+sprite.name);
 Require(cloak.sprite.rect.size==sprite.rect.size && cloak.sprite.pivot==sprite.pivot && cloak.sprite.pixelsPerUnit==sprite.pixelsPerUnit,"Frame geometry "+sprite.name);
 Require(Mathf.Abs(cloak.transform.localPosition.y-(.15f-1.5f/11))<.00001f,"Offset "+sprite.name);
 Require(cloak.flipX==flip,"Facing "+sprite.name);
 }
 report.AppendLine("PASS "+sprite.name);
 }
 body.sprite=library.animatedBodyReference;body.flipX=false;update.Invoke(p,null);
 report.AppendLine($"PASS {library.bodySprites.Length} frames, both directions; equipped render offset={cloak.transform.localPosition:F6}; scene authored offset remains 0.15");
 File.WriteAllText(dir+"runtime.txt",report.ToString());
 Selection.activeGameObject=p.gameObject;
 if(SceneView.lastActiveSceneView!=null){SceneView.lastActiveSceneView.in2DMode=true;SceneView.lastActiveSceneView.LookAtDirect(p.transform.position,Quaternion.identity,2);}
 EditorApplication.isPaused=true;
 }catch(Exception e){File.WriteAllText(dir+"runtime.txt",e.ToString());EditorApplication.isPlaying=false;}
 SessionState.SetBool("CloakAlignment",false);start=0;
 }
}
