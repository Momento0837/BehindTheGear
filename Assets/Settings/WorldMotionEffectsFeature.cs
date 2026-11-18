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
    private MotionPass pass;
    public override void Create() => pass = new MotionPass();
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
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
        renderer.EnqueuePass(pass);
    }
    private sealed class MotionPass : ScriptableRenderPass
    {
        public Material material;
        public Vector2 pixelSize;
        public float separation;
        public Vector3 healthTint;
        public float damageFlash;
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
            description.name = "World Chromatic Motion Color";
            description.clearBuffer = false;
            var destination = graph.CreateTexture(description);
            var properties = new MaterialPropertyBlock();
            properties.SetVector("_OutputPixelSize", new Vector4(pixelSize.x, pixelSize.y, 0, 0));
            properties.SetFloat("_SeparationPixels", separation);
            properties.SetVector("_HealthTint", new Vector4(healthTint.x, healthTint.y, healthTint.z, 0));
            properties.SetFloat("_DamageFlashOpacity", damageFlash);
            graph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, destination, material, 0)
                { propertyBlock = properties }, passName: "World Chromatic Motion");
            resources.cameraColor = destination;
        }
    }
}
