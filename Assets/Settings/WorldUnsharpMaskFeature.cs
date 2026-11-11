using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering;

// Keep editor scene overlays and asset previews outside the game's sharpening pass.
public sealed class WorldUnsharpMaskFeature : ScriptableRendererFeature
{
    public Material passMaterial;
    private SharpenPass pass;

    public override void Create() => pass = new SharpenPass();

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType != CameraType.Game || passMaterial == null) return;
        pass.material = passMaterial;
        renderer.EnqueuePass(pass);
    }

    private sealed class SharpenPass : ScriptableRenderPass
    {
        public Material material;
        public SharpenPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            requiresIntermediateTexture = true;
            // Renderer2D derives its color allocation from ConfigureInput, not this flag alone.
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer) return;
            var source = resources.activeColorTexture;
            var description = graph.GetTextureDesc(source);
            description.name = "World Unsharp Mask Color";
            description.clearBuffer = false;
            var destination = graph.CreateTexture(description);
            var properties = new MaterialPropertyBlock();
            properties.SetVector("_SourceTexelSize", new Vector4(1f / description.width, 1f / description.height,
                description.width, description.height));
            var parameters = new RenderGraphUtils.BlitMaterialParameters(source, destination, material, 0)
            { propertyBlock = properties };
            graph.AddBlitPass(parameters,
                passName: "World Unsharp Mask");
            // Hand the processed texture to URP. Never sample and overwrite the same attachment.
            resources.cameraColor = destination;
        }
    }
}
