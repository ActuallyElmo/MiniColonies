Shader "MiniColonies/MapResourceHighlight"
{
    Properties
    {
        _Color("Outline tint", Color) = (1, 0.8, 0.25, 1)
        _MainTex("Building placement outline sheet", 2D) = "white" {}
        _DashPixels("Dash length (pixels)", Range(1, 16)) = 3
        _GapPixels("Gap length (pixels)", Range(0, 16)) = 3
        _StepsPerSecond("Animation steps per second (signed)", Range(-32, 32)) = 8
        [Enum(Original, 0, Diagonal, 1)] _MotionMode("Motion mode", Float) = 1
        // Keep the property key so existing material overrides retain their value.
        _OutlineWidthPixels("Pixels across outline", Range(1, 4)) = 3
        [HideInInspector] _PixelsPerWorldUnit("Outline pixel density", Float) = 16
        [HideInInspector] _SolidOutline("Solid selection outline", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            // Supplied from Time.unscaledTime by the shared renderer. Cosmetic
            // hover feedback keeps moving through simulation pause/speed changes.
            float _WorldPlacementHighlightTime;
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _DashPixels;
                float _GapPixels;
                float _StepsPerSecond;
                float _MotionMode;
                float _OutlineWidthPixels;
                float _PixelsPerWorldUnit;
                float _SolidOutline;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 worldXZ : TEXCOORD1;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.worldXZ = TransformObjectToWorld(input.positionOS.xyz).xz;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                // Quantize both space and motion. This is the placement hatch's
                // diagonal stripe pattern masked by the existing outline geometry.
                float density = _OutlineWidthPixels >= 1.0 ? floor(min(_OutlineWidthPixels, 4.0) + 0.5) : 1.0;
                float2 pixel = floor(input.worldXZ * max(0.0001, _PixelsPerWorldUnit) * density);
                float dash = max(1.0, floor(_DashPixels + 0.5));
                float gap = max(0.0, floor(_GapPixels + 0.5));
                float period = dash + gap;
                float frame = floor(_WorldPlacementHighlightTime * _StepsPerSecond);
                // Positive speed travels SW -> NE (+X, +Z). The hatch uses
                // x + z so moving along that diagonal crosses its stripes.
                // Split one phase between the axes to retain every state.
                float diagonal = step(0.5, _MotionMode);
                float2 drift = lerp(float2(-frame, 0.0), float2(frame * 0.5, frame * 0.5), diagonal);
                pixel -= drift;
                // Positive modulo handles negative world positions and reverse playback.
                float index = lerp(pixel.x - pixel.y, pixel.x + pixel.y, diagonal);
                float phase = index - floor(index / period) * period;
                half mask = phase < dash ? 1.0h : 0.0h;
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _Color;
                // State is supplied per renderer, preserving the shared material.
                color.a *= _SolidOutline > 0.5 ? 1.0h : mask;
                return color;
            }
            ENDHLSL
        }
    }
}
