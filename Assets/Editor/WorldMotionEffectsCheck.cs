using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class WorldMotionEffectsCheck
{
    [InitializeOnLoadMethod]
    private static void Schedule()
    {
        if (File.Exists("Temp/CheckWorldMotionEffects.request")) EditorApplication.delayCall += Run;
    }

    [MenuItem("Tools/Behind The Gear/Check World Motion Effects")]
    private static void Run()
    {
        if (Application.unityVersion != "6000.3.23f1" || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete("Temp/CheckWorldMotionEffects.request");
        var scene = EditorSceneManager.NewPreviewScene();
        var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>("Assets/Settings/Renderer2D.asset");
        var feature = renderer.rendererFeatures.OfType<WorldMotionEffectsFeature>().Single();
        var material = feature.passMaterial;

        bool active = feature.isActive;
        var previous = RenderTexture.active;
        var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32);
        var input = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        var readback = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        Sprite sprite = null;
        Material spriteMaterial = null;
        try
        {
            var cameraObject = new GameObject("Unsharp validation camera", typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.GetComponent<Camera>();
            camera.scene = scene;
            camera.enabled = false;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.orthographic = true;
            camera.orthographicSize = 1;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.allowHDR = false;
            camera.GetUniversalAdditionalCameraData().SetRenderer(0);
            var pixels = new Color[1024];
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                pixels[y * 32 + x] = (x < 16 ? Color.black : Color.white);
            input.SetPixels(pixels); input.Apply(); input.filterMode = FilterMode.Point;
            sprite = Sprite.Create(input, new Rect(0, 0, 32, 32), new Vector2(.5f, .5f), 16);
            var art = new GameObject("Unsharp validation pattern", typeof(SpriteRenderer));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(art, scene);
            var sr = art.GetComponent<SpriteRenderer>();
            spriteMaterial = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
            sr.sprite = sprite; sr.sharedMaterial = spriteMaterial;
            target.Create(); camera.targetTexture = target;
            Color[] Capture()
            {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); readback.Apply();
                return readback.GetPixels();
            }
            feature.SetActive(false);
            var baseline = Capture();
            feature.SetActive(true);
            var effected = Capture();
            float separation = 0;
            for(int i=0;i<effected.Length;i++) separation=Mathf.Max(separation, Mathf.Abs(effected[i].r-effected[i].b));
            bool oldOff = renderer.rendererFeatures.OfType<WorldUnsharpMaskFeature>().All(f=>!f.isActive);
            bool passed = oldOff && separation > .05f && !ShaderUtil.ShaderHasError(material.shader);
            string report = $"Passed={passed}; Unity={Application.unityVersion}; sharpeningDisabled={oldOff}; renderedChannelDifference={separation}; shaderErrors={ShaderUtil.ShaderHasError(material.shader)}";            File.WriteAllText("Temp/WorldMotionEffectsCheck.txt", report);
            File.WriteAllBytes("Temp/WorldMotionEffectsCheck.png", readback.EncodeToPNG());
            if (!passed) Debug.LogError(report); else Debug.Log(report);
        }
        finally
        {
            feature.SetActive(active);
            RenderTexture.active = previous;
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(sprite); Object.DestroyImmediate(spriteMaterial);
            Object.DestroyImmediate(input); Object.DestroyImmediate(readback);
            target.Release(); Object.DestroyImmediate(target);
        }

    }

}