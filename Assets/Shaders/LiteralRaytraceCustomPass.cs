using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

class LiteralRaytraceCustomPass : CustomPass
{
    public Material LiteralRaytraceMaterial;

    int samplingPassId;
    int colorPassId;

    RenderTexture averageColor;
    RenderTexture samples;

    RenderTargetIdentifier[] samplePassTargets;

    protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
    {
        samplingPassId = LiteralRaytraceMaterial.FindPass("SamplingPass");
        colorPassId = LiteralRaytraceMaterial.FindPass("ColorPass");

        Init();
    }

    void Init()
    {
        if (samples == null || samples.width != Screen.width || samples.height != Screen.height)
        {
            if (samples != null)
            {
                samples.Release();
            }
            if (averageColor != null)
            {
                averageColor.Release();
            }

            averageColor = new RenderTexture(
                Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            samples = new RenderTexture(
                Screen.width, Screen.height, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
            averageColor.Create();
            samples.Create();

            samplePassTargets = new RenderTargetIdentifier[2]
            {
                 new RenderTargetIdentifier(averageColor),
                 new RenderTargetIdentifier(samples)
            };
        }
    }

    protected override void Execute(CustomPassContext ctx)
    {
        Init();

        // Disable in editor
        if (ctx.hdCamera.camera.cameraType == CameraType.SceneView)
        {
            return;
        }

        LiteralRaytraceMaterial.SetTexture("_AverageColor", averageColor);
        LiteralRaytraceMaterial.SetTexture("_Samples", samples);

        ctx.cmd.SetRenderTarget(samplePassTargets, averageColor.depthBuffer);
        CoreUtils.DrawFullScreen(ctx.cmd, LiteralRaytraceMaterial, shaderPassId: samplingPassId);
        SetRenderTargetAuto(ctx.cmd);
        CoreUtils.DrawFullScreen(ctx.cmd, LiteralRaytraceMaterial, shaderPassId: colorPassId);
    }

    protected override void Cleanup()
    {
        if (samples != null)
        {
            samples.Release();
        }
        if (averageColor != null)
        {
            averageColor.Release();
        }
    }
}