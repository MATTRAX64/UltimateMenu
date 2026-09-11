using UnityEngine;
using System;
using System.Reflection;
using System.Collections.Generic;

namespace UltimateMenu
{
    public class GorillaRooms : MonoBehaviour
    {
        public static GorillaRooms? Instance;

        private string currentRoomCode = "", inputCode = "", statusMsg = "";
        private float refreshTimer, statusTimer;
        private bool pageRegistered; private int regTries;
        private List<string> favoriteRooms = new List<string>();

        private object? _controller; private System.Type? _controllerType, _photonType;
        private MethodInfo? _joinMethod; private object? _joinTypeVal;

        // Etat du bouton principal pendant une connexion en cours (pour afficher "Rejoin...").
        private bool _connecting;
        private float _connectingTimeout;

        /// <summary>
        /// Le mod est actif uniquement via le switch global de la page Mods (liste des mods).
        /// Quand désactivé, la page reste visible mais tous les contrôles sont bloqués (GUI.enabled = false).
        /// </summary>
        public bool Enabled => MenuUI.Instance != null && MenuUI.Instance.IsModEnabled("Rooms.Title");

        void Awake() { Instance = this; LoadFavorites(); }
        void Start() { Invoke(nameof(Init), 3f); }

        void Update()
        {
            if (!pageRegistered && regTries++ % 60 == 0 && regTries < 600) TryRegister();
            if ((refreshTimer += Time.deltaTime) >= 2f) { refreshTimer = 0f; currentRoomCode = GetCurrentRoom(); }
            if (statusTimer > 0f) statusTimer -= Time.deltaTime;

            // Sort de l'état "connexion en cours" dès qu'on est effectivement en room,
            // ou après un timeout de sécurité si la connexion échoue silencieusement.
            if (_connecting)
            {
                bool nowInRoom = !string.IsNullOrEmpty(currentRoomCode) && currentRoomCode != I18n.T("Rooms.Menu") && currentRoomCode != "Menu";
                if (nowInRoom) { _connecting = false; }
                else if ((_connectingTimeout -= Time.deltaTime) <= 0f) { _connecting = false; }
            }
        }

        void TryRegister() { if (!pageRegistered && MenuUI.Instance != null) { MenuUI.Instance.RegisterPage("Rooms.Title", DrawPage, MenuCategory.Gameplay); pageRegistered = true; } }

