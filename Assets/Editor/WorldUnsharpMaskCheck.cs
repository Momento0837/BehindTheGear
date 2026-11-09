using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class WorldUnsharpMaskCheck
{
    [InitializeOnLoadMethod]
    private static void Schedule()
    {
        if (File.Exists("Temp/CheckWorldUnsharpMask.request")) EditorApplication.delayCall += Run;
    }

    [MenuItem("Tools/Behind The Gear/Check World Unsharp Mask")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete("Temp/CheckWorldUnsharpMask.request");
        var scene = EditorSceneManager.NewPreviewScene();
        var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>("Assets/Settings/Renderer2D.asset");
        var feature = renderer.rendererFeatures.OfType<WorldUnsharpMaskFeature>().Single();
        var material = feature.passMaterial;
        float amount = material.GetFloat("_Amount");
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
                pixels[y * 32 + x] = new Color(x < 16 ? .2f : .7f, y / 40f, .25f, 1);
            input.SetPixels(pixels); input.Apply(); input.filterMode = FilterMode.Point;
            sprite = Sprite.Create(input, new Rect(0, 0, 32, 32), new Vector2(.5f, .5f), 16);
            var art = new GameObject("Unsharp validation pattern", typeof(SpriteRenderer));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(art, scene);
            var sr = art.GetComponent<SpriteRenderer>();
            spriteMaterial = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
            sr.sprite = sprite; sr.sharedMaterial = spriteMaterial;
            target.Create();
            Color[] Capture()
            {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); readback.Apply();
                return readback.GetPixels();
            }
            feature.SetActive(false);
            var baseline = Capture();
            feature.SetActive(true); material.SetFloat("_Amount", 0);
            var identity = Capture();
            material.SetFloat("_Amount", .5f);
            var sharpened = Capture();
            float identityError = 0, effectDifference = 0;
            for (int i = 0; i < baseline.Length; i++)
            {
                identityError = Mathf.Max(identityError, Mathf.Abs(baseline[i].r - identity[i].r));
                effectDifference = Mathf.Max(effectDifference, Mathf.Abs(baseline[i].r - sharpened[i].r));
            }
            float range = sharpened.Max(c => c.r) - sharpened.Min(c => c.r);
            bool passed = identityError < .02f && effectDifference > .005f && range > .3f;
            string report = $"Passed={passed}; camera type={camera.cameraType}; identity error={identityError}; sharpen difference={effectDifference}; color range={range}";
            File.WriteAllText("Temp/WorldUnsharpMaskCheck.txt", report);
            File.WriteAllBytes("Temp/WorldUnsharpMaskCheck.png", readback.EncodeToPNG());
            if (!passed) Debug.LogError(report); else Debug.Log(report);
        }
        finally
        {
            material.SetFloat("_Amount", amount); feature.SetActive(active);
            RenderTexture.active = previous;
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(sprite); Object.DestroyImmediate(spriteMaterial);
            Object.DestroyImmediate(input); Object.DestroyImmediate(readback);
            target.Release(); Object.DestroyImmediate(target);
        }
        CheckForest();
    }

    private static void CheckForest()
    {
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/Main/Forest.unity");
        var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>("Assets/Settings/Renderer2D.asset");
        var feature = data.rendererFeatures.OfType<WorldUnsharpMaskFeature>().Single();
        bool active = feature.isActive;
        var previous = RenderTexture.active;
        var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            var camera = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true)).First();
            camera.scene = scene;
            target.Create();
            Color[] Capture(string file)
            {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                File.WriteAllBytes(file, image.EncodeToPNG());
                return image.GetPixels();
            }
            feature.SetActive(false);
            var before = Capture("Temp/ForestUnsharp-before.png");
            feature.SetActive(true);
            var after = Capture("Temp/ForestUnsharp-after.png");
            float difference = 0, maximum = 0;
            int changed = 0;
            for (int i = 0; i < before.Length; i++)
            {
                float delta = Mathf.Max(Mathf.Abs(before[i].r - after[i].r), Mathf.Abs(before[i].g - after[i].g), Mathf.Abs(before[i].b - after[i].b));
                difference += delta;
                maximum = Mathf.Max(maximum, delta);
                if (delta > .005f) changed++;
            }
            File.WriteAllText("Temp/ForestUnsharpCheck.txt", $"Camera={camera.name}; postProcessing={camera.GetUniversalAdditionalCameraData().renderPostProcessing}; meanDifference={difference / before.Length}; maxDifference={maximum}; changedPixels={changed}/{before.Length}");
            var portal = GameObject.CreatePrimitive(PrimitiveType.Quad);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(portal, scene);
            portal.transform.position = camera.transform.position + new Vector3(-1, 0, 1);
            portal.transform.localScale = new Vector3(6, 7, 1);
            var glow = new Material(Shader.Find("BehindTheGear/Portal Radiance"));
            try
            {
                if (ShaderUtil.ShaderHasError(glow.shader)) throw new System.InvalidOperationException("Portal Radiance shader compilation failed.");
                portal.GetComponent<MeshRenderer>().sharedMaterial = glow;
                portal.GetComponent<MeshRenderer>().sortingOrder = 30000;
                Capture("Temp/PortalSlit-preview.png");
            }
            finally { Object.DestroyImmediate(glow); }
        }
        finally
        {
            feature.SetActive(active);
            RenderTexture.active = previous;
            EditorSceneManager.ClosePreviewScene(scene);
            target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image);
        }
    }
}
