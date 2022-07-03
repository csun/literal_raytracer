using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering;
using LiteralRaytrace;

class LiteralRaytraceCustomPass : CustomPass
{
    const int MAX_RAYS = 256;

    public LiteralRaytraceCamera RaytraceCamera;
    public ComputeShader SamplingShader;
    public ComputeShader BrightnessPyramidShader;
    public Material DrawMaterial;
    public float blendAmount = 1;

    RenderTexture sampledColor;
    RenderTexture sampledTotalBrightness;

    Vector4[] screenspaceRayStartBuf = new Vector4[MAX_RAYS];
    Vector4[] screenspaceRayDeltaBuf = new Vector4[MAX_RAYS];
    Vector4[] rayColorBuf = new Vector4[MAX_RAYS];

    protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
    {
        ReinitializeTextures();
    }

    protected override void Execute(CustomPassContext ctx)
    {
        // Disable in scene view
        if (ctx.hdCamera.camera.cameraType == CameraType.SceneView)
        {
            return;
        }
        ReinitializeTextures();
        LoadRayBuffers();

        SamplingShader.SetTexture(0, "_Color", sampledColor);
        SamplingShader.SetTexture(0, "_TotalBrightness", sampledTotalBrightness);
        SamplingShader.SetVectorArray("_SSRayStarts", screenspaceRayStartBuf);
        SamplingShader.SetVectorArray("_SSRayDeltas", screenspaceRayDeltaBuf);
        SamplingShader.SetVectorArray("_RayColors", rayColorBuf);
        SamplingShader.SetInt("_RayCount", RaytraceCamera.RaysToDraw.Count);
        ctx.cmd.DispatchCompute(SamplingShader, 0, Screen.width / 8, Screen.height / 8, 1);

        DrawMaterial.SetTexture("_SampledColor", sampledColor);
        DrawMaterial.SetTexture("_SampledTotalBrightness", sampledTotalBrightness);
        DrawMaterial.SetFloat("_BlendAmount", blendAmount);
        SetRenderTargetAuto(ctx.cmd);
        CoreUtils.DrawFullScreen(ctx.cmd, DrawMaterial);
    }

    private void ReinitializeTextures()
    {
        if (sampledColor == null || sampledColor.width != Screen.width || sampledColor.height != Screen.height)
        {
            DestroyTextures();

            sampledColor = new RenderTexture(
                Screen.width, Screen.height, 0, RenderTextureFormat.RGB111110Float, RenderTextureReadWrite.Linear);
            sampledColor.enableRandomWrite = true;
            sampledColor.Create();

            sampledTotalBrightness = new RenderTexture(
                Screen.width, Screen.height, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
            sampledTotalBrightness.enableRandomWrite = true;
            sampledTotalBrightness.Create();
        }
    }

    void DestroyTextures()
    {
        if (sampledColor != null)
        {
            sampledColor.Release();
        }
        if (sampledTotalBrightness != null)
        {
            sampledTotalBrightness.Release();
        }
    }

    void LoadRayBuffers()
    {
        for (var i = 0; i < RaytraceCamera.RaysToDraw.Count; i++)
        {
            var ray = RaytraceCamera.RaysToDraw[i];
            screenspaceRayStartBuf[i] = ray.screenspaceStart;
            screenspaceRayDeltaBuf[i] = ray.screenspaceDelta;
            rayColorBuf[i] = ray.color;
        }
    }

    protected override void Cleanup()
    {
        DestroyTextures();
    }
}