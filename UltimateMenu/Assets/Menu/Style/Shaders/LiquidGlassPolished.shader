Shader "Hidden/UltimateMenu/LiquidGlassPolished"
{
    Properties { _MainTex ("UI texture", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _GUIClipTexture;
            float4x4 unity_GUIClipTextureMatrix;
            float4 _Size, _Style, _Fill;
            struct V { float4 pos:POSITION; float2 uv:TEXCOORD0; };
            struct F { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float2 clipUV:TEXCOORD1; };
            F vert(V v)
            {
                F o; o.pos=UnityObjectToClipPos(v.pos); o.uv=v.uv;
                float3 eye=UnityObjectToViewPos(v.pos);
                o.clipUV=mul(unity_GUIClipTextureMatrix,float4(eye.xy,0,1)).xy;
                return o;
            }
            half4 frag(F i):SV_Target
            {
                float2 size=max(_Size.xy,1);
                float2 p=(i.uv-.5)*(size+2*_Size.w);
                float r=clamp(_Size.z,0,min(size.x,size.y)*.5);
                float2 q=abs(p)-size*.5+r;
                float d=length(max(q,0))+min(max(q.x,q.y),0)-r;
                float aa=max(fwidth(d),.65);
                float cover=1-smoothstep(-aa,aa,d);
                float scale=max(.5,_Style.y);
                float2 corner=max(q,0)*sign(p);
                float2 n=dot(corner,corner)>.001 ? normalize(corner) : (q.x>q.y ? float2(sign(p.x),0) : float2(0,sign(p.y)));
                float depth=max(0,-d);
                float spec=pow(saturate(dot(n,float2(-.42,.9075))),2.4);
                float bounce=pow(saturate(dot(n,float2(.78,-.6258))),3.5);
                float rim=1-smoothstep(.4*scale,2.2*scale,depth);
                float bevel=1-smoothstep(1.3*scale,clamp(min(size.x,size.y)*.21,5*scale,19*scale),depth);
                float2 uv=saturate(p/size+.5);
                float soft=pow(saturate(.55*uv.y+.45*(1-uv.x)),3.0);
                float chroma=saturate(.5+.5*n.x);
                half3 iridescence=lerp(half3(.39,.74,1),half3(1,.74,.53),chroma);
                half3 baseColor=lerp(_Fill.rgb,_Fill.rgb*.78+half3(.065,.085,.12),.45);
                float lightTheme=saturate(_Style.w);
                float panel=step(1.5,_Style.x);
                float accent=saturate((max(_Fill.r,max(_Fill.g,_Fill.b))-min(_Fill.r,min(_Fill.g,_Fill.b)))*2.2);
                // A soft overhead reflection runs across the face, independent of the outline.
                float gloss=pow(saturate(uv.y),5.0)*lerp(.38,1.0,smoothstep(.15,.9,1-uv.x));
                baseColor+=half3(.24,.31,.41)*soft*lerp(.09,.23,lightTheme);
                baseColor+=half3(.55,.67,.85)*gloss*lerp(.30,.13,lightTheme)*(1-panel*.62);
                half3 edgeLight=half3(.88,.96,1)*rim*(.12+.40*spec+.12*bounce)
                    +iridescence*bevel*(.11+.23*spec+.15*bounce);
                edgeLight *= lerp(.31,1,lightTheme);
                // Keep the center transparent; the thicker edge carries the reflected light.
                float surfaceCap=lerp(.47,.43,lightTheme)+accent*.27;
                float alpha=lerp(min(_Fill.a,surfaceCap),_Fill.a,panel)*cover;
                float aura=(1-cover)*(1-smoothstep(0,2.6*scale,max(d,0)))*(.065+.10*spec)*_Fill.a*lerp(.23,1,lightTheme);
                half3 color=(alpha*(baseColor+edgeLight)+aura*iridescence)/max(.0001,alpha+aura);
                float mask=_Style.z>.5 ? tex2D(_GUIClipTexture,i.clipUV).a : 1;
                return half4(saturate(color),saturate(alpha+aura)*mask);
            }
            ENDCG
        }
    }
    Fallback Off
}
