using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace UltimateMenu
{
    public class GorillaStreams : MonoBehaviour
    {
        public static GorillaStreams Instance { get; private set; }
        public bool Enabled => MenuUI.Instance != null && MenuUI.Instance.IsModEnabled("Stream.Title");
        public bool Hidden;

        string rCode = "----", lRCode = "";
        int pCount, lPCount = -1;

        bool _drag;
        Vector2 _dOff;
        float pX, pY, oW = 400f, oH = 100f, _t;
        float _size = 1f, _textSize = 1f;
        int fSize = 25;
        Color txtCol = Color.white;
        const float INT = 0.5f;

        bool _bgOn = true;
        Color _bgCol = new Color(0f, 1f, 0f, 1f);
        float _bgOp = 1f;

        bool _shOn = true;
        Color _shCol = new Color(0f, 0f, 0f, 0.8f);
        bool _centerX, _centerY, _outlineOn, _bold = true;
        bool _textOpen, _backgroundOpen, _effectsOpen;
        float _outlineSize = 1.5f, _shadowOffset = 2f, _rounding = 12f;
        Color _outlineCol = Color.black;
        bool _frameOn, _frameShadowOn = true, _moving;
        float _frameWidth = 2f, _frameShadowOffset = 4f;
        Color _frameColor = Color.white, _frameShadowColor = new Color(0,0,0,.4f);
        Vector2 _targetPosition;
        int _savedAppearanceHash;
        bool _hasSavedAppearance;
        readonly OverlayFrame _frame = new OverlayFrame();

        string _expFolder, _expPath, _lastExp = "";
        const string EFN = "GorillaStreams", EFILE = "overlay.txt";

        bool _wsE;


        GUIStyle _sty, _shSty;
        int _lStyFS = -1;
        Color _lStyTxt;

        void Awake()
        {
            Instance = this;
            pX = MenuPrefs.GetFloat("MUM_StreamX", 10f); pY = MenuPrefs.GetFloat("MUM_StreamY", 10f);
            _size = Mathf.Clamp(MenuPrefs.GetFloat("MUM_StreamSize",1f),.5f,1.5f);
            _textSize = Mathf.Clamp(MenuPrefs.GetFloat("MUM_StreamTextSize",1f),.5f,1.25f);
            ApplySize();
            ColorUtility.TryParseHtmlString(MenuPrefs.GetString("MUM_StreamTxtCol", "#FFFFFFFF"), out txtCol);
            _bgOn = MenuPrefs.GetInt("MUM_StreamBgOn", 1) == 1;
            ColorUtility.TryParseHtmlString(MenuPrefs.GetString("MUM_StreamBgCol", "#00FF00FF"), out _bgCol);
            _bgOp = MenuPrefs.GetFloat("MUM_StreamBgOpacity", 1f);
            _shOn = MenuPrefs.GetInt("MUM_StreamShadowOn", 1) == 1;
            ColorUtility.TryParseHtmlString(MenuPrefs.GetString("MUM_StreamShadowCol", "#000000CC"), out _shCol);
            Hidden = MenuPrefs.GetInt("MUM_StreamHidden", 0) == 1;
            _centerX = MenuPrefs.GetInt("MUM_StreamCenterX", 0) == 1;
            _centerY = MenuPrefs.GetInt("MUM_StreamCenterY", 0) == 1;
            _outlineOn = MenuPrefs.GetInt("MUM_StreamOutlineOn", 0) == 1;
            _bold = MenuPrefs.GetInt("MUM_StreamBold", 1) == 1;
            _outlineSize = MenuPrefs.GetFloat("MUM_StreamOutlineSize", 1.5f);
            _shadowOffset = MenuPrefs.GetFloat("MUM_StreamShadowOffset", 2f);
            _rounding = MenuPrefs.GetFloat("MUM_StreamRounding", 12f);
            ColorUtility.TryParseHtmlString(MenuPrefs.GetString("MUM_StreamOutlineColor", "#000000FF"), out _outlineCol);
            _frameOn=MenuPrefs.GetInt("MUM_StreamFrameOn",0)==1;
            _frameShadowOn=MenuPrefs.GetInt("MUM_StreamFrameShadowOn",1)==1;
            _frameWidth=MenuPrefs.GetFloat("MUM_StreamFrameWidth",2f);
            _frameShadowOffset=MenuPrefs.GetFloat("MUM_StreamFrameShadowOffset",4f);
            ColorUtility.TryParseHtmlString(MenuPrefs.GetString("MUM_StreamFrameColor","#FFFFFFFF"),out _frameColor);
            ColorUtility.TryParseHtmlString(MenuPrefs.GetString("MUM_StreamFrameShadowColor","#00000066"),out _frameShadowColor);
            _centerX=_centerY=false;
            _targetPosition=new Vector2(pX,pY);
            SetupExportPath();
        }

        void Start() { RefreshRoom(); MenuUI.Instance?.RegisterPage("Stream.Title", DrawPage, MenuCategory.Stream); }

        void SetupExportPath()
        {
            try
            {
                _expFolder = Path.Combine(ReleaseUpdater.DataFolder, EFN);
                if (!Directory.Exists(_expFolder)) Directory.CreateDirectory(_expFolder);
                _expPath = Path.Combine(_expFolder, EFILE);
            }
            catch (System.Exception e) { Log.Err("GorillaStreams.SetupExportPath", e); }
        }

        void ExportForObs()
        {
            if (string.IsNullOrEmpty(_expPath)) return;
            string c = Enabled && !Hidden ? I18n.T("Stream.Room") + " " + rCode + "\n" + I18n.T("Stream.Players") + " " + pCount : "";
            if (c == _lastExp) return;
            _lastExp = c;
            try { File.WriteAllText(_expPath, c); } catch (System.Exception e) { Log.Err("GorillaStreams.ExportForObs", e); }
        }

        void Update()
        {
            if(_moving)
            {
                Vector2 position=Vector2.Lerp(new Vector2(pX,pY),_targetPosition,1f-Mathf.Exp(-Time.unscaledDeltaTime/.045f));
                pX=position.x;pY=position.y;
                if(!_drag && Vector2.Distance(position,_targetPosition)<.2f)
                {pX=_targetPosition.x;pY=_targetPosition.y;_moving=false;SaveAppearance();}
            }
            bool nowEn = Enabled;
            if (nowEn != _wsE) { _wsE = nowEn; if (nowEn) { RefreshRoom(); ExportForObs(); } else { _lastExp = ""; ExportForObs(); } }
            if (!nowEn || (_t += Time.deltaTime) < INT) return;
            _t = 0f;
            RefreshRoom();
            if (rCode != lRCode || pCount != lPCount) { lRCode = rCode; lPCount = pCount; ExportForObs(); }
        }

        void RefreshRoom()
        {
            // Photon already maintains the room and its members: no scene-wide scans or reflection.
            var room=Photon.Pun.PhotonNetwork.CurrentRoom;
            rCode=room!=null?room.Name:I18n.T("Rooms.Menu");
            pCount=room!=null?Mathf.Max(1,room.PlayerCount):1;
        }

        void ClampPosition()
        {
            if (_centerX) pX = (Screen.width-oW)*.5f;
            if (_centerY) pY = (Screen.height-oH)*.5f;
            pX = Mathf.Clamp(pX, 0, Mathf.Max(0,Screen.width-oW));
            pY = Mathf.Clamp(pY, 0, Mathf.Max(0,Screen.height-oH));
            if(_moving) _targetPosition=new Vector2(Mathf.Clamp(_targetPosition.x,0,Mathf.Max(0,Screen.width-oW)),Mathf.Clamp(_targetPosition.y,0,Mathf.Max(0,Screen.height-oH)));
        }
        void ApplySize()
        {
            oW=400f*_size;oH=100f*_size;
            fSize=Mathf.Max(1,Mathf.RoundToInt(25f*_size*_textSize));
        }
        void SaveAppearance()
        {
            int hash=17;
            hash=hash*31+oW.GetHashCode();hash=hash*31+oH.GetHashCode();hash=hash*31+fSize;
            hash=hash*31+_size.GetHashCode();hash=hash*31+_textSize.GetHashCode();
            hash=hash*31+txtCol.GetHashCode();hash=hash*31+_bgCol.GetHashCode();hash=hash*31+_bgOp.GetHashCode();
            hash=hash*31+_shCol.GetHashCode();hash=hash*31+_outlineCol.GetHashCode();hash=hash*31+_outlineSize.GetHashCode();
            hash=hash*31+_shadowOffset.GetHashCode();hash=hash*31+_rounding.GetHashCode();
            hash=hash*31+(_bold?1:0);hash=hash*31+(_bgOn?1:0);hash=hash*31+(_shOn?1:0);hash=hash*31+(_outlineOn?1:0);hash=hash*31+(Hidden?1:0);
            hash=hash*31+_frameColor.GetHashCode();hash=hash*31+_frameShadowColor.GetHashCode();hash=hash*31+_frameWidth.GetHashCode();hash=hash*31+_frameShadowOffset.GetHashCode();
            hash=hash*31+(_frameOn?1:0);hash=hash*31+(_frameShadowOn?1:0);
            hash=hash*31+(_moving?_targetPosition.x:pX).GetHashCode();hash=hash*31+(_moving?_targetPosition.y:pY).GetHashCode();
            if(_hasSavedAppearance && hash==_savedAppearanceHash) return;
            _savedAppearanceHash=hash;_hasSavedAppearance=true;
            MenuPrefs.SetFloat("MUM_StreamX",_moving?_targetPosition.x:pX); MenuPrefs.SetFloat("MUM_StreamY",_moving?_targetPosition.y:pY);
            MenuPrefs.SetFloat("MUM_StreamSize",_size); MenuPrefs.SetFloat("MUM_StreamTextSize",_textSize);
            MenuPrefs.SetInt("MUM_StreamHidden",Hidden?1:0);
            MenuPrefs.SetInt("MUM_StreamCenterX",_centerX?1:0); MenuPrefs.SetInt("MUM_StreamCenterY",_centerY?1:0);
            MenuPrefs.SetInt("MUM_StreamBold",_bold?1:0); MenuPrefs.SetInt("MUM_StreamBgOn",_bgOn?1:0);
            MenuPrefs.SetInt("MUM_StreamShadowOn",_shOn?1:0); MenuPrefs.SetInt("MUM_StreamOutlineOn",_outlineOn?1:0);
            MenuPrefs.SetFloat("MUM_StreamBgOpacity",_bgOp); MenuPrefs.SetFloat("MUM_StreamRounding",_rounding);
            MenuPrefs.SetFloat("MUM_StreamShadowOffset",_shadowOffset); MenuPrefs.SetFloat("MUM_StreamOutlineSize",_outlineSize);
            MenuPrefs.SetString("MUM_StreamTxtCol","#"+ColorUtility.ToHtmlStringRGBA(txtCol));
            MenuPrefs.SetString("MUM_StreamBgCol","#"+ColorUtility.ToHtmlStringRGBA(_bgCol));
            MenuPrefs.SetString("MUM_StreamShadowCol","#"+ColorUtility.ToHtmlStringRGBA(_shCol));
            MenuPrefs.SetString("MUM_StreamOutlineColor","#"+ColorUtility.ToHtmlStringRGBA(_outlineCol));
            MenuPrefs.SetInt("MUM_StreamFrameOn",_frameOn?1:0);MenuPrefs.SetInt("MUM_StreamFrameShadowOn",_frameShadowOn?1:0);
            MenuPrefs.SetFloat("MUM_StreamFrameWidth",_frameWidth);MenuPrefs.SetFloat("MUM_StreamFrameShadowOffset",_frameShadowOffset);
            MenuPrefs.SetString("MUM_StreamFrameColor","#"+ColorUtility.ToHtmlStringRGBA(_frameColor));
            MenuPrefs.SetString("MUM_StreamFrameShadowColor","#"+ColorUtility.ToHtmlStringRGBA(_frameShadowColor));
        }
        static void EditColor(MenuUI ui, ref Color color, string label)
        {
            float alpha=color.a;
            ui.ColorPickerHSV(ref color,label,compact:true);
            color.a=ui.Slider(I18n.T("Stream.Opacity"),alpha,0f,1f,"0.00");
        }

        void DrawPage(MenuUI ui)
        {
            ui.BeginCard();
            bool hidden = ui.Toggle(I18n.T("Stream.Hidden"),Hidden);
            if(hidden!=Hidden) { Hidden=hidden; ExportForObs(); }
            _size=ui.Slider(I18n.T("Stream.Size"),_size,.5f,1.5f,"0.00x");
            ApplySize();
            GUILayout.BeginHorizontal();
            if(ui.Btn(I18n.T("Stream.CenterHorizontal"),ui.TB3,Theme.Text))
            {_targetPosition=new Vector2(Mathf.Max(0,(Screen.width-oW)*.5f),_moving?_targetPosition.y:pY);_moving=true;}
            if(ui.Btn(I18n.T("Stream.CenterVertical"),ui.TB3,Theme.Text))
            {_targetPosition=new Vector2(_moving?_targetPosition.x:pX,Mathf.Max(0,(Screen.height-oH)*.5f));_moving=true;}
            GUILayout.EndHorizontal();
            ClampPosition();
            ui.EndCard();
            ui.ExpandableSection(I18n.T("Stream.Text"),"",ref _textOpen,()=>
            {
                _textSize=ui.Slider(I18n.T("Stream.Size"),_textSize,.5f,1.25f,"0.00x");
                ApplySize();
                _bold=ui.Toggle(I18n.T("Core.bold_text_label"),_bold);
                EditColor(ui,ref txtCol,I18n.T("Stream.TextColor"));
            });
            ui.ExpandableSection(I18n.T("Stream.Background"),"",ref _backgroundOpen,()=>
            {
                _bgOn=ui.Toggle(I18n.T("Stream.BgToggle"),_bgOn);
                if(_bgOn)
                {
                    ui.ColorPickerHSV(ref _bgCol,I18n.T("Stream.BgColor"),compact:true);
                    _bgOp=ui.Slider(I18n.T("Stream.BgOpacity"),_bgOp,0f,1f,"0.00");
                    _rounding=ui.Slider(I18n.T("Stream.Rounding"),_rounding,0f,40f,"0");
                }
                ui.Label(I18n.T("Stream.BgHint"),Theme.Dim,9);
            });
            ui.ExpandableSection(I18n.T("Stream.Effects"),"",ref _effectsOpen,()=>
            {
                _frameOn=ui.Toggle(I18n.T("Stream.Frame"),_frameOn);
                if(_frameOn)
                {
                    _frameWidth=ui.Slider(I18n.T("Stream.Thickness"),_frameWidth,1f,8f,"0.0");
                    EditColor(ui,ref _frameColor,I18n.T("Stream.FrameColor"));
                }
                _frameShadowOn=ui.Toggle(I18n.T("Stream.FrameShadow"),_frameShadowOn);
                if(_frameShadowOn)
                {
                    _frameShadowOffset=ui.Slider(I18n.T("Stream.ShadowOffset"),_frameShadowOffset,0f,16f,"0.0");
                    EditColor(ui,ref _frameShadowColor,I18n.T("Stream.ShadowColor"));
                }
                _outlineOn=ui.Toggle(I18n.T("Stream.Outline"),_outlineOn);
                if(_outlineOn)
                {
                    _outlineSize=ui.Slider(I18n.T("Stream.Thickness"),_outlineSize,.5f,4f,"0.0");
                    EditColor(ui,ref _outlineCol,I18n.T("Stream.OutlineColor"));
                }
                _shOn=ui.Toggle(I18n.T("Stream.ShadowToggle"),_shOn);
                if(_shOn)
                {
                    _shadowOffset=ui.Slider(I18n.T("Stream.ShadowOffset"),_shadowOffset,0f,12f,"0.0");
                    EditColor(ui,ref _shCol,I18n.T("Stream.ShadowColor"));
                }
            });
            if(!_drag) SaveAppearance();
            ui.BeginCard();
            ui.InfoRow(I18n.T("Stream.Room"),rCode);
            ui.InfoRow(I18n.T("Stream.Players"),pCount.ToString());
            if(!string.IsNullOrEmpty(_expPath))
            {
                ui.Space(6); ui.Label(I18n.T("Stream.ObsExport"),Theme.Text,10,true);
                ui.Label(EFN+"/"+EFILE,Theme.Dim,9);
                ui.Label(I18n.T("Stream.ObsExportHint"),Theme.Dim,9);
            }
            ui.EndCard();
        }
        GUIStyle _outlineStyle;
        static readonly Vector2[] OutlineDirections={new Vector2(1,0),new Vector2(.7071f,.7071f),new Vector2(0,1),new Vector2(-.7071f,.7071f),new Vector2(-1,0),new Vector2(-.7071f,-.7071f),new Vector2(0,-1),new Vector2(.7071f,-.7071f)};
        void DrawOverlayLabel(Rect rect,string text)
        {
            if(_shOn)
            {
                if(_shSty==null) _shSty=new GUIStyle(_sty);
                _shSty.normal.textColor=_shCol;
                GUI.Label(new Rect(rect.x+_shadowOffset*_size,rect.y+_shadowOffset*_size,rect.width,rect.height),text,_shSty);
            }
            if(_outlineOn)
            {
                if(_outlineStyle==null) _outlineStyle=new GUIStyle(_sty);
                _outlineStyle.normal.textColor=_outlineCol;
                for(int i=0;i<8;i++)
                {
                    Vector2 offset=OutlineDirections[i]*_outlineSize*_size;
                    GUI.Label(new Rect(rect.x+offset.x,rect.y+offset.y,rect.width,rect.height),text,_outlineStyle);
                }
            }
            GUI.Label(rect,text,_sty);
        }
        void OnGUI()
        {
            if(!Enabled) return;
            ClampPosition();
            Rect rect=new Rect(pX,pY,oW,oH);
            var ev=Event.current;
            if(ev.type==EventType.MouseDown && ev.button==0 && rect.Contains(ev.mousePosition))
            { _drag=true;_targetPosition=rect.position; _dOff=ev.mousePosition-rect.position; }
            if(_drag && ev.type==EventType.MouseDrag)
            {
                _centerX=_centerY=false;
                _targetPosition=new Vector2(Mathf.Clamp(ev.mousePosition.x-_dOff.x,0,Mathf.Max(0,Screen.width-oW)),Mathf.Clamp(ev.mousePosition.y-_dOff.y,0,Mathf.Max(0,Screen.height-oH)));
                _moving=true;
            }
            if(_drag && ev.type==EventType.MouseUp) { _drag=false; SaveAppearance(); }
            if(ev.type!=EventType.Repaint) return;
            if(_sty==null || _lStyFS!=fSize || _lStyTxt!=txtCol || _sty.font!=UI.FontFor(_bold))
            {
                _sty=new GUIStyle(GUI.skin.label)
                {
                    fontSize=fSize,font=UI.FontFor(_bold),fontStyle=FontStyle.Normal,
                    alignment=TextAnchor.MiddleCenter,normal={textColor=txtCol},
                    padding=new RectOffset(0,0,0,0),wordWrap=true,richText=false
                };
                _lStyFS=fSize; _lStyTxt=txtCol; _shSty=null; _outlineStyle=null;
            }
            string line1=Hidden?I18n.T("Stream.Hidden"):I18n.T("Stream.Room")+" "+rCode;
            string line2=Hidden?"":I18n.T("Stream.Players")+" "+pCount;
            Color previous=GUI.color;
            Color content=GUI.contentColor;
            try
            {
                GUI.color=GUI.contentColor=Color.white;
                if(_frameShadowOn) _frame.Draw(new Rect(rect.x+_frameShadowOffset*_size,rect.y+_frameShadowOffset*_size,rect.width,rect.height),_rounding*_size,_frameWidth*_size,_frameShadowColor);
                if(_bgOn) {Color bg=_bgCol;bg.a*=_bgOp;UI.DrawRounded(rect,bg,Mathf.RoundToInt(_rounding*_size));}
                if(_frameOn) _frame.Draw(rect,_rounding*_size,_frameWidth*_size,_frameColor);
                float width=Mathf.Max(1f,rect.width-20f*_size);
                float height1=_sty.CalcHeight(new GUIContent(line1),width);
                float height2=Hidden?0f:_sty.CalcHeight(new GUIContent(line2),width);
                float gap=Hidden?0f:6f*_size;
                float y=rect.center.y-(height1+height2+gap)*.5f;
                DrawOverlayLabel(new Rect(rect.x+10f*_size,y,width,height1),line1);
                if(!Hidden) DrawOverlayLabel(new Rect(rect.x+10f*_size,y+height1+gap,width,height2),line2);
            }
            finally { GUI.color=previous;GUI.contentColor=content; }
        }
        void OnDestroy() { _frame.Dispose(); }
        void OnApplicationQuit() { SaveAppearance(); try { if (!string.IsNullOrEmpty(_expPath) && File.Exists(_expPath)) File.WriteAllText(_expPath, ""); } catch { } }
    }
}
