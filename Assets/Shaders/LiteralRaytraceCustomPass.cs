using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering;
using LiteralRaytrace;

class LiteralRaytraceCustomPass : CustomPass
{
    const int MAX_RAYS = 256;
    const int PYRAMID_REGION_SIZE = 4;

    public LiteralRaytraceCamera RaytraceCamera;
    public ComputeShader SamplingShader;
    public ComputeShader BrightnessPyramidShader;
    public Material DrawMaterial;
    public float FixedMaxBrightness = -1;
    public float ExposureCurvePower = 1;
    public float Exposure = 1;
    public float BlendAmount = 1;

    RenderTexture sampledColor;
    RenderTexture sampledTotalBrightness;
    RenderTexture[] brightnessPyramids = new RenderTexture[2];
    int currentBrightnessPyramid = 0;
    int nextBrightnessPyramid { get { return (currentBrightnessPyramid + 1) % 2; } }

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

        if (FixedMaxBrightness <= 0)
        {
            var pyramidGroups = new Vector2((float)Screen.width / PYRAMID_REGION_SIZE, (float)Screen.height / PYRAMID_REGION_SIZE);
            ctx.cmd.SetComputeTextureParam(BrightnessPyramidShader, 0, "_Input", sampledTotalBrightness);
            ctx.cmd.SetComputeTextureParam(BrightnessPyramidShader, 0, "_Output", brightnessPyramids[currentBrightnessPyramid]);
            ctx.cmd.DispatchCompute(
                BrightnessPyramidShader, 0, Mathf.CeilToInt(pyramidGroups.x), Mathf.CeilToInt(pyramidGroups.y), 1);

            while (Mathf.Max(pyramidGroups.x, pyramidGroups.y) >= 1)
            {
                pyramidGroups /= PYRAMID_REGION_SIZE;

                ctx.cmd.SetComputeTextureParam(BrightnessPyramidShader, 0, "_Input", brightnessPyramids[currentBrightnessPyramid]);
                ctx.cmd.SetComputeTextureParam(BrightnessPyramidShader, 0, "_Output", brightnessPyramids[nextBrightnessPyramid]);

                ctx.cmd.DispatchCompute(
                    BrightnessPyramidShader, 0, Mathf.CeilToInt(pyramidGroups.x), Mathf.CeilToInt(pyramidGroups.y), 1);

                currentBrightnessPyramid = nextBrightnessPyramid;
            } 

            DrawMaterial.SetTexture("_BrightnessPyramid", brightnessPyramids[currentBrightnessPyramid]);
        }

        DrawMaterial.SetTexture("_SampledColor", sampledColor);
        DrawMaterial.SetTexture("_SampledTotalBrightness", sampledTotalBrightness);
        DrawMaterial.SetFloat("_FixedMaxBrightness", FixedMaxBrightness);
        DrawMaterial.SetFloat("_ExposureCurvePower", ExposureCurvePower);
        DrawMaterial.SetFloat("_Exposure", Exposure);
        DrawMaterial.SetFloat("_BlendAmount", BlendAmount);
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

            for (int i = 0; i < 2; i++)
            {
                brightnessPyramids[i] = new RenderTexture(
                    Screen.width / PYRAMID_REGION_SIZE, Screen.height / PYRAMID_REGION_SIZE, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
                brightnessPyramids[i].enableRandomWrite = true;
                brightnessPyramids[i].Create();
            }
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
        for (int i = 0; i < 2; i++)
        {
            if (brightnessPyramids[i] != null)
            {
                brightnessPyramids[i].Release();
            }
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