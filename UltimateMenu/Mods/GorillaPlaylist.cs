using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UltimateMenu
{
    public sealed class GorillaPlaylist : MonoBehaviour
    {
        const string Title = "Playlist.Title";
        const string Pref = "MUM_Playlist";
        const int PageSize = 12;
        static readonly char[] PathSeparators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        static readonly string[] Extensions = { ".mp3", ".wav", ".m4a", ".aac", ".wma", ".flac" };
        AudioSource _source;
        AudioClip _clip;
        Texture2D _artwork;
        CancellationTokenSource _decodeCancellation;
        Task<(PlaylistAudioDecoder.Track audio, Exception error)> _decode;
        Task<(string[] tracks, string[] folders, Exception error)> _scan;
        string[] _tracks = Array.Empty<string>();
        string[] _folders = Array.Empty<string>();
        int[] _filteredTracks = Array.Empty<int>();
        string[] _visibleFolders = Array.Empty<string>();
        string _libraryRoot = "", _browseFolder = "";
        readonly HashSet<int> _playedInShuffle = new HashSet<int>();
        readonly HashSet<int> _failedTracks = new HashSet<int>();
        readonly Stack<int> _shuffleHistory = new Stack<int>();
        string _search = "";
        string _folder = "", _status = "", _currentName = "";
        string _pendingBrowseFolder;
        int _index = -1, _page;
        bool _recursive, _shuffle, _repeat, _muted, _playing, _registered;
        float _volume, _speed;

        bool Enabled => MenuUI.Instance != null && MenuUI.Instance.IsModEnabled(Title);

        void Awake()
        {
            _folder = MenuPrefs.GetString(Pref + "Folder", "");
            _recursive = MenuPrefs.GetInt(Pref + "Recursive", 1) != 0;
            _shuffle = MenuPrefs.GetInt(Pref + "Shuffle", 0) != 0;
            _repeat = MenuPrefs.GetInt(Pref + "Repeat", 0) != 0;
            _volume = Mathf.Clamp01(MenuPrefs.GetFloat(Pref + "Volume", .5f));
            _speed = Mathf.Clamp(MenuPrefs.GetFloat(Pref + "Speed", 1f), .5f, 2f);

            I18n.Register(Title, "Playlist", "Playlist");
            I18n.Register("Playlist.Folder", "Dossier de musique", "Music folder");
            I18n.Register("Playlist.Recursive", "Inclure les sous-dossiers", "Include subfolders");
            I18n.Register("Playlist.Scan", "Actualiser", "Refresh");
            I18n.Register("Playlist.Scanning", "Recherche des morceaux…", "Finding tracks…");
            I18n.Register("Playlist.BadFolder", "Dossier introuvable ou inaccessible.", "Folder not found or inaccessible.");
            I18n.Register("Playlist.Empty", "Aucun morceau compatible.", "No compatible tracks.");
            I18n.Register("Playlist.Enable", "Active le mod pour lancer la lecture.", "Enable the mod to start playback.");
            I18n.Register("Playlist.Loading", "Chargement…", "Loading…");
            I18n.Register("Playlist.Error", "Impossible de lire ce morceau.", "Could not play this track.");
            I18n.Register("Playlist.Play", "Lire", "Play");
            I18n.Register("Playlist.Pause", "Pause", "Pause");
            I18n.Register("Playlist.Stop", "Arrêter", "Stop");
            I18n.Register("Playlist.Previous", "Précédent", "Previous");
            I18n.Register("Playlist.Next", "Suivant", "Next");
            I18n.Register("Playlist.Volume", "Volume", "Volume");
            I18n.Register("Playlist.Speed", "Vitesse", "Speed");
            I18n.Register("Playlist.Sound", "Son", "Sound");
            I18n.Register("Playlist.Shuffle", "Lecture aléatoire", "Shuffle");
            I18n.Register("Playlist.Repeat", "Répéter la liste", "Repeat playlist");
            I18n.Register("Playlist.Search", "Rechercher un morceau", "Find a track");
            I18n.Register("Playlist.Back", "Dossier parent", "Parent folder");
            I18n.Register("Playlist.NoMatches", "Aucun résultat.", "No matches.");
        }

        void Update()
        {
            if (!_registered && MenuUI.Instance != null)
            {
                MenuUI.Instance.RegisterPage(Title, DrawPage, MenuCategory.Social);
                _registered = true;
                if (!string.IsNullOrWhiteSpace(_folder)) RefreshTracks();
            }
            if (_pendingBrowseFolder != null)
            {
                _browseFolder = _pendingBrowseFolder;
                _pendingBrowseFolder = null;
                FilterTracks();
            }
            if (_scan?.IsCompleted == true)
            {
                var result = _scan.GetAwaiter().GetResult();
                _scan = null;
                _tracks = result.tracks;
                _folders = result.folders;
                _failedTracks.Clear();
                _index = -1;
                _playedInShuffle.Clear();
                _shuffleHistory.Clear();
                FilterTracks();
                _status = result.error != null ? "Playlist.BadFolder" : _tracks.Length == 0 ? "Playlist.Empty" : "";
            }
            if (!Enabled)
            {
                if (_decode != null || _clip != null) StopTrack();
                return;
            }
            if (_decode?.IsCompleted == true)
            {
                var result = _decode.GetAwaiter().GetResult();
                _decode = null;
                _decodeCancellation?.Dispose();
                _decodeCancellation = null;
                if (result.error != null)
                {
                    _status = "Playlist.Error";
                    Log.Info("[Playlist] Unable to decode track: " + result.error.Message);
                    if (_index >= 0) _failedTracks.Add(_index);
                    Skip(1, true);
                }
                else if (result.audio != null) PlayDecoded(result.audio);
            }
            if (_playing && _source != null && !_source.isPlaying)
            {
                _playing = false;
                Skip(1, true);
            }
        }

        void RefreshTracks()
        {
            StopTrack();
            _tracks = Array.Empty<string>();
            _folders = Array.Empty<string>();
            _filteredTracks = Array.Empty<int>();
            _visibleFolders = Array.Empty<string>();
            _index = -1;
            string folder = _folder.Trim().Trim('"');
            folder = PlaylistAudioDecoder.DisplayPath(folder);
            _pendingBrowseFolder = null;
            _libraryRoot = _browseFolder = folder;
            bool recursive = _recursive;
            _status = "Playlist.Scanning";
            _scan = Task.Run<(string[], string[], Exception)>(() =>
            {
                try
                {
                    var options = new EnumerationOptions {
                        RecurseSubdirectories = recursive, IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
                    };
                    string root = PlaylistAudioDecoder.ExtendedPath(folder);
                    var tracks = Directory.EnumerateFiles(root, "*", options)
                        .Where(file => Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                        .Select(PlaylistAudioDecoder.DisplayPath)
                        .OrderBy(file => file, StringComparer.OrdinalIgnoreCase).ToArray();
                    var folders = recursive
                        ? Directory.EnumerateDirectories(root, "*", options).Select(PlaylistAudioDecoder.DisplayPath).ToArray()
                        : Array.Empty<string>();
                    return (tracks, folders, null);
                }
                catch (Exception error) { return (Array.Empty<string>(), Array.Empty<string>(), error); }
            });
        }

        void StartTrack(int index, bool rememberPrevious = true)
        {
            if (!Enabled || index < 0 || index >= _tracks.Length) return;
            if (_shuffle && rememberPrevious && _index >= 0 && _index != index)
                _shuffleHistory.Push(_index);
            StopTrack();
            _index = index;
            int visibleIndex = Array.IndexOf(_filteredTracks, index);
            if (visibleIndex >= 0) _page = (_visibleFolders.Length + visibleIndex) / PageSize;
            _playedInShuffle.Add(index);
            string path = _tracks[index];
            _currentName = Path.GetFileNameWithoutExtension(path);
            _status = "Playlist.Loading";
            _decodeCancellation = new CancellationTokenSource();
            var token = _decodeCancellation.Token;
            _decode = Task.Run<(PlaylistAudioDecoder.Track, Exception)>(() =>
            {
                try { return (PlaylistAudioDecoder.Decode(path, token), null); }
                catch (OperationCanceledException) { return (null, null); }
                catch (Exception error) { return (null, error); }
            });
        }

        void PlayDecoded(PlaylistAudioDecoder.Track track)
        {
            if (_source == null)
            {
                var audioObject = new GameObject("UltimateMenu.PlaylistAudio");
                audioObject.transform.SetParent(transform, false);
                _source = audioObject.AddComponent<AudioSource>();
                _source.playOnAwake = false;
                _source.spatialBlend = 0f;
                _source.ignoreListenerVolume = true;
                _source.ignoreListenerPause = true;
                _source.priority = 0;
                _source.volume = _volume;
                _source.pitch = _speed;
                _source.mute = _muted;
            }
            _clip = AudioClip.Create(_currentName, track.Samples.Length / track.Channels, track.Channels, track.Rate, false);
            if (_clip == null || !_clip.SetData(track.Samples, 0))
            {
                StopTrack();
                _status = "Playlist.Error";
                return;
            }
            _source.clip = _clip;
            if (track.Artwork != null)
            {
                _artwork = new Texture2D(2, 2);
                if (!_artwork.LoadImage(track.Artwork, true)) { Destroy(_artwork); _artwork = null; }
            }
            _status = "";
            Play();
        }

        void Play()
        {
            if (!Enabled) return;
            if (_clip == null)
            {
                if (_decode == null && _tracks.Length > 0)
                {
                    int first = _shuffle ? UnityEngine.Random.Range(0, _tracks.Length)
                        : _index >= 0 ? _index : _filteredTracks.Length > 0 ? _filteredTracks[0] : 0;
                    StartTrack(first);
                }
                return;
            }
            _source.Play();
            _playing = true;
        }

        void StopTrack()
        {
            _decodeCancellation?.Cancel();
            _decodeCancellation?.Dispose();
            _decodeCancellation = null;
            _decode = null;
            _playing = false;
            _paused = false;
            if (_source != null) { _source.Stop(); _source.clip = null; }
            if (_clip != null) Destroy(_clip);
            _clip = null;
            if (_artwork != null) Destroy(_artwork);
            _artwork = null;
            _currentName = "";
            if (_status == "Playlist.Loading") _status = "";
        }

        bool _paused;

        void TogglePlayback()
        {
            if (!Enabled) return;
            if (_playing)
            {
                _source.Pause();
                _playing = false;
                _paused = true;
            }
            else if (_paused)
            {
                _source.UnPause();
                _playing = true;
                _paused = false;
            }
            else Play();
        }

        void Skip(int direction, bool automatic = false)
        {
            if (!Enabled || _tracks.Length == 0) return;
            if (direction < 0 && _clip != null && _source.time > 3f)
            {
                Seek(0f);
                return;
            }
            if (direction < 0 && _shuffle && _shuffleHistory.Count > 0)
            {
                StartTrack(_shuffleHistory.Pop(), false);
                return;
            }
            if (_shuffle && direction > 0)
            {
                int[] remaining = Enumerable.Range(0, _tracks.Length)
                    .Where(index => !_playedInShuffle.Contains(index) && !_failedTracks.Contains(index)).ToArray();
                if (remaining.Length == 0 && (!automatic || _repeat))
                {
                    _playedInShuffle.Clear();
                    remaining = Enumerable.Range(0, _tracks.Length)
                        .Where(index => !_failedTracks.Contains(index) && (index != _index || _tracks.Length == 1)).ToArray();
                }
                if (remaining.Length > 0)
                {
                    StartTrack(remaining[UnityEngine.Random.Range(0, remaining.Length)]);
                    return;
                }
            }
            else
            {
                int next = _index < 0 ? 0 : _index + direction;
                for (int scanned = 0; scanned < _tracks.Length; scanned++, next += direction)
                {
                    if (next < 0 || next >= _tracks.Length)
                    {
                        if (automatic && !_repeat) break;
                        next = (next + _tracks.Length) % _tracks.Length;
                    }
                    if (_failedTracks.Contains(next)) continue;
                    StartTrack(next);
                    return;
                }
            }
            StopTrack();
            _index = -1;
        }

        void Seek(float seconds)
        {
            if (_clip != null)
                _source.timeSamples = Mathf.Clamp(Mathf.RoundToInt(seconds * _clip.frequency), 0, _clip.samples - 1);
        }

        static string Clock(float seconds) => TimeSpan.FromSeconds(seconds).ToString(@"m\:ss");

        void FilterTracks()
        {
            string query = _search.Trim();
            var folders = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var tracks = new List<int>();
            if (query.Length == 0)
            {
                foreach (string folder in _folders)
                {
                    string relative = Path.GetRelativePath(_browseFolder, folder);
                    if (relative != "." && relative != ".." && !Path.IsPathRooted(relative)
                        && relative.IndexOfAny(PathSeparators) < 0)
                        folders.Add(relative);
                }
            }
            for (int index = 0; index < _tracks.Length; index++)
            {
                string path = _tracks[index];
                if (Path.GetFileNameWithoutExtension(path).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                string relative = Path.GetRelativePath(_browseFolder, path);
                int separator = relative.IndexOfAny(PathSeparators);
                if (Path.IsPathRooted(relative) || (separator >= 0 && relative.Substring(0, separator) == ".."))
                    continue;
                if (separator < 0) tracks.Add(index);
                else folders.Add(relative.Substring(0, separator));
            }
            _visibleFolders = folders.ToArray();
            _filteredTracks = tracks.ToArray();
            _page = 0;
        }

        void DrawLibrary(MenuUI ui)
        {
            ui.Label(I18n.T("Playlist.Search"));
            string search = ui.TextField(_search);
            if (search != _search)
            {
                _search = search;
                if (_tracks.Length > 0 || _folders.Length > 0) FilterTracks();
            }
            bool hasParent = (_tracks.Length > 0 || _folders.Length > 0) && !string.Equals(
                Path.GetFullPath(_browseFolder).TrimEnd(PathSeparators),
                Path.GetFullPath(_libraryRoot).TrimEnd(PathSeparators), StringComparison.OrdinalIgnoreCase);
            int count = _visibleFolders.Length + _filteredTracks.Length;
            int first = _page * PageSize;
            int rows = Mathf.Clamp(count - first, 0, PageSize) + (hasParent ? 1 : 0);
            float height = 29f * UI.Scale;
            Rect library = GUILayoutUtility.GetRect(0f, Mathf.Max(1, rows) * height, GUILayout.ExpandWidth(true));
            int capacity = Mathf.Max(0, Mathf.RoundToInt(library.height / height));
            int row = 0;
            if (hasParent && row < capacity)
            {
                if (ui.PlaylistLibraryRow(new Rect(library.x, library.y, library.width, height), I18n.T("Playlist.Back"), false, 0f))
                {
                    _pendingBrowseFolder = Path.GetDirectoryName(_browseFolder);
                }
                row++;
            }
            if (count == 0 && row < capacity)
                GUI.Label(new Rect(library.x, library.y + row * height, library.width, height), I18n.T("Playlist.NoMatches"), UI.Sty(Theme.Dim, 9));
            for (int i = first; i < Mathf.Min(first + PageSize, count) && row < capacity; i++, row++)
            {
                Rect rect = new Rect(library.x, library.y + row * height, library.width, height);
                if (i < _visibleFolders.Length)
                {
                    string folder = _visibleFolders[i];
                    if (ui.PlaylistLibraryRow(rect, folder, false, 180f))
                    {
                        _pendingBrowseFolder = Path.Combine(_browseFolder, folder);
                    }
                }
                else
                {
                    int index = _filteredTracks[i - _visibleFolders.Length];
                    if (ui.PlaylistLibraryRow(rect, Path.GetFileNameWithoutExtension(_tracks[index]), index == _index))
                        StartTrack(index);
                }
            }
            int pageCount = Mathf.Max(1, (count + PageSize - 1) / PageSize);
            GUILayout.BeginHorizontal();
            if (ui.PlaylistIconButton("Arrow", I18n.T("Playlist.Previous"))) _page = Mathf.Max(0, _page - 1);
            ui.CenterLabel($"{_page + 1} / {pageCount}");
            if (ui.PlaylistIconButton("Arrow", I18n.T("Playlist.Next"), angle: 180f)) _page = Mathf.Min(pageCount - 1, _page + 1);
            GUILayout.EndHorizontal();
        }

        void DrawPage(MenuUI ui)
        {
            ui.Section(I18n.T("Playlist.Folder"));
            ui.BeginCard();
            string folder = ui.TextField(_folder);
            if (folder != _folder) _folder = folder;
            bool recursive = ui.Toggle(I18n.T("Playlist.Recursive"), _recursive);
            bool refresh = ui.Btn(I18n.T("Playlist.Scan"));
            if (recursive != _recursive || refresh)
            {
                _recursive = recursive;
                MenuPrefs.SetString(Pref + "Folder", _folder);
                MenuPrefs.SetInt(Pref + "Recursive", _recursive ? 1 : 0);
                RefreshTracks();
            }
            ui.Label(!Enabled ? I18n.T("Playlist.Enable") : _status.Length > 0 ? I18n.T(_status) : "", Theme.Dim);
            ui.EndCard();

            DrawLibrary(ui);

            ui.BeginCard();
            Rect header = GUILayoutUtility.GetRect(0f, 42f * UI.Scale, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                if (_artwork != null)
                {
                    float size = header.height;
                    GUI.DrawTexture(new Rect(header.x, header.y, size, size), _artwork, ScaleMode.ScaleToFit);
                    header.xMin += size + 8f * UI.Scale;
                }
                var titleStyle = new GUIStyle(UI.Sty(Theme.Text, 10, true)) { clipping = TextClipping.Clip, wordWrap = false };
                GUI.Label(header, _currentName, titleStyle);
            }
            float position = _clip != null ? _source.time : 0f;
            float duration = _clip != null ? _clip.length : 0f;
            ui.RightLabel(Clock(position) + " / " + Clock(duration), Theme.Dim);
            float target = ui.PlaylistProgress(position, Mathf.Max(duration, .01f));
            if (_clip != null && !Mathf.Approximately(target, position)) Seek(target);
            float volume = ui.PlaylistVolume(_volume, ref _muted);
            float speed = ui.Slider(I18n.T("Playlist.Speed"), _speed, .5f, 2f, "0.00×");
            if (volume != _volume) MenuPrefs.SetFloat(Pref + "Volume", _volume = volume);
            if (speed != _speed) MenuPrefs.SetFloat(Pref + "Speed", _speed = speed);
            bool shuffle = ui.Toggle(I18n.T("Playlist.Shuffle"), _shuffle);
            if (shuffle != _shuffle)
            {
                MenuPrefs.SetInt(Pref + "Shuffle", (_shuffle = shuffle) ? 1 : 0);
                _playedInShuffle.Clear();
                _shuffleHistory.Clear();
                if (_index >= 0) _playedInShuffle.Add(_index);
            }
            bool repeat = ui.Toggle(I18n.T("Playlist.Repeat"), _repeat);
            if (repeat != _repeat) MenuPrefs.SetInt(Pref + "Repeat", (_repeat = repeat) ? 1 : 0);
            if (_source != null) { _source.volume = _volume; _source.pitch = _speed; _source.mute = _muted; }
            GUILayout.BeginHorizontal();
            if (ui.PlaylistIconButton(_playing ? "Pause" : "Play", I18n.T(_playing ? "Playlist.Pause" : "Playlist.Play"))) TogglePlayback();
            if (ui.PlaylistIconButton("Stop", I18n.T("Playlist.Stop"))) StopTrack();
            if (ui.PlaylistIconButton("Arrow", I18n.T("Playlist.Previous"))) Skip(-1);
            if (ui.PlaylistIconButton("Arrow", I18n.T("Playlist.Next"), angle: 180f)) Skip(1);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            ui.EndCard();
        }

        void OnDisable() => StopTrack();

        void OnDestroy()
        {
            StopTrack();
            if (_source != null) Destroy(_source.gameObject);
        }
    }

    public partial class MenuUI
    {
        internal float PlaylistProgress(float time, float duration) =>
            DrawStyledSlider(time, 0f, duration, "Playlist.Progress");

        internal bool PlaylistLibraryRow(Rect rect, string text, bool selected, float? arrow = null)
        {
            if (selected || rect.Contains(Event.current.mousePosition))
                UI.DrawRounded(rect, selected ? Theme.BG3 : Theme.BG2, Mathf.RoundToInt(6f * UI.Scale));
            Rect label = rect;
            label.xMin += 8f * UI.Scale;
            if (arrow.HasValue)
            {
                float size = 13f * UI.Scale;
                DrawMenuIcon(new Rect(label.x, rect.center.y - size * .5f, size, size),
                    NavigationIcon(arrow.Value == 180f ? "Folder" : "Arrow"));
                label.xMin += size + 8f * UI.Scale;
            }
            var style = new GUIStyle(UI.Sty(Theme.Text, 9)) { clipping = TextClipping.Clip, wordWrap = false };
            GUI.Label(label, text, style);
            return ControlButton(rect, new GUIContent("", text), GUIStyle.none);
        }

        internal float PlaylistVolume(float value, ref bool muted)
        {
            GUILayout.BeginHorizontal();
            float size = 26f * UI.Scale;
            Rect icon = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));
            DrawMenuIcon(icon, NavigationIcon("Sound"));
            float volume = DrawStyledSlider(value, 0f, 1f, "Playlist.Volume", rowHeight: size);
            GUILayout.Label(volume.ToString("0%"), UI.Sty(Theme.Text, 10, false, TextAnchor.MiddleRight),
                GUILayout.Width(49f * UI.Scale), GUILayout.Height(size));
            float switchWidth = 44f * UI.Scale;
            Rect cell = GUILayoutUtility.GetRect(switchWidth, size, GUILayout.Width(switchWidth));
            Rect toggle = new Rect(cell.x, cell.center.y - 11f * UI.Scale, switchWidth, 22f * UI.Scale);
            DrawSwitchAt(toggle, !muted);
            if (ControlButton(toggle, new GUIContent("", I18n.T("Playlist.Sound")), GUIStyle.none)) muted = !muted;
            GUILayout.EndHorizontal();
            return volume;
        }

        internal bool PlaylistIconButton(string icon, string tooltip, float angle = 0f)
        {
            float size = 36f * UI.Scale;
            Rect rect = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));
            return IconButton(rect, NavigationIcon(icon), tooltip, angle);
        }
    }
}
