#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SelenaWalkSetup
{
    const string Root="Temp/SelenaWalk/";
    static SelenaWalkSetup() => EditorApplication.update += Poll;
    static void Check(bool condition, string message) { if(!condition)throw new Exception(message); }
    static void Poll()
    {
        if(EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Root+"run"))return;
        File.Delete(Root+"run");
        try
        {
            Check(Application.unityVersion=="6000.3.23f1","Wrong editor version");
            const string path="Assets/Resources/Story/Selena_Walk.anim";
            Check(!File.Exists(path),"Will not overwrite an existing walk clip");
            var frames=AssetDatabase.LoadAllAssetsAtPath("Assets/!Animation/Selena/Selena_Walk.png").OfType<Sprite>()
                .OrderBy(s=>int.Parse(s.name.Substring(s.name.LastIndexOf('_')+1))).ToArray();
            Check(frames.Length==5,"Expected five sliced walk frames");
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Resources/Story/Selena.controller");
            var states=controller.layers[0].stateMachine.states;
            var walk=states.Single(s=>s.state.name=="Walk").state;
            var idle=(AnimationClip)states.Single(s=>s.state.name=="Idle").state.motion;
            var clip=new AnimationClip { name="Selena_Walk", frameRate=10 };
            var keys=new ObjectReferenceKeyframe[frames.Length+1];
            for(int i=0;i<keys.Length;i++) keys[i]=new ObjectReferenceKeyframe {time=i/10f,value=frames[i%frames.Length]};
            AnimationUtility.SetObjectReferenceCurve(clip,new EditorCurveBinding {path="",type=typeof(SpriteRenderer),propertyName="m_Sprite"},keys);
            var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=true;AnimationUtility.SetAnimationClipSettings(clip,settings);
            AssetDatabase.CreateAsset(clip,path);
            walk.motion=clip;EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();
            var scene=EditorSceneManager.NewPreviewScene();
            var graph=PlayableGraph.Create("Selena walk validation");
            try
            {
                var actor=new GameObject("Selena walk validation",typeof(SpriteRenderer),typeof(Animator));
                SceneManager.MoveGameObjectToScene(actor,scene);
                var renderer=actor.GetComponent<SpriteRenderer>();
                var animator=actor.GetComponent<Animator>();
                var control=AnimatorControllerPlayable.Create(graph,controller);
                AnimationPlayableOutput.Create(graph,"Selena",animator).SetSourcePlayable(control);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);graph.Play();graph.Evaluate(0);
                Check(control.GetCurrentAnimatorStateInfo(0).IsName("Idle"),"Starts idle");
                control.SetBool("IsMoving",true);for(int i=0;i<20;i++)graph.Evaluate(.02f);
                Check(control.GetCurrentAnimatorStateInfo(0).IsName("Walk"),"IsMoving transitions to Walk");
                var seen=new System.Collections.Generic.HashSet<Sprite>();
                for(int i=0;i<60;i++){graph.Evaluate(.02f);Check(frames.Contains(renderer.sprite),"Walk sprite resolves");seen.Add(renderer.sprite);}
                Check(seen.Count==5,"All five frames animate and loop");
                control.SetBool("IsMoving",false);for(int i=0;i<20;i++)graph.Evaluate(.02f);
                Check(control.GetCurrentAnimatorStateInfo(0).IsName("Idle"),"Stopping transitions to Idle");
                Check(AnimationUtility.GetObjectReferenceCurve(idle,AnimationUtility.GetObjectReferenceCurveBindings(idle)[0]).Any(k=>k.value==renderer.sprite),"Idle sprite restored");
            }
            finally {graph.Destroy();EditorSceneManager.ClosePreviewScene(scene);}
            File.WriteAllText(Root+"result.txt","PASS Unity "+Application.unityVersion+"; five frames, 10 fps looping clip; IsMoving Idle -> Walk -> Idle; all frames resolved and sampled; controller saved.");
        }
        catch(Exception e){File.WriteAllText(Root+"result.txt","FAIL "+e);Debug.LogException(e);}
    }
}
#endif
