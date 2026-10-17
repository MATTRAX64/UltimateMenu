using System;
using System.Collections.Generic;
using UnityEngine;

namespace UltimateMenu
{
    public sealed class GorillaTextOverlays : MonoBehaviour
    {
        const string Title="Texts.Title",Pref="MUM_TextOverlays";
        [Serializable] public sealed class Box
        {
            public string text="Texte";
            public bool visible=true,bold=true,background=true,outline=true,shadow=true;
            public float x=.25f,y=.25f,size=1f,textSize=1f,rounding=12f;
            public Color color=Color.white,bg=new Color(0,0,0,.7f),border=Color.white;
            [NonSerialized] internal GUIStyle style;
            [NonSerialized] internal OverlayFrame frame;
            [NonSerialized] internal Vector2 position,target;
            [NonSerialized] internal bool positioned;
        }
        [Serializable] public sealed class Data{public List<Box> boxes=new List<Box>();}
        Data _data=new Data();
        int _selected=-1;
        bool _registered,_appearance;
        float _saveAt=-1;
        Box _drag;
        Vector2 _offset;
        bool Enabled=>MenuUI.Instance!=null&&MenuUI.Instance.IsModEnabled(Title);
        void Awake()
        {
            try{_data=JsonUtility.FromJson<Data>(MenuPrefs.GetString(Pref,"{}"))??new Data();}
            catch(Exception e){Log.Err("Texts.Load",e);}
            if(_data.boxes==null)_data.boxes=new List<Box>();
            _data.boxes.RemoveAll(b=>b==null);
            foreach(var b in _data.boxes)
            {
                b.text=b.text??"";b.size=FiniteClamp(b.size,.5f,1.5f,1);b.textSize=FiniteClamp(b.textSize,.5f,1.25f,1);
                b.x=FiniteClamp(b.x,0,1,.25f);b.y=FiniteClamp(b.y,0,1,.25f);b.rounding=FiniteClamp(b.rounding,0,40,12);
            }
            if(_data.boxes.Count>0)_selected=0;
        }
        static float FiniteClamp(float f,float min,float max,float fallback)=>float.IsNaN(f)||float.IsInfinity(f)?fallback:Mathf.Clamp(f,min,max);
        void Dirty(){_saveAt=Time.unscaledTime+.4f;}
        void Save(){MenuPrefs.SetString(Pref,JsonUtility.ToJson(_data));MenuPrefs.Save();_saveAt=-1;}
        void Update()
        {
            if(!_registered&&MenuUI.Instance!=null){MenuUI.Instance.RegisterPage(Title,DrawPage,MenuCategory.Cosmetique);_registered=true;}
            if(!Enabled)_drag=null;
            foreach(var b in _data.boxes)if(b.positioned)
                b.position=Vector2.Lerp(b.position,b.target,1f-Mathf.Exp(-Time.unscaledDeltaTime/.045f));
            if(_saveAt>=0&&Time.unscaledTime>=_saveAt)Save();
        }
        static Vector2 Dimensions(Box b)=>new Vector2(Mathf.Min(Screen.width,400*b.size),Mathf.Min(Screen.height,100*b.size));
        Rect Bounds(Box b)
        {
            Vector2 size=Dimensions(b);
            b.target=new Vector2(b.x*Mathf.Max(0,Screen.width-size.x),b.y*Mathf.Max(0,Screen.height-size.y));
            if(!b.positioned){b.position=b.target;b.positioned=true;}
            b.position=new Vector2(Mathf.Clamp(b.position.x,0,Mathf.Max(0,Screen.width-size.x)),Mathf.Clamp(b.position.y,0,Mathf.Max(0,Screen.height-size.y)));
            return new Rect(b.position,size);
        }
        void OnGUI()
        {
            if(!Enabled||Screen.width<=0||Screen.height<=0)return;
            var ev=Event.current;
            bool blocked=MenuUI.Instance!=null&&MenuUI.Instance.ContainsScreenMenu(ev.mousePosition);
            if(ev.type==EventType.MouseDown&&ev.button==0&&!blocked)
                for(int i=_data.boxes.Count-1;i>=0;i--)
                {
                    var b=_data.boxes[i];if(!b.visible)continue;
                    Rect rect=Bounds(b);if(!rect.Contains(ev.mousePosition))continue;
                    _drag=b;_selected=i;_offset=ev.mousePosition-rect.position;ev.Use();break;
                }
            if(_drag!=null&&ev.type==EventType.MouseDrag)
            {
                Vector2 size=Dimensions(_drag);
                _drag.target=new Vector2(Mathf.Clamp(ev.mousePosition.x-_offset.x,0,Mathf.Max(0,Screen.width-size.x)),Mathf.Clamp(ev.mousePosition.y-_offset.y,0,Mathf.Max(0,Screen.height-size.y)));
                _drag.x=_drag.target.x/Mathf.Max(1,Screen.width-size.x);_drag.y=_drag.target.y/Mathf.Max(1,Screen.height-size.y);
                Dirty();ev.Use();
            }
            if(_drag!=null&&ev.type==EventType.MouseUp){_drag=null;Dirty();ev.Use();}
            if(ev.type!=EventType.Repaint)return;
            foreach(var b in _data.boxes)
            {
                if(!b.visible)continue;Rect rect=Bounds(b);
                if(b.style==null)b.style=new GUIStyle(GUI.skin.label){alignment=TextAnchor.MiddleCenter,wordWrap=true,richText=false,padding=new RectOffset(8,8,4,4)};
                b.style.font=UI.FontFor(b.bold);b.style.fontSize=Mathf.Max(8,Mathf.RoundToInt(25*b.size*b.textSize));
                if(b.shadow)UI.DrawRounded(new Rect(rect.x+3,rect.y+3,rect.width,rect.height),new Color(0,0,0,.35f),Mathf.RoundToInt(b.rounding*b.size));
                Rect inner=new Rect(rect.x+2,rect.y+2,Mathf.Max(1,rect.width-4),Mathf.Max(1,rect.height-4));
                if(b.background)UI.DrawRounded(rect,b.bg,Mathf.RoundToInt(b.rounding*b.size));
                if(b.outline){if(b.frame==null)b.frame=new OverlayFrame();b.frame.Draw(rect,b.rounding*b.size,2*b.size,b.border);}
                b.style.normal.textColor=b.color;GUI.Label(inner,b.text,b.style);
            }
        }
        static int Stamp(Box b)
        {
            unchecked{return b.text.GetHashCode()^b.visible.GetHashCode()^(b.bold.GetHashCode()<<1)^(b.background.GetHashCode()<<2)^(b.outline.GetHashCode()<<3)^(b.shadow.GetHashCode()<<4)^b.size.GetHashCode()^b.textSize.GetHashCode()^b.rounding.GetHashCode()^b.color.GetHashCode()^b.bg.GetHashCode()^b.border.GetHashCode()^b.x.GetHashCode()^b.y.GetHashCode();}
        }
        void DrawPage(MenuUI ui)
        {
            if(ui.Btn(I18n.T("Texts.Add"),ui.TA,Theme.Text))
            {var b=new Box{text=I18n.T("Texts.Text")+" "+(_data.boxes.Count+1)};_data.boxes.Add(b);_selected=_data.boxes.Count-1;Dirty();}
            ui.Label(I18n.T("Texts.DragHint"),Theme.Dim,9);
            for(int i=0;i<_data.boxes.Count;i++)
            {
                string name=_data.boxes[i].text.Replace('\n',' ');if(name.Length>36)name=name.Substring(0,36)+"…";
                if(ui.ChoiceRow(name.Length>0?name:I18n.T("Texts.Text"),_selected==i,true,out bool remove))_selected=i;
                if(remove)
                {
                    Box deleted=_data.boxes[i];deleted.frame?.Dispose();if(_drag==deleted)_drag=null;
                    _data.boxes.RemoveAt(i);
                    if(_selected>i)_selected--;else if(_selected==i)_selected=Mathf.Min(i,_data.boxes.Count-1);
                    Dirty();break;
                }
            }
            if(_selected<0||_selected>=_data.boxes.Count)return;
            Box box=_data.boxes[_selected];int before=Stamp(box);
            ui.BeginCard();
            box.text=ui.TextField(box.text);
            box.visible=ui.Toggle(I18n.T("Texts.Visible"),box.visible);
            box.size=ui.Slider(I18n.T("Stream.Size"),box.size,.5f,1.5f,"0.00x");
            box.textSize=ui.Slider(I18n.T("Texts.FontSize"),box.textSize,.5f,1.25f,"0.00x");
            GUILayout.BeginHorizontal();
            if(ui.Btn(I18n.T("Stream.CenterHorizontal"),ui.TB3,Theme.Text))box.x=.5f;
            GUILayout.Space(6*UI.Scale);
            if(ui.Btn(I18n.T("Stream.CenterVertical"),ui.TB3,Theme.Text))box.y=.5f;
            GUILayout.EndHorizontal();
            ui.EndCard();
            ui.ExpandableSection(I18n.T("Texts.Appearance"),"",ref _appearance,()=>
            {
                box.bold=ui.Toggle(I18n.T("Core.bold_text_label"),box.bold);
                ui.ColorPickerHSV(ref box.color,I18n.T("Stream.TextColor"),compact:true);
                box.background=ui.Toggle(I18n.T("Stream.BgToggle"),box.background);
                if(box.background){ui.ColorPickerHSV(ref box.bg,I18n.T("Stream.BgColor"),compact:true);box.bg.a=ui.Slider(I18n.T("Stream.Opacity"),box.bg.a,0,1,"0.00");}
                box.outline=ui.Toggle(I18n.T("Stream.Frame"),box.outline);
                if(box.outline)ui.ColorPickerHSV(ref box.border,I18n.T("Stream.FrameColor"),compact:true);
                box.shadow=ui.Toggle(I18n.T("Stream.FrameShadow"),box.shadow);
                box.rounding=ui.Slider(I18n.T("Stream.Rounding"),box.rounding,0,40,"0");
            });
            if(before!=Stamp(box))Dirty();
        }
        void OnDisable(){_drag=null;if(_saveAt>=0)Save();}
        void OnApplicationQuit(){if(_saveAt>=0)Save();}
        void OnDestroy(){if(_saveAt>=0)Save();foreach(var b in _data.boxes)b.frame?.Dispose();}
    }
}
