using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using UnityEngine;

namespace UltimateMenu
{
    // Keep Unity preferences for compatibility; mirror menu settings to a portable profile.
    internal static class MenuPrefs
    {
        [DataContract] sealed class Entry { [DataMember] public string key; [DataMember] public string kind; [DataMember] public string value; }
        [DataContract] sealed class Profile { [DataMember] public string version = "1.7.114"; [DataMember] public bool complete; [DataMember] public List<Entry> values = new List<Entry>(); }
        static readonly Dictionary<string, Entry> Values = new Dictionary<string, Entry>();
        static string _path;
        static bool _dirty, _authoritative;
        static float _nextSave;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        internal static void Initialize()
        {
            string folder = Path.Combine(BepInEx.Paths.PluginPath, "UltimateMenu");
            Directory.CreateDirectory(folder);
            _path = Path.Combine(folder, "Profile.json");
            if (File.Exists(_path))
            {
                try
                {
                    if (new FileInfo(_path).Length > 4194304) throw new InvalidDataException("Profile too large");
                    Profile profile;
                    using (var stream = File.OpenRead(_path)) profile = (Profile)new DataContractJsonSerializer(typeof(Profile)).ReadObject(stream);
                    if (profile?.values == null || profile.values.Count > 4096) throw new InvalidDataException("Invalid profile");
                    _authoritative = profile.complete;
                    foreach (var entry in profile.values)
                    {
                        if (entry == null || entry.key == null || !entry.key.StartsWith("MUM_", StringComparison.Ordinal) || entry.key.Length > 256 || entry.value == null || entry.value.Length > 1048576) continue;
                        if (entry.kind == "int" && int.TryParse(entry.value, NumberStyles.Integer, Inv, out int number)) PlayerPrefs.SetInt(entry.key, number);
                        else if (entry.kind == "float" && float.TryParse(entry.value, NumberStyles.Float, Inv, out float real) && !float.IsNaN(real) && !float.IsInfinity(real)) PlayerPrefs.SetFloat(entry.key, real);
                        else if (entry.kind == "string") PlayerPrefs.SetString(entry.key, entry.value);
                        else continue;
                        Values[entry.key] = entry;
                    }
                }
                catch (Exception e) { Log.Err("Profile load", e); }
            }
            _dirty = true;
            Save();
        }

        static void Track(string key, string kind, string value)
        {
            if (!key.StartsWith("MUM_", StringComparison.Ordinal)) return;
            if (Values.TryGetValue(key, out var old) && old.kind == kind && old.value == value) return;
            Values[key] = new Entry { key = key, kind = kind, value = value }; _dirty = true;
        }
        public static int GetInt(string key, int fallback = 0) { int value = _authoritative && key.StartsWith("MUM_", StringComparison.Ordinal) ? (Values.TryGetValue(key, out var entry) && entry.kind == "int" && int.TryParse(entry.value, NumberStyles.Integer, Inv, out var parsed) ? parsed : fallback) : PlayerPrefs.GetInt(key, fallback); Track(key, "int", value.ToString(Inv)); return value; }
        public static float GetFloat(string key, float fallback = 0f) { float value = _authoritative && key.StartsWith("MUM_", StringComparison.Ordinal) ? (Values.TryGetValue(key, out var entry) && entry.kind == "float" && float.TryParse(entry.value, NumberStyles.Float, Inv, out var parsed) ? parsed : fallback) : PlayerPrefs.GetFloat(key, fallback); Track(key, "float", value.ToString("R", Inv)); return value; }
        public static string GetString(string key, string fallback = "") { string value = _authoritative && key.StartsWith("MUM_", StringComparison.Ordinal) ? (Values.TryGetValue(key, out var entry) && entry.kind == "string" ? entry.value : fallback) : PlayerPrefs.GetString(key, fallback); Track(key, "string", value); return value; }
        public static bool HasKey(string key) => _authoritative && key.StartsWith("MUM_", StringComparison.Ordinal) ? Values.ContainsKey(key) : PlayerPrefs.HasKey(key);
        public static void SetInt(string key, int value) { PlayerPrefs.SetInt(key, value); Track(key, "int", value.ToString(Inv)); if (_dirty) Save(); }
        public static void SetFloat(string key, float value) { PlayerPrefs.SetFloat(key, value); Track(key, "float", value.ToString("R", Inv)); if (_dirty) Save(); }
        public static void SetString(string key, string value) { PlayerPrefs.SetString(key, value); Track(key, "string", value); if (_dirty) Save(); }
        public static void DeleteKey(string key) { PlayerPrefs.DeleteKey(key); if (Values.Remove(key)) { _dirty = true; Save(); } }
        internal static void Tick()
        {
            if (!_dirty || Time.unscaledTime < _nextSave) return;
            _nextSave = Time.unscaledTime + 2f;
            Save();
        }
        public static void Save()
        {
            PlayerPrefs.Save();
            if (!_dirty || _path == null) return;
            try
            {
                var profile = new Profile { complete = true, version = Log.PluginVersion ?? "1.7.114", values = new List<Entry>(Values.Values) };
                profile.values.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
                using (var stream = File.Create(_path + ".tmp"))
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false, true, "  "))
                    new DataContractJsonSerializer(typeof(Profile)).WriteObject(writer, profile);
                if (File.Exists(_path)) File.Replace(_path + ".tmp", _path, null);
                else File.Move(_path + ".tmp", _path);
                _dirty = false;
            }
            catch (Exception e) { Log.Err("Profile save", e); }
        }
    }
}
