using UnityEngine;
using System;
using System.Reflection;
using System.Collections.Generic;

namespace UltimateMenu
{
    public class GorillaRooms : MonoBehaviour
    {
        public static GorillaRooms? Instance;
        private string currentRoomCode = "", inputCode = "", statusMsg = "", _retryCode = "";
        private float refreshTimer, statusTimer, _connectingTimeout;
        private bool pageRegistered, _connecting;
        private int regTries, _retryCount;
        private const int MaxRetries = 3;
        private List<string> favoriteRooms = new List<string>();
        private object? _controller, _joinTypeVal;
        private System.Type? _controllerType, _photonType;
        private MethodInfo? _joinMethod;

        public bool Enabled => true;

        void Awake() { Instance = this; LoadFavorites(); }
        void Start() { Invoke(nameof(Init), 3f); }

        void Update()
        {
            if (!pageRegistered && regTries++ % 60 == 0 && regTries < 600 && MenuUI.Instance != null) { MenuUI.Instance.RegisterPage("Rooms.Title", DrawPage, MenuCategory.Gameplay, false); pageRegistered = true; }
            if ((refreshTimer += Time.deltaTime) >= 2f) { refreshTimer = 0f; currentRoomCode = GetCurrentRoom(); }
            if (statusTimer > 0f) statusTimer -= Time.deltaTime;
            if (!_connecting) return;

            if (!string.IsNullOrEmpty(currentRoomCode) && currentRoomCode != I18n.T("Rooms.Menu") && currentRoomCode != "Menu") { _connecting = false; _retryCount = 0; }
            else if ((_connectingTimeout -= Time.deltaTime) <= 0f)
            {
                _connecting = false;
                if (_retryCount < MaxRetries) { _retryCount++; JoinRoom(_retryCode, true); }
                else SetStatus("Echec apres " + MaxRetries + " tentatives");
            }
        }

