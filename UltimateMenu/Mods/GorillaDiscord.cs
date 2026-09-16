using UnityEngine;
using System;
using System.IO.Pipes;
using System.Text;
using System.Collections;
using System.Reflection;
namespace UltimateMenu
{
    public class GorillaDiscord : MonoBehaviour
    {
        public static GorillaDiscord? Instance;
        public bool Enabled => MenuUI.Instance != null && MenuUI.Instance.IsModEnabled("Discord.Title");
        private bool _wasEnabled = true;
        private const string PREFS_APP_ID = "MUM_DiscordAppID";
        private const int STEAM_APP_ID = 1533390;
        private const float UPDATE_INTERVAL = 15f;
        private bool _pageRegistered, _connected, _showKey;
        private int _regTries, _nonce = 1;
        private float _updateTimer;
        private string _appId = "", _currentRoom = "", _lastStatus = "";
        private int _playerCount = 1;
        private long _startTimestamp;
        private NamedPipeClientStream? _pipe;
        private System.Type? _photonType;
        void Awake()
        {
            Instance = this;
            _appId = PlayerPrefs.GetString(PREFS_APP_ID, "");
            _lastStatus = I18n.T("Discord.NotConnected");
        }
        void Start()
        {
            _startTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            CachePhoton();
            if (Enabled && !string.IsNullOrEmpty(_appId)) StartCoroutine(Connect());
        }
        void Update()
        {
            if (!_pageRegistered && _regTries++ % 60 == 0 && _regTries < 600) TryRegister();
            bool nowEnabled = Enabled;
            if (nowEnabled != _wasEnabled)
            {
                _wasEnabled = nowEnabled;
                if (!nowEnabled) Disconnect();
                else if (!string.IsNullOrEmpty(_appId)) StartCoroutine(Connect());
            }
            if (!nowEnabled) return;
            if (_connected && (_updateTimer += Time.deltaTime) >= UPDATE_INTERVAL) { _updateTimer = 0f; RefreshAndSend(); }
        }
        void TryRegister() { if (!_pageRegistered && MenuUI.Instance != null) { MenuUI.Instance.RegisterPage("Discord.Title", DrawPage, MenuCategory.Social); _pageRegistered = true; } }
        void CachePhoton()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                foreach (var t in asm.GetTypes())
                    if (t.Name == "PhotonNetwork") { _photonType = t; return; }
        }
        void Disconnect()
        {
            _pipe?.Close();
            _pipe = null;
            _connected = false;
            _lastStatus = I18n.T("Discord.Disconnected");
        }
        IEnumerator Connect()
        {
            if (!Enabled) yield break;
            _lastStatus = I18n.T("Discord.Connecting"); _pipe?.Close(); _pipe = null; _connected = false;
            for (int i = 0; i < 10; i++)
                try { var p = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut); p.Connect(300); if (p.IsConnected) { _pipe = p; break; } } catch { }
            if (_pipe == null) { _lastStatus = I18n.T("Discord.PipeNotFound"); yield break; }
            SendRaw(0, "{\"v\":1,\"client_id\":\"" + _appId + "\"}");
            yield return new WaitForSeconds(1f);
            try
            {
                byte[] buf = new byte[4096]; _pipe.ReadTimeout = 500;
                int n = _pipe.Read(buf, 0, buf.Length);
                if (n > 8)
                {
                    string resp = Encoding.UTF8.GetString(buf, 8, n - 8);
                    Debug.Log("[Discord] " + resp);
                    if (resp.Contains("READY")) _lastStatus = I18n.T("Discord.Connected");
                    else if (resp.Contains("Invalid")) { _lastStatus = I18n.T("Discord.InvalidAppID"); yield break; }
                    else _lastStatus = I18n.T("Discord.Connected");
                }
                else _lastStatus = I18n.T("Discord.ConnectedEmptyResponse");
            }
            catch { _lastStatus = I18n.T("Discord.ConnectedTimeout"); }
            _connected = true;
            RefreshAndSend();
        }
        void RefreshAndSend()
        {
            if (!Enabled) return;
            if (GorillaRooms.Instance != null)
                _currentRoom = typeof(GorillaRooms).GetField("currentRoomCode", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(GorillaRooms.Instance)?.ToString() ?? "";
            try
            {
                if (_photonType != null)
                {
                    var room = _photonType.GetProperty("CurrentRoom", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                    if (room != null)
                    {
                        var p = room.GetType().GetProperty("PlayerCount") ?? room.GetType().GetProperty("playerCount");
                        if (p != null) _playerCount = (int)p.GetValue(room);
                    }
                }
            }
            catch { }
            SendPresence();
        }
        void SendPresence()
        {
            if (!Enabled) return;
            if (_pipe == null || !_pipe.IsConnected) { _connected = false; _lastStatus = I18n.T("Discord.Disconnected"); return; }
            bool inRoom = !string.IsNullOrEmpty(_currentRoom) && _currentRoom != "Menu";
            string json = "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":" + System.Diagnostics.Process.GetCurrentProcess().Id + ",\"activity\":{" +
                "\"details\":\"" + (inRoom ? "Room : " + _currentRoom : "Menu principal") + "\"," +
                "\"state\":\"" + (inRoom ? _playerCount + " joueurs" : "En attente") + "\"," +
                "\"timestamps\":{\"start\":" + _startTimestamp + "}" +
                (inRoom ? ",\"party\":{\"id\":\"" + _currentRoom + "\",\"size\":[" + _playerCount + ",20]}" : "") +
                (inRoom ? ",\"buttons\":[{\"label\":\"Lancer Gorilla Tag\",\"url\":\"steam://run/" + STEAM_APP_ID + "\"}]" : "") +
                ",\"instance\":" + (inRoom ? "true" : "false") +
            "}},\"nonce\":\"" + (_nonce++) + "\"}";
            SendRaw(1, json);
            Debug.Log("[Discord] " + json);
        }
        void SendRaw(int op, string json)
        {
            try
            {
                byte[] d = Encoding.UTF8.GetBytes(json); byte[] b = new byte[8 + d.Length];
                b[0] = (byte)(op & 0xFF); b[1] = (byte)((op >> 8) & 0xFF); b[2] = (byte)((op >> 16) & 0xFF); b[3] = (byte)((op >> 24) & 0xFF);
                b[4] = (byte)(d.Length & 0xFF); b[5] = (byte)((d.Length >> 8) & 0xFF); b[6] = (byte)((d.Length >> 16) & 0xFF); b[7] = (byte)((d.Length >> 24) & 0xFF);
                d.CopyTo(b, 8); _pipe!.Write(b, 0, b.Length); _pipe.Flush();
            }
            catch (Exception e) { _connected = false; _lastStatus = I18n.T("Discord.Error") + " : " + e.Message; }
        }
        void OnDestroy() { _pipe?.Close(); }
        void DrawPage(MenuUI ui)
        {
            ui.Section(I18n.T("Discord.Section"));
            ui.BeginCard();
            ui.StatusPill(_lastStatus, _connected);
            if (_connected)
            {
                ui.Space(4);
                ui.InfoRow(I18n.T("Discord.Room"), _currentRoom == "" || _currentRoom == "Menu" ? I18n.T("Rooms.Menu") : _currentRoom, 60f);
                ui.InfoRow(I18n.T("Discord.Players"), _playerCount.ToString(), 60f);
            }
            ui.EndCard();
            ui.Space(6);
            ui.BeginCard();
            ui.Label(I18n.T("Discord.AppID"), Theme.Dim, 9, true);
            ui.Space(4);
            GUILayout.BeginHorizontal();
            if (_showKey) _appId = ui.TextField(_appId, 160);
            else ui.Label(_appId.Length > 0 ? new string('*', _appId.Length) : I18n.T("Discord.EmptyAppIDHint"), Theme.Dim, 9);
            GUILayout.FlexibleSpace();
            if (ui.SmallBtn(_showKey ? I18n.T("Discord.Hide") : I18n.T("Discord.Show"), ui.TB3, Theme.Text)) _showKey = !_showKey;
            GUILayout.EndHorizontal();
            ui.Space(6);
            if (ui.Btn(I18n.T("Discord.SaveConnect"), ui.TA, Color.white) && Enabled)
            { PlayerPrefs.SetString(PREFS_APP_ID, _appId); PlayerPrefs.Save(); StartCoroutine(Connect()); }
            ui.EndCard();
            if (_connected)
            {
                ui.Space(6);
                ui.BeginCard();
                GUILayout.BeginHorizontal();
                if (ui.Btn(I18n.T("Discord.ForceUpdate"), ui.TA, Color.white) && Enabled) RefreshAndSend();
                GUILayout.Space(6);
                if (ui.Btn(I18n.T("Discord.DisconnectBtn"), ui.TR, Color.white) && Enabled) Disconnect();
                GUILayout.EndHorizontal();
                ui.EndCard();
            }
            ui.Space(6);
            ui.BeginCard();
            ui.Label(I18n.T("Discord.HowTo"), Theme.Accent, 9, true);
            ui.Space(4);
            ui.Label(I18n.T("Discord.Step1"), Theme.Dim, 8);
            ui.Label(I18n.T("Discord.Step2"), Theme.Dim, 8);
            ui.Label(I18n.T("Discord.Step3"), Theme.Dim, 8);
            ui.Label(I18n.T("Discord.Step4"), Theme.Dim, 8);
            ui.Label(I18n.T("Discord.Step5"), Theme.Dim, 8);
            ui.EndCard();
        }
    }
}