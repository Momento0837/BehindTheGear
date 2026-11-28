Shader "BehindTheGear/Base Ground Atmosphere"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        float4 _BlackSpread;
        float Dark(float2 uv)
        {
            half3 display=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,saturate(uv)).rgb;
            #if !defined(UNITY_COLORSPACE_GAMMA)
            display=LinearToSRGB(display);
            #endif
            return 1-smoothstep(_BlackSpread.w*.35,_BlackSpread.w,max(display.r,max(display.g,display.b)));
        }
        ENDHLSL
        Pass
        {
            Cull Off ZWrite Off ZTest Always Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            TEXTURE2D_X(_BaseFloorMask);
            TEXTURE2D_X(_BaseBlurredBlack);
            float4 _GroundFog;
            float3 _GroundOrigin, _GroundAxisX, _GroundAxisY;
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv=input.texcoord;
                half4 original=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv);
                float mask=SAMPLE_TEXTURE2D_X(_BaseFloorMask,sampler_PointClamp,uv).r;
                if(mask < .001) return original;
                float dark=Dark(uv);
                float soft=SAMPLE_TEXTURE2D_X(_BaseBlurredBlack,sampler_LinearClamp,uv).r;
                // Only extracted black spreads. Bright artwork retains its original detail.
                float spread=saturate(soft-dark)*_BlackSpread.z;
                float2 screenUV=uv;
                float2 world=(_GroundOrigin+_GroundAxisX*screenUV.x+_GroundAxisY*screenUV.y).xy;
                // Inverse isometric coordinates keep the cloud field on the ground as the camera pans/zooms.
                float2 ground=float2(world.x+2*world.y,-world.x+2*world.y)/max(.1,_GroundFog.y);
                ground+=float2(_GroundFog.z,0);
                float cloud=Noise(ground)*.65+Noise(ground*1.91+17.2)*.25+Noise(ground*3.83+43.7)*.10;
                float fog=smoothstep(.18,.82,cloud)*_GroundFog.x;
                float opacity=(1-(1-spread)*(1-fog))*saturate(mask);
                return half4(original.rgb*(1-opacity),original.a);
            }
            ENDHLSL
        }
        Pass
        {
            Name "Extract black"
            Cull Off ZWrite Off ZTest Always Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment ExtractBlack
            half4 ExtractBlack(Varyings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return Dark(input.texcoord).xxxx;
            }
            ENDHLSL
        }
        Pass
        {
            Name "Gaussian black blur"
            Cull Off ZWrite Off ZTest Always Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment BlurBlack
            float2 _BlurDirection;
            half4 BlurBlack(Varyings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // Dense, separable 33x33 Gaussian footprint; no widely spaced silhouette copies.
                // Blur only the extracted mask, leaving the actual pixel artwork untouched.
                float sum=0, weights=0;
                [unroll] for(int tap=-16;tap<=16;tap++)
                {
                    float weight=exp(-.5*(tap/6.0)*(tap/6.0));
                    sum+=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,
                        input.texcoord+_BlurDirection*(tap/16.0)).r*weight;
                    weights+=weight;
                }
                return (sum/weights).xxxx;
            }
            ENDHLSL
        }
    }
}