        void Init()
        {
            var go = GameObject.Find("Networking Scripts/Photon Manager");
            if (go != null)
                foreach (var c in go.GetComponents<Component>())
                    if (c.GetType().Name == "PhotonNetworkController") { _controller = c; _controllerType = c.GetType(); break; }

            if (_controller == null)
                foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                    if (mb.GetType().Name == "PhotonNetworkController") { _controller = mb; _controllerType = mb.GetType(); break; }

            if (_controllerType == null) { SetStatus("PhotonNetworkController introuvable"); return; }

            foreach (var m in _controllerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                if (m.Name == "AttemptToAutoJoinSpecificRoom") { _joinMethod = m; break; }

            if (_joinMethod != null)
            {
                var prms = _joinMethod.GetParameters();
                if (prms.Length >= 2 && prms[1].ParameterType.IsEnum)
                    _joinTypeVal = Enum.GetValues(prms[1].ParameterType).GetValue(0);
            }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                foreach (var t in asm.GetTypes())
                    if (t.Name == "PhotonNetwork") { _photonType = t; break; }

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

        void JoinRoom(string code)
        {
            if (string.IsNullOrEmpty(code) || _joinMethod == null || _controller == null) { SetStatus("Non initialise"); return; }
            code = code.Trim().ToUpper();
            try
            {
                var args = new object[_joinMethod.GetParameters().Length];
                args[0] = code;
                if (args.Length >= 2) args[1] = _joinTypeVal;
                _joinMethod.Invoke(_controller, args);
                _connecting = true;
                _connectingTimeout = 8f;
                SetStatus("Connexion a " + code + "...");
            }
            catch (Exception e) { SetStatus("Erreur : " + e.Message); }
        }

        void LeaveRoom()
        {
            if (_photonType == null) return;
            try { foreach (var m in _photonType.GetMethods(BindingFlags.Public | BindingFlags.Static)) if (m.Name == "LeaveRoom") { m.Invoke(null, new object[m.GetParameters().Length]); SetStatus(I18n.T("Rooms.Leaving")); return; } }
            catch (Exception e) { SetStatus("Erreur : " + e.Message); }
        }

        // Le préfixe pour l'aléatoire est directement le contenu du champ unique inputCode.
        // Si le champ est vide, on génère juste 4 chiffres sans préfixe forcé.
        string GenerateRoomCode()
        {
            string p = string.IsNullOrEmpty(inputCode) ? "" : inputCode.Trim().ToUpper();
            return p + UnityEngine.Random.Range(1000, 9999);
        }

        void SetStatus(string msg) { statusMsg = msg; statusTimer = 4f; }

        void LoadFavorites()
        {
            favoriteRooms.Clear();
            string raw = PlayerPrefs.GetString("MUM_FavRooms", "");
            if (!string.IsNullOrEmpty(raw)) favoriteRooms.AddRange(raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            favoriteRooms.Sort(StringComparer.OrdinalIgnoreCase);
        }

        void SaveFavorites()
        {
            favoriteRooms.Sort(StringComparer.OrdinalIgnoreCase);
            PlayerPrefs.SetString("MUM_FavRooms", string.Join(",", favoriteRooms));
            PlayerPrefs.Save();
        }

        void AddFavorite(string code) { code = code.Trim().ToUpper(); if (!string.IsNullOrEmpty(code) && !favoriteRooms.Contains(code)) { favoriteRooms.Add(code); SaveFavorites(); } }
        void RemoveFavorite(string code) { if (favoriteRooms.Remove(code)) SaveFavorites(); }

        void DrawPage(MenuUI ui)
        {
            ui.Section(I18n.T("Rooms.Title"));

            bool inRoom = !string.IsNullOrEmpty(currentRoomCode) && currentRoomCode != I18n.T("Rooms.Menu") && currentRoomCode != "Menu";

            // --- Statut room actuelle : code à gauche, bouton rond Favori à droite (visible seulement si en room) ---
            ui.BeginCard();
            GUILayout.BeginHorizontal();
            ui.StatusPill(inRoom ? currentRoomCode : I18n.T("Rooms.Menu"), inRoom);
            if (inRoom)
            {
                GUILayout.FlexibleSpace();
                bool alreadyFavCurrent = favoriteRooms.Contains(currentRoomCode.Trim().ToUpper());
                if (!alreadyFavCurrent)
                {
                    if (ui.PlusBtn() && Enabled) AddFavorite(currentRoomCode);
                }
            }
            GUILayout.EndHorizontal();
            if (statusTimer > 0f && !string.IsNullOrEmpty(statusMsg))
            {
                ui.Space(4);
                ui.Label(statusMsg, Theme.Accent, 9, true);
            }
            if (!Enabled)
            {
                ui.Space(4);
                ui.Label(I18n.T("Rooms.DisabledHint"), Theme.Dim, 9);
            }
            ui.EndCard();

            // --- Carte unique : un seul champ (code / préfixe) + bouton principal (3 états) + Aléatoire ---
            // Le champ sert à la fois de code de room à rejoindre et de préfixe pour l'aléatoire,
            // ça évite d'avoir deux inputs qui font quasi la même chose.
            ui.Space(6); ui.Section(I18n.T("Rooms.Join"));
            ui.BeginCard();

            GUILayout.BeginHorizontal();
            string newInputCode = ui.TextField(inputCode.ToUpper(), 110f);
            if (Enabled) inputCode = newInputCode;
            GUILayout.Space(6);

            // Bouton principal à 3 états :
            // - en connexion -> "Rejoin..." (accent, reste cliquable mais no-op)
            // - en room -> "Quitter" (rouge) -> LeaveRoom()
            // - sinon -> "Rejoindre" (accent) -> JoinRoom(inputCode)
            string mainLabel = _connecting ? I18n.T("Rooms.Joining") : (inRoom ? I18n.T("Rooms.Leave") : I18n.T("Rooms.JoinBtn"));
            Texture2D mainBg = inRoom ? ui.TR : ui.TA;
            if (ui.Btn(mainLabel, mainBg, Color.white) && Enabled)
            {
                if (_connecting) { /* connexion en cours, ignore */ }
                else if (inRoom) LeaveRoom();
                else JoinRoom(inputCode);
            }

            GUILayout.Space(6);

            // Aléatoire : utilise le texte du champ comme préfixe pour générer un code, sans modifier le champ
            // (sinon en cliquant plusieurs fois de suite les chiffres s'accumuleraient sur le préfixe).
            if (ui.Btn(I18n.T("Rooms.Random"), ui.TB3, Theme.Accent) && Enabled)
            {
                string g = GenerateRoomCode();
                JoinRoom(g);
            }
            GUILayout.EndHorizontal();
            ui.EndCard();

            // --- Favoris : triés A→Z, suppression via le vrai bouton rond rouge du menu (MinusBtn) ---
            ui.Space(6); ui.Section(I18n.T("Rooms.Favorites") + " (" + favoriteRooms.Count + ")");
            ui.BeginCard();
            if (favoriteRooms.Count == 0)
            {
                ui.Label(I18n.T("Rooms.NoFavorites"), Theme.Dim, 9);
            }
            else
            {
                foreach (var fav in new List<string>(favoriteRooms))
                    DrawFavoriteRow(ui, fav);
            }
            ui.EndCard();
        }

        /// <summary>
        /// Row de favori : un seul rect de fond avec hover, le nom de la salle à gauche,
        /// le bouton rond rouge standard du menu (MinusBtn) en overlay à droite pour supprimer,
        /// et un clic sur le reste de la row rejoint directement la salle.
        /// </summary>
        void DrawFavoriteRow(MenuUI ui, string fav)
        {
            GUILayout.BeginHorizontal();

            float rowH = 34f * UI.Scale;
            float btnD = MenuUI.StandardRoundBtnSize + 6f * UI.Scale;
            float btnRightMargin = 10f * UI.Scale;
            float rightReserved = btnD + btnRightMargin;

            Rect rowRect = GUILayoutUtility.GetRect(new GUIContent(fav),
                new GUIStyle(GUI.skin.button)
                {
                    fontSize = UI.Px(11),
                    fontStyle = Theme.BoldText ? FontStyle.Bold : FontStyle.Normal,
                    font = UI.BodyFont,
                    padding = new RectOffset(14, 14, 9, 9),
                    wordWrap = true
                }, GUILayout.ExpandWidth(true), GUILayout.MinHeight(rowH));

            bool hover = Event.current != null && rowRect.Contains(Event.current.mousePosition);
            Color rowBg = Theme.BG3;
            if (hover) rowBg = new Color(rowBg.r * 0.82f, rowBg.g * 0.82f, rowBg.b * 0.82f, rowBg.a);
            UI.DrawRounded(rowRect, rowBg, Mathf.RoundToInt(12 * UI.Scale));

            var rowStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = UI.Px(11),
                fontStyle = Theme.BoldText ? FontStyle.Bold : FontStyle.Normal,
                font = UI.BodyFont,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(14, Mathf.RoundToInt(rightReserved + 6f * UI.Scale), 9, 9),
                wordWrap = true,
                normal = { textColor = Theme.Text }
            };
            GUI.Label(rowRect, fav, rowStyle);

            // Bouton rond rouge en overlay, positionné manuellement pour rester dans la row,
            // mais dessiné/géré par RoundBtn (même ombre/contour/son que le reste du menu).
            Rect btnRect = new Rect(
                rowRect.x + rowRect.width - btnD - btnRightMargin,
                rowRect.y + (rowRect.height - btnD) * 0.5f,
                btnD, btnD);

            bool btnHover = Event.current != null && btnRect.Contains(Event.current.mousePosition);
            Color btnCol = btnHover ? new Color(Theme.Red.r * 0.86f, Theme.Red.g * 0.86f, Theme.Red.b * 0.86f, Theme.Red.a) : Theme.Red;

            UI.DrawShadow(btnRect, radius: Mathf.RoundToInt(btnD * 0.5f), offsetY: 1.2f * UI.Scale, strength: 0.20f, blur: 1f);
            float outerBorder = Mathf.Max(1f, 1.6f * UI.Scale);
            Rect outerRect = new Rect(btnRect.x - outerBorder, btnRect.y - outerBorder, btnRect.width + outerBorder * 2f, btnRect.height + outerBorder * 2f);
            UI.DrawRounded(outerRect, new Color(1f, 1f, 1f, 0.18f), Mathf.RoundToInt(outerRect.width * 0.5f));
            float innerBorder = Mathf.Max(1f, 1f * UI.Scale);
            Rect innerRect = new Rect(btnRect.x - innerBorder, btnRect.y - innerBorder, btnRect.width + innerBorder * 2f, btnRect.height + innerBorder * 2f);
            UI.DrawRounded(innerRect, new Color(0f, 0f, 0f, 0.35f), Mathf.RoundToInt(innerRect.width * 0.5f));
            UI.DrawRounded(btnRect, btnCol, Mathf.RoundToInt(btnD * 0.5f));

            bool deleteClicked = GUI.Button(btnRect, "", GUIStyle.none) && Enabled;
            if (deleteClicked) { SoundFX.PlaySound("Grave"); RemoveFavorite(fav); }

            bool rowClicked = GUI.Button(rowRect, "", GUIStyle.none) && Enabled;
            if (rowClicked && !deleteClicked &&
                (Event.current == null || !btnRect.Contains(Event.current.mousePosition)))
            {
                SoundFX.PlaySound("Moyen");
                JoinRoom(fav);
            }

            GUILayout.EndHorizontal();
            ui.Space(4);
        }
    }
}