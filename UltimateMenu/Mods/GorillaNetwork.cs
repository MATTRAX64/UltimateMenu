using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace UltimateMenu
{
    // Presence is a public declaration, not authentication or a scan of other PCs.
    public sealed class GorillaNetwork : MonoBehaviour
    {
        public static GorillaNetwork Instance { get; private set; } = null!;
        const string Title = "UltimateMenu - Multijoueur", Key = "UltimateMenu", Pref = "MUM_Presence";
        const int MaxPlayers = 32, MaxVersionLength = 24;
        readonly Dictionary<int, string> _users = new Dictionary<int, string>(MaxPlayers);
        readonly Dictionary<int,string> _names=new Dictionary<int,string>();
        Room? _room;
        Player? _localPlayer;
        string _statusKey = "Network.NoRoom";
        float _nextPublish;
        bool _registered, _announce = true, _published, _advertisementKnown, _errorLogged;
        float _nextRefresh;

        void Awake()
        {
            Instance = this;
            if(!MenuPrefs.HasKey("MUM_NetworkUnified"))
            {
                bool previous=MenuPrefs.GetInt(Pref,1)==1&&MenuPrefs.GetInt("MUM_ModEnabled_"+Title,0)==1;
                MenuPrefs.SetInt(Pref,previous?1:0);MenuPrefs.SetInt("MUM_NetworkUnified",1);MenuPrefs.Save();
            }
            _announce=MenuPrefs.GetInt(Pref,0)==1;
        }
        internal static bool SharingEnabled => Instance!=null && Instance.isActiveAndEnabled && Instance._announce;
        void SetSharing(bool active)
        {
            _announce=active;MenuPrefs.SetInt(Pref,active?1:0);_nextRefresh=_nextPublish=0f;
            if(active)
            {
                foreach(string page in new[]{"Spawner.Title","Ragdoll.Title","NameTags.Title"})MenuPrefs.SetInt("MUM_ModEnabled_"+page,1);
            }
            else
            {
                GorillaSpawners.Instance?.StopNetworkSharing();
                GetComponent<GorillaRagdoll>()?.StopNetworkSharing();
                try { PhotonNetwork.LocalPlayer?.SetCustomProperties(new Hashtable{[Key]=null,["UltimateMenu.Objects1"]=null,["UltimateMenu.Ragdoll1"]=null}); }
                catch(Exception e){Log.Err("Network shutdown",e);}
                _users.Clear();_names.Clear();_published=false;_advertisementKnown=false;
            }
            MenuPrefs.Save();
        }

        void Update()
        {
            if (!_registered && MenuUI.Instance != null)
            {
                MenuUI.Instance.RegisterPage(Title, DrawPage, MenuCategory.Social, false);
                MenuUI.Instance.ConfigurePageActivation(Title,()=>_announce,SetSharing,"Network.SharingHint");
                _registered = true;
            }
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 1f;
            try
            {
                var system = NetworkSystem.Instance;
                bool inRoom = system != null ? system.InRoom : PhotonNetwork.InRoom;
                if (!inRoom)
                {
                    _users.Clear(); _names.Clear(); _room = null; _localPlayer = null;
                    _published = false; _advertisementKnown = false; _statusKey = "Network.NoRoom"; return;
                }
                var localPlayer = system != null && system.LocalPlayer != null ? system.LocalPlayer.GetPlayerRef() : PhotonNetwork.LocalPlayer;
                if (localPlayer == null) { _statusKey = "Network.Unsupported"; _users.Clear(); _names.Clear(); return; }
                if (_room != PhotonNetwork.CurrentRoom || _localPlayer != localPlayer)
                {
                    _room = PhotonNetwork.CurrentRoom; _localPlayer = localPlayer;
                    _published = false; _advertisementKnown = false; _nextPublish = 0f; _users.Clear(); _names.Clear();
                }
                bool enabled = SharingEnabled;
                bool confirmed = enabled ? localPlayer.CustomProperties[Key] is string current && current == Log.PluginVersion : localPlayer.CustomProperties[Key] == null;
                if ((!_advertisementKnown || enabled != _published || !confirmed) && Time.unscaledTime >= _nextPublish)
                {
                    _nextPublish = Time.unscaledTime + 5f;
                    string version;
                    if (enabled && !TryVersion(Log.PluginVersion, out version)) return;
                    if (localPlayer.SetCustomProperties(new Hashtable { [Key] = enabled ? Log.PluginVersion : null }))
                    {
                        _published = enabled; _advertisementKnown = true;
                    }
                }
                _statusKey = !enabled ? "Network.Hidden" : confirmed ? "Network.Ready" : "Network.Wait";
                _users.Clear(); _names.Clear();
                if(!enabled)return;
                int inspected = 0;
                ReadPlayer(localPlayer);
                if (system != null)
                {
                    var others = system.PlayerListOthers;
                    if (others != null) foreach (var player in others)
                    {
                        if (++inspected >= MaxPlayers) break;
                        if (player != null) ReadPlayer(player.GetPlayerRef());
                    }
                }
                else if (_room != null) foreach (var entry in _room.Players)
                {
                    if (++inspected >= MaxPlayers) break;
                    ReadPlayer(entry.Value);
                }
            }
            catch (Exception e)
            {
                _users.Clear(); _names.Clear();
                _statusKey = "Network.Unsupported";
                if (!_errorLogged) { _errorLogged = true; Log.Err("UltimateMenu presence", e); }
            }
        }

        void ReadPlayer(Player? player)
        {
            if (player == null) return;
            // Exact key only. Never stringify untrusted remote values.
            if (TryVersion(player.CustomProperties[Key], out var version))
            {
                _users[player.ActorNumber]=version;
                string name=player.NickName??"";
                if(name.Length>32)name=name.Substring(0,32);
                var safe=new System.Text.StringBuilder(name.Length);
                foreach(char c in name)if(!char.IsControl(c))safe.Append(c);
                _names[player.ActorNumber]=safe.ToString();
            }
        }

        internal static bool TryVersion(object? value, out string version)
        {
            version = "";
            if (!(value is string text) || text.Length < 3 || text.Length > MaxVersionLength) return false;
            for (int i = 0; i < text.Length; i++)
                if ((text[i] < '0' || text[i] > '9') && text[i] != '.') return false;
            if (!Version.TryParse(text, out var parsed) || parsed.Major < 0) return false;
            version = text; return true;
        }

        internal bool SharingObjectsAllowed => SharingEnabled;

        internal static string VersionFor(int actorNumber)
            => SharingEnabled && Instance != null && Instance._users.TryGetValue(actorNumber, out var version) ? version : "";

        void DrawPage(MenuUI ui)
        {
            ui.BeginCard();
            ui.Label(I18n.T("Network.SharingHint"),Theme.Dim,10);
            ui.Label(I18n.T(_statusKey),Theme.Dim,9);
            ui.EndCard();
            ui.Section(I18n.T("UI.MenuUsers")+" · "+_users.Count);
            if(_users.Count==0)ui.Label(I18n.T("UI.NoMenuUsers"),Theme.Dim,9);
            foreach(var user in _users)
            {
                ui.BeginCard();
                string name=_names.TryGetValue(user.Key,out string found)&&found.Length>0?found:I18n.T("Joueur #")+user.Key;
                ui.InfoRow(name,"v"+user.Value);
                ui.EndCard();
            }

        }
        void OnDisable()
        {
            GorillaSpawners.Instance?.StopNetworkSharing();
            GetComponent<GorillaRagdoll>()?.StopNetworkSharing();
            try
            {
                if (_published && _localPlayer != null)
                    _localPlayer.SetCustomProperties(new Hashtable { [Key] = null });
            }
            catch (Exception e) { Log.Err("Presence cleanup", e); }
            _published = false; _advertisementKnown = false; _users.Clear(); _names.Clear();
        }
        void OnDestroy() { if (Instance == this) Instance = null!; }
    }
}
