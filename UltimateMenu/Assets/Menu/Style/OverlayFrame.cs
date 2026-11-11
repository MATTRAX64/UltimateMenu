using UnityEngine;

namespace UltimateMenu
{
    // A cached transparent nine-slice outline: resizing and recoloring need no new texture.
    internal sealed class OverlayFrame
    {
        Texture2D _texture;
        GUIStyle _style;
        int _radius=-1, _thickness=-1;
        internal void Draw(Rect rect,float rounding,float width,Color color)
        {
            int radius=Mathf.Max(0,Mathf.RoundToInt(rounding));
            int thickness=Mathf.Max(1,Mathf.RoundToInt(width));
            if(_texture==null || radius!=_radius || thickness!=_thickness)
            {
                Dispose(); _radius=radius;_thickness=thickness;
                int inset=radius+thickness+3;
                int size=inset*2+4;
                _texture=new Texture2D(size,size,TextureFormat.RGBA32,false)
                {hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
                Color32[] pixels=new Color32[size*size];
                float half=size*.5f-1f;
                for(int y=0;y<size;y++) for(int x=0;x<size;x++)
                {
                    float qx=Mathf.Abs(x+.5f-size*.5f)-(half-radius);
                    float qy=Mathf.Abs(y+.5f-size*.5f)-(half-radius);
                    float ax=Mathf.Max(0,qx),ay=Mathf.Max(0,qy);
                    float distance=Mathf.Sqrt(ax*ax+ay*ay)+Mathf.Min(Mathf.Max(qx,qy),0)-radius;
                    float coverage=Mathf.Clamp01(.5f-distance)*Mathf.Clamp01(distance+thickness+.5f);
                    pixels[y*size+x]=new Color32(255,255,255,(byte)Mathf.RoundToInt(coverage*255f));
                }
                _texture.SetPixels32(pixels);_texture.Apply(false,true);
                _style=new GUIStyle(GUIStyle.none){normal={background=_texture},border=new RectOffset(inset,inset,inset,inset)};
            }
            Color saved=GUI.color;
            try {GUI.color=saved*color;GUI.Box(rect,GUIContent.none,_style);}
            finally {GUI.color=saved;}
        }
        internal void Dispose()
        {if(_texture!=null) Object.Destroy(_texture);_texture=null;_style=null;}
    }
}
