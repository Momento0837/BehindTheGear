Shader "BehindTheGear/Portal Radiance"
{
    Properties
    {
        _Opacity ("Haze Opacity", Range(0,1)) = 0.2
        _Direction ("Light Direction", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            Blend SrcAlpha One
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
                float _Opacity;
                float _Direction;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float x = _Direction > 0 ? input.uv.x : 1 - input.uv.x;
                float y = abs(input.uv.y - .5) * 2;
                float slitEnds = 1 - smoothstep(.78, .88, y);
                float slit = (1 - smoothstep(.0015, .005, abs(x))) * slitEnds;
                float nearGlow = exp(-abs(x) * 42) * (1 - smoothstep(.75, .98, y));
                float spread = exp(-x * 5.5);
                float hazeEnds = 1 - smoothstep(.35, 1, y);
                float beams = .9 + .1 * sin(input.uv.y * 38 + x * 6 + sin(_Time.y * .22));
                float farFade = 1 - smoothstep(.65, 1, x);
                float light = (slit * 3.5 + nearGlow * .75 + spread * hazeEnds * beams) * farFade;
                return half4(light, light, light, _Opacity);
            }
            ENDHLSL
        }
    }
}
