Shader "BehindTheGear/UI/Health Heartbeat Ripple"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Phase ("Heartbeat time", Float) = 0
        _Strength ("Low health intensity", Range(0,1)) = 0
        _RectSize ("Effect dimensions", Vector) = (300,90,0,0)
        _Bar ("Fill center and half size", Vector) = (0,0,110,9)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        // Add light without darkening the HUD or the surrounding screen.
        Blend SrcAlpha One
        ColorMask RGB
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            float _Phase, _Strength;
            float4 _RectSize, _Bar;
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }
            float Wave(float2 p, float age)
            {
                float t = saturate(age / .8);
                float radius = min(4.0, min(_Bar.z, _Bar.w));
                float2 q = abs(p) - max(_Bar.zw - radius, 0);
                float distance = length(max(q, 0)) + min(max(q.x, q.y), 0) - radius;
                float expansion = lerp(2.0, lerp(16.0, 28.0, _Strength), t);
                float crest = 1 - smoothstep(.5, 2.6, abs(distance - expansion));
                float glow = exp2(-abs(distance - expansion) * .42) * .28;
                float fade = smoothstep(0, .10, t) * pow(1 - t, 1.5);
                return (crest + glow) * fade * step(0, age);
            }
            half4 frag(v2f i) : SV_Target
            {
                float2 p = (i.uv - .5) * _RectSize.xy - _Bar.xy;
                float light = Wave(p, _Phase) + .55 * Wave(p, _Phase - .32);
                float opacity = saturate(light * lerp(.24, .85, _Strength)) * i.color.a;
                // Warm pale-red light, with a brighter crest as the player's HP falls.
                half3 tint = lerp(half3(1, .28, .20), half3(1, .58, .42), _Strength);
                return half4(tint * i.color.rgb, opacity);
            }
            ENDCG
        }
    }
}
