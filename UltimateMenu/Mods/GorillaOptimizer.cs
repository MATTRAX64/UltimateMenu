#region GorillaOptimizer
namespace UltimateMenu
{
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.XR;

    public sealed class GorillaOptimizer : MonoBehaviour
    {
        const string Title = "GorillaOptimizer", Pref = "MUM_Optimizer_Active";
        bool _registered, _applied, _showFps;
        readonly PerformanceProbe _probe=new PerformanceProbe();
        bool _testing;
        float _warmUntil;
        string _testResult="";
        readonly List<XRDisplaySubsystem> _displays=new List<XRDisplaySubsystem>();
        void StartPerformanceTest()
        {
            float target=60f;
            if(XRSettings.enabled)
            {
                SubsystemManager.GetSubsystems(_displays);
                foreach(var display in _displays)
                    if(display.running&&display.TryGetDisplayRefreshRate(out float refresh)&&refresh>0){target=refresh;break;}
            }
            else
            {
                if(QualitySettings.vSyncCount>0)target=Mathf.Min(target,(float)Screen.currentResolution.refreshRateRatio.value/QualitySettings.vSyncCount);
                if(Application.targetFrameRate>0)target=Mathf.Min(target,Application.targetFrameRate);
            }
            _probe.Reset(Mathf.Max(20f,target));_warmUntil=Time.unscaledTime+2f;_testing=true;_testResult="";
        }
        void UpdatePerformanceTest()
        {
            if(!_testing)return;
            if(!Application.isFocused){_testing=false;_testResult="Optimizer.TestInterrupted";return;}
            if(Time.unscaledTime<_warmUntil)return;
            _probe.Record(Time.unscaledDeltaTime);
            if(!_probe.Ready)return;
            _testing=false;
            if(_probe.NeedsOptimization)
            {
                MenuPrefs.SetInt("MUM_ModEnabled_"+Title,1);
                MenuStyle.FastInterface=true;MenuPrefs.SetInt("MUM_FastInterface",1);MenuPrefs.Save();
                _testResult="Optimizer.TestApplied";
            }
            else _testResult="Optimizer.TestGood";
        }
        int _hudRevision=-1;
        string _hudNumber="—",_hudTiming="— ms";
        float _hudScale;
        GUIStyle _hudValueStyle,_hudCaptionStyle;
        ShadowQuality _originalShadows;
        bool _originalReflections;
        float _originalResolution, _originalLod, _nextCameraScan;
        readonly List<Camera> _livCameras = new List<Camera>();
        readonly Dictionary<Camera, bool> _originalOcclusion = new Dictionary<Camera, bool>();
        GameObject _tablet;
        readonly FpsHistory _fps=new FpsHistory();
        readonly FpsGraph _graph=new FpsGraph();

        void Awake()
        {
            _showFps=MenuPrefs.GetInt("MUM_Optimizer_ShowFps",1)==1;
            if (!MenuPrefs.HasKey(Pref)) return;
            string pageKey = "MUM_ModEnabled_" + Title;
            bool wasActive = MenuPrefs.GetInt(pageKey, 0) == 1 && MenuPrefs.GetInt(Pref, 0) == 1;
            MenuPrefs.SetInt(pageKey, wasActive ? 1 : 0);
            MenuPrefs.DeleteKey(Pref);
            MenuPrefs.Save();
        }
        void Update()
        {
            if (!_registered && MenuUI.Instance != null)
            {
                MenuUI.Instance.RegisterPage(Title, DrawPage, MenuCategory.Technique, false); _registered = true;
            }
            UpdatePerformanceTest();
            bool active = _registered && MenuUI.Instance != null && MenuUI.Instance.IsModEnabled(Title);
            if (active && !_applied)
            {
                _originalShadows = QualitySettings.shadows;
                _originalReflections = QualitySettings.realtimeReflectionProbes;
                _originalResolution = XRSettings.eyeTextureResolutionScale;
                _originalLod = QualitySettings.lodBias;
                QualitySettings.shadows = ShadowQuality.Disable;
                QualitySettings.realtimeReflectionProbes = false;
                if (XRSettings.enabled) XRSettings.eyeTextureResolutionScale = _originalResolution * 0.85f;
                QualitySettings.lodBias = _originalLod * 0.75f;
                _applied = true; _nextCameraScan = 0f;
            }
            else if (!active && _applied) Restore();
            _fps.Record(Time.unscaledDeltaTime,Time.unscaledTime,_applied);
            if(_fps.Count>0&&_fps.Revision!=_hudRevision)
            {
                _hudRevision=_fps.Revision;
                float fps=_fps.At(_fps.Count-1).Fps;
                _hudNumber=fps.ToString("0");
                _hudTiming=(fps>0?1000f/fps:0).ToString("0.0")+" ms";
            }
            if (active && Time.unscaledTime >= _nextCameraScan)
            {
                _nextCameraScan = Time.unscaledTime + 1f;
                if (_tablet == null) _tablet = GameObject.Find("LCKTablet(Clone)");
                if (_tablet != null)
                {
                    _livCameras.Clear(); _tablet.GetComponentsInChildren<Camera>(true, _livCameras);
                    foreach (var camera in _livCameras)
                    {
                        if (camera == null || _originalOcclusion.ContainsKey(camera) || _originalOcclusion.Count >= 32) continue;
                        _originalOcclusion[camera] = camera.useOcclusionCulling;
                        camera.useOcclusionCulling = false;
                    }
                }
            }
        }