        void Init()
        {
            var go = GameObject.Find("Networking Scripts/Photon Manager");
            if (go != null) foreach (var c in go.GetComponents<Component>()) if (c.GetType().Name == "PhotonNetworkController") { _controller = c; _controllerType = c.GetType(); break; }
            if (_controller == null) foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)) if (mb.GetType().Name == "PhotonNetworkController") { _controller = mb; _controllerType = mb.GetType(); break; }
            if (_controllerType == null) { SetStatus("PhotonNetworkController introuvable"); return; }
            foreach (var m in _controllerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) if (m.Name == "AttemptToAutoJoinSpecificRoom") { _joinMethod = m; break; }
            if (_joinMethod != null) { var prms = _joinMethod.GetParameters(); if (prms.Length >= 2 && prms[1].ParameterType.IsEnum) _joinTypeVal = Enum.GetValues(prms[1].ParameterType).GetValue(0); }
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in asm.GetTypes()) if (t.Name == "PhotonNetwork") { _photonType = t; break; }
            SetStatus(_joinMethod != null ? "Photon OK" : "AttemptToAutoJoinSpecificRoom introuvable");
        }

        string GetCurrentRoom()
        {
            if (_photonType == null) return "";
            try
            {
                var inRoom = _photonType.GetProperty("InRoom", BindingFlags.Public | BindingFlags.Static);
                if (inRoom == null || !(bool)inRoom.GetValue(null)) return I18n.T("Rooms.Menu");
                var room = _photonType.GetProperty("CurrentRoom", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                return room?.GetType().GetProperty("Name")?.GetValue(room)?.ToString() ?? "";
            }
            catch { return ""; }
        }

        void JoinRoom(string code, bool isRetry = false)
        {
            if (string.IsNullOrEmpty(code) || _joinMethod == null || _controller == null) { SetStatus("Non initialise"); return; }
            code = code.Trim().ToUpper();
            if (!isRetry) { _retryCount = 0; _retryCode = code; }
            try
            {
                var args = new object[_joinMethod.GetParameters().Length];
                args[0] = code;
                if (args.Length >= 2) args[1] = _joinTypeVal;
                _joinMethod.Invoke(_controller, args);
                _connecting = true; _connectingTimeout = 8f;
                SetStatus("Connexion a " + code + (isRetry ? " (" + (_retryCount + 1) + "/" + MaxRetries + ")..." : "..."));
            }
            catch (Exception e) { SetStatus("Erreur : " + e.Message); }
        }

        void LeaveRoom()
        {
            if (_photonType == null) return;
            try { foreach (var m in _photonType.GetMethods(BindingFlags.Public | BindingFlags.Static)) if (m.Name == "LeaveRoom") { m.Invoke(null, new object[m.GetParameters().Length]); SetStatus(I18n.T("Rooms.Leaving")); return; } }
            catch (Exception e) { SetStatus("Erreur : " + e.Message); }
        }

        string GenerateRoomCode() => (string.IsNullOrEmpty(inputCode) ? "" : inputCode.Trim().ToUpper()) + UnityEngine.Random.Range(1000, 9999);
        void SetStatus(string msg) { statusMsg = msg; statusTimer = 4f; }

        void LoadFavorites()
        {
            favoriteRooms.Clear();
            string raw = MenuPrefs.GetString("MUM_FavRooms", "");
            if (!string.IsNullOrEmpty(raw)) favoriteRooms.AddRange(raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            favoriteRooms.Sort(StringComparer.OrdinalIgnoreCase);
        }

        void SaveFavorites() { favoriteRooms.Sort(StringComparer.OrdinalIgnoreCase); MenuPrefs.SetString("MUM_FavRooms", string.Join(",", favoriteRooms)); MenuPrefs.Save(); }
        void AddFavorite(string code) { code = code.Trim().ToUpper(); if (!string.IsNullOrEmpty(code) && !favoriteRooms.Contains(code)) { favoriteRooms.Add(code); SaveFavorites(); } }
        void RemoveFavorite(string code) { if (favoriteRooms.Remove(code)) SaveFavorites(); }

        void DrawPage(MenuUI ui)
        {
            bool inRoom=!string.IsNullOrEmpty(currentRoomCode)&&currentRoomCode!=I18n.T("Rooms.Menu")&&currentRoomCode!="Menu";
            ui.BeginCard();
            ui.InfoRow(I18n.T("Rooms.Title"),inRoom?currentRoomCode:I18n.T("Rooms.Menu"));
            if(statusTimer>0&&!string.IsNullOrEmpty(statusMsg))ui.Label(statusMsg,Theme.Dim,9);
            if(inRoom)
            {
                GUILayout.BeginHorizontal();
                if(ui.Btn(I18n.T("Rooms.Leave"),ui.TB3,Theme.Text)&&Enabled)LeaveRoom();
                if(ui.Btn(I18n.T("UI.Favorite"),ui.TB3,Theme.Text))AddFavorite(currentRoomCode);
                GUILayout.EndHorizontal();
            }
            ui.EndCard();
            ui.BeginCard();
            ui.Label(I18n.T("Rooms.Join"),Theme.Text,11,true);
            inputCode=ui.TextField(inputCode).Trim().ToUpperInvariant();
            if(inputCode.Length>16)inputCode=inputCode.Substring(0,16);
            GUILayout.BeginHorizontal();
            if(ui.Btn(_connecting?I18n.T("Rooms.Joining"):I18n.T("Rooms.JoinBtn"),ui.TB3,Theme.Text)&&Enabled&&!_connecting)JoinRoom(inputCode);
            if(ui.Btn(I18n.T("Rooms.Random"),ui.TB3,Theme.Text)&&Enabled&&!_connecting)JoinRoom(GenerateRoomCode());
            GUILayout.EndHorizontal();
            ui.EndCard();
            ui.Section(I18n.T("Rooms.Favorites")+" · "+favoriteRooms.Count);
            if(favoriteRooms.Count==0)ui.Label(I18n.T("Rooms.NoFavorites"),Theme.Dim,9);
            for(int i=0;i<favoriteRooms.Count;i++)
            {
                string code=favoriteRooms[i];
                bool join=ui.FavoriteRow(code,out bool remove);
                if(join&&Enabled&&!_connecting)JoinRoom(code);
                if(remove){RemoveFavorite(code);break;}
                ui.Space(4);
            }
        }
    }
}