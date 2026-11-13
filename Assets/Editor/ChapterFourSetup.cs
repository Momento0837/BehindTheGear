#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>Explicit, non-destructive asset wiring in the already open project editor.</summary>
[InitializeOnLoad]
public static class ChapterFourSetup
{
    static ChapterFourSetup() => EditorApplication.update += Tick;
    private static void Tick()
    {
        const string request = "Temp/ChapterFourValidation/setup";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
        File.Delete(request);
        try
        {
            if (Application.unityVersion != "6000.3.23f1") throw new Exception("Requires Unity 6000.3.23f1.");
            const string stylePath = "Assets/Resources/Dialogue/VisualStyle.asset";
            var style = AssetDatabase.LoadAssetAtPath<DialogueVisualStyle>(stylePath);
            if (style == null)
            {
                style = ScriptableObject.CreateInstance<DialogueVisualStyle>();
                style.triangle = AssetDatabase.LoadAllAssetsAtPath("Assets/!UI/Other/Triangle.png").OfType<Sprite>().First();
                style.glowShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/!UI/Main/MenuSoftImage.shader");
                AssetDatabase.CreateAsset(style, stylePath);
            }
            const string controllerPath = "Assets/Resources/Story/Selena.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) == null)
            {
                var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
                var machine = controller.layers[0].stateMachine;
                var idle = machine.AddState("Idle", new Vector3(240, 80));
                var walk = machine.AddState("Walk", new Vector3(480, 80));
                var sprite = AssetDatabase.LoadAllAssetsAtPath("Assets/!Animation/Selena/Selena.png").OfType<Sprite>().First();
                var clip = new AnimationClip { name = "Selena_Idle" };
                AnimationUtility.SetObjectReferenceCurve(clip, new EditorCurveBinding { path = "", type = typeof(SpriteRenderer), propertyName = "m_Sprite" },
                    new[] { new ObjectReferenceKeyframe { time = 0, value = sprite } });
                AssetDatabase.CreateAsset(clip, "Assets/Resources/Story/Selena_Idle.anim");
                idle.motion = clip;
                // Assign Selena's future walking clip here; the static sprite remains until then.
                walk.motion = clip;
                machine.defaultState = idle;
                var start = idle.AddTransition(walk); start.hasExitTime = false; start.duration = .1f;
                start.AddCondition(AnimatorConditionMode.If, 0, "IsMoving");
                var stop = walk.AddTransition(idle); stop.hasExitTime = false; stop.duration = .1f;
                stop.AddCondition(AnimatorConditionMode.IfNot, 0, "IsMoving");
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/ChapterFourValidation/setup-result.txt", "PASS: Triangle reference, glow shader, Selena Idle/Walk states and transitions created.");
        }
        catch (Exception error) { File.WriteAllText("Temp/ChapterFourValidation/setup-result.txt", error.ToString()); Debug.LogException(error); }
    }
}
#endif
