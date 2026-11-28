using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace BehindTheGear.HideOut
{
    public sealed class BaseGroundAtmospherePass : ScriptableRenderPass
    {
        private sealed class MaskData { public BaseGroundAtmosphere atmosphere; }
        private sealed class CompositeData
        {
            public TextureHandle source, mask;
            public Material material;
            public MaterialPropertyBlock properties;
        }
        public BaseGroundAtmospherePass()
        {
            renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingPostProcessing + 1);
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Color);
        }
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var camera = frameData.Get<UniversalCameraData>().camera;
            if (resources.isActiveTargetBackBuffer || !BaseGroundAtmosphere.TryGet(camera, out var owner)) return;
            owner.PrepareMask();
            var description = graph.GetTextureDesc(resources.activeColorTexture);
            description.name = "Base visible floor mask";
            description.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
            description.depthBufferBits = DepthBits.None;
            description.msaaSamples = MSAASamples.None;
            description.clearBuffer = true; description.clearColor = Color.clear;
            description.filterMode = FilterMode.Point;
            var mask = graph.CreateTexture(description);
            using (var builder = graph.AddRasterRenderPass<MaskData>("Base floor and wall occlusion", out var data))
            {
                data.atmosphere = owner;
                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (MaskData value, RasterGraphContext context) =>
                {
                    foreach (var entry in value.atmosphere.MaskSprites)
                    {
                        var sprite = entry.renderer;
                        if (sprite == null || !sprite.enabled || !sprite.gameObject.activeInHierarchy || sprite.sprite == null) continue;
                        Matrix4x4 matrix = sprite.localToWorldMatrix * Matrix4x4.Scale(new Vector3(sprite.flipX ? -1 : 1, sprite.flipY ? -1 : 1, 1));
                        context.cmd.DrawMesh(entry.mesh, matrix, value.atmosphere.MaskMaterial, 0, 0, entry.properties);
                    }
                });
            }
            var outputDescription = graph.GetTextureDesc(resources.activeColorTexture);
            outputDescription.name = "Base black floor atmosphere";
            outputDescription.clearBuffer = false;
            var destination = graph.CreateTexture(outputDescription);
            using (var builder = graph.AddRasterRenderPass<CompositeData>("Base black-only spread and ground fog", out var data))
            {
                data.source = resources.activeColorTexture; data.mask = mask;
                data.material = owner.CompositeMaterial; data.properties = new MaterialPropertyBlock();
                owner.ConfigureComposite(data.properties, camera);
                builder.UseTexture(data.source, AccessFlags.Read); builder.UseTexture(mask, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (CompositeData value, RasterGraphContext context) =>
                {
                    RTHandle source = value.source, floor = value.mask;
                    value.properties.SetTexture("_BlitTexture", source.rt);
                    value.properties.SetTexture("_BaseFloorMask", floor.rt);
                    value.properties.SetVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
                    context.cmd.DrawProcedural(Matrix4x4.identity, value.material, 0, MeshTopology.Triangles, 3, 1, value.properties);
                });
            }
            resources.cameraColor = destination;
        }
    }
}
