Shader "MiniColonies/Pixel Water"
{
    Properties
    {
        [Header(Water Palette)]
        _ShallowColor("Shallow Water", Color) = (0.2, 0.72, 0.69, 1)
        _DeepColor("Deep Water", Color) = (0.055, 0.34, 0.49, 1)
        _RippleColor("Ripple Color", Color) = (0.24, 0.65, 0.73, 1)
        _CrestColor("Wave Crest Color", Color) = (0.65, 0.91, 0.88, 1)
        _DepthDistance("Depth Color Distance", Float) = 2.5
        _ShallowOpacity("Shallow Opacity", Range(0, 1)) = 0.65
        _DeepOpacity("Deep Opacity", Range(0, 1)) = 0.96
        _LightingInfluence("Scene Lighting Influence", Range(0, 1)) = 0.3

        [Header(Pixel Ripples)]
        _PixelSize("Pixel Size (World Units)", Float) = 0.1
        _FlowDirection("Flow Direction (World XZ)", Vector) = (1, 0.35, 0, 0)
        _FlowSpeed("Flow Speed (World Units Per Second)", Float) = 0.28
        _WaveLength("Wave Length (World Units)", Float) = 3.5
        _WaveStrength("Wave Normal Strength", Range(0, 1)) = 0.18
        _RippleContrast("Ripple Color Contrast", Range(0, 1)) = 0.45
        _CrestStrength("Wave Crest Strength", Range(0, 1)) = 0.65

        [Header(Building Reflections)]
        [ToggleUI] _ScreenReflections("Reflect Visible Scene Objects", Float) = 1
        _ReflectionDirection("Reflection Ray Direction (World XZ)", Vector) = (-0.4, 0.4, 0, 0)
        _ReflectionHeight("Reflection Height Reach", Range(0.5, 20)) = 8
        _ReflectionDistortion("Reflection Ripple Distortion", Range(0, 0.3)) = 0.055
        _ReflectionTint("Reflection Tint", Color) = (0.72, 0.91, 0.98, 1)
        _ReflectionStrength("Object Reflection Strength", Range(0, 1)) = 0.7
        _Smoothness("Environment Reflection Smoothness", Range(0, 1)) = 0.8
        _SpecularStrength("Sun Highlight Strength", Range(0, 1)) = 0.18

        [Header(Shoreline)]
        _FoamColor("Foam Color", Color) = (0.88, 0.98, 0.9, 1)
        _FoamWidth("Foam Width (Depth Units)", Float) = 0.5
        _FoamStrength("Foam Strength", Range(0, 1)) = 0.9
        _FoamSpeed("Shore Wave Speed", Range(0, 2)) = 0.4
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "WaterForward"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _RippleColor;
                half4 _CrestColor;
                half4 _ReflectionTint;
                half4 _FoamColor;
                float4 _FlowDirection;
                float4 _ReflectionDirection;
                float _DepthDistance;
                float _PixelSize;
                float _FlowSpeed;
                float _WaveLength;
                float _FoamWidth;
                float _FoamSpeed;
                float _ReflectionHeight;
                float _ReflectionDistortion;
                float _ScreenReflections;
                half _ShallowOpacity;
                half _DeepOpacity;
                half _LightingInfluence;
                half _WaveStrength;
                half _RippleContrast;
                half _CrestStrength;
                half _ReflectionStrength;
                half _Smoothness;
                half _SpecularStrength;
                half _FoamStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            float WaterHash(float2 p)
            {
                float3 q = frac(p.xyx * 0.1031);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }

            // Smooth spatial noise and its analytic gradient. Only the coordinates
            // move: there is no independently animated noise per pixel.
            float3 WaterNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float2 du = 6.0 * f * (1.0 - f);
                float a = WaterHash(cell);
                float b = WaterHash(cell + float2(1, 0));
                float c = WaterHash(cell + float2(0, 1));
                float d = WaterHash(cell + 1.0);
                float crossTerm = a - b - c + d;
                return float3(a + (b - a) * u.x + (c - a) * u.y + crossTerm * u.x * u.y,
                    du.x * (b - a + crossTerm * u.y),
                    du.y * (c - a + crossTerm * u.x));
            }

            void SampleWater(float2 pixelPosition, float footprint,
                out float band, out float crest, out float breakup, out float2 slope)
            {
                float2 direction = _FlowDirection.xy;
                float directionLength = dot(direction, direction);
                direction = directionLength > 0.0001
                    ? direction * rsqrt(max(directionLength, 0.0001)) : float2(1, 0);
                float2 across = float2(-direction.y, direction.x);
                float wavelength = max(abs(_WaveLength), max(abs(_PixelSize), 0.001) * 16.0);
                float2 q = float2(dot(pixelPosition, direction), dot(pixelPosition, across)) / wavelength;
                q.x -= _Time.y * _FlowSpeed / wavelength;

                float3 bend = WaterNoise(q * float2(0.8, 0.7) + 5.3);
                float phaseA = 6.2831853 * (q.x + bend.x * 0.6);
                float phaseB = 6.2831853 * (q.x * 1.6 + q.y * 0.2) + bend.x * 1.7;
                float wave = sin(phaseA) * 0.72 + sin(phaseB) * 0.28;
                float2 gradientA = float2(1, 0) + bend.yz * float2(0.8, 0.7) * 0.6;
                float2 gradientB = float2(1.6, 0.2) + bend.yz * float2(0.8, 0.7) * (1.7 / 6.2831853);
                float2 gradient = cos(phaseA) * 0.72 * gradientA + cos(phaseB) * 0.28 * gradientB;
                slope = (direction * gradient.x + across * gradient.y) * saturate(_WaveStrength);

                // Three deliberate tones, with curved, broken highlights at the crests.
                float aa = clamp(footprint / wavelength * 4.0, 0.015, 0.25);
                band = 0.5 * smoothstep(-0.25 - aa, -0.25 + aa, wave)
                    + 0.5 * smoothstep(0.35 - aa, 0.35 + aa, wave);
                breakup = WaterNoise(q * float2(1.3, 3.2) + float2(17.1, 9.2)).x;
                float segments = smoothstep(0.44, 0.62, breakup);
                crest = smoothstep(0.88 - aa, 0.98 + aa, sin(phaseA)) * segments;
                crest += smoothstep(0.95 - aa, 1.0 + aa, sin(phaseB))
                    * smoothstep(0.7, 0.85, breakup) * 0.3;
                // Two crisp highlight tones keep the wavelets from looking like
                // blurry noise patches while preserving neighboring pixel groups.
                crest = smoothstep(0.22 - aa * 0.25, 0.22 + aa * 0.25, crest) * 0.55
                    + smoothstep(0.65 - aa * 0.25, 0.65 + aa * 0.25, crest) * 0.45;
                crest *= 1.0 - smoothstep(0.06, 0.18, footprint / wavelength);
            }

            float SceneEyeDepth(float2 uv)
            {
                if (_CameraDepthTexture_TexelSize.z < 2.0) return _ProjectionParams.z;
                float2 depthUV = ClampAndScaleUVForBilinear(UnityStereoTransformScreenSpaceTex(uv),
                    _CameraDepthTexture_TexelSize.xy);
                // Explicit LOD is required inside the divergent reflection search.
                float raw = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_PointClamp, depthUV, 0).r;
                return unity_OrthoParams.w > 0.5
                    ? LinearDepthToEyeDepth(raw) : LinearEyeDepth(raw, _ZBufferParams);
            }

            bool ProjectWaterRay(float3 positionVS, out float2 uv)
            {
                float4 clip = mul(UNITY_MATRIX_P, float4(positionVS, 1.0));
                uv = (float2(clip.x, clip.y * _ProjectionParams.x) / max(clip.w, 0.0001)) * 0.5 + 0.5;
                return clip.w > 0.0 && all(uv > 0.001) && all(uv < 0.999);
            }

            // Stylized screen-space reprojection for the straight-down camera.
            // The ray shear reverses the buildings' apparent lean across the water.
            // At default building bend (-0.105, +0.105), a ray of (-0.21, +0.21)
            // produces an equal opposite lean; the default lengthens it for clarity.
            // Captures visible opaque colors/depth only, not off-screen geometry.
            half4 ReflectVisibleScene(float3 pixelPositionWS, float2 slope)
            {
                if (_ScreenReflections < 0.5 || _ReflectionStrength <= 0.0
                    || _CameraOpaqueTexture_TexelSize.z < 2.0 || _CameraDepthTexture_TexelSize.z < 2.0)
                    return half4(0, 0, 0, 0);

                float3 originVS = TransformWorldToView(pixelPositionWS);
                float2 rayShear = _ReflectionDirection.xy + slope * clamp(_ReflectionDistortion, 0.0, 0.3);
                float3 rayVS = mul((float3x3)UNITY_MATRIX_V, float3(rayShear.x, 1.0, rayShear.y));
                float reach = clamp(_ReflectionHeight, 0.5, 20.0);
                const int steps = 24;
                float stepHeight = reach / steps;
                float previousHeight = 0.0;
                float previousGap = 1.0;
                int candidates = 0;
                float2 uv;

                // An upward ray enters the screen's height field, then exits its
                // visible roof/side. Refine that exit, rejecting silhouette jumps
                // that have no matching surface depth (which would smear the bank).
                [loop]
                for (int i = 1; i <= steps; i++)
                {
                    float height = i * stepHeight;
                    float3 sampleVS = originVS + rayVS * height;
                    if (!ProjectWaterRay(sampleVS, uv)) break;
                    float gap = SceneEyeDepth(uv) + sampleVS.z;
                    if (previousGap < 0.0 && gap >= 0.0)
                    {
                        candidates++;
                        float low = previousHeight;
                        float high = height;
                        [unroll]
                        for (int refinement = 0; refinement < 5; refinement++)
                        {
                            float middle = (low + high) * 0.5;
                            float3 middleVS = originVS + rayVS * middle;
                            float2 middleUV;
                            ProjectWaterRay(middleVS, middleUV);
                            float middleGap = SceneEyeDepth(middleUV) + middleVS.z;
                            if (middleGap < 0.0) low = middle;
                            else high = middle;
                        }

                        float3 hitVS = originVS + rayVS * low;
                        float2 hitUV;
                        ProjectWaterRay(hitVS, hitUV);
                        float error = abs(SceneEyeDepth(hitUV) + hitVS.z);
                        if (error < max(0.025, stepHeight * 0.12))
                        {
                            float edge = min(min(hitUV.x, hitUV.y), min(1.0 - hitUV.x, 1.0 - hitUV.y));
                            float fade = smoothstep(0.005, 0.04, edge)
                                * (1.0 - smoothstep(reach * 0.75, reach, low));
                            float2 colorUV = ClampAndScaleUVForBilinear(UnityStereoTransformScreenSpaceTex(hitUV),
                                _CameraOpaqueTexture_TexelSize.xy);
                            half3 reflectedColor = SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture,
                                sampler_PointClamp, colorUV, 0).rgb;
                            return half4(reflectedColor, fade);
                        }
                        // Bound the cost when several silhouette edges cross a ray.
                        if (candidates >= 2) break;
                    }
                    previousGap = gap;
                    previousHeight = height;
                }
                return half4(0, 0, 0, 0);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float pixelSize = max(abs(_PixelSize), 0.001);
                float footprint = max(length(ddx(input.positionWS.xz)), length(ddy(input.positionWS.xz)));
                float2 gridPosition = (floor(input.positionWS.xz / pixelSize) + 0.5) * pixelSize;
                float pixelWeight = 1.0 - smoothstep(pixelSize, pixelSize * 3.0, footprint);
                float2 pixelPosition = lerp(input.positionWS.xz, gridPosition, pixelWeight);
                float band, crest, breakup;
                float2 slope;
                SampleWater(pixelPosition, footprint, band, crest, breakup, slope);

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float eyeDepth = SceneEyeDepth(screenUV);
                float surfaceDepth = -TransformWorldToView(input.positionWS).z;
                float depth = eyeDepth - surfaceDepth;
                depth = depth < -0.01 ? max(_DepthDistance, 0.001) + max(_FoamWidth, 0.0) : max(depth, 0.0);
                float depthBlend = saturate(depth / max(_DepthDistance, 0.001));
                // Subtle steps make the shallow shelf read as a designed color band.
                float depthBands = floor(depthBlend * 4.0) * 0.25;
                depthBlend = lerp(depthBlend, depthBands, 0.45);
                half3 color = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthBlend);
                color = lerp(color * (1.0h - saturate(_RippleContrast) * 0.22h),
                    _RippleColor.rgb, band * saturate(_RippleContrast));

                half3 normal = normalize(input.normalWS + float3(-slope.x, 0, -slope.y));
                half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                Light sun = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half sunAmount = saturate(dot(normal, sun.direction)) * sun.shadowAttenuation;
                half3 illumination = max(SampleSH(normal), half3(0.25h, 0.25h, 0.25h))
                    + sun.color * (sunAmount * 0.55h);
                illumination = lerp(half3(1, 1, 1), illumination, saturate(_LightingInfluence));
                color *= illumination;

                half grazing = pow(1.0h - saturate(dot(normal, view)), 4.0h);
                half3 environment = GlossyEnvironmentReflection(reflect(-view, normal),
                    1.0h - saturate(_Smoothness), 1.0h);
                color = lerp(color, environment * _ReflectionTint.rgb,
                    saturate(_ReflectionStrength) * (0.04h + grazing * 0.25h));

                half4 reflected = ReflectVisibleScene(float3(pixelPosition.x, input.positionWS.y, pixelPosition.y), slope);
                half reflectionWeight = reflected.a * saturate(_ReflectionStrength);
                color = lerp(color, reflected.rgb * _ReflectionTint.rgb, reflectionWeight);
                // Crests and foam sit over the reflection, breaking up its silhouette.
                color = lerp(color, _CrestColor.rgb * illumination, saturate(crest * _CrestStrength));

                half3 halfDirection = SafeNormalize(sun.direction + view);
                half highlight = pow(saturate(dot(normal, halfDirection)), lerp(24.0h, 160.0h, saturate(_Smoothness)));
                color += sun.color * highlight * saturate(_SpecularStrength) * sun.shadowAttenuation;

                float foamWidth = max(_FoamWidth, 0.001);
                float shoreNoise = WaterNoise(pixelPosition / max(abs(_WaveLength), 0.25) * 2.0).x;
                float shore = 1.0 - smoothstep(foamWidth * 0.12,
                    foamWidth * (0.55 + shoreNoise * 0.35), depth);
                float washPhase = depth / foamWidth * 8.0 - _Time.y * _FoamSpeed * 6.2831853 + shoreNoise * 2.0;
                float wash = smoothstep(0.82, 0.98, sin(washPhase))
                    * (1.0 - smoothstep(foamWidth * 0.4, foamWidth * 1.8, depth))
                    * smoothstep(0.25, 0.55, breakup);
                half foam = saturate(max(shore, wash * 0.7) * _FoamStrength);
                foam = _FoamWidth > 0.0 ? foam : 0.0h;
                color = lerp(color, _FoamColor.rgb * illumination, foam);

                half opacity = lerp(saturate(_ShallowOpacity), saturate(_DeepOpacity), depthBlend);
                opacity = lerp(opacity, 1.0h, max(foam, reflectionWeight));
                return half4(MixFog(color, input.fogFactor), opacity);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
