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
                half3 color = SplitColor(uv);
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                return half4(color, original.a);
            }
            ENDHLSL
        }
    }
}
