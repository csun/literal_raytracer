using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

class LiteralRaytraceCustomPass : CustomPass
{
    public Material LiteralRaytraceMaterial;

    int samplingPassId;
    int colorPassId;

    RenderTexture[] samplingOutputs;
    int currentSamplingIndex = 0;
    int nextSamplingIndex { get { return (currentSamplingIndex + 1) % 2; } }

    protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
    {
        samplingPassId = LiteralRaytraceMaterial.FindPass("SamplingPass");
        colorPassId = LiteralRaytraceMaterial.FindPass("ColorPass");
        ReinitialzeTextures();
    }

    private void ReinitialzeTextures()
    {
        if (samplingOutputs == null || samplingOutputs[0].width != Screen.width || samplingOutputs[0].height != Screen.height)
        {
            DestroyTextures();

            samplingOutputs = new RenderTexture[2]
            {
                new RenderTexture(
                    Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear),
                new RenderTexture(
                    Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            };

            samplingOutputs[0].Create();
            samplingOutputs[1].Create();
        }
    }

    protected override void Execute(CustomPassContext ctx)
    {
        ReinitialzeTextures();

        // Disable in scene view
        if (ctx.hdCamera.camera.cameraType == CameraType.SceneView)
        {
            return;
        }

        LiteralRaytraceMaterial.SetTexture("_ColorAndSamples", samplingOutputs[currentSamplingIndex]);

        ctx.cmd.SetRenderTarget(samplingOutputs[nextSamplingIndex]);
        CoreUtils.DrawFullScreen(ctx.cmd, LiteralRaytraceMaterial, shaderPassId: samplingPassId);
        SetRenderTargetAuto(ctx.cmd);
        CoreUtils.DrawFullScreen(ctx.cmd, LiteralRaytraceMaterial, shaderPassId: colorPassId);

        currentSamplingIndex = nextSamplingIndex;
    }

    void DestroyTextures()
    {
        if (samplingOutputs != null)
        {
            samplingOutputs[0].Release();
            samplingOutputs[1].Release();
        }
    }

    protected override void Cleanup()
    {
        DestroyTextures();
    }
}