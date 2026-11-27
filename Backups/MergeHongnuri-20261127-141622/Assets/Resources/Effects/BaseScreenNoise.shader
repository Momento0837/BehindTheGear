Shader "BehindTheGear/Base Screen Noise"
{
    Properties { _MainTex("Texture",2D)="white" {} _Opacity("Opacity",Range(0,1))=.03 }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            float _Opacity, _NoiseFrame;
            struct Attributes { float4 position:POSITION; half4 color:COLOR; };
            struct Varyings { float4 position:SV_POSITION; half4 color:COLOR; };
            Varyings Vert(Attributes v) { Varyings o; o.position=UnityObjectToClipPos(v.position); o.color=v.color; return o; }
            half4 Frag(Varyings v):SV_Target
            {
                float2 p=floor(v.position.xy)+_NoiseFrame*float2(71.3,19.7);
                float gray=frac(sin(dot(p,float2(12.9898,78.233)))*43758.5453);
                // Dark gray grain keeps 3% alpha subtle even over the black background in linear lighting.
                gray *= .3;
                #ifndef UNITY_COLORSPACE_GAMMA
                gray=GammaToLinearSpace(gray);
                #endif
                return half4(gray,gray,gray,_Opacity*v.color.a);
            }
            ENDHLSL
        }
    }
}
