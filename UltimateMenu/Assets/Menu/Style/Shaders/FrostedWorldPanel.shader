Shader "Hidden/UltimateMenu/FrostedWorldPanel"
{
    Properties { [PerRendererData] _MainTex("Rounded mask",2D)="white" {} _SceneReady("Scene ready",Float)=0 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Back ZWrite Off ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ UM_STEREO
            #if defined(UM_STEREO) && !defined(STEREO_INSTANCING_ON)
                #define STEREO_INSTANCING_ON 1
            #endif
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float _SceneReady;
            struct A { float3 pos:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            V Vert(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos=TransformObjectToHClip(v.pos);o.uv=v.uv;o.color=v.color;return o; }
            half4 Frag(V i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                if(_SceneReady<.5) return 0;
                float2 uv=GetNormalizedScreenSpaceUV(i.pos);
                float2 b=8.0/_ScaledScreenParams.xy;
                half3 scene=SampleSceneColor(uv)*.24;
                scene+=.12*(SampleSceneColor(saturate(uv+float2(b.x,0)))+SampleSceneColor(saturate(uv-float2(b.x,0)))+
                            SampleSceneColor(saturate(uv+float2(0,b.y)))+SampleSceneColor(saturate(uv-float2(0,b.y))));
                scene+=.07*(SampleSceneColor(saturate(uv+b))+SampleSceneColor(saturate(uv-b))+
                            SampleSceneColor(saturate(uv+float2(b.x,-b.y)))+SampleSceneColor(saturate(uv+float2(-b.x,b.y))));
                half mask=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).a;
                return half4(lerp(scene,i.color.rgb,.16),mask*i.color.a*.90);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
