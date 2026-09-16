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

        public bool Enabled => MenuUI.Instance != null && MenuUI.Instance.IsModEnabled("Rooms.Title");

        void Awake() { Instance = this; LoadFavorites(); }
        void Start() { Invoke(nameof(Init), 3f); }

        void Update()
        {
            if (!pageRegistered && regTries++ % 60 == 0 && regTries < 600 && MenuUI.Instance != null) { MenuUI.Instance.RegisterPage("Rooms.Title", DrawPage, MenuCategory.Gameplay); pageRegistered = true; }
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
            string raw = PlayerPrefs.GetString("MUM_FavRooms", "");
            if (!string.IsNullOrEmpty(raw)) favoriteRooms.AddRange(raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            favoriteRooms.Sort(StringComparer.OrdinalIgnoreCase);
        }

        void SaveFavorites() { favoriteRooms.Sort(StringComparer.OrdinalIgnoreCase); PlayerPrefs.SetString("MUM_FavRooms", string.Join(",", favoriteRooms)); PlayerPrefs.Save(); }
        void AddFavorite(string code) { code = code.Trim().ToUpper(); if (!string.IsNullOrEmpty(code) && !favoriteRooms.Contains(code)) { favoriteRooms.Add(code); SaveFavorites(); } }
        void RemoveFavorite(string code) { if (favoriteRooms.Remove(code)) SaveFavorites(); }

        void DrawPage(MenuUI ui)
        {
            ui.Section(I18n.T("Rooms.Title"));
            bool inRoom = !string.IsNullOrEmpty(currentRoomCode) && currentRoomCode != I18n.T("Rooms.Menu") && currentRoomCode != "Menu";

            ui.BeginCard();
            GUILayout.BeginHorizontal();
            ui.StatusPill(inRoom ? currentRoomCode : I18n.T("Rooms.Menu"), inRoom);
            if (inRoom)
            {
                GUILayout.FlexibleSpace();
                if (!favoriteRooms.Contains(currentRoomCode.Trim().ToUpper()) && ui.PlusBtn() && Enabled) AddFavorite(currentRoomCode);
            }
            GUILayout.EndHorizontal();
            if (statusTimer > 0f && !string.IsNullOrEmpty(statusMsg)) { ui.Space(4); ui.Label(statusMsg, Theme.Accent, 9, true); }
            if (!Enabled) { ui.Space(4); ui.Label(I18n.T("Rooms.DisabledHint"), Theme.Dim, 9); }
            ui.EndCard();

            ui.Space(6); ui.Section(I18n.T("Rooms.Join"));
            ui.BeginCard();
            GUILayout.BeginHorizontal();
            string newInputCode = ui.TextField(inputCode.ToUpper(), 110f);
            if (Enabled) inputCode = newInputCode;
            GUILayout.Space(6);

            if (ui.Btn(_connecting ? I18n.T("Rooms.Joining") : (inRoom ? I18n.T("Rooms.Leave") : I18n.T("Rooms.JoinBtn")), inRoom ? ui.TR : ui.TA, Color.white) && Enabled && !_connecting)
            {
                if (inRoom) LeaveRoom(); else JoinRoom(inputCode);
            }

            GUILayout.Space(6);
            if (ui.Btn(I18n.T("Rooms.Random"), ui.TB3, Theme.Accent) && Enabled) JoinRoom(GenerateRoomCode());
            GUILayout.EndHorizontal();
            ui.EndCard();

            ui.Space(6); ui.Section(I18n.T("Rooms.Favorites") + " (" + favoriteRooms.Count + ")");
            ui.BeginCard();
            if (favoriteRooms.Count == 0) ui.Label(I18n.T("Rooms.NoFavorites"), Theme.Dim, 9);
            else foreach (var fav in new List<string>(favoriteRooms)) DrawFavoriteRow(ui, fav);
            ui.EndCard();
        }

        void DrawFavoriteRow(MenuUI ui, string fav)
        {
            GUILayout.BeginHorizontal();
            float btnD = MenuUI.StandardRoundBtnSize + 6f * UI.Scale, btnRightMargin = 10f * UI.Scale;

            Rect rowRect = GUILayoutUtility.GetRect(new GUIContent(fav),
                new GUIStyle(GUI.skin.button) { fontSize = UI.Px(11), fontStyle = Theme.BoldText ? FontStyle.Bold : FontStyle.Normal, font = UI.BodyFont, padding = new RectOffset(14, 14, 9, 9), wordWrap = true },
                GUILayout.ExpandWidth(true), GUILayout.MinHeight(34f * UI.Scale));

            bool hover = Event.current != null && rowRect.Contains(Event.current.mousePosition);
            UI.DrawRounded(rowRect, hover ? new Color(Theme.BG3.r * 0.82f, Theme.BG3.g * 0.82f, Theme.BG3.b * 0.82f, Theme.BG3.a) : Theme.BG3, Mathf.RoundToInt(12 * UI.Scale));

            GUI.Label(rowRect, fav, new GUIStyle(GUI.skin.label) { fontSize = UI.Px(11), fontStyle = Theme.BoldText ? FontStyle.Bold : FontStyle.Normal, font = UI.BodyFont, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(14, Mathf.RoundToInt(btnD + btnRightMargin + 6f * UI.Scale), 9, 9), wordWrap = true, normal = { textColor = Theme.Text } });

            Rect btnRect = new Rect(rowRect.x + rowRect.width - btnD - btnRightMargin, rowRect.y + (rowRect.height - btnD) * 0.5f, btnD, btnD);
            Color btnCol = Event.current != null && btnRect.Contains(Event.current.mousePosition) ? new Color(Theme.Red.r * 0.86f, Theme.Red.g * 0.86f, Theme.Red.b * 0.86f, Theme.Red.a) : Theme.Red;

            UI.DrawShadow(btnRect, radius: Mathf.RoundToInt(btnD * 0.5f), offsetY: 1.2f * UI.Scale, strength: 0.20f, blur: 1f);
            float outerBorder = Mathf.Max(1f, 1.6f * UI.Scale);
            UI.DrawRounded(new Rect(btnRect.x - outerBorder, btnRect.y - outerBorder, btnRect.width + outerBorder * 2f, btnRect.height + outerBorder * 2f), new Color(1f, 1f, 1f, 0.18f), Mathf.RoundToInt((btnRect.width + outerBorder * 2f) * 0.5f));
            float innerBorder = Mathf.Max(1f, 1f * UI.Scale);
            UI.DrawRounded(new Rect(btnRect.x - innerBorder, btnRect.y - innerBorder, btnRect.width + innerBorder * 2f, btnRect.height + innerBorder * 2f), new Color(0f, 0f, 0f, 0.35f), Mathf.RoundToInt((btnRect.width + innerBorder * 2f) * 0.5f));
            UI.DrawRounded(btnRect, btnCol, Mathf.RoundToInt(btnD * 0.5f));

            bool deleteClicked = MenuUI.ControlButton(btnRect, "", GUIStyle.none) && Enabled;
            if (deleteClicked) { SoundFX.PlaySound("Grave"); RemoveFavorite(fav); }

            if (MenuUI.ControlButton(rowRect, "", GUIStyle.none) && Enabled && !deleteClicked && (MenuUI.IsVrActivation || Event.current == null || !btnRect.Contains(Event.current.mousePosition))) { SoundFX.PlaySound("Moyen"); JoinRoom(fav); }

            GUILayout.EndHorizontal();
            ui.Space(4);
        }
    }
}