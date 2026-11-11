Shader "BehindTheGear/HideOut Lock Cover"
{
    Properties { _MainTex("Texture", 2D) = "white" {} _Color("Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+10" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Back
            ZWrite On
            ZTest LEqual
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float4 _MainTex_ST;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionHCS:SV_POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; };
            Varyings Vert(Attributes v)
            {
                Varyings o; o.positionHCS=TransformObjectToHClip(v.positionOS.xyz);
                o.color=v.color; o.uv=TRANSFORM_TEX(v.uv,_MainTex); return o;
            }
            half4 Frag(Varyings v):SV_Target
            {
                half3 color=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,v.uv).rgb*v.color.rgb*_Color.rgb;
                half gray=dot(color,half3(.2126,.7152,.0722));
                return half4(gray,gray,gray,1);
            }
            ENDHLSL
        }
    }
}