        void Restore()
        {
            if (!_applied) return;
            if (QualitySettings.shadows == ShadowQuality.Disable) QualitySettings.shadows = _originalShadows;
            if (!QualitySettings.realtimeReflectionProbes) QualitySettings.realtimeReflectionProbes = _originalReflections;
            if (Mathf.Approximately(XRSettings.eyeTextureResolutionScale, _originalResolution * 0.85f)) XRSettings.eyeTextureResolutionScale = _originalResolution;
            if (Mathf.Approximately(QualitySettings.lodBias, _originalLod * 0.75f)) QualitySettings.lodBias = _originalLod;
            foreach (var entry in _originalOcclusion)
                if (entry.Key != null && !entry.Key.useOcclusionCulling) entry.Key.useOcclusionCulling = entry.Value;
            _originalOcclusion.Clear(); _livCameras.Clear(); _applied = false;
        }

        void DrawPage(MenuUI ui)
        {
            ui.Section(I18n.T(Title));
            ui.BeginCard();
            bool game=MenuPrefs.GetInt("MUM_ModEnabled_"+Title,0)==1;
            bool nextGame=ui.Toggle(I18n.T("Optimizer.Game"),game);
            if(nextGame!=game){MenuPrefs.SetInt("MUM_ModEnabled_"+Title,nextGame?1:0);MenuPrefs.Save();}
            bool fast=ui.Toggle(I18n.T("Optimizer.FastInterface"),MenuStyle.FastInterface);
            if(fast!=MenuStyle.FastInterface){MenuStyle.FastInterface=fast;MenuPrefs.SetInt("MUM_FastInterface",fast?1:0);MenuPrefs.Save();}
            bool show=ui.Toggle(I18n.T("Optimizer.ShowFps"),_showFps);
            if(show!=_showFps){_showFps=show;MenuPrefs.SetInt("MUM_Optimizer_ShowFps",show?1:0);}
            ui.EndCard();
            ui.BeginCard();
            if(!_testing&&ui.Btn(I18n.T("Optimizer.Test")))StartPerformanceTest();
            if(_testing)ui.Label(I18n.T("Optimizer.TestRunning")+" · "+Mathf.CeilToInt(Mathf.Max(0,_warmUntil-Time.unscaledTime)+Mathf.Max(0,8f-_probe.Elapsed))+" s",Theme.Dim,10);
            else if(_testResult.Length>0)
            {
                ui.Label(I18n.T(_testResult),Theme.Text,10);
                if(_probe.Ready)ui.Label(_probe.Average.ToString("0.0")+" FPS · "+_probe.Target.ToString("0")+" FPS",Theme.Dim,10);
            }
            ui.EndCard();
            if(!MenuStyle.FastInterface)_graph.Draw(ui,_fps);
            ui.Label(I18n.T("Optimizer.SimpleHint"), Theme.Dim, 10);
        }
        void OnGUI()
        {
            if(!_showFps||!_registered||Event.current.type!=EventType.Repaint)return;
            float scale=Mathf.Clamp(Screen.height/1080f,.7f,1.5f);
            if(_hudValueStyle==null||_hudScale!=scale)
            {
                _hudScale=scale;
                _hudValueStyle=new GUIStyle(GUI.skin.label){font=UI.Sty(Color.white,20,true).font,fontSize=Mathf.RoundToInt(27*scale),fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleLeft,normal={textColor=Color.white}};
                _hudCaptionStyle=new GUIStyle(GUI.skin.label){font=_hudValueStyle.font,fontSize=Mathf.RoundToInt(11*scale),alignment=TextAnchor.MiddleLeft,normal={textColor=new Color(.8f,.83f,.9f,1)}};
            }
            Matrix4x4 matrix=GUI.matrix;Color tint=GUI.color;int depth=GUI.depth;
            try
            {
                GUI.matrix=Matrix4x4.identity;GUI.color=Color.white;GUI.depth=-100;
                Rect card=new Rect(16*scale,16*scale,148*scale,69*scale);
                UI.DrawRounded(new Rect(card.x+2*scale,card.y+3*scale,card.width,card.height),new Color(0,0,0,.25f),Mathf.RoundToInt(12*scale));
                UI.DrawRounded(card,new Color(.035f,.045f,.065f,.9f),Mathf.RoundToInt(12*scale));
                UI.DrawRounded(new Rect(card.x+10*scale,card.y+15*scale,3*scale,39*scale),Theme.Accent,Mathf.RoundToInt(2*scale));
                GUI.Label(new Rect(card.x+23*scale,card.y+5*scale,83*scale,37*scale),_hudNumber,_hudValueStyle);
                GUI.Label(new Rect(card.x+105*scale,card.y+16*scale,35*scale,20*scale),"FPS",_hudCaptionStyle);
                GUI.Label(new Rect(card.x+24*scale,card.y+41*scale,116*scale,21*scale),_hudTiming,_hudCaptionStyle);
            }
            finally {GUI.matrix=matrix;GUI.color=tint;GUI.depth=depth;}
        }
        void OnDisable() { _testing=false; Restore(); }
        void OnDestroy() { Restore(); _graph.Dispose(); }
    }
}
#endregion

