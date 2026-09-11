using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace UltimateMenu
{
    public class GorillaFaces : MonoBehaviour
    {
        public static GorillaFaces Instance { get; private set; }

        private string _dataFolder;
        private readonly List<string> _availableFiles = new List<string>();
        private string _search = "", _status = "", _currentAppliedName = "";
        private int _selectedIndex = -1;

        private const string FacePath = "rig/head/gorillaface";
        private const string ChestPath = "rig/gorillachest";

        // Nom logique de la ressource embarquée dans le DLL (voir configuration du .csproj).
        private const string DEFAULT_FACE_RESOURCE = "UltimateMenu.Assets.GorillaFaces.Default.png";
        private const string DEFAULT_FACE_FILENAME = "Default.png";

        private Transform _rigRoot;
        private MeshRenderer _faceRenderer, _chestRenderer;
        private Material _originalFaceMat, _originalChestMat;
        private bool _hierarchyDumped;

        // Etat activé/désactivé piloté uniquement par le switch global de la page Mods.
        private bool Enabled => MenuUI.Instance != null && MenuUI.Instance.IsModEnabled("Face.Title");
        private bool _wasEnabled = true;

        // Auto-refresh de la liste des fichiers à l'ouverture de la page : on détecte une réouverture
        // quand DrawPage n'a pas été appelée à la frame juste précédente (page fermée entre-temps).
        private int _lastDrawFrame = -1;

        void Awake()
        {
            Instance = this;
            CreateDataFolder();
            EnsureDefaultFaceExtracted();
            RefreshFileList();
            FindRig();
        }

        void Start() => MenuUI.Instance?.RegisterPage("Face.Title", DrawPage, MenuCategory.Cosmetique);

        void Update()
        {
            bool nowEnabled = Enabled;
            if (nowEnabled != _wasEnabled)
            {
                _wasEnabled = nowEnabled;
                if (nowEnabled)
                {
                    if (_selectedIndex >= 0 && _selectedIndex < _availableFiles.Count) ApplyFace(_availableFiles[_selectedIndex]);
                    else ApplyDefaultOrLastFace();
                }
                else
                {
                    RestoreOriginalMaterials();
                }
            }
        }

        void CreateDataFolder()
        {
            string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Directory.GetCurrentDirectory();
            _dataFolder = Path.Combine(dllDir, "GorillaFaces");
            if (!Directory.Exists(_dataFolder)) Directory.CreateDirectory(_dataFolder);
        }

        void EnsureDefaultFaceExtracted()
        {
            string targetPath = Path.Combine(_dataFolder, DEFAULT_FACE_FILENAME);
            if (File.Exists(targetPath)) return;

            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream(DEFAULT_FACE_RESOURCE))
                {
                    if (stream == null)
                    {
                        Log.Err("GorillaFaces.EnsureDefaultFaceExtracted",
                            new Exception("Ressource embarquée introuvable : " + DEFAULT_FACE_RESOURCE +
                                           ". Vérifie que Default.png est bien marqué 'Embedded Resource' dans le projet."));
                        return;
                    }

                    using (var fileStream = File.Create(targetPath))
                        stream.CopyTo(fileStream);
                }
                Log.Info("[GorillaFaces] Default.png extrait vers " + targetPath);
            }
            catch (Exception e)
            {
                Log.Err("GorillaFaces.EnsureDefaultFaceExtracted", e);
            }
        }

        void RefreshFileList()
        {
            _availableFiles.Clear();
            if (!Directory.Exists(_dataFolder)) return;
            foreach (var ext in new[] { "*.jpg", "*.jpeg", "*.png" })
                foreach (var f in Directory.GetFiles(_dataFolder, ext)) _availableFiles.Add(f);
            _availableFiles.Sort(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(_currentAppliedName))
            {
                string appliedPath = Path.Combine(_dataFolder, _currentAppliedName);
                _selectedIndex = _availableFiles.IndexOf(appliedPath);
            }
        }

        void FindRig()
        {
            var rig = GameObject.Find("Player Objects/Local VRRig/Local Gorilla Player");
            if (rig == null) { Invoke(nameof(FindRig), 1f); return; }
            _rigRoot = rig.transform;

            var faceT = _rigRoot.Find(FacePath) ?? FindByNameContains(_rigRoot, "gorillaface") ?? FindByNameContains(_rigRoot, "face");
            var chestT = _rigRoot.Find(ChestPath) ?? FindByNameContains(_rigRoot, "gorillachest") ?? FindByNameContains(_rigRoot, "chest");

            if (faceT == null || chestT == null)
            {
                if (!_hierarchyDumped) { DumpHierarchy(_rigRoot); _hierarchyDumped = true; }
                Invoke(nameof(FindRig), 1f);
                return;
            }

            _faceRenderer = faceT.GetComponent<MeshRenderer>();
            _chestRenderer = chestT.GetComponent<MeshRenderer>();

            if (_faceRenderer == null || _chestRenderer == null)
            {
                if (!_hierarchyDumped) { DumpHierarchy(_rigRoot); _hierarchyDumped = true; }
                Invoke(nameof(FindRig), 1f);
                return;
            }

            _originalFaceMat = _faceRenderer.material;
            _originalChestMat = _chestRenderer.material;

            if (Enabled) ApplyDefaultOrLastFace();
        }

        void ApplyDefaultOrLastFace()
        {
            string savedName = PlayerPrefs.GetString("MUM_LastFace", "");
            if (!string.IsNullOrEmpty(savedName))
            {
                string savedPath = Path.Combine(_dataFolder, savedName);
                if (File.Exists(savedPath))
                {
                    _selectedIndex = _availableFiles.IndexOf(savedPath);
                    ApplyFace(savedPath);
                    return;
                }
            }

            string defaultPath = Path.Combine(_dataFolder, DEFAULT_FACE_FILENAME);
            if (File.Exists(defaultPath))
            {
                _selectedIndex = _availableFiles.IndexOf(defaultPath);
                ApplyFace(defaultPath);
            }
        }

        void RestoreOriginalMaterials()
        {
            if (_faceRenderer == null || _chestRenderer == null) return;
            if (_originalFaceMat != null) _faceRenderer.material = _originalFaceMat;
            if (_originalChestMat != null) _chestRenderer.material = _originalChestMat;
            _status = I18n.T("Face.Disabled");
        }

        Transform FindByNameContains(Transform root, string needle)
        {
            needle = needle.ToLowerInvariant();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.ToLowerInvariant().Contains(needle) && t.GetComponent<MeshRenderer>() != null) return t;
            return null;
        }

        void DumpHierarchy(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.GetComponent<MeshRenderer>() != null) Log.Info("[MeshRenderer] " + GetPath(t));
        }

        string GetPath(Transform t)
        {
            string path = t.name; var p = t.parent; int guard = 0;
            while (p != null && p != _rigRoot && guard++ < 20) { path = p.name + "/" + path; p = p.parent; }
            return path;
        }

        public void ApplyFace(string path)
        {
            if (_faceRenderer == null || _chestRenderer == null) { _status = I18n.T("Face.RigNotFound"); return; }
            if (!File.Exists(path)) { _status = I18n.T("Face.FileNotFound") + " : " + path; return; }

            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(File.ReadAllBytes(path))) { _status = I18n.T("Face.DecodeError") + " : " + Path.GetFileName(path); return; }

                var mat = new Material(_originalFaceMat);
                ApplyTextureToKnownSlots(mat, tex);
                _faceRenderer.material = mat;

                var chestMat = new Material(_originalChestMat);
                ApplyTextureToKnownSlots(chestMat, tex);
                _chestRenderer.material = chestMat;

                _currentAppliedName = Path.GetFileName(path);
                _status = I18n.T("Face.Applied") + " " + _currentAppliedName;

                PlayerPrefs.SetString("MUM_LastFace", _currentAppliedName);
                PlayerPrefs.Save();
            }
            catch (Exception e) { _status = I18n.T("Face.Error") + " : " + e.Message; Log.Err("GorillaFace.ApplyFace", e); }
        }

        void ApplyTextureToKnownSlots(Material mat, Texture2D tex)
        {
            string[] knownSlots = { "_MouthMap", "_MainTex", "_BaseMap", "_FaceMap", "_Texture" };
            bool applied = false;
            foreach (var slot in knownSlots) if (mat.HasProperty(slot)) { mat.SetTexture(slot, tex); applied = true; }
            if (!applied) mat.mainTexture = tex;
        }

        void DrawPage(MenuUI ui)
        {
            // Si au moins une frame s'est écoulée sans que DrawPage soit appelée, la page vient
            // d'être (ré)ouverte : on rafraîchit la liste des fichiers pour voir les ajouts récents.
            if (Time.frameCount - _lastDrawFrame > 1) RefreshFileList();
            _lastDrawFrame = Time.frameCount;

            ui.Section(I18n.T("Face.Section"));

            ui.Label(I18n.T("Face.SimpleHint"), Theme.Dim, 8);
            ui.Space(6);

            GUILayout.BeginHorizontal();
            _search = ui.TextField(_search);
            GUILayout.EndHorizontal();
            ui.Space(6);

            var filtered = _availableFiles.Where(f => string.IsNullOrEmpty(_search) || Path.GetFileName(f).ToLower().Contains(_search.ToLower())).ToList();
            if (filtered.Count == 0)
            {
                ui.Label(I18n.T("Face.NoImages"), Theme.Dim, 9);
            }
            else
            {
                foreach (var f in filtered)
                {
                    bool applied = Path.GetFileName(f) == _currentAppliedName;
                    bool isDefault = Path.GetFileName(f) == DEFAULT_FACE_FILENAME;
                    string label = Path.GetFileName(f) + (isDefault ? " (" + I18n.T("Face.DefaultTag") + ")" : "");
                    if (ui.Btn(label, applied ? ui.TG : ui.TB3, applied ? Color.white : Theme.Text) && Enabled)
                    {
                        _selectedIndex = _availableFiles.IndexOf(f);
                        ApplyFace(f);
                    }
                    ui.Space(4);
                }
            }

            if (!string.IsNullOrEmpty(_status)) { ui.Space(4); ui.Label(_status, Theme.Accent, 9); }
            if (_faceRenderer == null) { ui.Space(4); ui.Label(I18n.T("Face.RigNotFound"), Theme.Red, 9); }
        }
    }
}