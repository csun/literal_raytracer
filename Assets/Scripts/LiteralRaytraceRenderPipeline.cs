using UnityEngine;
using UnityEngine.Rendering;

namespace LiteralRaytrace
{
    public class LiteralRaytraceRenderPipeline : RenderPipeline
    {
        CommandBuffer buffer = new CommandBuffer
        {
            name = "LiteralRaytrace Render"
        };

        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            foreach (var camera in cameras)
            {
                RenderCamera(context, camera);
            }
        }

        void RenderCamera(ScriptableRenderContext context, Camera camera)
        {
            SetupCamera(context, camera);
            DrawCamera(context, camera);
            context.Submit();
        }

        void SetupCamera(ScriptableRenderContext context, Camera camera)
        {
            context.SetupCameraProperties(camera);
        }

        void DrawCamera(ScriptableRenderContext context, Camera camera)
        {
        }
    }
}