#region FpsHistory
namespace UltimateMenu
{
    using System;

    internal sealed class FpsHistory
    {
        internal struct Sample { internal float Time, Fps, Duration; internal bool Active; }
        const int Capacity=256;
        readonly Sample[] _samples=new Sample[Capacity];
        int _next,_count,_frames;
        float _elapsed;
        bool _active;
        internal int Revision {get;private set;}
        internal int Count=>_count;
        internal Sample At(int index)=>_samples[(_next-_count+index+Capacity)%Capacity];
        internal void Record(float delta,float now,bool active)
        {
            if(delta<=0 || float.IsNaN(delta) || float.IsInfinity(delta)) return;
            if(active!=_active){_frames=0;_elapsed=0;_active=active;}
            _frames++;_elapsed+=delta;
            if(_elapsed<.25f) return;
            _samples[_next]=new Sample{Time=now,Fps=_frames/_elapsed,Duration=_elapsed,Active=active};
            _next=(_next+1)%Capacity;_count=Math.Min(_count+1,Capacity);Revision++;
            _frames=0;_elapsed=0;
        }
        internal void Stats(float now,out float current,out float average,out float minimum,out float maximum)
        {
            current=average=maximum=0;minimum=float.MaxValue;
            float duration=0,frames=0;
            for(int i=0;i<_count;i++)
            {
                Sample sample=At(i);if(now-sample.Time>60f) continue;
                current=sample.Fps;minimum=Math.Min(minimum,sample.Fps);maximum=Math.Max(maximum,sample.Fps);
                duration+=sample.Duration;frames+=sample.Fps*sample.Duration;
            }
            if(duration>0) average=frames/duration;else minimum=0;
        }
        internal static float ScaleMaximum(float value)
        {
            if(value<=72f) return 72f;
            double magnitude=Math.Pow(10,Math.Floor(Math.Log10(value/4)));
            float step=(float)(Math.Ceiling(value/4/magnitude)*magnitude);
            return (float)Math.Ceiling(value/step)*step;
        }
    }
}
#endregion

#region FpsGraph
namespace UltimateMenu
{
    using UnityEngine;

