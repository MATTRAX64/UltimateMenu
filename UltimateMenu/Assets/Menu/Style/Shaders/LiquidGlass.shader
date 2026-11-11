Shader "Hidden/UltimateMenu/LiquidGlassUI"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Fill ("Fill", Color) = (0.15,0.18,0.25,0.9)
        _Size ("Width height radius padding", Vector) = (320,80,40,12)
        _Style ("Glass pixel scale clip", Vector) = (1,1,1,0)
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _GUIClipTexture;
            float4x4 unity_GUIClipTextureMatrix;
            float4 _Fill, _Size, _Style;
            struct Input { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct Output { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float2 clipUV:TEXCOORD1; };
            Output vert(Input v)
            {
                Output o;
                o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv;
                float3 eye=UnityObjectToViewPos(v.vertex);
                o.clipUV=mul(unity_GUIClipTextureMatrix,float4(eye.xy,0,1)).xy;
                return o;
            }
            float roundedDistance(float2 position,float2 halfSize,float radius)
            {
                float2 q=abs(position)-halfSize+radius;
                return length(max(q,0))+min(max(q.x,q.y),0)-radius;
            }
            float4 frag(Output i):SV_Target
            {
                float2 size=max(_Size.xy,1);
                float2 p=(i.uv-0.5)*(size+2*_Size.w);
                float radius=clamp(_Size.z,0,min(size.x,size.y)*0.5);
                float d=roundedDistance(p,size*0.5,radius);
                float aa=max(fwidth(d),0.65);
                float coverage=1-smoothstep(-aa,aa,d);
                float top=saturate(0.5+p.y/size.y);
                float inner=(1-smoothstep(0,1.5*_Style.y,-d))*coverage;
                float outer=(1-smoothstep(0,1.2*_Style.y,d))*(1-coverage);
                float shadowD=roundedDistance(p+float2(0,3*_Style.y),size*0.5,radius);
                float shadow=exp(-pow(max(shadowD,0)/max(3.5*_Style.y,1),2))*0.18*(1-coverage)*_Style.x;
                float3 color=_Fill.rgb;
                color+=float3(0.76,0.84,1)*pow(top,5)*0.055*_Style.x;
                color=lerp(color,float3(0.88,0.94,1),inner*(0.12+0.30*top)*_Style.x);
                float alpha=coverage*_Fill.a;
                float edge=outer*0.30*_Style.x*_Fill.a;
                float combined=alpha+edge+shadow;
                color=(color*alpha+float3(0.84,0.91,1)*edge)/max(combined,0.0001);
                float clipMask=_Style.z>0.5 ? tex2D(_GUIClipTexture,i.clipUV).a : 1;
                return float4(color,saturate(combined)*clipMask);
            }
            ENDCG
        }
    }
    Fallback Off
}
