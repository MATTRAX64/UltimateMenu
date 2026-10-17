#region GorillaShaders
namespace UltimateMenu
{
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;

    public class GorillaShaders : MonoBehaviour
    {
        public static GorillaShaders Instance { get; private set; }
        public bool Enabled => MenuUI.Instance != null && MenuUI.Instance.IsModEnabled("Filter.Title");
        const string P = "MUM_SCF_";
        const string CustomListKey = P + "CustomList";
        float _bright, _warm, _vign, _vignSoft = 0.5f, _contrast, _saturation, _tint, _grain, _sepia, _gray, _pulse;
        Color _tintColor = Color.white;
        Texture2D _white, _black, _radial, _grainTex;
        float _grainTimer, _pulseT, _vignRedrawTimer;
        bool _vignSoftDirty;
        string _newPresetName = "";
        List<string> _customPresets = new List<string>();
        float _intensity=1f;
        int _look;
        bool _toneOpen, _edgeOpen, _colorOpen, _presetsOpen, _changed;
        Vector2 _grainOffset;
        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadPrefs();
            LoadCustomList();
            _white = Texture2D.whiteTexture;
            _black = Solid(Color.black);
            _radial = Vignette(128, _vignSoft);
            _grainTex = Grain(96);
            StartCoroutine(RegisterPageWhenReady());
        }
        IEnumerator RegisterPageWhenReady()
        {
            for (float t = 0f; MenuUI.Instance == null && t < 15f; t += Time.unscaledDeltaTime) yield return null;
            if (MenuUI.Instance != null) MenuUI.Instance.RegisterPage("Filter.Title", DrawPage, MenuCategory.Apparence);
        }
        void Update()
        {
            if (!Enabled) return;
            if (_grain > 0.001f && (_grainTimer += Time.unscaledDeltaTime) >= 0.08f)
            { _grainTimer=0f;_grainOffset=new Vector2(Mathf.Repeat(_grainOffset.x+.371f,1f),Mathf.Repeat(_grainOffset.y+.613f,1f)); }
            if (_pulse > 0.001f) _pulseT += Time.unscaledDeltaTime;
            if (_vignSoftDirty && (_vignRedrawTimer += Time.unscaledDeltaTime) >= 0.08f) { _vignRedrawTimer = 0f; _vignSoftDirty = false; RedrawVignetteRef(); }
        }
        static Texture2D Solid(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }
        static Texture2D Vignette(int size, float softness)
        {
            var tex = new Texture2D(size, size, TextureFormat.Alpha8, false);
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float maxDist = center.magnitude, start = Mathf.Lerp(0.15f, 0.55f, softness);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, new Color(0, 0, 0, Mathf.Clamp01(Mathf.SmoothStep(0f, 1f, (Vector2.Distance(new Vector2(x, y), center) / maxDist - start) / (1f - start)))));
            tex.Apply();
            return tex;
        }
        static Texture2D Grain(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            RandomizeGrain(t);
            return t;
        }
        static void RandomizeGrain(Texture2D tex)
        {
            var random=new System.Random(42);
            var px = new Color32[tex.width * tex.height];
            for (int i = 0; i < px.Length; i++)
            {byte value=(byte)(random.Next(2)==0?0:255);px[i]=new Color32(value,value,value,(byte)random.Next(40,180));}
            tex.SetPixels32(px);
            tex.Apply(false,true);
        }
        void OnGUI()
        {
            if(!Enabled || Event.current.type!=EventType.Repaint || _intensity<=0f) return;
            Rect full=new Rect(0,0,Screen.width,Screen.height);
            Color previous=GUI.color;
            int depth=GUI.depth;
            try
            {
                GUI.depth=100;
                Color overlay=ScreenFilterBlend.Compose(_bright,_warm,_tintColor,_tint,_sepia,_pulse,_pulseT,_intensity);
                if(overlay.a>.001f){GUI.color=overlay;GUI.DrawTexture(full,_white);}
                if(_grain>.001f)
                {
                    GUI.color=new Color(1,1,1,_grain*_intensity*.12f);
                    GUI.DrawTextureWithTexCoords(full,_grainTex,new Rect(_grainOffset.x,_grainOffset.y,Screen.width/128f,Screen.height/128f));
                }
                if(_vign>.001f){GUI.color=new Color(0,0,0,_vign*_intensity*.55f);GUI.DrawTexture(full,_radial);}
            }
            finally{GUI.color=previous;GUI.depth=depth;}
        }        void LoadPrefs()
        {
            if (!MenuPrefs.HasKey(P + "Init")) { ApplyDefaults(); MenuPrefs.SetInt(P + "Init", 1); SavePrefs(); return; }
            _intensity=Mathf.Clamp01(MenuPrefs.GetFloat(P+"Intensity",1f));
            _look=MenuPrefs.GetInt(P+"Look",0);
            _bright = Mathf.Clamp(MenuPrefs.GetFloat(P + "Bright", 0f), -0.35f, 0.35f);
            _warm = Mathf.Clamp(MenuPrefs.GetFloat(P + "Warmth", 0f), -0.35f, 0.35f);
            _vign = MenuPrefs.GetFloat(P + "Vig", 0f);
            _vignSoft = MenuPrefs.GetFloat(P + "VigSoft", 0.5f);
            _contrast = Mathf.Clamp(MenuPrefs.GetFloat(P + "Contrast", 0f), -1f, 1f);
            _saturation = Mathf.Clamp01(MenuPrefs.GetFloat(P + "Sat", 0f));
            _tint = Mathf.Clamp01(MenuPrefs.GetFloat(P + "Tint", 0f));
            _tintColor = new Color(MenuPrefs.GetFloat(P + "TintR", 1f), MenuPrefs.GetFloat(P + "TintG", 1f), MenuPrefs.GetFloat(P + "TintB", 1f));
            _grain = Mathf.Clamp01(MenuPrefs.GetFloat(P + "Grain", 0f));
            _sepia = Mathf.Clamp01(MenuPrefs.GetFloat(P + "Sepia", 0f));
            _gray = Mathf.Clamp01(MenuPrefs.GetFloat(P + "Gray", 0f));
            _pulse = Mathf.Clamp01(MenuPrefs.GetFloat(P + "Pulse", 0f));
        }
        void SavePrefs()
        {
            MenuPrefs.SetFloat(P+"Intensity",_intensity);MenuPrefs.SetInt(P+"Look",_look);
            MenuPrefs.SetFloat(P + "Bright", _bright); MenuPrefs.SetFloat(P + "Warmth", _warm);
            MenuPrefs.SetFloat(P + "Vig", _vign); MenuPrefs.SetFloat(P + "VigSoft", _vignSoft);
            MenuPrefs.SetFloat(P + "Contrast", _contrast); MenuPrefs.SetFloat(P + "Sat", _saturation);
            MenuPrefs.SetFloat(P + "Tint", _tint); MenuPrefs.SetFloat(P + "TintR", _tintColor.r);
            MenuPrefs.SetFloat(P + "TintG", _tintColor.g); MenuPrefs.SetFloat(P + "TintB", _tintColor.b);
            MenuPrefs.SetFloat(P + "Grain", _grain); MenuPrefs.SetFloat(P + "Sepia", _sepia);
            MenuPrefs.SetFloat(P + "Gray", _gray); MenuPrefs.SetFloat(P + "Pulse", _pulse);
            MenuPrefs.Save();
        }
        public void ReloadConfig() => LoadPrefs();
        void ApplyDefaults() { _intensity=1f;_look=0; _bright = _warm = _vign = _contrast = _saturation = _tint = _grain = _sepia = _gray = _pulse = 0f; _vignSoft = 0.5f; _tintColor = Color.white; }
        void ResetAll() { ApplyDefaults(); RedrawVignetteRef(); SavePrefs(); }
        void RedrawVignetteRef()
        {
            int size = _radial.width;
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float maxDist = center.magnitude, start = Mathf.Lerp(0.15f, 0.55f, _vignSoft);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = new Color(0, 0, 0, Mathf.Clamp01(Mathf.SmoothStep(0f, 1f, (Vector2.Distance(new Vector2(x, y), center) / maxDist - start) / (1f - start))));
            _radial.SetPixels(px);
            _radial.Apply();
        }

        void LoadCustomList()
        {
            _customPresets.Clear();
            string raw = MenuPrefs.GetString(CustomListKey, "");
            if (string.IsNullOrEmpty(raw)) return;
            _customPresets.AddRange(raw.Split('\n'));
            // Import every preset, including presets not selected in this session.
            foreach (string name in _customPresets)
            {
                string key = P + "C_" + name + "_";
                foreach (string field in new[] { "Bright", "Warmth", "Vig", "VigSoft", "Contrast", "Sat", "Tint", "TintR", "TintG", "TintB", "Grain", "Sepia", "Gray", "Pulse", "Intensity" })
                    MenuPrefs.GetFloat(key + field, field == "Intensity" ? 1f : 0f);
            }
        }
        void SaveCustomList() { MenuPrefs.SetString(CustomListKey, string.Join("\n", _customPresets)); MenuPrefs.Save(); }

        void SaveCustomPreset(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            string k = P + "C_" + name + "_";
            MenuPrefs.SetFloat(k+"Intensity",_intensity);
            MenuPrefs.SetFloat(k + "Bright", _bright); MenuPrefs.SetFloat(k + "Warmth", _warm);
            MenuPrefs.SetFloat(k + "Vig", _vign); MenuPrefs.SetFloat(k + "VigSoft", _vignSoft);
            MenuPrefs.SetFloat(k + "Contrast", _contrast); MenuPrefs.SetFloat(k + "Sat", _saturation);
            MenuPrefs.SetFloat(k + "Tint", _tint); MenuPrefs.SetFloat(k + "TintR", _tintColor.r);
            MenuPrefs.SetFloat(k + "TintG", _tintColor.g); MenuPrefs.SetFloat(k + "TintB", _tintColor.b);
            MenuPrefs.SetFloat(k + "Grain", _grain); MenuPrefs.SetFloat(k + "Sepia", _sepia);
            MenuPrefs.SetFloat(k + "Gray", _gray); MenuPrefs.SetFloat(k + "Pulse", _pulse);
            if (!_customPresets.Contains(name)) _customPresets.Add(name);
            MenuPrefs.SetString(CustomListKey, string.Join("\n", _customPresets));
            MenuPrefs.Save();
        }
        void LoadCustomPreset(string name)
        {
            string k = P + "C_" + name + "_";
            if (!MenuPrefs.HasKey(k + "Bright")) return;
            _intensity=MenuPrefs.GetFloat(k+"Intensity",1f);_look=-1;
            _bright = MenuPrefs.GetFloat(k + "Bright"); _warm = MenuPrefs.GetFloat(k + "Warmth");
            _vign = MenuPrefs.GetFloat(k + "Vig"); _vignSoft = MenuPrefs.GetFloat(k + "VigSoft");
            _contrast = MenuPrefs.GetFloat(k + "Contrast"); _saturation = MenuPrefs.GetFloat(k + "Sat");
            _tint = MenuPrefs.GetFloat(k + "Tint");
            _tintColor = new Color(MenuPrefs.GetFloat(k + "TintR"), MenuPrefs.GetFloat(k + "TintG"), MenuPrefs.GetFloat(k + "TintB"));
            _grain = MenuPrefs.GetFloat(k + "Grain"); _sepia = MenuPrefs.GetFloat(k + "Sepia");
            _gray = MenuPrefs.GetFloat(k + "Gray"); _pulse = MenuPrefs.GetFloat(k + "Pulse");
            RedrawVignetteRef();
            SavePrefs();
        }
        void DeleteCustomPreset(string name)
        {
            string k = P + "C_" + name + "_";
            foreach (var suffix in new[] { "Bright", "Warmth", "Vig", "VigSoft", "Contrast", "Sat", "Tint", "TintR", "TintG", "TintB", "Grain", "Sepia", "Gray", "Pulse", "Intensity" })
                MenuPrefs.DeleteKey(k + suffix);
            _customPresets.Remove(name);
            SaveCustomList();
        }

        void ApplyLook(int look)
        {
            ApplyDefaults();_look=look;
            switch(look)
            {
                case 1:_warm=.18f;_bright=.025f;break;
                case 2:_warm=-.18f;_bright=-.015f;break;
                case 3:_bright=-.08f;_warm=-.05f;_vign=.30f;_grain=.12f;_tint=.05f;_tintColor=new Color(.2f,.65f,.65f);break;
                case 4:_bright=-.18f;_warm=-.10f;_vign=.22f;break;
                case 5:_warm=.08f;_sepia=.35f;_grain=.2f;_vign=.22f;break;
            }
            RedrawVignetteRef();SavePrefs();
        }
        void DrawPage(MenuUI ui)
        {
            _changed=false;
            ui.BeginCard();
            string[] looks={I18n.T("Filter.Neutral"),I18n.T("Filter.WarmLook"),I18n.T("Filter.CoolLook"),I18n.T("Filter.CinemaLook"),I18n.T("Filter.NightLook"),I18n.T("Filter.VintageLook")};
            int selected=ui.ModePicker(looks,_look,3);
            if(selected!=_look) ApplyLook(selected);
            float previousIntensity=_intensity;
            _intensity=ui.Slider(I18n.T("Filter.Intensity"),_intensity,0f,1f,"0.00");
            bool intensityChanged=!Mathf.Approximately(previousIntensity,_intensity);
            if(ui.Btn(I18n.T("Filter.Reset"),ui.TB3,Theme.Text)) ResetAll();
            ui.EndCard();
            ui.ExpandableSection(I18n.T("Filter.Tone"),"",ref _toneOpen,()=>
            {
                _bright=SliderSave(ui,"Filter.Brightness",_bright,-.35f,.35f);
                _warm=SliderSave(ui,"Filter.Warmth",_warm,-.35f,.35f);
                _sepia=SliderSave(ui,"Filter.Sepia",_sepia,0f,1f);
            });
            ui.ExpandableSection(I18n.T("Filter.Effects"),"",ref _edgeOpen,()=>
            {
                _vign=SliderSave(ui,"Filter.Vignette",_vign,0f,1f);
                float softness=SliderSave(ui,"Filter.VignetteSoftness",_vignSoft,0f,1f);
                if(!Mathf.Approximately(softness,_vignSoft)){_vignSoft=softness;_vignSoftDirty=true;}
                _grain=SliderSave(ui,"Filter.Grain",_grain,0f,1f);
                _pulse=SliderSave(ui,"Filter.Pulse",_pulse,0f,1f);
            });
            ui.ExpandableSection(I18n.T("Filter.CustomTint"),"",ref _colorOpen,()=>
            {
                Color before=_tintColor;
                ui.ColorPickerHSV(ref _tintColor,I18n.T("Core.color_accent"),compact:true);
                if(!Mathf.Approximately(before.r,_tintColor.r)||!Mathf.Approximately(before.g,_tintColor.g)||!Mathf.Approximately(before.b,_tintColor.b)) _changed=true;
                _tint=SliderSave(ui,"Filter.TintIntensity",_tint,0f,1f);
            });
            ui.ExpandableSection(I18n.T("Filter.CustomPresets"),_customPresets.Count.ToString(),ref _presetsOpen,()=>
            {
                GUILayout.BeginHorizontal();
                _newPresetName=ui.TextField(_newPresetName);
                if(ui.SmallBtn(I18n.T("Filter.SaveAs"),ui.TB3,Theme.Text) && !string.IsNullOrWhiteSpace(_newPresetName))
                {SaveCustomPreset(_newPresetName.Trim());_newPresetName="";}
                GUILayout.EndHorizontal();
                if(_customPresets.Count==0) ui.Label(I18n.T("Filter.NoCustomPresets"),Theme.Dim,9);
                for(int i=0;i<_customPresets.Count;i++)
                {
                    string name=_customPresets[i];
                    GUILayout.BeginHorizontal();
                    if(ui.Btn(name,ui.TB3,Theme.Text)) LoadCustomPreset(name);
                    bool delete=ui.SmallBtn(I18n.T("Spawner.Delete"),ui.TB3,Theme.Text);
                    GUILayout.EndHorizontal();
                    if(delete){DeleteCustomPreset(name);break;}
                }
            });
            ui.Label(I18n.T("Filter.OverlayHint"),Theme.Dim,9);
            if(_changed || intensityChanged){if(_changed)_look=-1;SavePrefs();}
        }
        float SliderSave(MenuUI ui,string key,float value,float min,float max)
        {
            float next=ui.Slider(I18n.T(key),value,min,max,"0.00");
            if(!Mathf.Approximately(next,value)) _changed=true;
            return next;
        }
        void OnDestroy()
        {
            if(_black!=null) Destroy(_black);
            if(_radial!=null) Destroy(_radial);
            if(_grainTex!=null) Destroy(_grainTex);
            if(Instance==this)Instance=null;
        }
    }
}
#endregion

