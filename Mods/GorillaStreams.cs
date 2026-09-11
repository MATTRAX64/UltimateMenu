using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace UltimateMenu
{
    public class GorillaStreams : MonoBehaviour
    {
        public static GorillaStreams Instance { get; private set; }

        /// <summary>
        /// Le mod est actif uniquement via le switch global de la page Mods (liste des mods).
        /// Il n'y a plus de toggle "Actif/Inactif" propre à cette page.
        /// </summary>
        public bool Enabled => MenuUI.Instance != null && MenuUI.Instance.IsModEnabled("Stream.Title");

        public bool Hidden;
        private string roomCode = "----", lastRoomCode = "";
        private int playerCount, lastPlayerCount = -1;
        private List<GameObject> players = new List<GameObject>();
        private bool[] lastActive = new bool[0];
        private bool dragging;
        private Vector2 dragOff;
        private float posX, posY, overlayW = 200f, overlayH = 100f, timer, animTimer;
        private int fontSize = 14;
        private Color textColor = Color.white;
        private const float INTERVAL = 0.5f, MIN_W = 100f, MAX_W = 400f, MIN_H = 100f, MAX_H = 400f;

        // --- Apparence du fond (pensé pour l'usage OBS : chroma key, transparent, ou couleur perso) ---
        private bool _bgEnabled = true;
        private Color _bgColor = new Color(0f, 1f, 0f, 1f); // vert par défaut, pratique pour le chroma key OBS
        private float _bgOpacity = 1f;

        // --- Ombre du texte (lisibilité sur fond clair/complexe) ---
        private bool _shadowEnabled = true;
        private Color _shadowColor = new Color(0f, 0f, 0f, 0.8f);

        // --- Export fichier texte pour OBS (Text Source, mode "Read from file") ---
        private string _exportFolder;
        private string _exportFilePath;
        private string _lastExportedContent = "";
        private const string EXPORT_FOLDER_NAME = "GorillaStreams";
        private const string EXPORT_FILE_NAME = "overlay.txt";

        private bool _wasEnabled;

        void Awake()
        {
            Instance = this;
            posX = PlayerPrefs.GetFloat("MUM_StreamX", 10f);
            posY = PlayerPrefs.GetFloat("MUM_StreamY", 10f);
            overlayW = PlayerPrefs.GetFloat("MUM_StreamW", 200f);
            overlayH = PlayerPrefs.GetFloat("MUM_StreamH", 100f);
            fontSize = PlayerPrefs.GetInt("MUM_StreamFS", 14);
            ColorUtility.TryParseHtmlString(PlayerPrefs.GetString("MUM_StreamTxtCol", "#FFFFFFFF"), out textColor);

            _bgEnabled = PlayerPrefs.GetInt("MUM_StreamBgOn", 1) == 1;
            ColorUtility.TryParseHtmlString(PlayerPrefs.GetString("MUM_StreamBgCol", "#00FF00FF"), out _bgColor);
            _bgOpacity = PlayerPrefs.GetFloat("MUM_StreamBgOpacity", 1f);

            _shadowEnabled = PlayerPrefs.GetInt("MUM_StreamShadowOn", 1) == 1;
            ColorUtility.TryParseHtmlString(PlayerPrefs.GetString("MUM_StreamShadowCol", "#000000CC"), out _shadowColor);

            SetupExportPath();
        }

        void Start()
        {
            FindPlayers();
            MenuUI.Instance?.RegisterPage("Stream.Title", DrawPage, MenuCategory.Stream);
        }

        void SetupExportPath()
        {
            try
            {
                string dllFolder = Path.GetDirectoryName(GetType().Assembly.Location);
                _exportFolder = Path.Combine(dllFolder, EXPORT_FOLDER_NAME);
                if (!Directory.Exists(_exportFolder)) Directory.CreateDirectory(_exportFolder);
                _exportFilePath = Path.Combine(_exportFolder, EXPORT_FILE_NAME);
            }
            catch (System.Exception e)
            {
                Log.Err("GorillaStreams.SetupExportPath", e);
            }
        }

        void ExportForObs()
        {
            if (string.IsNullOrEmpty(_exportFilePath)) return;

            string content = Enabled && !Hidden
                ? I18n.T("Stream.Room") + " " + roomCode + "\n" + I18n.T("Stream.Players") + " " + playerCount
                : "";

            if (content == _lastExportedContent) return;
            _lastExportedContent = content;

            try { File.WriteAllText(_exportFilePath, content); }
            catch (System.Exception e) { Log.Err("GorillaStreams.ExportForObs", e); }
        }

        void Update()
        {
            bool nowEnabled = Enabled;
            if (nowEnabled != _wasEnabled)
            {
                _wasEnabled = nowEnabled;
                if (nowEnabled) { FindPlayers(); RefreshRoom(); ExportForObs(); }
                else { _lastExportedContent = ""; ExportForObs(); }
            }

            if (!nowEnabled || (timer += Time.deltaTime) < INTERVAL) return;
            timer = 0f;
            CheckPlayers();
            RefreshRoom();
            if (roomCode != lastRoomCode || playerCount != lastPlayerCount)
            {
                lastRoomCode = roomCode;
                lastPlayerCount = playerCount;
                animTimer = 0.6f;
                ExportForObs();
            }
            if (animTimer > 0f) animTimer -= Time.deltaTime;
        }

        void FindPlayers()
        {
            players.Clear();
            players.AddRange(Resources.FindObjectsOfTypeAll<GameObject>().Where(go => go.name == "Gorilla Player Networked(Clone)"));
            lastActive = new bool[players.Count];
            for (int i = 0; i < players.Count; i++) lastActive[i] = players[i].activeInHierarchy;
            CountPlayers();
        }

        void CheckPlayers()
        {
            if (players.Count == 0) { FindPlayers(); return; }
            bool changed = false;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] == null) continue;
                bool a = players[i].activeInHierarchy;
                if (a != lastActive[i]) { lastActive[i] = a; changed = true; }
            }
            if (changed) CountPlayers();
        }

        void CountPlayers()
        {
            playerCount = 1 + players.Count(go => go != null && go.activeInHierarchy);
        }

        void RefreshRoom()
        {
            try
            {
                var pn = System.AppDomain.CurrentDomain.GetAssemblies()
                    .Select(asm => asm.GetType("Photon.Pun.PhotonNetwork")).FirstOrDefault(t => t != null);
                if (pn != null)
                {
                    var room = pn.GetProperty("CurrentRoom", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
                    roomCode = room?.GetType().GetProperty("Name")?.GetValue(room)?.ToString() ?? I18n.T("Rooms.Menu");
                }
            }
            catch { }
        }

        void ClampPosition()
        {
            posX = Mathf.Clamp(posX, 0, Screen.width - overlayW);
            posY = Mathf.Clamp(posY, 0, Screen.height - overlayH);
        }

        void DrawPage(MenuUI ui)
        {
            ui.Section(I18n.T("Stream.Section"));

            // --- Visibilité générale ---
            ui.BeginCard();
            bool newHidden = ui.Toggle(I18n.T("Stream.Hidden"), Hidden, 140f);
            if (newHidden != Hidden && Enabled) { Hidden = newHidden; ExportForObs(); }
            ui.EndCard();

            // --- Dimensions & police ---
            ui.Space(6);
            ui.BeginCard();
            ui.Label(I18n.T("Stream.Dimensions"), Theme.Dim, 9, true);
            ui.Space(4);
            float newW = ui.Slider(I18n.T("Stream.Width"), overlayW, MIN_W, MAX_W, "0", 70f);
            float newH = ui.Slider(I18n.T("Stream.Height"), overlayH, MIN_H, MAX_H, "0", 70f);
            if (Enabled && (Mathf.Abs(newW - overlayW) > 0.5f || Mathf.Abs(newH - overlayH) > 0.5f))
            {
                overlayW = newW; overlayH = newH;
                PlayerPrefs.SetFloat("MUM_StreamW", overlayW); PlayerPrefs.SetFloat("MUM_StreamH", overlayH);
                ClampPosition(); PlayerPrefs.Save();
            }

            ui.Space(4);
            int newFS = Mathf.RoundToInt(ui.Slider(I18n.T("Stream.FontSize"), fontSize, 10, 40, "0", 70f));
            if (Enabled && newFS != fontSize)
            {
                fontSize = newFS;
                PlayerPrefs.SetInt("MUM_StreamFS", fontSize);
                PlayerPrefs.Save();
            }

            ui.Space(4);
            if (Enabled && ui.ColorPickerHSV(ref textColor, I18n.T("Stream.TextColor")))
            {
                PlayerPrefs.SetString("MUM_StreamTxtCol", "#" + ColorUtility.ToHtmlStringRGBA(textColor));
                PlayerPrefs.Save();
            }
            ui.EndCard();

            // --- Fond (pensé pour OBS : chroma key vert, transparent, ou couleur personnalisée) ---
            ui.Space(6);
            ui.BeginCard();
            bool newBgEnabled = ui.Toggle(I18n.T("Stream.BgToggle"), _bgEnabled, 140f);
            if (newBgEnabled != _bgEnabled && Enabled)
            {
                _bgEnabled = newBgEnabled;
                PlayerPrefs.SetInt("MUM_StreamBgOn", _bgEnabled ? 1 : 0);
                PlayerPrefs.Save();
            }
            ui.Space(2);
            ui.Label(I18n.T("Stream.BgHint"), Theme.Dim, 8);

            if (_bgEnabled)
            {
                ui.Space(6); ui.Separator();
                ui.Space(6);
                if (Enabled && ui.ColorPickerHSV(ref _bgColor, I18n.T("Stream.BgColor")))
                {
                    PlayerPrefs.SetString("MUM_StreamBgCol", "#" + ColorUtility.ToHtmlStringRGBA(_bgColor));
                    PlayerPrefs.Save();
                }
                ui.Space(4);
                float newOpacity = ui.Slider(I18n.T("Stream.BgOpacity"), _bgOpacity, 0f, 1f, "0.00", 70f);
                if (Enabled && !Mathf.Approximately(newOpacity, _bgOpacity))
                {
                    _bgOpacity = newOpacity;
                    PlayerPrefs.SetFloat("MUM_StreamBgOpacity", _bgOpacity);
                    PlayerPrefs.Save();
                }
            }
            ui.EndCard();

            // --- Ombre du texte (lisibilité sur n'importe quel fond derrière dans OBS) ---
            ui.Space(6);
            ui.BeginCard();
            bool newShadowEnabled = ui.Toggle(I18n.T("Stream.ShadowToggle"), _shadowEnabled, 140f);
            if (newShadowEnabled != _shadowEnabled && Enabled)
            {
                _shadowEnabled = newShadowEnabled;
                PlayerPrefs.SetInt("MUM_StreamShadowOn", _shadowEnabled ? 1 : 0);
                PlayerPrefs.Save();
            }
            ui.Space(2);
            ui.Label(I18n.T("Stream.ShadowHint"), Theme.Dim, 8);

            if (_shadowEnabled)
            {
                ui.Space(6); ui.Separator();
                ui.Space(6);
                if (Enabled && ui.ColorPickerHSV(ref _shadowColor, I18n.T("Stream.ShadowColor")))
                {
                    PlayerPrefs.SetString("MUM_StreamShadowCol", "#" + ColorUtility.ToHtmlStringRGBA(_shadowColor));
                    PlayerPrefs.Save();
                }
            }
            ui.EndCard();

            // --- Infos actuelles ---
            ui.Space(6);
            ui.BeginCard();
            ui.InfoRow(I18n.T("Stream.Room"), roomCode, 60f);
            ui.InfoRow(I18n.T("Stream.Players"), playerCount.ToString(), 60f);
            ui.EndCard();

            // --- Export OBS ---
            if (!string.IsNullOrEmpty(_exportFilePath))
            {
                ui.Space(6);
                ui.BeginCard();
                ui.Label(I18n.T("Stream.ObsExport"), Theme.Dim, 9, true);
                ui.Space(2);
                ui.Label(EXPORT_FOLDER_NAME + "/" + EXPORT_FILE_NAME, Theme.Accent, 9);
                ui.Space(2);
                ui.Label(I18n.T("Stream.ObsExportHint"), Theme.Dim, 8);
                ui.EndCard();
            }
        }

        void OnGUI()
        {
            if (!Enabled) return;
            string l1 = Hidden ? I18n.T("Stream.Hidden") : I18n.T("Stream.Room") + " " + roomCode;
            string l2 = Hidden ? "" : I18n.T("Stream.Players") + " " + playerCount;
            int nbLines = Hidden ? 1 : 2;
            var rect = new Rect(posX, posY, overlayW, overlayH);

            var ev = Event.current;
            if (ev != null)
            {
                if (ev.type == EventType.MouseDown && rect.Contains(ev.mousePosition))
                {
                    dragging = true; dragOff = ev.mousePosition - new Vector2(posX, posY);
                }
                if (dragging && ev.type == EventType.MouseDrag)
                {
                    posX = Mathf.Clamp(ev.mousePosition.x - dragOff.x, 0, Screen.width - overlayW);
                    posY = Mathf.Clamp(ev.mousePosition.y - dragOff.y, 0, Screen.height - overlayH);
                    PlayerPrefs.SetFloat("MUM_StreamX", posX); PlayerPrefs.SetFloat("MUM_StreamY", posY);
                }
                if (ev.type == EventType.MouseUp)
                {
                    if (dragging) PlayerPrefs.Save();
                    dragging = false;
                }
            }

            // Fond : dessiné uniquement si activé, sinon totalement transparent (pratique en capture OBS avec alpha)
            if (_bgEnabled)
            {
                Color bg = _bgColor; bg.a *= _bgOpacity;
                GUI.color = bg;
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            Color txtCol = textColor;
            if (animTimer > 0f)
                txtCol = Color.Lerp(textColor, Color.yellow, Mathf.PingPong(animTimer * 4f, 1f));

            var sty = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = txtCol },
                wordWrap = true
            };

            float lineH = fontSize + 6f;
            float totalTextH = nbLines * lineH;
            float startY = rect.y + (rect.height - totalTextH) / 2f;
            float textAreaW = rect.width - 16f;

            Rect line1Rect = new Rect(rect.x + 8, startY, textAreaW, lineH);
            Rect line2Rect = new Rect(rect.x + 8, startY + lineH, textAreaW, lineH);

            // Ombre : dessinée derrière le texte pour rester lisible sur n'importe quel fond (utile si le fond est désactivé/transparent)
            if (_shadowEnabled)
            {
                var shadowSty = new GUIStyle(sty) { normal = { textColor = _shadowColor } };
                float off = Mathf.Max(1f, fontSize * 0.06f);
                Rect shOff1 = new Rect(line1Rect.x + off, line1Rect.y + off, line1Rect.width, line1Rect.height);
                GUI.Label(shOff1, l1, shadowSty);
                if (!Hidden)
                {
                    Rect shOff2 = new Rect(line2Rect.x + off, line2Rect.y + off, line2Rect.width, line2Rect.height);
                    GUI.Label(shOff2, l2, shadowSty);
                }
            }

            GUI.Label(line1Rect, l1, sty);
            if (!Hidden)
                GUI.Label(line2Rect, l2, sty);
        }

        void OnApplicationQuit()
        {
            try { if (!string.IsNullOrEmpty(_exportFilePath) && File.Exists(_exportFilePath)) File.WriteAllText(_exportFilePath, ""); }
            catch { }
        }
    }
}