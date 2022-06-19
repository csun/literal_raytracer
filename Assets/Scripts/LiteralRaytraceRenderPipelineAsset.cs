using UnityEngine;
using UnityEngine.Rendering;

namespace LiteralRaytrace
{
    [CreateAssetMenu(menuName = "Rendering/Literal Raytrace Render Pipeline")]
    public class LiteralRaytraceRenderPipelineAsset : RenderPipelineAsset
    {
        protected override RenderPipeline CreatePipeline()
        {
            return new LiteralRaytraceRenderPipeline();
        }
    }
}
