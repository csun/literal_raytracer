Shader "FullScreen/LiteralRaytraceShader"
{
	HLSLINCLUDE

	#pragma vertex Vert

	#pragma target 4.5
	#pragma only_renderers d3d11 vulkan metal

	#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

	// Sample count in alpha channel
	Texture2D<float4> _ColorAndSamples;

	float4 ColorPass(Varyings varyings) : SV_Target
	{
		// Need to add Properties block if want to edit material properties in inspector https://docs.unity3d.com/Manual/SL-Properties.html
		// positionCS is normalized [0, 1] screenspace position
		// depth appears to be in world units
		float4 col = _ColorAndSamples[varyings.positionCS.xy];
		col.a = 1;

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
