using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering;
using LiteralRaytrace;

class LiteralRaytraceCustomPass : CustomPass
{
    const int MAX_RAYS = 256;

    public LiteralRaytraceCamera RaytraceCamera;
    public ComputeShader SamplingShader;
    public Material DrawMaterial;
    public float blendAmount = 1;

    RenderTexture colorAndSamples;

    Vector4[] screenspaceRayStartBuf = new Vector4[MAX_RAYS];
    Vector4[] screenspaceRayDeltaBuf = new Vector4[MAX_RAYS];
    float[] worldspaceRayLengthBuf = new float[MAX_RAYS];
    Vector4[] rayColorBuf = new Vector4[MAX_RAYS];
    float[] rayStartIntensitiesBuf = new float[MAX_RAYS];

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

        SamplingShader.SetTexture(0, "_ColorAndSamples", colorAndSamples);
        SamplingShader.SetVectorArray("_SSRayStarts", screenspaceRayStartBuf);
        SamplingShader.SetVectorArray("_SSRayDeltas", screenspaceRayDeltaBuf);
        SamplingShader.SetFloats("_WSRayLengths", worldspaceRayLengthBuf);
        SamplingShader.SetVectorArray("_RayColors", rayColorBuf);
        SamplingShader.SetFloats("_RayNormalizedStartIntensities", rayStartIntensitiesBuf);
        SamplingShader.SetInt("_RayCount", RaytraceCamera.RaysToDraw.Count);
        ctx.cmd.DispatchCompute(SamplingShader, 0, Screen.width / 8, Screen.height / 8, 1);

        DrawMaterial.SetTexture("_ColorAndSamples", colorAndSamples);
        DrawMaterial.SetFloat("_BlendAmount", blendAmount);
        SetRenderTargetAuto(ctx.cmd);
        CoreUtils.DrawFullScreen(ctx.cmd, DrawMaterial);
    }

    private void ReinitializeTextures()
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

    void DestroyTextures()
    {
        if (colorAndSamples != null)
        {
            colorAndSamples.Release();
        }
    }

    void LoadRayBuffers()
    {
        for (var i = 0; i < RaytraceCamera.RaysToDraw.Count; i++)
        {
            var ray = RaytraceCamera.RaysToDraw[i];
            screenspaceRayStartBuf[i] = ray.screenspaceStart;
            screenspaceRayDeltaBuf[i] = ray.screenspaceDelta;
            worldspaceRayLengthBuf[i] = ray.worldspaceLength;
            rayColorBuf[i] = ray.color;
            rayStartIntensitiesBuf[i] = ray.normalizedStartIntensity;
        }
    }

    protected override void Cleanup()
    {
        DestroyTextures();
    }
}