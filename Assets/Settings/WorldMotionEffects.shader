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
                half luminance = dot(color, half3(.2126, .7152, .0722));
                color = lerp(color, luminance.xxx, saturate(_HealthTint.z));
                // Overlay is evaluated in display color space, then returned to the render target's space.
                half3 display = LinearToSRGB(color);
                half3 tint = half3(110.0 / 255.0, 0, 0);
                half3 overlay = lerp(2 * display * tint, 1 - 2 * (1 - display) * (1 - tint), step(.5, display));
                display = lerp(display, overlay, _HealthTint.x) * (1 - _HealthTint.y);
                color = SRGBToLinear(display);
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                return half4(color, original.a);
            }
            ENDHLSL
        }
    }
}
