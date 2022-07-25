Shader "FullScreen/LiteralRaytraceShader"
{
	HLSLINCLUDE

	#pragma vertex Vert

	#pragma target 4.5
	#pragma only_renderers d3d11 vulkan metal

	#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

	// Sample count in alpha channel
	Texture2D<float3> _SampledColor;
	Texture2D<float> _SampledTotalBrightness;
	Texture2D<float> _BrightnessPyramid;
	float _Exposure;
	float _BlendAmount;

	float4 ColorPass(Varyings varyings) : SV_Target
	{
		float brightness = clamp(_SampledTotalBrightness[varyings.positionCS.xy] * _Exposure / _BrightnessPyramid[uint2(0,0)], 0, 1);
		float3 sampledColor = _SampledColor[varyings.positionCS.xy];

		// Normalize the color first because the brightness is captured separately
		return float4(sampledColor > 0 ? normalize(sampledColor) * brightness : 0, _BlendAmount);
	}

		ENDHLSL

	SubShader
	{
		Pass
		{
			Name "ColorPass"

			ZWrite Off
			ZTest Always
			Blend SrcAlpha OneMinusSrcAlpha
			Cull Off

			HLSLPROGRAM
				#pragma fragment ColorPass
			ENDHLSL
		}
	}
	Fallback Off
}
