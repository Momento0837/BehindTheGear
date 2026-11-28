Shader "BehindTheGear/Base Ground Mask"
{
    Properties { _MainTex("Sprite", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float _FloorMode, _FloorBrightness, _Opacity;
            struct Attributes { float3 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            Varyings Vert(Attributes v) { Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS); o.uv=v.uv; return o; }
            half4 Frag(Varyings v):SV_Target
            {
                half4 source = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, v.uv);
                float floorMask = _FloorMode > .5 ? 1 : 0;
                if (_FloorMode > 1.5)
                {
                    half3 display = LinearToSRGB(source.rgb);
                    floorMask = 1 - step(_FloorBrightness, max(display.r, max(display.g, display.b)));
                }
                // Walls/props overwrite earlier floor pixels in the same sprite sorting order.
                return half4(floorMask, floorMask, floorMask, source.a * _Opacity);
            }
            ENDHLSL
        }
    }
}
