using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Video;
using UltimateMenu.Media;

namespace UltimateMenu
{
    // Joue en boucle les vidéos tutoriel embarquées (Assets/Menu/VideosTutorial) dans le panneau de présentation d'un mod.
    internal static class TutorialVideos
    {
        // kind (clé de PresentationKeys) -> nom du fichier embarqué
        static readonly Dictionary<string, string> Files = new Dictionary<string, string>
        {

            {"Face","GorillaFaces.mp4"},{"Camera","GorillaCamera.mp4"},{"Mocap","GorillaMocaps.mp4"},{"Spawner","GorillaSpawners.mp4"},
            {"Stream","GorillaStreams.mp4"},{"Mirror","GorillaMiror.mp4"},{"Ragdoll","GorillaRagdoll.mp4"},{"Texts","GorillaTextOverlays.mp4"},
            {"Voice","GorillaVoices.mp4"},{"Filter","GorillaFilter.mp4"},{"Weather","GorillaWeather.mp4"},{"Rooms","GorillaRooms.mp4"},{"Discord","GorillaDiscord.mp4"},
            {"Casting","GorillaCasting.mp4"},{"Tracking","GorillaTrackingSafe.mp4"},{"NameTags","GorillaNameTags.mp4"},{"Names","GorillaNames.mp4"},
            {"OpenMenu","menu.mp4"}
        };
        static TutorialVideoHost _host;

        internal static bool Has(string kind) => Files.ContainsKey(kind);

        // true = la vidéo gère l'affichage (lecture ou chargement), false = indisponible -> animation de secours
        internal static bool Draw(Rect area, string kind)
        {
            if (!Files.TryGetValue(kind, out string file)) return false;
            if (_host == null)
            {
                var go = new GameObject("UltimateMenu.TutorialVideo");
                go.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(go);
                _host = go.AddComponent<TutorialVideoHost>();
            }
            return _host.Draw(area, kind, file);
        }
    }

}

// Espace de noms séparé : Log.cs ajoute automatiquement au plugin tout MonoBehaviour de l'espace UltimateMenu (les mods).
namespace UltimateMenu.Media
{
    internal sealed class TutorialVideoHost : MonoBehaviour
    {
        const string ResourcePrefix = "UltimateMenu.Assets.Menu.VideosTutorial.";
        readonly Dictionary<string, string> _paths = new Dictionary<string, string>();
        readonly HashSet<string> _failed = new HashSet<string>();
        VideoPlayer _player;
        string _kind, _url;
        bool _prepared;
        float _lastDraw = -10f;

        internal bool Draw(Rect area, string kind, string file)
        {
            if (_failed.Contains(kind)) return false;
            if (_kind != kind && !Load(kind, file)) return false;
            _lastDraw = Time.unscaledTime;
            if (!_prepared) return true;
            if (!_player.isPlaying) _player.Play();
            Texture frame = _player.texture;
            if (frame != null) GUI.DrawTexture(area, frame, ScaleMode.ScaleToFit, false);
            return true;
        }

        // Met la lecture en pause dès que le panneau n'est plus dessiné (menu fermé, autre page, hors écran).
        void Update()
        {
            if (_player != null && _prepared && _player.isPlaying && Time.unscaledTime - _lastDraw > .35f) _player.Pause();
        }

        bool Load(string kind, string file)
        {
            try
            {
                string path = Extract(file);
                if (path == null) { _failed.Add(kind); return false; }
                if (_player == null)
                {
                    _player = gameObject.AddComponent<VideoPlayer>();
                    _player.playOnAwake = false;
                    _player.isLooping = true;
                    _player.skipOnDrop = true;
                    _player.waitForFirstFrame = true;
                    _player.source = VideoSource.Url;
                    _player.renderMode = VideoRenderMode.APIOnly;
                    _player.audioOutputMode = VideoAudioOutputMode.None;
                    _player.prepareCompleted += OnPrepared;
                    _player.errorReceived += OnError;
                }
                _player.Stop();
                _prepared = false;
                _kind = kind;
                _url = new Uri(path).AbsoluteUri;
                _player.url = _url;
                _player.Prepare();
                return true;
            }
            catch (Exception e)
            {
                Log.Info("[Tutorial] Video unavailable (" + kind + ") : " + e.Message);
                _failed.Add(kind);
                return false;
            }
        }

        void OnPrepared(VideoPlayer source)
        {
            if (source.url != _url) return; // préparation d'une vidéo précédente
            _prepared = true;
            source.Play();
        }

        void OnError(VideoPlayer source, string message)
        {
            Log.Info("[Tutorial] Video error (" + _kind + ") : " + message);
            if (_kind != null) _failed.Add(_kind);
            _prepared = false;
        }

        // VideoPlayer lit un fichier : on extrait la ressource embarquée dans le cache temporaire (une fois par session).
        string Extract(string file)
        {
            if (_paths.TryGetValue(file, out string known)) return known;
            string path = null;
            try
            {
                using (var stream = EmbeddedAssets.OpenPacked(ResourcePrefix + file))
                {
                    if (stream == null) Log.Info("[Tutorial] Missing embedded video: " + file);
                    else
                    {
                        string folder = Path.Combine(Application.temporaryCachePath, "UltimateMenu", "Tutorials");
                        Directory.CreateDirectory(folder);
                        path = Path.Combine(folder, file);
                        try { using (var output = File.Create(path)) stream.CopyTo(output); }
                        catch (IOException) { if (!File.Exists(path)) throw; }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Info("[Tutorial] Extraction failed (" + file + ") : " + e.Message);
                path = null;
            }
            _paths[file] = path;
            return path;
        }
    }
}
