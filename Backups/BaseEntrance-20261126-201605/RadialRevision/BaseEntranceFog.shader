Shader "BehindTheGear/Base Entrance Fog"
{
    Properties
    {
        [PerRendererData] _MainTex("Fog alpha", 2D) = "white" {}
        _Tint("Fog tint (sRGB)", Color) = (.1215686,.1215686,.1215686,1)
        [HideInInspector] _Color("Color", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("Renderer Color", Color) = (1,1,1,1)
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
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            struct Attributes { COMMON_2D_INPUTS half4 color:COLOR; };
            struct Varyings { COMMON_2D_OUTPUTS half4 color:COLOR; };
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half4 _Color;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                Varyings output = CommonUnlitVertex(input);
                output.color = input.color * _Color * unity_SpriteColor;
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
