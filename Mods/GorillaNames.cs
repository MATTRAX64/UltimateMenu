using UltimateMenu;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UltimateMenu
{
    public class GorillaNames : MonoBehaviour
    {
        public static GorillaNames Instance { get; private set; }

        /// <summary>
        /// Le mod est actif uniquement via le switch global de la page Mods (liste des mods).
        /// Dès qu'il est activé, l'auto-application du pseudo par tenue tourne automatiquement
        /// (voir Update) — pas de toggle séparé dans cette page, juste un statut affiché.
        /// </summary>
        public bool Enabled => MenuUI.Instance != null && MenuUI.Instance.IsModEnabled("Names.Title");

        readonly Dictionary<string, string> saved = new Dictionary<string, string>();
        object outfitTMP, nameTMP;
        PropertyInfo outfitProp, nameProp;
        string currentOutfit = "";

        string lastWrittenText = "";
        string lastWrittenPseudo = "";

        void Awake() { Instance = this; Load(); }
        void Start() { Invoke(nameof(Init), 3f); MenuUI.Instance?.RegisterPage("Names.Title", Draw, MenuCategory.Gameplay); }

        void Init()
        {
            nameTMP = GetTMP("Player Objects/Local VRRig/Local Gorilla Player/rig/body_pivot/body_AnchorFrontRight_NameTag/Scaling Text Name");
            outfitTMP = FindOutfitTMP();
            if (nameTMP != null) nameProp = nameTMP.GetType().GetProperty("text") ?? nameTMP.GetType().GetProperty("Text");
            if (outfitTMP != null) outfitProp = outfitTMP.GetType().GetProperty("text") ?? outfitTMP.GetType().GetProperty("Text");
            if (outfitTMP != null && outfitProp != null) currentOutfit = Top(TMP(outfitTMP, outfitProp));
        }

        void Update()
        {
            // Le comportement passif (auto-application du pseudo par tenue) tourne tant que
            // le mod est activé dans la liste — plus de préférence "auto" séparée.
            if (!Enabled || outfitTMP == null || nameTMP == null) return;

            string full = TMP(outfitTMP, outfitProp).Trim();
            string top = Top(full);
            string currentName = GetPlayerName();

            string desired = currentOutfit + "\n" + currentName;
            if (full != desired && (top != currentOutfit || !full.EndsWith(currentName)))
            {
                if (desired != lastWrittenText || currentName != lastWrittenPseudo)
                {
                    outfitProp.SetValue(outfitTMP, desired);
                    lastWrittenText = desired;
                    lastWrittenPseudo = currentName;
                }
            }

            if (string.IsNullOrEmpty(top) || top == currentOutfit) return;

            if (!string.IsNullOrEmpty(currentOutfit))
            {
                saved[currentOutfit] = currentName;
                Save();
            }

            if (!saved.TryGetValue(top, out string preset))
            {
                preset = currentName;
                saved[top] = preset;
                Save();
            }
            ApplyPreset(preset);
            currentOutfit = top;

            lastWrittenText = "";
            lastWrittenPseudo = "";
        }

        void ApplyPreset(string name)
        {
            if (string.IsNullOrEmpty(name)) name = "Gorilla";
            PlayerPrefs.SetString("playerName", name);
            PlayerPrefs.Save();

            foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var gt = a.GetType("GorillaTagger"); if (gt == null) continue;
                var inst = gt.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (inst == null) continue;
                var rig = gt.GetField("offlineVRRig", BindingFlags.Public | BindingFlags.Instance)?.GetValue(inst);
                if (rig == null) continue;
                rig.GetType().GetField("playerName", BindingFlags.Public | BindingFlags.Instance)?.SetValue(rig, name);
                break;
            }

            var pnType = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Photon.Pun.PhotonNetwork")).FirstOrDefault(t => t != null);
            if (pnType != null)
            {
                var nickProp = pnType.GetProperty("NickName", BindingFlags.Public | BindingFlags.Static);
                nickProp?.SetValue(null, name);
            }

            if (nameTMP != null && nameProp != null) nameProp.SetValue(nameTMP, name);
        }

        string GetPlayerName()
        {
            if (nameTMP != null && nameProp != null)
            {
                string n = nameProp.GetValue(nameTMP)?.ToString();
                if (!string.IsNullOrEmpty(n)) return n;
            }
            foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var gc = a.GetType("GorillaComputer"); if (gc == null) continue;
                var inst = FindObjectOfType(gc); if (inst == null) continue;
                var v = gc.GetField("currentName", BindingFlags.Public | BindingFlags.Instance)?.GetValue(inst)?.ToString();
                if (!string.IsNullOrEmpty(v)) return v;
            }
            return PlayerPrefs.GetString("playerName", "Gorilla");
        }

        string TMP(object o, PropertyInfo p) { try { return p?.GetValue(o)?.ToString() ?? ""; } catch { return ""; } }
        string Top(string s) => string.IsNullOrEmpty(s) ? "" : s.Split('\n')[0].Trim();

        object FindOutfitTMP()
        {
            var go = GameObject.Find("Environment Objects/LocalObjects_Prefab/TreeRoom/TreeRoomInteractables/UI/SatelliteWardrobe/Outfit");
            if (go != null) return go.GetComponents<Component>().FirstOrDefault(c => c.GetType().Name.Contains("TextMeshPro"));
            var wardrobe = GameObject.Find("Environment Objects/LocalObjects_Prefab/TreeRoom/TreeRoomInteractables/UI/SatelliteWardrobe");
            if (wardrobe != null)
                foreach (Transform t in wardrobe.transform)
                    if (t.name.ToLower().Contains("outfit"))
                        return t.GetComponents<Component>().FirstOrDefault(c => c.GetType().Name.Contains("TextMeshPro"));
            foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>())
                if (g.name.ToLower().Contains("outfit") && g.activeInHierarchy)
                {
                    var tmp = g.GetComponents<Component>().FirstOrDefault(c => c.GetType().Name.Contains("TextMeshPro"));
                    if (tmp != null) return tmp;
                }
            return null;
        }

        object GetTMP(string path) { var go = GameObject.Find(path); return go?.GetComponents<Component>().FirstOrDefault(c => c.GetType().Name.Contains("TextMeshPro")); }

        /// <summary>
        /// Interface simplifiée : une seule carte "état" (tenue/pseudo/switch auto-save),
        /// puis les presets sous forme de rows uniformes (même pattern que les favoris de Rooms)
        /// au lieu de GUILayout bruts avec styles ad-hoc — tout passe par les helpers de MenuUI
        /// pour rester visuellement cohérent avec le reste du menu.
        /// </summary>
        void Draw(MenuUI ui)
        {
            string curOutfit = outfitTMP != null ? Top(TMP(outfitTMP, outfitProp)) : "?";
            string name = GetPlayerName();

            ui.Section(I18n.T("Names.Section"));

            ui.BeginCard();
            GUILayout.BeginHorizontal();
            ui.Label(I18n.T("Names.Outfit") + " " + curOutfit, Theme.Accent, 11, true);
            GUILayout.FlexibleSpace();
            ui.Label(I18n.T("Names.Pseudo") + " " + name, Theme.Text, 11, true, TextAnchor.MiddleRight);
            GUILayout.EndHorizontal();

            ui.Space(6); ui.Separator(); ui.Space(6);

            ui.StatusPill(Enabled ? I18n.T("Names.Active") : I18n.T("Names.Disabled"), Enabled);
            ui.Space(4);
            ui.Label(Enabled ? I18n.T("Names.AutoSaveOn") : I18n.T("Names.EnableInModsList"), Theme.Dim, 9);
            ui.EndCard();

            ui.Space(6);
            ui.Section(I18n.T("Names.Presets") + " (" + saved.Count + ")");
            ui.BeginCard();
            if (saved.Count == 0)
            {
                ui.Label(I18n.T("Names.NoPresets"), Theme.Dim, 9);
            }
            else
            {
                foreach (var kv in saved.OrderBy(x => NumericKey(x.Key)).ThenBy(x => x.Key, System.StringComparer.OrdinalIgnoreCase).ToList())
                    DrawPresetRow(ui, kv.Key, kv.Value);
            }
            ui.EndCard();
        }

        /// <summary>
        /// Extrait le numéro final d'un nom de tenue ("Outfit 10" -> 10) pour trier 1 à 10 correctement
        /// au lieu d'un tri alphabétique qui placerait "Outfit 10" avant "Outfit 2".
        /// Les tenues sans numéro (custom, etc.) finissent après, triées entre elles par nom.
        /// </summary>
        int NumericKey(string outfit)
        {
            var digits = new string(outfit.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
            return int.TryParse(digits, out int n) ? n : int.MaxValue;
        }

        /// <summary>
        /// Row de preset : nom de la tenue (accentué si c'est la tenue actuelle) à gauche,
        /// pseudo associé en dessous en petit, bouton rond rouge standard en overlay à droite
        /// pour supprimer — même rendu exact que DrawFavoriteRow dans GorillaRooms (ombre,
        /// double contour, pas de glyphe dedans).
        /// </summary>
        void DrawPresetRow(MenuUI ui, string outfit, string presetName)
        {
            bool cur = outfit == currentOutfit;

            float rowH = 40f * UI.Scale;
            float btnD = MenuUI.StandardRoundBtnSize + 6f * UI.Scale;
            float btnRightMargin = 10f * UI.Scale;

            GUILayout.BeginHorizontal();
            Rect rowRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(rowH));
            UI.DrawRounded(rowRect, Theme.BG3, Mathf.RoundToInt(12 * UI.Scale));

            float pad = 12f * UI.Scale;
            Rect textRect = new Rect(rowRect.x + pad, rowRect.y, rowRect.width - pad * 2f - btnD - btnRightMargin, rowRect.height);
            var outfitStyle = UI.Sty(cur ? Theme.Accent : Theme.Text, 11, cur, TextAnchor.UpperLeft);
            var nameStyle = UI.Sty(Theme.Dim, 9, false, TextAnchor.LowerLeft);
            GUI.Label(new Rect(textRect.x, textRect.y + 4f * UI.Scale, textRect.width, textRect.height * 0.55f), outfit, outfitStyle);
            GUI.Label(new Rect(textRect.x, textRect.y + textRect.height * 0.5f, textRect.width, textRect.height * 0.45f - 4f * UI.Scale), presetName, nameStyle);

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
            if (deleteClicked)
            {
                SoundFX.PlaySound("Grave");
                saved.Remove(outfit);
                Save();
            }

            GUILayout.EndHorizontal();
            ui.Space(4);
        }

        void Save()
        {
            var sb = new System.Text.StringBuilder("{");
            bool first = true;
            foreach (var kv in saved)
            {
                if (!first) sb.Append(",");
                sb.Append($"\"{Esc(kv.Key)}\":\"{Esc(kv.Value)}\"");
                first = false;
            }
            sb.Append("}");
            PlayerPrefs.SetString("MUM_names", sb.ToString());
            PlayerPrefs.Save();
        }

        void Load()
        {
            saved.Clear();
            string raw = PlayerPrefs.GetString("MUM_names", "{}").Trim('{', '}');
            if (string.IsNullOrEmpty(raw)) return;
            foreach (var pair in raw.Split(','))
            {
                var parts = pair.Split(':');
                if (parts.Length != 2) continue;
                string key = Une(parts[0].Trim('"', ' '));
                string val = Une(parts[1].Trim('"', ' '));
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(val))
                    saved[key] = val;
            }
        }

        string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        string Une(string s) => s.Replace("\\\"", "\"").Replace("\\\\", "\\");
    }
}