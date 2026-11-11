Shader "BehindTheGear/World Unsharp Mask"
{
    Properties
    {
        _Amount ("Amount", Range(0, 2)) = 0.5
        _Radius ("Radius (Pixels)", Range(0, 4)) = 1
        _Threshold ("Threshold", Range(0, 0.2)) = 0.01
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "World Unsharp Mask"
            ZWrite Off ZTest Always Cull Off Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float4 _SourceTexelSize;
            CBUFFER_START(UnityPerMaterial)
                float _Amount;
                float _Radius;
                float _Threshold;
            CBUFFER_END

            half3 ReadColor(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float2 d = _SourceTexelSize.xy * _Radius;
                // Nine-tap Gaussian approximation; sharpen by adding the high-frequency residual.
                half3 blurred = original.rgb * 4;
                blurred += 2 * (ReadColor(uv + float2(d.x, 0)) + ReadColor(uv - float2(d.x, 0))
                    + ReadColor(uv + float2(0, d.y)) + ReadColor(uv - float2(0, d.y)));
                blurred += ReadColor(uv + d) + ReadColor(uv - d)
                    + ReadColor(uv + float2(d.x, -d.y)) + ReadColor(uv + float2(-d.x, d.y));
                blurred *= 1.0 / 16.0;
                half3 detail = original.rgb - blurred;
                half contrast = max(abs(detail.r), max(abs(detail.g), abs(detail.b)));
                half mask = _Threshold <= 0 ? 1 : smoothstep(_Threshold, _Threshold * 2, contrast);
                // Keep HDR highlights and source alpha, but avoid negative color ringing.
                return half4(max(0, original.rgb + detail * (_Amount * mask)), original.a);
            }
            ENDHLSL
        }
    }
}
