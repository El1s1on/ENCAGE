using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Ilumisoft.Rendering
{
    [SupportedOnRenderer(typeof(UniversalRendererData), typeof(Renderer2DData))]
    [DisallowMultipleRendererFeature("Blur")]
    public class BlurRendererFeature : ScriptableRendererFeature
    {
        class BlurRenderPass : ScriptableRenderPass
        {
            private static readonly int lerpID = Shader.PropertyToID("_Lerp");
            private static readonly int blurTexAID = Shader.PropertyToID("_BlurTexA");
            private static readonly int blurTexBID = Shader.PropertyToID("_BlurTexB");

            Material material;

            public void Setup(Material material)
            {
                this.material = material;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                // Get the texture descriptor
                var activeColorTexture = resourceData.activeColorTexture;
                var descriptor = activeColorTexture.GetDescriptor(renderGraph);
                descriptor.depthBufferBits = 0;

                var volumeComponent = VolumeManager.instance.stack.GetComponent<BlurVolumeComponent>();

                float blurAmount = volumeComponent.intensity.overrideState ? volumeComponent.intensity.value : 0.0f;

                blurAmount = Mathf.Clamp01(blurAmount);

                // Clamp blur amount and normalize it for a screen width of 1920.0f
                float blurLevel = Mathf.Lerp(0, 8, blurAmount);
                blurLevel *= descriptor.width / 1920.0f;

                // Compute the number of iterations required to achieve the requested blur
                float requestedIterations = Mathf.Log(blurLevel, 2);
                requestedIterations = Mathf.Max(requestedIterations, 0);

                // The algorithm will perform a blur with the amount of iterations and blend in the next iteration
                int iterations = Mathf.FloorToInt(requestedIterations);
                float lerp = requestedIterations - iterations;

                // Cancel if no blur will be visible
                if (Mathf.Approximately(lerp, 0.0f) && iterations == 0)
                {
                    return;
                }

                TextureHandle sourceHandle = activeColorTexture;
                TextureHandle targetHandle;

                if (!sourceHandle.IsValid())
                {
                    return;
                }

                // Downscale iterations
                for (int i = 0; i < iterations; i++)
                {
                    descriptor.name = $"_BlurMipDown";
                    descriptor.width >>= 1;
                    descriptor.height >>= 1;
                    targetHandle = renderGraph.CreateTexture(descriptor);
                    AddBlurDownsamplePass(renderGraph, sourceHandle, targetHandle);
                    sourceHandle = targetHandle;
                }

                // Blend the next iteration in if necessary
                if (!Mathf.Approximately(lerp, 0.0f))
                {
                    // Downscale blur
                    descriptor.width >>= 1;
                    descriptor.height >>= 1;
                    var tmpDownscaleHandle = renderGraph.CreateTexture(descriptor);
                    AddBlurDownsamplePass(renderGraph, sourceHandle, tmpDownscaleHandle);

                    // Upscale blur
                    descriptor.name = $"_BlurMipUp";
                    descriptor.width <<= 1;
                    descriptor.height <<= 1;
                    var tmpUpscaleHandle = renderGraph.CreateTexture(descriptor);
                    AddBlurUpsamplePass(renderGraph, tmpDownscaleHandle, tmpUpscaleHandle);

                    // Blend
                    descriptor.name = $"_BlurBlend";
                    targetHandle = renderGraph.CreateTexture(descriptor);
                    AddBlendPass(renderGraph, sourceHandle, tmpUpscaleHandle, targetHandle, lerp);
                    sourceHandle = targetHandle;

                    // If no iterations are performed, apply blend directly to active color texture and return
                    if (iterations == 0)
                    {
                        // Apply to active color texture
                        renderGraph.AddBlitPass(sourceHandle, activeColorTexture, Vector2.one, Vector2.zero, passName: "Blur Blit Pass");
                        return;
                    }
                }

                // Upscale iterations
                for (int i = 0; i < iterations - 1; i++)
                {
                    descriptor.name = $"_BlurMipUp";
                    descriptor.width <<= 1;
                    descriptor.height <<= 1;
                    targetHandle = renderGraph.CreateTexture(descriptor);
                    AddBlurUpsamplePass(renderGraph, sourceHandle, targetHandle);
                    sourceHandle = targetHandle;
                }

                // The last upscale uses the active color texture as the target
                AddBlurUpsamplePass(renderGraph, sourceHandle, activeColorTexture);

            }

            /// <summary>
            /// Adds a blit pass using the downsample blur pass of the material
            /// </summary>
            /// <param name="renderGraph"></param>
            /// <param name="input"></param>
            /// <param name="output"></param>
            private void AddBlurDownsamplePass(RenderGraph renderGraph, TextureHandle input, TextureHandle output)
            {
                var parameters = new RenderGraphUtils.BlitMaterialParameters(input, output, material, 0);
                renderGraph.AddBlitPass(parameters, "Blur Downsample");
            }

            /// <summary>
            /// Adds a blit pass using the upsample blur pass of the material
            /// </summary>
            /// <param name="renderGraph"></param>
            /// <param name="input"></param>
            /// <param name="output"></param>
            private void AddBlurUpsamplePass(RenderGraph renderGraph, TextureHandle input, TextureHandle output)
            {
                var parameters = new RenderGraphUtils.BlitMaterialParameters(input, output, material, 1);
                renderGraph.AddBlitPass(parameters, "Blur Upsample");
            }

            /// <summary>
            /// Blends textures a and b using the given lerp
            /// </summary>
            /// <param name="renderGraph"></param>
            /// <param name="a"></param>
            /// <param name="b"></param>
            /// <param name="target"></param>
            /// <param name="lerp"></param>
            void AddBlendPass(RenderGraph renderGraph, TextureHandle a, TextureHandle b, TextureHandle target, float lerp)
            {
                using (var builder = renderGraph.AddRasterRenderPass<BlendPassData>("Blur Blend", out var passData))
                {
                    passData.inputA = a;
                    passData.inputB = b;
                    passData.lerp = lerp;
                    passData.material = material;

                    builder.UseTexture(passData.inputA, AccessFlags.Read);
                    builder.UseTexture(passData.inputB, AccessFlags.Read);
                    builder.SetRenderAttachment(target, 0);

                    builder.SetRenderFunc((BlendPassData passData, RasterGraphContext context) =>
                    {
                        passData.material.SetFloat(lerpID, passData.lerp);
                        passData.material.SetTexture(blurTexAID, passData.inputA);
                        passData.material.SetTexture(blurTexBID, passData.inputB);

                        Blitter.BlitTexture(context.cmd, passData.inputA, new Vector4(1, 1, 0, 0), passData.material, 2);
                    });
                }
            }

            /// <summary>
            /// Pass data for the blend pass
            /// </summary>
            class BlendPassData
            {
                public TextureHandle inputA;
                public TextureHandle inputB;
                public Material material;
                public float lerp;
            }
        }

        Material material;

        BlurRenderPass renderPass;

        public override void Create()
        {
            var shader = Shader.Find("Ilumisoft/PostProcessing/Blur");

            if (shader == null)
            {
                Debug.LogWarning("Fast Blur: Could not find blur shader");
                return;
            }

            material = new Material(shader);

            renderPass = new BlurRenderPass();
            renderPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderPass == null || renderingData.cameraData.cameraType != CameraType.Game)
            {
                return;
            }

            renderPass.Setup(material);

            renderer.EnqueuePass(renderPass);
        }

        protected override void Dispose(bool disposing)
        {
            if (Application.isPlaying)
            {
                Destroy(material);
            }
            else
            {
                DestroyImmediate(material);
            }
        }
    }
}