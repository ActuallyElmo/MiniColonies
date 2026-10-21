Shader "MiniColonies/BuildingPlacementPreview"
{
    Properties
    {
        _MainTex("Placement sprite sheet", 2D) = "white" {}
        _Color("Hatch tint", Color) = (1, 1, 1, 0.5)
        _OutlineColor("Outline tint", Color) = (1, 1, 1, 1)
        _HatchRect("Hatch atlas rectangle", Vector) = (0, 0, 1, 1)
        _HatchTiling("Hatch repeats per cell", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" "RenderType" = "Transparent" }
        Pass
        {
            ZWrite Off
            // Keep invalid cells readable over existing buildings and higher terrain.
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _OutlineColor;
                float4 _HatchRect;
                float _HatchTiling;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                bool fill = input.uv.z > 0.5;
                bool repeating = fill && input.uv.z < 1.5;
                float2 uv = repeating
                    ? _HatchRect.xy + frac(input.uv.xy * _HatchTiling) * _HatchRect.zw
                    : input.uv.xy;
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * (fill ? _Color : _OutlineColor);
            }
            ENDHLSL
        }
    }
}