#region ScreenFilterBlend
namespace UltimateMenu
{
    using UnityEngine;

    // Alpha-composited screen overlays only; never samples or alters the scene image.
    internal static class ScreenFilterBlend
    {
        internal static Color Add(Color below,Color tint,float opacity)
        {
            float alpha=Mathf.Clamp01(opacity);
            float resultAlpha=alpha+below.a*(1f-alpha);
            if(resultAlpha<=0f) return Color.clear;
            return new Color((tint.r*alpha+below.r*below.a*(1-alpha))/resultAlpha,
                (tint.g*alpha+below.g*below.a*(1-alpha))/resultAlpha,
                (tint.b*alpha+below.b*below.a*(1-alpha))/resultAlpha,resultAlpha);
        }
        internal static Color Compose(float brightness,float warmth,Color tint,float tintAmount,float sepia,float pulse,float time,float intensity)
        {
            Color result=Color.clear;
            result=Add(result,brightness>=0?Color.white:Color.black,Mathf.Abs(brightness)*.45f);
            result=Add(result,warmth>=0?new Color(1f,.66f,.36f):new Color(.36f,.62f,1f),Mathf.Abs(warmth)*.22f);
            result=Add(result,tint,Mathf.Clamp01(tintAmount)*.22f);
            result=Add(result,new Color(.58f,.36f,.16f),Mathf.Clamp01(sepia)*.18f);
            result=Add(result,Color.white,(Mathf.Sin(time*1.5f)*.5f+.5f)*Mathf.Clamp01(pulse)*.06f);
            result.a*=Mathf.Clamp01(intensity);
            return result;
        }
    }
}
#endregion
