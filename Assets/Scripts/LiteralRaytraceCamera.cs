using UnityEngine;

namespace LiteralRaytrace
{
    // Mostly copied from here http://blog.three-eyed-games.com/2018/05/03/gpu-ray-tracing-in-unity-part-1/
    public class LiteralRaytraceCamera : MonoBehaviour
    {
        public ComputeShader RayTracingShader;
        [HideInInspector]
        public RenderTexture Target;

        private void Update()
        {
            // Make sure we have a current render target
            InitRenderTexture();

            // Set the target and dispatch the compute shader
            RayTracingShader.SetTexture(0, "Result", Target);
            int threadGroupsX = Mathf.CeilToInt(Screen.width / 8.0f);
            int threadGroupsY = Mathf.CeilToInt(Screen.height / 8.0f);
            RayTracingShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
        }

        private void InitRenderTexture()
        {
            if (Target == null || Target.width != Screen.width || Target.height != Screen.height)
            {
                // Release render texture if we already have one
                if (Target != null)
                    Target.Release();

                // Get a render target for Ray Tracing
                Target = new RenderTexture(Screen.width, Screen.height, 0,
                    RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                Target.enableRandomWrite = true;
                Target.Create();
            }
        }
    }
}
