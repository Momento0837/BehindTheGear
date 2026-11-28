Shader "BehindTheGear/World Chromatic Motion"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Chromatic Motion"
            ZWrite Off ZTest Always Cull Off Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float4 _OutputPixelSize;
            float _SeparationPixels;
            float4 _HealthTint;
            float _DamageFlashOpacity;
            float4 _EdgeHaze; // edge threshold, opacity, blur radius, spread (output pixels)
            float4 _HpRippleBar; // normalized screen-space center and half size, bottom-left origin
            float4 _HpRippleParameters; // phase, intensity, HUD scale, enabled
            float RippleHeight(float2 p, float2 halfSize, float age)
            {
                float t = saturate(age / .8);
                float radius = min(4.0, min(halfSize.x, halfSize.y));
                float2 q = abs(p) - max(halfSize - radius, 0);
                float d = length(max(q, 0)) + min(max(q.x, q.y), 0) - radius;
                float expansion = lerp(2.0, lerp(16.0, 28.0, _HpRippleParameters.y), t);
                float crest = exp2(-pow((d - expansion) / 3.0, 2.0));
                return crest * smoothstep(0, .10, t) * pow(1 - t, 1.5) * step(0, age);
            }
            float RippleProfile(float2 p, float2 halfSize)
            {
                return RippleHeight(p, halfSize, _HpRippleParameters.x)
                    + .55 * RippleHeight(p, halfSize, _HpRippleParameters.x - .32);
            }
            float2 RippleSlope(float2 uv)
            {
                if (_HpRippleParameters.w < .5) return 0;
                float2 screenUV = uv;
                #if UNITY_UV_STARTS_AT_TOP
                screenUV.y = 1 - screenUV.y;
                #endif
                float2 unitUV = _OutputPixelSize.xy * max(.01, _HpRippleParameters.z);
                float2 p = (screenUV - _HpRippleBar.xy) / unitUV;
                float2 halfSize = _HpRippleBar.zw / unitUV;
                if (any(abs(p) > halfSize + 36)) return 0;
                // Surface-normal gradient: convex and concave edges bend the scene in opposite directions.
                return float2(RippleProfile(p + float2(1,0), halfSize) - RippleProfile(p - float2(1,0), halfSize),
                    RippleProfile(p + float2(0,1), halfSize) - RippleProfile(p - float2(0,1), halfSize)) * .5;
            }
            half3 SplitColor(float2 uv)
            {
                float2 shift = float2(_OutputPixelSize.x * _SeparationPixels, 0);
                return half3(
                    SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + shift).r,
                    SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).g,
                    SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - shift).b);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 slope = RippleSlope(uv);
                float emboss = dot(slope, normalize(float2(-1, 1))) * lerp(.10, .22, _HpRippleParameters.y);
                float2 displacement = slope * lerp(2.0, 4.0, _HpRippleParameters.y) * _OutputPixelSize.xy;
                #if UNITY_UV_STARTS_AT_TOP
                displacement.y = -displacement.y;
                #endif
                uv = saturate(uv + displacement);
                half3 color = SplitColor(uv);
                // A continuous screen-space veil, without any ground geometry or camera-following strip.
                float2 fromCenter = abs(uv * 2 - 1);
                // Dilate the haze density contour rather than adding more blur.
                fromCenter += 2.0 * _OutputPixelSize.xy * max(0.0, _EdgeHaze.w);
                float edge = smoothstep(_EdgeHaze.x, 1.0, max(fromCenter.x, fromCenter.y));
                if (_EdgeHaze.y > 0 && edge > 0)
                {
                    float2 radius = _OutputPixelSize.xy * _EdgeHaze.z;
                    half3 soft = SplitColor(uv) * .4;
                    soft += SplitColor(uv + float2(radius.x, 0)) * .15;
                    soft += SplitColor(uv - float2(radius.x, 0)) * .15;
                    soft += SplitColor(uv + float2(0, radius.y)) * .15;
                    soft += SplitColor(uv - float2(0, radius.y)) * .15;
                    color = lerp(color, soft, edge * .3);
                }
                half luminance = dot(color, half3(.2126, .7152, .0722));
                color = lerp(color, luminance.xxx, saturate(_HealthTint.z));
                // Overlay is evaluated in display color space, then returned to the render target's space.
                half3 display = LinearToSRGB(color);
                half3 tint = half3(110.0 / 255.0, 0, 0);
                half3 overlay = lerp(2 * display * tint, 1 - 2 * (1 - display) * (1 - tint), step(.5, display));
                display = lerp(display, overlay, _HealthTint.x) * (1 - _HealthTint.y);
                // A very light overall haze, increasing smoothly toward the frame. HUD is rendered later.
                display = lerp(display, half3(1, 1, 1), _EdgeHaze.y * lerp(.12, 1.0, edge));
                display = lerp(display, half3(1, 0, 0), saturate(_DamageFlashOpacity));
                // Bevel-style highlight/shadow belongs to the refracted scenery, not the light contour.
                display = saturate(display + emboss);
                color = SRGBToLinear(display);
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                return half4(color, original.a);
            }
            ENDHLSL
        }

        // Transient camera-pan blur is performed before chromatic separation and health/haze grading.
        Pass
        {
            Name "Story Pan Blur"
            ZWrite Off ZTest Always Cull Off Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment PanBlur
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float4 _OutputPixelSize;
            float _StoryPanBlurPixels;

            half4 PanBlur(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 stepUV = float2(_OutputPixelSize.x * _StoryPanBlurPixels / 8.0, 0);
                half4 sum = 0;
                // Symmetric triangular weights keep the sweep smooth without a sharp original overlay.
                [unroll]
                for (int sampleIndex = -8; sampleIndex <= 8; sampleIndex++)
                {
                    float weight = 9.0 - abs(sampleIndex);
                    sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp,
                        input.texcoord + stepUV * sampleIndex) * weight;
                }
                return sum / 81.0;
            }
            ENDHLSL
        }
    }
}
