using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization.Json;

namespace UltimateMenu
{
    public enum Lang { Fr = 0, En = 1, De = 2, Es = 3, It = 4, Pt = 5, Ja = 6, Ru = 7, Nl = 8, Pl = 9, Tr = 10, Uk = 11, Cs = 12, Ro = 13, Sv = 14, Da = 15, Fi = 16, Hu = 17 }

    public static class I18n
    {
        static Dictionary<string, string> _selected;
        static Dictionary<string, string> _english;
        static Lang _loaded = (Lang)(-1);
        static readonly Dictionary<string, string[]> Overrides = new Dictionary<string, string[]>();
        public static Lang Current
        {
            get
            {
                int value = MenuPrefs.GetInt("MUM_Lang", (int)Lang.Fr);
                return value >= 0 && value <= (int)Lang.Hu ? (Lang)value : Lang.Fr;
            }
            set
            {
                MenuPrefs.SetInt("MUM_Lang", (int)value >= 0 && (int)value <= (int)Lang.Hu ? (int)value : (int)Lang.Fr);
                MenuPrefs.Save();
            }
        }
        public static void Initialize() => EnsureLanguage();
        static void EnsureLanguage()
        {
            Lang current = Current;
            if (_loaded == current && _selected != null) return;
            _selected = current == Lang.En && _english != null ? _english : Load(current);
            if (current == Lang.En) _english = _selected;
            _loaded = current;
        }
        static Dictionary<string,string> Load(Lang language)
        {
            try
            {
                string resource = "UltimateMenu.Assets.Menu.Translations." + language + ".json.gz";
                using (var stream = typeof(I18n).Assembly.GetManifestResourceStream(resource))
                {
                    if (stream == null) throw new FileNotFoundException(resource);
                    using (var compressed = new GZipStream(stream, CompressionMode.Decompress))
                    {
                        var serializer = new DataContractJsonSerializer(typeof(Dictionary<string,string>),
                            new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
                        return (Dictionary<string,string>)serializer.ReadObject(compressed);
                    }
                }
            }
            catch (Exception error) { Log.Err("Translation " + language, error); return new Dictionary<string,string>(); }
        }
        public static string T(string key)
        {
            if (key == null) return "";
            EnsureLanguage();
            if (Overrides.TryGetValue(key, out var values))
            {
                int index = (int)_loaded;
                if (index < values.Length && !string.IsNullOrEmpty(values[index])) return values[index];
                if (values.Length > 1 && !string.IsNullOrEmpty(values[1])) return values[1];
            }
            if (_selected.TryGetValue(key, out string text) && !string.IsNullOrEmpty(text)) return text;
            if (_english == null) _english = Load(Lang.En);
            return _english.TryGetValue(key, out text) && !string.IsNullOrEmpty(text) ? text : key;
        }
        public static bool Has(string key)
        {
            if (key == null) return false;
            EnsureLanguage();
            if (_english == null) _english = Load(Lang.En);
            return Overrides.ContainsKey(key) || _selected.ContainsKey(key) || _english.ContainsKey(key);
        }
        public static void Register(string key, params string[] translations) => Overrides[key] = translations;
    }
    public static class Trad
    {
        public static void Register() => I18n.Initialize();
    }
}