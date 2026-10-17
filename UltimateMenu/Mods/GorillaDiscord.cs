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
        private bool _pageRegistered, _connected, _helpOpen, _editId;
        private int _regTries, _nonce = 1;
        private float _updateTimer;
        private string _appId = "", _currentRoom = "", _lastStatus = "", _customPrefix = "";
        private int _playerCount = 1;
        private long _startTimestamp;
        private NamedPipeClientStream? _pipe;
        private System.Type? _photonType;
        void Awake()
        {
            Instance = this;
            _appId = MenuPrefs.GetString(PREFS_APP_ID, "");
            _editId=string.IsNullOrEmpty(_appId);
            _customPrefix=SafeText(MenuPrefs.GetString("MUM_DiscordPrefix",""));
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
                "\"details\":\"" + JsonEscape((_customPrefix.Length>0?_customPrefix+" · ":"")+(inRoom?I18n.T("Discord.Room")+" "+SafeText(_currentRoom):I18n.T("Rooms.Menu"))) + "\"," +
                "\"state\":\"" + JsonEscape(inRoom?_playerCount+" "+I18n.T("Stream.Players"):I18n.T("Rooms.Menu")) + "\"," +
                "\"timestamps\":{\"start\":" + _startTimestamp + "}" +
                (inRoom ? ",\"party\":{\"id\":\"" + JsonEscape(SafeText(_currentRoom)) + "\",\"size\":[" + _playerCount + ",20]}" : "") +
                (inRoom ? ",\"buttons\":[{\"label\":\"Lancer Gorilla Tag\",\"url\":\"steam://run/" + STEAM_APP_ID + "\"}]" : "") +
                ",\"instance\":" + (inRoom ? "true" : "false") +
            "}},\"nonce\":\"" + (_nonce++) + "\"}";
            SendRaw(1, json);

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
        static string SafeText(string text)
        {
            if(string.IsNullOrEmpty(text))return "";
            var value=new StringBuilder(64);
            foreach(char c in text){if(!char.IsControl(c))value.Append(c);if(value.Length>=64)break;}
            return value.ToString().Trim();
        }
        static string JsonEscape(string text)=>text.Replace("\\","\\\\").Replace("\"","\\\"");
        void DrawPage(MenuUI ui)
        {
            ui.BeginCard();
            ui.StatusPill(_lastStatus,_connected);
            ui.Label(I18n.T("UI.CustomMessage"),Theme.Text,10,true);
            string prefix=SafeText(ui.TextField(_customPrefix));
            if(prefix!=_customPrefix){_customPrefix=prefix;MenuPrefs.SetString("MUM_DiscordPrefix",prefix);}
            ui.Label((_customPrefix.Length>0?_customPrefix+" · ":"")+I18n.T("Discord.Room")+" "+(string.IsNullOrEmpty(_currentRoom)?"ABCD":_currentRoom),Theme.Dim,9);
            if(_connected)
            {
                GUILayout.BeginHorizontal();
                if(ui.Btn(I18n.T("Discord.ForceUpdate"),ui.TB3,Theme.Text)&&Enabled)RefreshAndSend();
                if(ui.Btn(I18n.T("Discord.DisconnectBtn"),ui.TB3,Theme.Text)&&Enabled)Disconnect();
                GUILayout.EndHorizontal();
            }
            ui.EndCard();
            ui.BeginCard();
            if(_editId)
            {
                ui.Label(I18n.T("Discord.AppID"),Theme.Text,10,true);
                _appId=ui.TextField(_appId);
                if(ui.Btn(I18n.T("Discord.SaveConnect"),ui.TB3,Theme.Text)&&Enabled)
                {MenuPrefs.SetString(PREFS_APP_ID,_appId.Trim());MenuPrefs.Save();_editId=false;StartCoroutine(Connect());}
            }
            else
            {
                ui.InfoRow(I18n.T("Discord.AppID"),"••••••••");
                if(ui.Btn(I18n.T("UI.Edit"),ui.TB3,Theme.Text))_editId=true;
            }
            ui.EndCard();
            ui.ExpandableSection(I18n.T("Discord.HowTo"),"",ref _helpOpen,()=>
            {
                ui.Label(I18n.T("Discord.Step1"),Theme.Dim,9);
                ui.Label(I18n.T("Discord.Step2"),Theme.Dim,9);
                ui.Label(I18n.T("Discord.Step3"),Theme.Dim,9);
                ui.Label(I18n.T("Discord.Step4"),Theme.Dim,9);
                ui.Label(I18n.T("Discord.Step5"),Theme.Dim,9);
            });
        }
    }
}
