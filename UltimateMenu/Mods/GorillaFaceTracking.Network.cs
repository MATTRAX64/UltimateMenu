using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace UltimateMenu
{
    public sealed partial class GorillaFaceTracking
    {
        const string FacePoseKey = "UltimateMenu.Face1";
        readonly Dictionary<int, RemoteFace> _remoteFaces = new Dictionary<int, RemoteFace>();
        readonly HashSet<int> _visibleFaces = new HashSet<int>();
        readonly List<int> _removedFaces = new List<int>();
        Room _faceRoom;
        float _nextFaceNetwork, _faceHeartbeat;
        byte _faceSequence;
        byte[] _publishedFace;

        sealed class RemoteFace
        {
            public MeshRenderer Renderer;
            public Material Original, Overlay;
            public Texture2D Texture;
            public byte[] Packet;
            public float LastSeen;
        }

        void UpdateFaceNetwork(bool publish)
        {
            if (!GorillaNetwork.SharingEnabled || !PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null)
            {
                StopFaceNetwork();
                return;
            }
            if (_faceRoom != PhotonNetwork.CurrentRoom)
            {
                ClearRemoteFaces();
                _faceRoom = PhotonNetwork.CurrentRoom;
                _publishedFace = null;
                _nextFaceNetwork = 0f;
            }
            if (!publish) ClearPublishedFace();
            float now = Time.unscaledTime;
            if (now < _nextFaceNetwork) return;
            _nextFaceNetwork = now + .12f;
            if (publish)
            {
                byte[] pose = {
                    (byte)(_lastGaze.x + 4), (byte)(_lastGaze.y + 3),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(_lastShape.x * 10f), 0, 60),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(_lastShape.y * 10f), 0, 60),
                    (byte)(_lastShape.z + 4), (byte)(_lastShape.w + 4),
                    (byte)(_lastAngles.x + 35), (byte)(_lastAngles.y + 35),
                    (byte)Mathf.RoundToInt(_lastPupilSize * 20f), (byte)_lastRigMouthFrame,
                    _faceSequence
                };
                if (!SameFacePose(pose, _publishedFace) || now >= _faceHeartbeat)
                {
                    pose[10] = ++_faceSequence;
                    if (PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { [FacePoseKey] = pose }))
                    {
                        _publishedFace = pose;
                        _faceHeartbeat = now + 1f;
                    }
                }
            }
            if (EnsureRigFace()) RefreshRemoteFaces(now);
        }

        static bool SameFacePose(byte[] first, byte[] second)
        {
            if (second == null) return false;
            for (int i = 0; i < 10; i++)
                if (first[i] != second[i]) return false;
            return true;
        }

        static bool ValidFacePose(byte[] p) => p != null && p.Length == 11
            && p[0] <= 8 && p[1] <= 6 && p[2] <= 60 && p[3] <= 60 && p[4] <= 8 && p[5] <= 8
            && p[6] <= 70 && p[7] <= 70 && p[8] >= 10 && p[8] <= 25 && p[9] < RigMouthPoses.Length;

        void RefreshRemoteFaces(float now)
        {
            _visibleFaces.Clear();
            foreach (var rig in VRRigCache.ActiveRigs)
            {
                if (rig == null || rig.Creator == null) continue;
                int actor = rig.Creator.ActorNumber;
                if (actor == PhotonNetwork.LocalPlayer.ActorNumber || string.IsNullOrEmpty(GorillaNetwork.VersionFor(actor))) continue;
                var player = _faceRoom.GetPlayer(actor);
                if (player == null || !(player.CustomProperties[FacePoseKey] is byte[] packet) || !ValidFacePose(packet)) continue;
                if (!_remoteFaces.TryGetValue(actor, out var remote))
                {
                    var renderer = rig.transform.Find("rig/head/gorillaface")?.GetComponent<MeshRenderer>();
                    if (renderer == null)
                    {
                        foreach (var candidate in rig.GetComponentsInChildren<MeshRenderer>(true))
                        {
                            if (!string.Equals(candidate.name, "gorillaface", System.StringComparison.OrdinalIgnoreCase)) continue;
                            renderer = candidate;
                            break;
                        }
                    }
                    if (renderer == null || renderer.sharedMaterial == null) continue;
                    var original = renderer.sharedMaterial;
                    int slot = original.HasProperty("_MouthMap") ? Shader.PropertyToID("_MouthMap")
                        : original.HasProperty("_BaseMap") ? Shader.PropertyToID("_BaseMap")
                        : original.HasProperty("_MainTex") ? Shader.PropertyToID("_MainTex") : -1;
                    if (slot == -1) continue;
                    remote = new RemoteFace {
                        Renderer = renderer, Original = original,
                        Overlay = new Material(original),
                        Texture = new Texture2D(Atlas, Atlas, TextureFormat.RGBA32, false) {
                            filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
                        }
                    };
                    remote.Overlay.SetTexture(slot, remote.Texture);
                    _remoteFaces.Add(actor, remote);
                }
                if (remote.Renderer == null || !remote.Renderer.transform.IsChildOf(rig.transform)) continue;
                bool changed = !SameFacePose(packet, remote.Packet);
                if (changed || packet[10] != remote.Packet[10]) remote.LastSeen = now;
                remote.Packet = packet;
                if (now - remote.LastSeen > 2.5f)
                {
                    if (remote.Renderer.sharedMaterial == remote.Overlay) remote.Renderer.sharedMaterial = remote.Original;
                    _visibleFaces.Add(actor);
                    continue;
                }
                _visibleFaces.Add(actor);
                if (changed)
                    RenderFace(remote.Texture, false, new Vector2Int(packet[0] - 4, packet[1] - 3),
                        new Vector4(packet[2] / 10f, packet[3] / 10f, packet[4] - 4, packet[5] - 4),
                        new Vector4(packet[6] - 35, packet[7] - 35, 0, 0), packet[8] / 20f, packet[9]);
                if (remote.Renderer.sharedMaterial != remote.Overlay)
                {
                    remote.Original = remote.Renderer.sharedMaterial;
                    remote.Renderer.sharedMaterial = remote.Overlay;
                }
            }
            _removedFaces.Clear();
            foreach (var pair in _remoteFaces)
                if (!_visibleFaces.Contains(pair.Key)) _removedFaces.Add(pair.Key);
            foreach (int actor in _removedFaces)
            {
                ReleaseRemoteFace(_remoteFaces[actor]);
                _remoteFaces.Remove(actor);
            }
        }

        void ReleaseRemoteFace(RemoteFace remote)
        {
            if (remote.Renderer != null && remote.Renderer.sharedMaterial == remote.Overlay)
                remote.Renderer.sharedMaterial = remote.Original;
            Destroy(remote.Overlay);
            Destroy(remote.Texture);
        }

        void ClearRemoteFaces()
        {
            foreach (var remote in _remoteFaces.Values) ReleaseRemoteFace(remote);
            _remoteFaces.Clear();
        }

        void ClearPublishedFace()
        {
            if (_publishedFace != null && _faceRoom == PhotonNetwork.CurrentRoom && PhotonNetwork.InRoom)
                PhotonNetwork.LocalPlayer?.SetCustomProperties(new Hashtable { [FacePoseKey] = null });
            _publishedFace = null;
        }

        void StopFaceNetwork()
        {
            ClearPublishedFace();
            ClearRemoteFaces();
            _faceRoom = null;
        }
    }
}
