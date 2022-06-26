using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

class LiteralRaytraceCustomPass : CustomPass
{
    public ComputeShader SamplingShader;
    public Material LiteralRaytraceMaterial;

    RenderTexture colorAndSamples;

    protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
    {
        ReinitialzeTextures();
    }

    private void ReinitialzeTextures()
    {
        if (colorAndSamples == null || colorAndSamples.width != Screen.width || colorAndSamples.height != Screen.height)
        {
            DestroyTextures();

            colorAndSamples = new RenderTexture(
                Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            colorAndSamples.enableRandomWrite = true;
            colorAndSamples.Create();
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

        SamplingShader.SetTexture(0, "_ColorAndSamples", colorAndSamples);
        ctx.cmd.DispatchCompute(SamplingShader, 0, Screen.width / 8, Screen.height / 8, 1);

        LiteralRaytraceMaterial.SetTexture("_ColorAndSamples", colorAndSamples);
        SetRenderTargetAuto(ctx.cmd);
        CoreUtils.DrawFullScreen(ctx.cmd, LiteralRaytraceMaterial);
    }

    void DestroyTextures()
    {
        if (colorAndSamples != null)
        {
            colorAndSamples.Release();
        }
    }

    protected override void Cleanup()
    {
        DestroyTextures();
    }
}