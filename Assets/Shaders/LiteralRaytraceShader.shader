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
	float _BlendAmount;

	float4 ColorPass(Varyings varyings) : SV_Target
	{
		float4 col = float4(
			_SampledColor[varyings.positionCS.xy] * clamp(_SampledTotalBrightness[varyings.positionCS.xy] / 500, 0, 1),
			_BlendAmount);

		return col;
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
