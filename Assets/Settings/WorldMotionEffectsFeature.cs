using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

/// <summary>Screen-space RGB separation for the game camera.</summary>
public sealed class WorldMotionEffectsFeature : ScriptableRendererFeature
{
    public Material passMaterial;
    [Min(.01f)] public float fullEffectSpeed = 30f;
    [Min(0f)] public float minimumSeparationPixels = 2f;
    [Min(0f)] public float maximumSeparationPixels = 5f;
    [Header("Soft white edge haze")]
    [Range(0f, .95f)] public float hazeThreshold = .5f;
    [Range(0f, .3f)] public float hazeOpacity = .14f;
    [Range(0f, 5f)] public float hazeBlurPixels = 2f;
    [Tooltip("Expands the denser haze inward in output pixels without increasing blur or opacity.")]
    [Range(0f, 8f)] public float hazeSpreadPixels = 1f;
    private MotionPass pass;
    private ItemForegroundPass itemPass;
    private BehindTheGear.HideOut.BaseGroundAtmospherePass baseGroundPass;
    public override void Create()
    {
        pass = new MotionPass();
        itemPass = new ItemForegroundPass();
        baseGroundPass = new BehindTheGear.HideOut.BaseGroundAtmospherePass();
    }
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (BehindTheGear.HideOut.BaseGroundAtmosphere.TryGet(renderingData.cameraData.camera, out _))
            renderer.EnqueuePass(baseGroundPass);
        var cameraType = renderingData.cameraData.cameraType;
        if (cameraType == CameraType.Game || cameraType == CameraType.SceneView)
            renderer.EnqueuePass(itemPass);
        if (renderingData.cameraData.cameraType != CameraType.Game || passMaterial == null) return;
        var camera = renderingData.cameraData.camera;
        Vector2 velocity = Vector2.zero;
        var player = PlayerTargetRegistry.Current;
        if (Application.isPlaying && Time.timeScale > 0 && !SceneFadeTransition.IsRunning && player != null)
        {
            var body = player.GetComponent<Rigidbody2D>();
            if (body != null) velocity = body.linearVelocity;
        }
        Vector4 healthEffect = Application.isPlaying ? PrototypeScreenEffects.CurrentHealthEffect : Vector4.zero;
        float maximum = Mathf.Lerp(minimumSeparationPixels, maximumSeparationPixels,
            Mathf.Clamp01(velocity.magnitude / Mathf.Max(.01f, fullEffectSpeed)));
        float separation = PrototypeScreenEffects.Separation(maximum, Time.unscaledTime, healthEffect.x);
        pass.material = passMaterial;
        pass.pixelSize = new Vector2(1f / Mathf.Max(1, camera.pixelWidth), 1f / Mathf.Max(1, camera.pixelHeight));
        pass.separation = separation;
        pass.healthTint = new Vector3(healthEffect.y, healthEffect.z, healthEffect.w);
        pass.damageFlash = Application.isPlaying ? PrototypeScreenEffects.DamageFlashOpacity : 0f;
        pass.storyPanBlurPixels = Application.isPlaying && camera == Camera.main
            ? Mathf.Clamp(PrototypeScreenEffects.StoryPanBlurPixels, 0f, 64f) : 0f;
        bool gameplay = Application.isPlaying && player != null && camera == Camera.main
            && (camera.gameObject.scene.name == "Forest" || camera.gameObject.scene.name == "Market");
        pass.haze = new Vector4(hazeThreshold, gameplay ? hazeOpacity : 0f, hazeBlurPixels, hazeSpreadPixels);
        pass.rippleBar = pass.rippleParameters = Vector4.zero;
        if (Application.isPlaying && camera == Camera.main)
            InGameVitalsHUD.TryGetRipple(out pass.rippleBar, out pass.rippleParameters);
        renderer.EnqueuePass(pass);
    }

    // Item sprites use a dedicated LightMode, so they are drawn once, after the world's
    // tint/flash/haze and before the overlay UI (including the black scene transition).
    private sealed class ItemForegroundPass : ScriptableRenderPass
    {
        private static readonly ShaderTagId ItemTag = new("WorldItemForeground");
        private sealed class PassData { public RendererListHandle items; }
        public ItemForegroundPass() => renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var rendering = frameData.Get<UniversalRenderingData>();
            var camera = frameData.Get<UniversalCameraData>();
            var lights = frameData.Get<UniversalLightData>();
            var drawing = RenderingUtils.CreateDrawingSettings(ItemTag, rendering, camera, lights, SortingCriteria.CommonTransparent);
            var filtering = new FilteringSettings(RenderQueueRange.transparent, camera.camera.cullingMask);
            var parameters = new RendererListParams(rendering.cullResults, drawing, filtering);
            using (var builder = graph.AddRasterRenderPass<PassData>("World Items Above Effects", out var data))
            {
                data.items = graph.CreateRendererList(parameters);
                builder.UseRendererList(data.items);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderFunc(static (PassData value, RasterGraphContext context) => context.cmd.DrawRendererList(value.items));
            }
        }
    }

    private sealed class MotionPass : ScriptableRenderPass
    {
        public Material material;
        public Vector2 pixelSize;
        public float separation;
        public Vector3 healthTint;
        public float damageFlash;
        public float storyPanBlurPixels;
        public Vector4 haze;
        public Vector4 rippleBar, rippleParameters;
        public MotionPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Color);
        }
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer) return;
            var source = resources.activeColorTexture;
            var description = graph.GetTextureDesc(source);
            if (storyPanBlurPixels > .01f)
            {
                var blurDescription = description;
                blurDescription.name = "Story Pan Blur Color";
                blurDescription.clearBuffer = false;
                var blurred = graph.CreateTexture(blurDescription);
                var blurProperties = new MaterialPropertyBlock();
                blurProperties.SetVector("_OutputPixelSize", new Vector4(pixelSize.x, pixelSize.y, 0, 0));
                blurProperties.SetFloat("_StoryPanBlurPixels", storyPanBlurPixels);
                graph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, blurred, material, 1)
                    { propertyBlock = blurProperties }, passName: "Story Pan Horizontal Blur");
                source = blurred;
            }
            description.name = "World Chromatic Motion Color";
            description.clearBuffer = false;
            var destination = graph.CreateTexture(description);
            var properties = new MaterialPropertyBlock();
            properties.SetVector("_OutputPixelSize", new Vector4(pixelSize.x, pixelSize.y, 0, 0));
            properties.SetFloat("_SeparationPixels", separation);
            properties.SetVector("_HealthTint", new Vector4(healthTint.x, healthTint.y, healthTint.z, 0));
            properties.SetFloat("_DamageFlashOpacity", damageFlash);
            properties.SetVector("_EdgeHaze", haze);
            properties.SetVector("_HpRippleBar", rippleBar);
            properties.SetVector("_HpRippleParameters", rippleParameters);
            graph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, destination, material, 0)
                { propertyBlock = properties }, passName: "World Chromatic Motion");
            resources.cameraColor = destination;
        }
    }
}
