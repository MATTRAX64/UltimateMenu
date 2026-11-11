Shader "Hidden/UltimateMenu/FrostedBackdrop"
{
    Properties { _MainTex ("UI texture", 2D) = "white" {} _Scene ("Processed scene", 2D) = "black" {} }
    SubShader
    {
        Tags { "Queue"="Overlay" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _Scene, _GUIClipTexture;
            float4x4 unity_GUIClipTextureMatrix;
            float4 _Panel, _Screen, _Tint;
            float _Opacity, _Clip, _LightTheme;
            struct V { float4 pos:POSITION; float2 uv:TEXCOORD0; };
            struct F { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float2 clipUV:TEXCOORD1; };
            F vert(V v) {
                F o; o.pos=UnityObjectToClipPos(v.pos); o.uv=v.uv;
                float3 eye=UnityObjectToViewPos(v.pos);
                o.clipUV=mul(unity_GUIClipTextureMatrix,float4(eye.xy,0,1)).xy;
                return o;
            }
            half4 frag(F i):SV_Target
            {
                float2 p=(i.uv-.5)*_Panel.xy;
                float r=min(_Panel.z,min(_Panel.x,_Panel.y)*.5);
                float2 q=abs(p)-_Panel.xy*.5+r;
                float d=length(max(q,0))+min(max(q.x,q.y),0)-r;
                float coverage=1-smoothstep(-1,0,d);
                float scale=max(_Panel.w,.5);
                float thickness=clamp(min(_Panel.x,_Panel.y)*.22,7*scale,29*scale);
                float bevel=1-smoothstep(0,thickness,-d);
                float2 corner=max(q,0)*sign(p);
                float2 edgeNormal=dot(corner,corner)>.001 ? normalize(corner) : (q.x>q.y ? float2(sign(p.x),0) : float2(0,sign(p.y)));
                #if UNITY_UV_STARTS_AT_TOP
                    edgeNormal.y=-edgeNormal.y;
                #endif
                float2 offset=edgeNormal*(thickness*.85*bevel*bevel)*_Screen.xy;
                float2 uv=i.pos.xy*_Screen.xy-offset;
                if (_Screen.z>.5) uv.y=1-uv.y;
                float2 b=13.0*scale*_Screen.xy;
                half3 base=tex2D(_Scene,saturate(uv)).rgb*.28;
                base+=.12*(tex2D(_Scene,saturate(uv+float2(b.x,0))).rgb+tex2D(_Scene,saturate(uv-float2(b.x,0))).rgb+
                           tex2D(_Scene,saturate(uv+float2(0,b.y))).rgb+tex2D(_Scene,saturate(uv-float2(0,b.y))).rgb);
                base+=.06*(tex2D(_Scene,saturate(uv+b)).rgb+tex2D(_Scene,saturate(uv-b)).rgb+
                           tex2D(_Scene,saturate(uv+float2(b.x,-b.y))).rgb+tex2D(_Scene,saturate(uv+float2(-b.x,b.y))).rgb);
                // A small chromatic split is visible only in the curved rim.
                half3 prism = half3(tex2D(_Scene,saturate(uv-offset*.16)).r, base.g, tex2D(_Scene,saturate(uv+offset*.16)).b);
                base=lerp(base,prism,bevel*.40);
                float luma=dot(base, float3(.2126,.7152,.0722));
                // Keep white labels readable over bright scenery; light themes get the inverse treatment.
                half3 scene=lerp(base*(.88-.27*luma),lerp(base,half3(1,1,1),.10),_LightTheme);
                float saturation=max(_Tint.r,max(_Tint.g,_Tint.b))-min(_Tint.r,min(_Tint.g,_Tint.b));
                float tint=lerp(lerp(.33,.53,saturate(_Tint.a)),lerp(.08,.22,saturate(_Tint.a)),_LightTheme)*lerp(1,1.16,saturate(saturation*1.5));
                half3 glass=lerp(scene,_Tint.rgb,saturate(tint));
                float rim=1-smoothstep(0,1.8*scale,-d);
                float inner=1-smoothstep(0,6*scale,-d);
                float spec=pow(saturate(abs(dot(edgeNormal,normalize(float2(-.45,.89))))),3);
                glass+=half3(.88,.95,1)*(rim*(.22+.32*spec)+inner*.11*spec)*lerp(.29,1,saturate(_LightTheme));
                float clipMask=_Clip>.5 ? tex2D(_GUIClipTexture,i.clipUV).a : 1;
                return half4(glass,coverage*_Opacity*clipMask*lerp(1,.83,_LightTheme));
            }
            ENDCG
        }
    }
}
