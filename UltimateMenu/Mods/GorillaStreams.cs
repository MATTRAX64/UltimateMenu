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
        List<GameObject> _plr = new List<GameObject>();
        bool[] _lAct = new bool[0];
        bool _drag;
        Vector2 _dOff;
        float pX, pY, oW = 200f, oH = 100f, _t;
        int fSize = 14;
        Color txtCol = Color.white;
        const float INT = 0.5f, MINW = 100f, MAXW = 400f, MINH = 100f, MAXH = 400f;

        bool _bgOn = true;
        Color _bgCol = new Color(0f, 1f, 0f, 1f);
        float _bgOp = 1f;

        bool _shOn = true;
        Color _shCol = new Color(0f, 0f, 0f, 0.8f);

        string _expFolder, _expPath, _lastExp = "";
        const string EFN = "GorillaStreams", EFILE = "overlay.txt";

        bool _wsE;
        System.Reflection.PropertyInfo _pnProp, _rNameProp;
        bool _phRes;

        GUIStyle _sty, _shSty;
        int _lStyFS = -1;
        Color _lStyTxt, _lStyShCol;

        void Awake()
        {
            Instance = this;
            pX = PlayerPrefs.GetFloat("MUM_StreamX", 10f); pY = PlayerPrefs.GetFloat("MUM_StreamY", 10f);
            oW = PlayerPrefs.GetFloat("MUM_StreamW", 200f); oH = PlayerPrefs.GetFloat("MUM_StreamH", 100f);
            fSize = PlayerPrefs.GetInt("MUM_StreamFS", 14);
            ColorUtility.TryParseHtmlString(PlayerPrefs.GetString("MUM_StreamTxtCol", "#FFFFFFFF"), out txtCol);
            _bgOn = PlayerPrefs.GetInt("MUM_StreamBgOn", 1) == 1;
            ColorUtility.TryParseHtmlString(PlayerPrefs.GetString("MUM_StreamBgCol", "#00FF00FF"), out _bgCol);
            _bgOp = PlayerPrefs.GetFloat("MUM_StreamBgOpacity", 1f);
            _shOn = PlayerPrefs.GetInt("MUM_StreamShadowOn", 1) == 1;
            ColorUtility.TryParseHtmlString(PlayerPrefs.GetString("MUM_StreamShadowCol", "#000000CC"), out _shCol);
            SetupExportPath();
        }

        void Start() { FindPlayers(); MenuUI.Instance?.RegisterPage("Stream.Title", DrawPage, MenuCategory.Stream); }

        void SetupExportPath()
        {
            try
            {
                _expFolder = Path.Combine(Path.GetDirectoryName(GetType().Assembly.Location), EFN);
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
            bool nowEn = Enabled;
            if (nowEn != _wsE) { _wsE = nowEn; if (nowEn) { FindPlayers(); RefreshRoom(); ExportForObs(); } else { _lastExp = ""; ExportForObs(); } }
            if (!nowEn || (_t += Time.deltaTime) < INT) return;
            _t = 0f;
            CheckPlayers();
            RefreshRoom();
            if (rCode != lRCode || pCount != lPCount) { lRCode = rCode; lPCount = pCount; ExportForObs(); }
        }

        void FindPlayers()
        {
            _plr.Clear();
            _plr.AddRange(Resources.FindObjectsOfTypeAll<GameObject>().Where(go => go.name == "Gorilla Player Networked(Clone)"));
            _lAct = new bool[_plr.Count];
            for (int i = 0; i < _plr.Count; i++) _lAct[i] = _plr[i].activeInHierarchy;
            CountPlayers();
        }

        void CheckPlayers()
        {
            if (_plr.Count == 0) { FindPlayers(); return; }
            bool ch = false;
            for (int i = 0; i < _plr.Count; i++)
            {
                if (_plr[i] == null) continue;
                bool a = _plr[i].activeInHierarchy;
                if (a != _lAct[i]) { _lAct[i] = a; ch = true; }
            }
            if (ch) CountPlayers();
        }

        void CountPlayers() => pCount = 1 + _plr.Count(go => go != null && go.activeInHierarchy);

        void RefreshRoom()
        {
            try
            {
                if (!_phRes) { _phRes = true; _pnProp = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Photon.Pun.PhotonNetwork")).FirstOrDefault(t => t != null)?.GetProperty("CurrentRoom", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static); }
                var room = _pnProp?.GetValue(null);
                if (room != null) { if (_rNameProp == null) _rNameProp = room.GetType().GetProperty("Name"); rCode = _rNameProp?.GetValue(room)?.ToString() ?? I18n.T("Rooms.Menu"); }
                else rCode = I18n.T("Rooms.Menu");
            }
            catch { }
        }

        void ClampPosition() { pX = Mathf.Clamp(pX, 0, Screen.width - oW); pY = Mathf.Clamp(pY, 0, Screen.height - oH); }

        void DrawPage(MenuUI ui)
        {
            ui.Section(I18n.T("Stream.Section"));

            ui.BeginCard();
            bool nHid = ui.Toggle(I18n.T("Stream.Hidden"), Hidden, 140f);
            if (nHid != Hidden && Enabled) { Hidden = nHid; ExportForObs(); }
            ui.EndCard();

            ui.Space(6); ui.BeginCard();
            ui.Label(I18n.T("Stream.Dimensions"), Theme.Dim, 9, true); ui.Space(4);
            float nW = ui.Slider(I18n.T("Stream.Width"), oW, MINW, MAXW, "0", 70f);
            float nH = ui.Slider(I18n.T("Stream.Height"), oH, MINH, MAXH, "0", 70f);
            if (Enabled && (Mathf.Abs(nW - oW) > 0.5f || Mathf.Abs(nH - oH) > 0.5f)) { oW = nW; oH = nH; PlayerPrefs.SetFloat("MUM_StreamW", oW); PlayerPrefs.SetFloat("MUM_StreamH", oH); ClampPosition(); PlayerPrefs.Save(); }

            ui.Space(4);
            int nFS = Mathf.RoundToInt(ui.Slider(I18n.T("Stream.FontSize"), fSize, 10, 40, "0", 70f));
            if (Enabled && nFS != fSize) { fSize = nFS; PlayerPrefs.SetInt("MUM_StreamFS", fSize); PlayerPrefs.Save(); }

            ui.Space(4);
            if (Enabled && ui.ColorPickerHSV(ref txtCol, I18n.T("Stream.TextColor"))) { PlayerPrefs.SetString("MUM_StreamTxtCol", "#" + ColorUtility.ToHtmlStringRGBA(txtCol)); PlayerPrefs.Save(); }
            ui.EndCard();

            ui.Space(6); ui.BeginCard();
            bool nBgOn = ui.Toggle(I18n.T("Stream.BgToggle"), _bgOn, 140f);
            if (nBgOn != _bgOn && Enabled) { _bgOn = nBgOn; PlayerPrefs.SetInt("MUM_StreamBgOn", _bgOn ? 1 : 0); PlayerPrefs.Save(); }
            ui.Space(2); ui.Label(I18n.T("Stream.BgHint"), Theme.Dim, 8);

            if (_bgOn)
            {
                ui.Space(6); ui.Separator(); ui.Space(6);
                if (Enabled && ui.ColorPickerHSV(ref _bgCol, I18n.T("Stream.BgColor"))) { PlayerPrefs.SetString("MUM_StreamBgCol", "#" + ColorUtility.ToHtmlStringRGBA(_bgCol)); PlayerPrefs.Save(); }
                ui.Space(4);
                float nOp = ui.Slider(I18n.T("Stream.BgOpacity"), _bgOp, 0f, 1f, "0.00", 70f);
                if (Enabled && !Mathf.Approximately(nOp, _bgOp)) { _bgOp = nOp; PlayerPrefs.SetFloat("MUM_StreamBgOpacity", _bgOp); PlayerPrefs.Save(); }
            }
            ui.EndCard();

            ui.Space(6); ui.BeginCard();
            bool nShOn = ui.Toggle(I18n.T("Stream.ShadowToggle"), _shOn, 140f);
            if (nShOn != _shOn && Enabled) { _shOn = nShOn; PlayerPrefs.SetInt("MUM_StreamShadowOn", _shOn ? 1 : 0); PlayerPrefs.Save(); }
            ui.Space(2); ui.Label(I18n.T("Stream.ShadowHint"), Theme.Dim, 8);

            if (_shOn)
            {
                ui.Space(6); ui.Separator(); ui.Space(6);
                if (Enabled && ui.ColorPickerHSV(ref _shCol, I18n.T("Stream.ShadowColor"))) { PlayerPrefs.SetString("MUM_StreamShadowCol", "#" + ColorUtility.ToHtmlStringRGBA(_shCol)); PlayerPrefs.Save(); }
            }
            ui.EndCard();

            ui.Space(6); ui.BeginCard();
            ui.InfoRow(I18n.T("Stream.Room"), rCode, 60f); ui.InfoRow(I18n.T("Stream.Players"), pCount.ToString(), 60f);
            ui.EndCard();

            if (!string.IsNullOrEmpty(_expPath))
            {
                ui.Space(6); ui.BeginCard();
                ui.Label(I18n.T("Stream.ObsExport"), Theme.Dim, 9, true); ui.Space(2);
                ui.Label(EFN + "/" + EFILE, Theme.Accent, 9); ui.Space(2);
                ui.Label(I18n.T("Stream.ObsExportHint"), Theme.Dim, 8);
                ui.EndCard();
            }
        }

        void OnGUI()
        {
            if (!Enabled) return;
            string l1 = Hidden ? I18n.T("Stream.Hidden") : I18n.T("Stream.Room") + " " + rCode;
            string l2 = Hidden ? "" : I18n.T("Stream.Players") + " " + pCount;
            var rect = new Rect(pX, pY, oW, oH);

            var ev = Event.current;
            if (ev != null)
            {
                if (ev.type == EventType.MouseDown && rect.Contains(ev.mousePosition)) { _drag = true; _dOff = ev.mousePosition - new Vector2(pX, pY); }
                if (_drag && ev.type == EventType.MouseDrag)
                {
                    pX = Mathf.Clamp(ev.mousePosition.x - _dOff.x, 0, Screen.width - oW);
                    pY = Mathf.Clamp(ev.mousePosition.y - _dOff.y, 0, Screen.height - oH);
                    PlayerPrefs.SetFloat("MUM_StreamX", pX); PlayerPrefs.SetFloat("MUM_StreamY", pY);
                }
                if (ev.type == EventType.MouseUp) { if (_drag) PlayerPrefs.Save(); _drag = false; }
            }

            if (_bgOn) { Color bg = _bgCol; bg.a *= _bgOp; GUI.color = bg; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = Color.white; }

            Color tc = txtCol;

            if (_sty == null || _lStyFS != fSize || _lStyTxt != tc)
            {
                _sty = new GUIStyle(GUI.skin.label) { fontSize = fSize, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = tc }, wordWrap = true };
                _lStyFS = fSize; _lStyTxt = tc;
            }

            float lineH = fSize + 6f;
            float startY = rect.y + (rect.height - (Hidden ? 1 : 2) * lineH) / 2f;
            float areaW = rect.width - 16f;

            Rect l1R = new Rect(rect.x + 8, startY, areaW, lineH);
            Rect l2R = new Rect(rect.x + 8, startY + lineH, areaW, lineH);

            if (_shOn)
            {
                if (_shSty == null || _lStyFS != fSize || _lStyShCol != _shCol) { _shSty = new GUIStyle(_sty) { normal = { textColor = _shCol } }; _lStyShCol = _shCol; }
                float off = Mathf.Max(1f, fSize * 0.06f);
                GUI.Label(new Rect(l1R.x + off, l1R.y + off, l1R.width, l1R.height), l1, _shSty);
                if (!Hidden) GUI.Label(new Rect(l2R.x + off, l2R.y + off, l2R.width, l2R.height), l2, _shSty);
            }

            GUI.Label(l1R, l1, _sty);
            if (!Hidden) GUI.Label(l2R, l2, _sty);
        }

        void OnApplicationQuit() { try { if (!string.IsNullOrEmpty(_expPath) && File.Exists(_expPath)) File.WriteAllText(_expPath, ""); } catch { } }
    }
}