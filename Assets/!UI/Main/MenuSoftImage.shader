Shader "BehindTheGear/UI/MenuSoftImage"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _UVRect ("Sprite UV bounds", Vector) = (0,0,1,1)
        _UVStep ("UV per canvas unit", Vector) = (0.001,0.001,0,0)
        _BlurRadius ("Background blur", Float) = 0
        _GlowRadius ("Title glow spread", Float) = 0
        _GlowOpacity ("Title glow opacity", Range(0,1)) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 local:TEXCOORD1; };
            Texture2D _MainTex;
            SamplerState sampler_MainTex;
            // Only the halo uses linear filtering; artwork keeps its imported filter.
            SamplerState menu_linear_clamp_sampler;
            float4 _MainTex_TexelSize, _UVRect, _UVStep, _ClipRect;
            float _BlurRadius, _GlowRadius, _GlowOpacity;
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.local = v.vertex; o.uv = v.uv; o.color = v.color;
                return o;
            }
            float4 Sample(float2 uv)
            {
                float inside = step(_UVRect.x, uv.x) * step(_UVRect.y, uv.y) * step(uv.x, _UVRect.z) * step(uv.y, _UVRect.w);
                float2 inset = _MainTex_TexelSize.xy * .5;
                float4 c = _MainTex.Sample(sampler_MainTex, clamp(uv, _UVRect.xy + inset, _UVRect.zw - inset));
                c.a *= inside;
                c.rgb *= c.a;
                return c;
            }
            float SampleGlowAlpha(float2 uv)
            {
                float inside = step(_UVRect.x, uv.x) * step(_UVRect.y, uv.y) * step(uv.x, _UVRect.z) * step(uv.y, _UVRect.w);
                float2 inset = _MainTex_TexelSize.xy * .5;
                return _MainTex.Sample(menu_linear_clamp_sampler,
                    clamp(uv, _UVRect.xy + inset, _UVRect.zw - inset)).a * inside;
            }
            float4 frag(v2f i):SV_Target
            {
                float4 source = Sample(i.uv);
                if (_BlurRadius > 0)
                {
                    float2 d = _UVStep.xy * _BlurRadius;
                    // Gaussian-weighted 3x3 kernel; alpha is premultiplied to avoid dark sprite fringes.
                    source *= 4;
                    source += 2 * (Sample(i.uv + float2(d.x,0)) + Sample(i.uv - float2(d.x,0)) + Sample(i.uv + float2(0,d.y)) + Sample(i.uv - float2(0,d.y)));
                    source += Sample(i.uv+d) + Sample(i.uv-d) + Sample(i.uv+float2(d.x,-d.y)) + Sample(i.uv+float2(-d.x,d.y));
                    source /= 16;
                }
                float glow = 0;
                if (_GlowOpacity > 0)
                {
                    // Dense, staggered Gaussian rings avoid visible copies of thin lettering.
                    float totalWeight = 0;
                    [loop] for (int ring = 1; ring <= 8; ring++)
                    {
                        float radius = (ring - .5) / 8.0;
                        // Include annulus area when integrating the Gaussian disk.
                        float weight = radius * exp(-4.5 * radius * radius);
                        [loop] for (int tap = 0; tap < 24; tap++)
                        {
                            float angle = (tap + ring * .381966) * 0.261799388;
                            float2 delta = float2(cos(angle), sin(angle)) * (_GlowRadius * radius) * _UVStep.xy;
                            glow += SampleGlowAlpha(i.uv + delta) * weight;
                        }
                        totalWeight += 24 * weight;
                    }
                    glow *= _GlowOpacity / totalWeight;
                }
                float alpha = source.a + glow * (1-source.a);
                float3 rgb = (source.rgb * i.color.rgb + glow * (1-source.a)) / max(alpha, .0001);
                alpha *= i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                alpha *= UnityGet2DClipping(i.local.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - .001);
                #endif
                return float4(rgb, alpha);
            }
            ENDCG
        }
    }
}
