Shader "FullScreen/LiteralRaytraceShader"
{
	HLSLINCLUDE

#pragma vertex Vert

#pragma target 4.5
#pragma only_renderers d3d11 vulkan metal

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

	// The PositionInputs struct allow you to retrieve a lot of useful information for your fullScreenShader:
	// struct PositionInputs
	// {
	//     float3 positionWS;  // World space position (could be camera-relative)
	//     float2 positionNDC; // Normalized screen coordinates within the viewport    : [0, 1) (with the half-pixel offset)
	//     uint2  positionSS;  // Screen space pixel coordinates                       : [0, NumPixels)
	//     uint2  tileCoord;   // Screen tile coordinates                              : [0, NumTiles)
	//     float  deviceDepth; // Depth from the depth buffer                          : [0, 1] (typically reversed)
	//     float  linearDepth; // View space Z coordinate                              : [Near, Far]
	// };

	// To sample custom buffers, you have access to these functions:
	// But be careful, on most platforms you can't sample to the bound color buffer. It means that you
	// can't use the SampleCustomColor when the pass color buffer is set to custom (and same for camera the buffer).
	// float4 SampleCustomColor(float2 uv);
	// float4 LoadCustomColor(uint2 pixelCoords);
	// float LoadCustomDepth(uint2 pixelCoords);
	// float SampleCustomDepth(float2 uv);

	// There are also a lot of utility function you can use inside Common.hlsl and Color.hlsl,
	// you can check them out in the source code of the core SRP package.

	// Sample count in alpha channel
	Texture2D<float4> _ColorAndSamples;
	float _TotalRays;

	float4 ColorPass(Varyings varyings) : SV_Target
	{
		// Need to add Properties block if want to edit material properties in inspector https://docs.unity3d.com/Manual/SL-Properties.html
		// positionCS is normalized [0, 1] screenspace position
		// depth appears to be in world units

		float depth = LoadCameraDepth(varyings.positionCS.xy);
		PositionInputs posInput = GetPositionInput(varyings.positionCS.xy, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);

		float4 col = _ColorAndSamples[varyings.positionCS.xy];
		col.b = depth * 10;

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
