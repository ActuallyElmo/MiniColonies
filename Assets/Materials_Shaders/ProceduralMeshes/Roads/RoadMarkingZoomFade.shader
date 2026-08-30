Shader "MiniColonies/Road Marking Zoom Dither"
{
    Properties
    {
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        _FadeStartZoomPercentage("Dither Out Below (%)", Range(0, 100)) = 65
        _FullyHiddenZoomPercentage("Fully Hidden At (%)", Range(0, 100)) = 50
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
        }

        Pass
        {
            Name "RoadMarkingDither"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float4 shadowCoord : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _FadeStartZoomPercentage;
                float _FullyHiddenZoomPercentage;
            CBUFFER_END

            float _CameraZoomPercentage;
            float _CameraZoomPercentageSet;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.shadowCoord = GetShadowCoord(positionInputs);
                return output;
            }

            float Bayer2(float x, float y)
            {
                return (2.0 * x) + (y * (3.0 - (4.0 * x)));
            }

            float Bayer4x4(float2 pixelPosition)
            {
                float2 pixel = floor(pixelPosition);
                float xLow = fmod(pixel.x, 2.0);
                float yLow = fmod(pixel.y, 2.0);
                float xHigh = fmod(floor(pixel.x * 0.5), 2.0);
                float yHigh = fmod(floor(pixel.y * 0.5), 2.0);
                float threshold = (4.0 * Bayer2(xLow, yLow)) + Bayer2(xHigh, yHigh);

                // Offset into the middle of each of the 16 coverage steps so
                // 0% always rejects every pixel and 100% always keeps every pixel.
                return (threshold + 0.5) / 16.0;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Keep markings visible in edit mode before a CameraController publishes its value.
                float cameraZoomPercentage = lerp(
                    100.0,
                    _CameraZoomPercentage,
                    saturate(_CameraZoomPercentageSet));

                float hiddenAt = min(_FullyHiddenZoomPercentage, _FadeStartZoomPercentage);
                float fadeStartsAt = max(_FullyHiddenZoomPercentage, _FadeStartZoomPercentage);
                float fadeRange = max(fadeStartsAt - hiddenAt, 0.0001);
                float zoomFade = smoothstep(0.0, 1.0, (cameraZoomPercentage - hiddenAt) / fadeRange);

                // Opaque, depth-writing dither instead of transparent blending.
                // This preserves shadow receiving while changing apparent coverage.
                clip(zoomFade - Bayer4x4(input.positionCS.xy));

                half4 color = _BaseColor;
                half3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight(input.shadowCoord);

                half3 ambientLighting = SampleSH(normalWS);
                half3 directLighting = LightingLambert(
                    mainLight.color,
                    mainLight.direction,
                    normalWS);
                directLighting *= mainLight.distanceAttenuation * mainLight.shadowAttenuation;

                color.rgb *= ambientLighting + directLighting;
                color.a = 1.0h;
                return color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
