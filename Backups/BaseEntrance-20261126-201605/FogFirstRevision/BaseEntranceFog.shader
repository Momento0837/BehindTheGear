Shader "BehindTheGear/Base Entrance Fog"
{
    Properties
    {
        [PerRendererData] _MainTex("Fog alpha", 2D) = "white" {}
        _Tint("Fog tint (sRGB)", Color) = (.1215686,.1215686,.1215686,1)
        [PerRendererData] _Opacity("Opacity", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            struct Attributes { float3 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _Opacity;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                half3 tint = _Tint.rgb;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                tint = SRGBToLinear(tint);
                #endif
                // Original fog art is black; tint its alpha silhouette instead of multiplying black RGB.
                return half4(tint, SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv).a * _Opacity * _Tint.a);
            }
            ENDHLSL
        }
    }
}