    internal sealed class FpsGraph
    {
        const int Width=512,Height=160;
        Texture2D _texture;
        readonly Color32[] _pixels=new Color32[Width*Height];
        int _revision=-1;
        float _maximum;
        Color _color;
        Color _inactiveColor;
        internal void Draw(MenuUI ui,FpsHistory history)
        {
            history.Stats(Time.unscaledTime,out float current,out float average,out float minimum,out float maximum);
            float scale=FpsHistory.ScaleMaximum(maximum);
            ui.BeginCard();
            ui.Label(current.ToString("0")+" FPS",Theme.Text,20,true);
            ui.Label(I18n.T("FPS.Average")+" "+average.ToString("0")+"  ·  "+I18n.T("FPS.Min")+" "+minimum.ToString("0")+"  ·  "+I18n.T("FPS.Max")+" "+maximum.ToString("0"),Theme.Dim,10);
            float height=176f*UI.Scale;
            Rect row=GUILayoutUtility.GetRect(GUIContent.none,GUIStyle.none,GUILayout.ExpandWidth(true),GUILayout.Height(height));
            Rect plot=new Rect(row.x+48f*UI.Scale,row.y+8f*UI.Scale,Mathf.Max(1,row.width-56f*UI.Scale),height-34f*UI.Scale);
            UI.DrawRounded(plot,MenuStyle.Input,8);
            for(int i=0;i<=4;i++)
            {
                float y=plot.y+plot.height*i/4f;
                Color grid=Theme.Text;grid.a=.1f;
                UI.DrawRounded(new Rect(plot.x,y,plot.width,UI.Scale),grid,0);
                GUI.Label(new Rect(row.x,y-9f*UI.Scale,40f*UI.Scale,20f*UI.Scale),(scale*(1f-i/4f)).ToString("0"),UI.Sty(UI.SecondaryText,8,false,TextAnchor.MiddleRight));
            }
            if(Event.current.type==EventType.Repaint)
            {
                if(_texture==null) _texture=new Texture2D(Width,Height,TextureFormat.RGBA32,false){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
                if(_revision!=history.Revision || _maximum!=scale || _color!=Theme.Accent || _inactiveColor!=Theme.Dim)
                {Build(history,scale);_revision=history.Revision;_maximum=scale;_color=Theme.Accent;_inactiveColor=Theme.Dim;}
                GUI.DrawTexture(plot,_texture);
            }
            GUI.Label(new Rect(plot.x,plot.yMax+4f*UI.Scale,80f*UI.Scale,20f*UI.Scale),"60 s",UI.Sty(Theme.Dim,8));
            GUI.Label(new Rect(plot.center.x-40f*UI.Scale,plot.yMax+4f*UI.Scale,80f*UI.Scale,20f*UI.Scale),"30 s",UI.Sty(Theme.Dim,8,false,TextAnchor.MiddleCenter));
            GUI.Label(new Rect(plot.xMax-80f*UI.Scale,plot.yMax+4f*UI.Scale,80f*UI.Scale,20f*UI.Scale),"0 s",UI.Sty(Theme.Dim,8,false,TextAnchor.MiddleRight));
            ui.EndCard();
        }
        void Build(FpsHistory history,float scale)
        {
            System.Array.Clear(_pixels,0,_pixels.Length);
            if(history.Count>0)
            {
                float now=history.At(history.Count-1).Time;
                bool hasPrevious=false;Vector2 previous=Vector2.zero;bool previousActive=false;
                for(int i=0;i<history.Count;i++)
                {
                    var sample=history.At(i);float age=now-sample.Time;if(age>60) continue;
                    Vector2 point=new Vector2((1-age/60f)*(Width-1),Mathf.Clamp01(sample.Fps/scale)*(Height-1));
                    if(hasPrevious) Line(previous,point,sample.Active?Theme.Accent:Theme.Dim);
                    if(hasPrevious && previousActive!=sample.Active)
                        Line(new Vector2(point.x,0),new Vector2(point.x,Height-1),new Color(1,1,1,.18f));
                    previous=point;previousActive=sample.Active;hasPrevious=true;
                }
            }
            _texture.SetPixels32(_pixels);_texture.Apply(false,false);
        }
        void Line(Vector2 from,Vector2 to,Color color)
        {
            int steps=Mathf.Max(1,Mathf.CeilToInt(Mathf.Max(Mathf.Abs(to.x-from.x),Mathf.Abs(to.y-from.y))));
            for(int i=0;i<=steps;i++)
            {
                Vector2 p=Vector2.Lerp(from,to,(float)i/steps);int x=Mathf.FloorToInt(p.x),y=Mathf.FloorToInt(p.y);
                float dx=p.x-x,dy=p.y-y;
                Pixel(x,y,color,(1-dx)*(1-dy));Pixel(x+1,y,color,dx*(1-dy));Pixel(x,y+1,color,(1-dx)*dy);Pixel(x+1,y+1,color,dx*dy);
            }
        }
        void Pixel(int x,int y,Color color,float weight)
        {
            if(x<0||x>=Width||y<0||y>=Height||weight<=0) return;
            int index=y*Width+x;float alpha=Mathf.Clamp01(color.a*weight*1.8f);
            Color old=_pixels[index];if(alpha>old.a) _pixels[index]=new Color(color.r,color.g,color.b,alpha);
        }
        internal void Dispose(){if(_texture!=null) Object.Destroy(_texture);_texture=null;}
    }
}
#endregion

#region PerformanceProbe
namespace UltimateMenu
{
    internal sealed class PerformanceProbe
    {
        int _frames;
        float _slowTime;
        internal float Target {get;private set;}
        internal float Elapsed {get;private set;}
        internal float Average=>Elapsed>0?_frames/Elapsed:0;
        internal bool Ready=>Elapsed>=8f;
        internal bool NeedsOptimization=>Ready&&(Average<Target*.85f||_slowTime/Elapsed>.2f);
        internal void Reset(float target){Target=target;Elapsed=0;_frames=0;_slowTime=0;}
        internal void Record(float delta)
        {
            if(Ready||delta<=0||float.IsNaN(delta)||float.IsInfinity(delta))return;
            _frames++;Elapsed+=delta;
            if(delta>1.5f/Target)_slowTime+=delta;
        }
    }
}
#endregion
