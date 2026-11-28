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
            public TextureHandle source, mask, blurredBlack;
            public Material material;
            public MaterialPropertyBlock properties;
        }
        private sealed class BlurData
        {
            public TextureHandle source;
            public Material material;
            public MaterialPropertyBlock properties;
            public int pass;
        }
        private static TextureHandle FilterBlack(RenderGraph graph, TextureHandle source,
            BaseGroundAtmosphere owner, Camera camera, string name, int pass, Vector2 direction)
        {
            var description = graph.GetTextureDesc(source);
            description.name = name;
            description.colorFormat = GraphicsFormat.R16_SFloat;
            description.depthBufferBits = DepthBits.None;
            description.msaaSamples = MSAASamples.None;
            description.filterMode = FilterMode.Bilinear;
            description.clearBuffer = false;
            var destination = graph.CreateTexture(description);
            using (var builder = graph.AddRasterRenderPass<BlurData>(name, out var data))
            {
                data.source = source; data.material = owner.CompositeMaterial; data.pass = pass;
                data.properties = new MaterialPropertyBlock();
                owner.ConfigureComposite(data.properties, camera);
                data.properties.SetVector("_BlurDirection", new Vector4(direction.x, direction.y, 0, 0));
                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (BlurData value, RasterGraphContext context) =>
                {
                    RTHandle input = value.source;
                    value.properties.SetTexture("_BlitTexture", input.rt);
                    value.properties.SetVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
                    context.cmd.DrawProcedural(Matrix4x4.identity, value.material, value.pass,
                        MeshTopology.Triangles, 3, 1, value.properties);
                });
            }
            return destination;
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
            float radius = owner.Configuration.blackSpreadArtworkPixels * owner.ArtworkPixelSize
                / (2 * camera.orthographicSize) * 1.6f;
            var extracted = FilterBlack(graph, resources.activeColorTexture, owner, camera,
                "Base extract black silhouette", 1, Vector2.zero);
            var horizontal = FilterBlack(graph, extracted, owner, camera,
                "Base black Gaussian horizontal", 2, new Vector2(radius / camera.aspect, 0));
            var blurred = FilterBlack(graph, horizontal, owner, camera,
                "Base black Gaussian vertical", 2, new Vector2(0, radius));
            var outputDescription = graph.GetTextureDesc(resources.activeColorTexture);
            outputDescription.name = "Base black floor atmosphere";
            outputDescription.clearBuffer = false;
            var destination = graph.CreateTexture(outputDescription);
            using (var builder = graph.AddRasterRenderPass<CompositeData>("Base black-only spread and ground fog", out var data))
            {
                data.source = resources.activeColorTexture; data.mask = mask; data.blurredBlack = blurred;
                data.material = owner.CompositeMaterial; data.properties = new MaterialPropertyBlock();
                owner.ConfigureComposite(data.properties, camera);
                builder.UseTexture(data.source, AccessFlags.Read); builder.UseTexture(mask, AccessFlags.Read);
                builder.UseTexture(blurred, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (CompositeData value, RasterGraphContext context) =>
                {
                    RTHandle source = value.source, floor = value.mask, black = value.blurredBlack;
                    value.properties.SetTexture("_BlitTexture", source.rt);
                    value.properties.SetTexture("_BaseFloorMask", floor.rt);
                    value.properties.SetTexture("_BaseBlurredBlack", black.rt);
                    value.properties.SetVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
                    context.cmd.DrawProcedural(Matrix4x4.identity, value.material, 0, MeshTopology.Triangles, 3, 1, value.properties);
                });
            }
            resources.cameraColor = destination;
        }
    }
}
