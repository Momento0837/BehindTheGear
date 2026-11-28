Shader "BehindTheGear/Base Entrance Fog"
{
    Properties
    {
        [PerRendererData] _MainTex("Fog alpha", 2D) = "white" {}
        _Tint("Fog tint (sRGB)", Color) = (.1215686,.1215686,.1215686,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            struct Attributes { float3 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                output.color = input.color * unity_SpriteColor;
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                half3 tint = _Tint.rgb;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                tint = SRGBToLinear(tint);
                #endif
                // Original fog art is black; tint its alpha silhouette instead of multiplying black RGB.
                return half4(tint, SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv).a * input.color.a * _Tint.a);
            }
            ENDHLSL
        }
    }
}
