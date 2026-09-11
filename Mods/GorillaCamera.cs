using UnityEngine;
using UnityEngine.InputSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UltimateMenu
{
    public class GorillaCamera : MonoBehaviour
    {
        public static GorillaCamera Instance { get; private set; }

        public bool Enabled => MenuUI.Instance != null && MenuUI.Instance.IsModEnabled("Camera.Title");
        private bool _wasEnabled;

        public bool Active { get; private set; }
        public bool IsPlayingKeyframes { get; private set; }
        public bool LoopKeyframes { get; private set; }
        public GameObject LckCamera => lckCamera;

        private GameObject lckCamera;
        private float rotX, rotY;
        public float speed = 5f, sens = 2f;
        private const float PITCH = 85f;

        private enum MoveDir { Forward, Back, Left, Right, Up, Down }

        // Table physique -> KeyCode "logique voulu", construite une seule fois à partir des
        // LABELS affichés par KeyLayout.Display (seule source de vérité pour ce qui est montré
        // à l'écran). On parcourt A-Z physiques, on demande à KeyLayout.Display quel label
        // chaque touche physique affiche dans le layout donné, puis on indexe par label.
        // Ainsi GorillaCamera ne peut plus diverger de MenuUI : les deux lisent la même règle.
        static Dictionary<string, KeyCode> BuildLabelToPhysical(Layout layout)
        {
            var map = new Dictionary<string, KeyCode>();
            Layout prevLayout = KeyLayout.Current;
            KeyLayout.Current = layout; // temporairement, pour interroger Display() dans ce layout
            for (KeyCode kc = KeyCode.A; kc <= KeyCode.Z; kc++)
            {
                string label = KeyLayout.Display(kc);
                if (!map.ContainsKey(label)) map[label] = kc;
            }
            KeyLayout.Current = prevLayout;
            return map;
        }

        static readonly Dictionary<Layout, Dictionary<string, KeyCode>> _labelMapCache = new Dictionary<Layout, Dictionary<string, KeyCode>>();

        static KeyCode PhysicalForLabel(string label, Layout layout)
        {
            if (!_labelMapCache.TryGetValue(layout, out var map))
            {
                map = BuildLabelToPhysical(layout);
                _labelMapCache[layout] = map;
            }
            return map.TryGetValue(label, out var kc) ? kc : KeyCode.None;
        }

        // Labels voulus par direction : Azerty -> ZQSD + A/E, Qwerty/Qwertz -> WASD + Q/E.
        static string LabelFor(MoveDir dir, Layout layout)
        {
            if (layout == Layout.Azerty)
            {
                switch (dir)
                {
                    case MoveDir.Forward: return "Z";
                    case MoveDir.Back: return "S";
                    case MoveDir.Left: return "Q";
                    case MoveDir.Right: return "D";
                    case MoveDir.Up: return "E";
                    case MoveDir.Down: return "A";
                }
            }
            else
            {
                switch (dir)
                {
                    case MoveDir.Forward: return "W";
                    case MoveDir.Back: return "S";
                    case MoveDir.Left: return "A";
                    case MoveDir.Right: return "D";
                    case MoveDir.Up: return "E";
                    case MoveDir.Down: return "Q";
                }
            }
            return "?";
        }

        static Key KeyFor(MoveDir dir, Layout layout)
        {
            string label = LabelFor(dir, layout);
            KeyCode physical = PhysicalForLabel(label, layout);
            var k = KCtoKeyStatic(physical);
            return k ?? Key.None;
        }

        static Key? KCtoKeyStatic(KeyCode kc)
        {
            if (kc >= KeyCode.A && kc <= KeyCode.Z) return Key.A + (kc - KeyCode.A);
            return null;
        }

        // Le texte affiché est désormais toujours identique au label utilisé pour choisir
        // la touche physique (LabelFor) : plus aucun risque de divergence affichage/input.
        static string DisplayFor(MoveDir dir, Layout layout) => LabelFor(dir, layout);

        public List<CamKF> Keyframes = new List<CamKF>();
        public int selectedKF = -1;

        public KeyCode keyAddKF = KeyCode.K;

        private Transform playerHead;
        private bool waitingRebind;
        private int kfIndex; private float kfT;

        private enum CamState { None, Finding, Ready }
        private CamState _state = CamState.None;

        void Awake() { Instance = this; LoadConfig(); FindPlayerHead(); }
        void Start() { MenuUI.Instance?.RegisterPage("Camera.Title", DrawPage, MenuCategory.Camera); }

        void FindPlayerHead()
        {
            var rig = GameObject.Find("Player Objects/Local VRRig/Local Gorilla Player");
            if (rig != null) playerHead = rig.transform.Find("rig/body_pivot/head_pivot/head");
        }

        void LoadConfig()
        {
            speed = PlayerPrefs.GetFloat("MUM_CamSpeed", 5f);
            sens = PlayerPrefs.GetFloat("MUM_CamSens", 2f);
            keyAddKF = GetKey("MUM_CamAddKF", KeyCode.K);
            LoopKeyframes = PlayerPrefs.GetInt("MUM_CamLoop", 0) == 1;
        }

        KeyCode GetKey(string p, KeyCode d) { try { return (KeyCode)Enum.Parse(typeof(KeyCode), PlayerPrefs.GetString(p, d.ToString())); } catch { return d; } }
        public void SetKey(string n, KeyCode k) { PlayerPrefs.SetString("MUM_" + n, k.ToString()); PlayerPrefs.Save(); LoadConfig(); }
        public void ReloadConfig() => LoadConfig();

        void SaveSpeedSens()
        {
            PlayerPrefs.SetFloat("MUM_CamSpeed", speed);
            PlayerPrefs.SetFloat("MUM_CamSens", sens);
            PlayerPrefs.Save();
        }

        void SaveLoop()
        {
            PlayerPrefs.SetInt("MUM_CamLoop", LoopKeyframes ? 1 : 0);
            PlayerPrefs.Save();
        }

        void Update()
        {
            bool nowEnabled = Enabled;
            if (nowEnabled != _wasEnabled)
            {
                _wasEnabled = nowEnabled;
                if (nowEnabled) SpawnCameraClone();
                else DestroyCameraClone();
            }

            if (Keyboard.current == null) return;
            HandleRebind();

            if (!Active) return;
            if (lckCamera == null || !lckCamera.activeInHierarchy) { Disable(); return; }
            if (IsKeyDown(keyAddKF)) AddKF();
            if (IsKeyDown(KeyCode.Delete) && selectedKF >= 0 && selectedKF < Keyframes.Count) { Keyframes.RemoveAt(selectedKF); selectedKF = -1; }
            if (IsPlayingKeyframes) { PlayKF(); return; }
            Move(Keyboard.current);
            if (Mouse.current.rightButton.isPressed) MouseLook(Mouse.current);
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f) { speed = Mathf.Clamp(speed + scroll * 0.005f, 1f, 20f); SaveSpeedSens(); }
        }

        public void SpawnCameraClone()
        {
            if (_state != CamState.None) return;
            TriggerSpawn();
        }

        public void DestroyCameraClone()
        {
            IsPlayingKeyframes = false;
            if (Active) Disable();
            CancelInvoke(nameof(DelayedFind));
            if (lckCamera != null) Destroy(lckCamera);
            lckCamera = null;
            _state = CamState.None;
        }

        void TriggerSpawn()
        {
            try
            {
                var wallGO = GameObject.Find("WallSpawner");
                if (wallGO == null) return;

                var wallComp = wallGO.GetComponents<Component>().FirstOrDefault(c => c.GetType().Name == "LckWallCameraSpawner");
                if (wallComp == null) return;

                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                var getOrCreate = wallComp.GetType().GetMethod("GetOrCreateBodyCameraSpawner", flags);
                if (getOrCreate == null) return;

                var bodySpawner = getOrCreate.Invoke(wallComp, null);
                if (bodySpawner == null) return;

                var spawnMethod = bodySpawner.GetType().GetMethod("SpawnCamera", flags);
                if (spawnMethod == null) return;

                try { spawnMethod.Invoke(bodySpawner, new object[spawnMethod.GetParameters().Length]); } catch { }
                _state = CamState.Finding;
                Invoke(nameof(DelayedFind), 0.5f);
            }
            catch (Exception e) { Log.Err("GorillaCamera.TriggerSpawn", e); }
        }

        void DelayedFind()
        {
            FindCamera();
            if (_state == CamState.Finding)
                Invoke(nameof(DelayedFind), 0.5f);
        }

        void FindCamera()
        {
            var found = GameObject.Find("LCKTablet(Clone)");
            if (found != null) { GrabCamera(found); return; }
        }

        void GrabCamera(GameObject go)
        {
            CancelInvoke(nameof(DelayedFind));
            lckCamera = go;
            lckCamera.transform.SetParent(null);
            MoveToPlayer();
            _state = CamState.Ready;
            Enable();
        }

        void MoveToPlayer()
        {
            if (lckCamera == null) return;
            if (playerHead != null) { lckCamera.transform.position = playerHead.position + playerHead.forward * 1.5f; lckCamera.transform.rotation = Quaternion.LookRotation(-playerHead.forward, Vector3.up); }
            else { var cam = Camera.main; if (cam != null) { lckCamera.transform.position = cam.transform.position + cam.transform.forward * 1.5f; lckCamera.transform.rotation = Quaternion.LookRotation(-cam.transform.forward, Vector3.up); } }
        }

        // Conversions locales complètes (F1-F12, ponctuation, flèches, etc.),
        // équivalentes à celles de MenuUI mais indépendantes pour ne pas dépendre
        // de leur protection level.
        Key? KCtoKey(KeyCode kc)
        {
            if (kc >= KeyCode.A && kc <= KeyCode.Z) return Key.A + (kc - KeyCode.A);
            if (kc >= KeyCode.Alpha0 && kc <= KeyCode.Alpha9) return Key.Digit0 + (kc - KeyCode.Alpha0);
            if (kc >= KeyCode.F1 && kc <= KeyCode.F12) return Key.F1 + (kc - KeyCode.F1);
            if (kc >= KeyCode.Keypad0 && kc <= KeyCode.Keypad9) return Key.Numpad0 + (kc - KeyCode.Keypad0);
            switch (kc)
            {
                case KeyCode.Space: return Key.Space;
                case KeyCode.Return: return Key.Enter;
                case KeyCode.Escape: return Key.Escape;
                case KeyCode.Tab: return Key.Tab;
                case KeyCode.Backspace: return Key.Backspace;
                case KeyCode.Delete: return Key.Delete;
                case KeyCode.LeftShift: return Key.LeftShift;
                case KeyCode.RightShift: return Key.RightShift;
                case KeyCode.LeftControl: return Key.LeftCtrl;
                case KeyCode.RightControl: return Key.RightCtrl;
                case KeyCode.LeftAlt: return Key.LeftAlt;
                case KeyCode.RightAlt: return Key.RightAlt;
                case KeyCode.UpArrow: return Key.UpArrow;
                case KeyCode.DownArrow: return Key.DownArrow;
                case KeyCode.LeftArrow: return Key.LeftArrow;
                case KeyCode.RightArrow: return Key.RightArrow;
                case KeyCode.CapsLock: return Key.CapsLock;
                case KeyCode.Comma: return Key.Comma;
                case KeyCode.Period: return Key.Period;
                case KeyCode.Slash: return Key.Slash;
                case KeyCode.Semicolon: return Key.Semicolon;
                case KeyCode.Quote: return Key.Quote;
                case KeyCode.LeftBracket: return Key.LeftBracket;
                case KeyCode.RightBracket: return Key.RightBracket;
                case KeyCode.Backslash: return Key.Backslash;
                case KeyCode.Minus: return Key.Minus;
                case KeyCode.Equals: return Key.Equals;
                case KeyCode.BackQuote: return Key.Backquote;
                default: return null;
            }
        }

        KeyCode KeyToKC(Key key)
        {
            if (key >= Key.A && key <= Key.Z) return KeyCode.A + (key - Key.A);
            if (key >= Key.Digit0 && key <= Key.Digit9) return KeyCode.Alpha0 + (key - Key.Digit0);
            if (key >= Key.F1 && key <= Key.F12) return KeyCode.F1 + (key - Key.F1);
            if (key >= Key.Numpad0 && key <= Key.Numpad9) return KeyCode.Keypad0 + (key - Key.Numpad0);
            switch (key)
            {
                case Key.Space: return KeyCode.Space;
                case Key.Enter: case Key.NumpadEnter: return KeyCode.Return;
                case Key.Escape: return KeyCode.Escape;
                case Key.Tab: return KeyCode.Tab;
                case Key.Backspace: return KeyCode.Backspace;
                case Key.Delete: return KeyCode.Delete;
                case Key.LeftShift: return KeyCode.LeftShift;
                case Key.RightShift: return KeyCode.RightShift;
                case Key.LeftCtrl: return KeyCode.LeftControl;
                case Key.RightCtrl: return KeyCode.RightControl;
                case Key.LeftAlt: return KeyCode.LeftAlt;
                case Key.RightAlt: return KeyCode.RightAlt;
                case Key.UpArrow: return KeyCode.UpArrow;
                case Key.DownArrow: return KeyCode.DownArrow;
                case Key.LeftArrow: return KeyCode.LeftArrow;
                case Key.RightArrow: return KeyCode.RightArrow;
                case Key.CapsLock: return KeyCode.CapsLock;
                case Key.Comma: return KeyCode.Comma;
                case Key.Period: return KeyCode.Period;
                case Key.Slash: return KeyCode.Slash;
                case Key.Semicolon: return KeyCode.Semicolon;
                case Key.Quote: return KeyCode.Quote;
                case Key.LeftBracket: return KeyCode.LeftBracket;
                case Key.RightBracket: return KeyCode.RightBracket;
                case Key.Backslash: return KeyCode.Backslash;
                case Key.Minus: return KeyCode.Minus;
                case Key.Equals: return KeyCode.Equals;
                case Key.Backquote: return KeyCode.BackQuote;
                default: return KeyCode.None;
            }
        }

        bool IsKeyDown(KeyCode kc) { var kb = Keyboard.current; if (kb == null) return false; var k = KCtoKey(kc); return k.HasValue && kb[k.Value].wasPressedThisFrame; }

        string _currentRebindTarget = "";
        public void StartLocalRebind(string t) { waitingRebind = true; _currentRebindTarget = t; }

        void HandleRebind()
        {
            if (!waitingRebind || Keyboard.current == null) return;
            foreach (var key in Keyboard.current.allKeys)
                if (key.wasPressedThisFrame) { KeyCode kc = KeyToKC(key.keyCode); if (kc != KeyCode.None && kc != KeyCode.Escape) { SetKey(_currentRebindTarget, kc); waitingRebind = false; _currentRebindTarget = ""; return; } }
            if (Keyboard.current.escapeKey.wasPressedThisFrame) { waitingRebind = false; _currentRebindTarget = ""; }
        }

        void Move(Keyboard kb)
        {
            if (lckCamera == null || kb == null) return;
            var t = lckCamera.transform; var d = Vector3.zero;
            Layout layout = KeyLayout.Current;

            Key fwd = KeyFor(MoveDir.Forward, layout);
            Key bck = KeyFor(MoveDir.Back, layout);
            Key lft = KeyFor(MoveDir.Left, layout);
            Key rgt = KeyFor(MoveDir.Right, layout);
            Key up = KeyFor(MoveDir.Up, layout);
            Key dwn = KeyFor(MoveDir.Down, layout);

            // Nouveau Input System : kb[Key] est basé sur la position physique de la touche
            // (indépendant du layout OS), donc le mapping ZQSD/WASD choisi dans le menu
            // correspond bien aux touches physiquement pressées, quel que soit le clavier système.
            if (fwd != Key.None && kb[fwd].isPressed) d += t.forward;
            if (bck != Key.None && kb[bck].isPressed) d -= t.forward;
            if (lft != Key.None && kb[lft].isPressed) d -= t.right;
            if (rgt != Key.None && kb[rgt].isPressed) d += t.right;
            if (up != Key.None && kb[up].isPressed) d += Vector3.up;
            if (dwn != Key.None && kb[dwn].isPressed) d -= Vector3.up;
            if (kb.leftShiftKey.isPressed) d *= 2f;
            if (kb.leftCtrlKey.isPressed) d *= 0.5f;
            t.position += d * speed * Time.deltaTime;
        }

        void MouseLook(Mouse m)
        {
            if (lckCamera == null) return;
            var delta = m.delta.ReadValue();
            rotY += delta.x * sens * 0.1f;
            rotX = Mathf.Clamp(rotX - delta.y * sens * 0.1f, -PITCH, PITCH);
            lckCamera.transform.rotation = Quaternion.Euler(rotX, rotY, 0f);
        }

        public void Enable()
        {
            if (lckCamera == null || !lckCamera.activeInHierarchy) { _state = CamState.None; return; }
            rotX = lckCamera.transform.eulerAngles.x; rotY = lckCamera.transform.eulerAngles.y;
            Cursor.lockState = CursorLockMode.Confined; Cursor.visible = true; Active = true;
        }

        public void Disable()
        {
            IsPlayingKeyframes = false;
            if (lckCamera != null && playerHead != null) { lckCamera.transform.position = playerHead.position + playerHead.forward * -1f + Vector3.up * 0.2f; lckCamera.transform.rotation = playerHead.rotation; }
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true; Active = false;
            if (lckCamera != null && lckCamera.activeInHierarchy) _state = CamState.Ready;
            else { lckCamera = null; _state = CamState.None; }
        }

        public void TeleportCamera(Vector3 pos, Quaternion rot) { if (lckCamera == null) return; lckCamera.transform.position = pos; lckCamera.transform.rotation = rot; rotX = rot.eulerAngles.x; rotY = rot.eulerAngles.y; }

        public void AddKF()
        {
            if (lckCamera == null) return;
            var kf = new CamKF(lckCamera.transform.position, lckCamera.transform.rotation, "KF " + (Keyframes.Count + 1));
            if (Keyframes.Count > 0) { var prev = Keyframes[^1]; var tang = (kf.Pos - prev.Pos) * 0.33f; prev.TOut = tang; kf.TIn = -tang; }
            Keyframes.Add(kf); selectedKF = Keyframes.Count - 1;
        }

        public void StartPlay() { if (Keyframes.Count >= 2) { kfIndex = 0; kfT = 0f; IsPlayingKeyframes = true; } }
        public void StopPlay() => IsPlayingKeyframes = false;

        public void SetLoop(bool loop) { LoopKeyframes = loop; SaveLoop(); }

        void PlayKF()
        {
            if (kfIndex >= Keyframes.Count - 1)
            {
                if (LoopKeyframes && Keyframes.Count >= 2) { kfIndex = 0; kfT = 0f; }
                else { IsPlayingKeyframes = false; return; }
            }
            var f = Keyframes[kfIndex]; var t = Keyframes[kfIndex + 1];
            kfT += Time.deltaTime / Mathf.Max(0.01f, t.Duration);
            float e = Mathf.Clamp01(kfT), et = t.Ease == EaseT.Smooth ? Mathf.SmoothStep(0f, 1f, e) : e;
            lckCamera.transform.position = t.Ease == EaseT.Bezier ? Bezier(f.Pos, f.Pos + f.TOut, t.Pos + t.TIn, t.Pos, et) : Vector3.Lerp(f.Pos, t.Pos, et);
            lckCamera.transform.rotation = Quaternion.Slerp(f.Rot, t.Rot, et);
            if (kfT >= 1f) { kfIndex++; kfT = 0f; }
        }

        Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t) { float u = 1f - t; return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3; }

        void DrawPage(MenuUI ui)
        {
            if (Active && (lckCamera == null || !lckCamera.activeInHierarchy)) Disable();

            ui.Section(I18n.T("Camera.Title"));
            Layout layout = KeyLayout.Current;

            ui.Space(6);
            ui.BeginCard();
            float newSpeed = ui.Slider(I18n.T("Camera.Speed"), speed, 1f, 20f, "0.0");
            float newSens = ui.Slider(I18n.T("Camera.Sensitivity"), sens, 0.5f, 10f, "0.0");
            if (Enabled && (!Mathf.Approximately(newSpeed, speed) || !Mathf.Approximately(newSens, sens)))
            {
                speed = newSpeed; sens = newSens; SaveSpeedSens();
            }
            ui.Space(6); ui.Separator(); ui.Space(6);

            ui.Label(
                I18n.T("Camera.MoveKeysLabel") + "  " +
                DisplayFor(MoveDir.Forward, layout) + " " +
                DisplayFor(MoveDir.Left, layout) + " " +
                DisplayFor(MoveDir.Back, layout) + " " +
                DisplayFor(MoveDir.Right, layout) + "  |  " +
                DisplayFor(MoveDir.Up, layout) + "/" +
                DisplayFor(MoveDir.Down, layout) + " " + I18n.T("Camera.UpDownSuffix"),
                Theme.Dim, 9);
            ui.Space(4);

            if (waitingRebind)
            {
                ui.Label(I18n.T("Camera.WaitingKey"), Theme.Accent, 10, true);
                ui.Label(I18n.T("Camera.EscapeCancel"), Theme.Dim, 8);
            }
            else
            {
                if (ui.RebindBtn(I18n.T("Camera.AddKF"), keyAddKF) && Enabled) StartLocalRebind("CamAddKF");
            }
            ui.EndCard();

            ui.Space(6); ui.Section(I18n.T("Camera.Keyframes") + " (" + Keyframes.Count + ")");
            ui.BeginCard();
            if (Keyframes.Count == 0) ui.Label(I18n.T("Camera.NoKeyframes"), Theme.Dim, 9);
            else
            {
                for (int i = 0; i < Keyframes.Count; i++)
                {
                    var kf = Keyframes[i];
                    bool sel = selectedKF == i;
                    GUILayout.BeginHorizontal();
                    if (ui.Btn((sel ? "> " : "   ") + kf.Label + " - " + kf.Duration.ToString("F1") + "s - " + kf.Ease,
                                sel ? ui.TA : ui.TB3, sel ? Color.white : Theme.Text) && Enabled)
                        selectedKF = i;
                    GUILayout.Space(4);
                    if (ui.SmallBtn(I18n.T("Camera.Go"), ui.TB3, Theme.Accent) && Enabled) TeleportCamera(kf.Pos, kf.Rot);
                    if (ui.SmallBtn("X", ui.TR, Color.white) && Enabled) { Keyframes.RemoveAt(i); if (selectedKF >= i) selectedKF--; break; }
                    GUILayout.EndHorizontal();

                    if (sel)
                    {
                        ui.Space(2);
                        float newDur = ui.Slider(I18n.T("Camera.Duration"), kf.Duration, 0.5f, 10f, "0.0", 70f);
                        if (Enabled && !Mathf.Approximately(newDur, kf.Duration)) kf.Duration = newDur;
                        GUILayout.BeginHorizontal();
                        ui.Label(I18n.T("Camera.Mode"), Theme.Dim, 9, false, TextAnchor.MiddleLeft);
                        GUILayout.Space(4);
                        string[] modes = { "Linear", "Smooth", "Bezier" };
                        if (ui.SmallBtn(modes[(int)kf.Ease], ui.TB3, Theme.Text) && Enabled) kf.Ease = (EaseT)(((int)kf.Ease + 1) % 3);
                        GUILayout.EndHorizontal();
                    }
                    ui.Space(4);
                }
            }
            ui.Space(4);
            if (ui.Btn(I18n.T("Camera.AddKF"), ui.TB3, Theme.Text) && Enabled) AddKF();
            ui.EndCard();

            ui.Space(6);
            ui.BeginCard();
            bool newLoop = ui.Toggle(I18n.T("Camera.Loop"), LoopKeyframes, 140f);
            if (Enabled && newLoop != LoopKeyframes) SetLoop(newLoop);
            ui.Space(6);
            if (ui.Btn(IsPlayingKeyframes ? I18n.T("Camera.Stop") : I18n.T("Camera.Start"),
                        IsPlayingKeyframes ? ui.TR : ui.TA, Color.white) && Enabled)
            { if (IsPlayingKeyframes) StopPlay(); else StartPlay(); }
            ui.EndCard();
        }

        void OnDestroy() { if (Active) Disable(); }
    }

    public class CamKF
    {
        public Vector3 Pos, TIn, TOut;
        public Quaternion Rot;
        public string Label;
        public float Duration = 2f;
        public EaseT Ease = EaseT.Smooth;
        public CamKF(Vector3 p, Quaternion r, string l) { Pos = p; Rot = r; Label = l; }
    }

    public enum EaseT { Linear, Smooth, Bezier }
}