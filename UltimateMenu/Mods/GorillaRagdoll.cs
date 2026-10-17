#region GorillaRagdoll
namespace UltimateMenu
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Globalization;
    using Photon.Pun;
    using Photon.Realtime;
    using UnityEngine;
    using Hashtable=ExitGames.Client.Photon.Hashtable;

    public sealed class GorillaRagdoll : MonoBehaviour
    {
        const string Title="Ragdoll.Title",NetworkKey="UltimateMenu.Ragdoll1";
        const int MaxRemote=8,MaxBones=12;
        bool Enabled=>MenuUI.Instance!=null&&MenuUI.Instance.IsModEnabled(Title);
        bool _registered,_hide=true;
        bool _network=>GorillaNetwork.SharingEnabled;
        bool _wasNetwork;
        float _speed=2f,_random=1f,_stretch=.03f,_duration=15f,_nextPoll;
        string _status="";
        Vector3 _vector=new Vector3(0,.35f,1),_rotation;
        readonly string[] _vectorDraft=new string[3],_rotationDraft=new string[3];
        bool _directionDirty;
        float _delay,_pendingStart=-1f;
        bool _closeMenu=true;
        bool _advanced;
        string _speedDraft,_randomDraft;
        int _collisionMode;
        Doll _local;
        Player _publisher;
        Room _room;
        readonly List<int> _expiredRemote=new List<int>();
        readonly Dictionary<int,Doll> _remote=new Dictionary<int,Doll>();
        readonly Dictionary<int,float> _seenRemote=new Dictionary<int,float>();
        sealed class Doll
        {
            internal GameObject Root;
            internal BoneDriver RootDriver;
            internal Transform VisualRoot;
            internal Vector3 VisualOffset;
            internal Quaternion VisualRotation;
            internal VRRig Source;
            internal float Seed,Expires;
            internal Vector3 Origin;
            internal bool Hide;
            internal float[] Packet;
            internal readonly List<Rigidbody> Bodies=new List<Rigidbody>();
            internal readonly Dictionary<Renderer,bool> Originals=new Dictionary<Renderer,bool>();
            internal readonly Dictionary<Renderer,bool> ForcedRendering=new Dictionary<Renderer,bool>();
            internal readonly List<BoneDriver> Drivers=new List<BoneDriver>();
            internal readonly Dictionary<Transform,Vector3> FixedBonePositions=new Dictionary<Transform,Vector3>();
            internal readonly Dictionary<Transform,Quaternion> FixedBoneRotations=new Dictionary<Transform,Quaternion>();
            internal readonly Dictionary<Transform,Vector3> FixedBoneScales=new Dictionary<Transform,Vector3>();
            internal readonly List<Mesh> Meshes=new List<Mesh>();
        }
        sealed class BoneDriver
        {
            internal Transform Bone;
            internal Rigidbody Body;
            internal Vector3 RestPosition;
            internal bool Root;
        }
        void ApplyCollisionLayer(Doll d)
        {
            int layer=8,groundMask=0;
            var player=GorillaLocomotion.GTPlayer.Instance;
            if(player!=null)groundMask=player.locomotionEnabledLayers.value;
            // Include the real floor too: locomotion masks can change with the player's state.
            Vector3 probe=d.Source.head.rigTarget!=null?d.Source.head.rigTarget.position:d.Source.transform.position;
            float closest=float.MaxValue;int floorLayer=-1;
            foreach(var hit in Physics.RaycastAll(probe+Vector3.up*.1f,Vector3.down,10f,~0,QueryTriggerInteraction.Ignore))
            {
                var collider=hit.collider;
                if(collider==null||collider.attachedRigidbody!=null||collider.transform.IsChildOf(d.Source.transform)||
                    (player!=null&&collider.transform.IsChildOf(player.transform))||hit.distance>=closest)continue;
                closest=hit.distance;floorLayer=collider.gameObject.layer;
            }
            if(floorLayer>=0)groundMask|=1<<floorLayer;
            if(_collisionMode==0)
            {
                if(groundMask!=0&&(groundMask&(1<<layer))==0)
                    for(int i=0;i<32;i++)if((groundMask&(1<<i))!=0){layer=i;break;}
            }
            else
            {
                // Cosmetic colliders can come first in a rig; use the actual locomotion body.
                var body=player!=null?player.bodyCollider:null;
                if(body!=null)layer=body.gameObject.layer;
                else
                {
                    var collider=d.Source.GetComponentsInChildren<Collider>(true).FirstOrDefault(c=>!c.isTrigger&&c.name.IndexOf("body",StringComparison.OrdinalIgnoreCase)>=0);
                    if(collider!=null)layer=collider.gameObject.layer;
                }
            }
            foreach(var body in d.Bodies)
            {
                foreach(var node in body.GetComponentsInChildren<Transform>(true))node.gameObject.layer=layer;
                // Per-body overrides keep map contact even when the game's layer matrix changes.
                body.includeLayers=groundMask;body.excludeLayers=0;
                body.detectCollisions=true;body.constraints=RigidbodyConstraints.None;
                body.WakeUp();
            }
            Log.Info("Ragdoll collision layer: "+layer+" / "+LayerMask.LayerToName(layer)+"; ground mask="+groundMask+"; floor="+floorLayer);
        }
        void Awake()
        {
            _collisionMode=MenuPrefs.GetInt("MUM_RagdollCollisionLayer",0)==1?1:0;

            _hide=MenuPrefs.GetInt("MUM_RagdollHide",1)==1;
            _speed=ValidSpeed(MenuPrefs.GetFloat("MUM_RagdollSpeed",2),2);
            _random=ValidSpeed(MenuPrefs.GetFloat("MUM_RagdollRandom",1),1);
            _speedDraft=_speed.ToString("G",CultureInfo.InvariantCulture);
            _randomDraft=_random.ToString("G",CultureInfo.InvariantCulture);
            _stretch=Mathf.Clamp(MenuPrefs.GetFloat("MUM_RagdollStretch",.03f),0,.15f);
            _duration=Mathf.Clamp(MenuPrefs.GetFloat("MUM_RagdollDuration",15),5,30);
            _delay=Mathf.Clamp(MenuPrefs.GetFloat("MUM_RagdollDelay",0),0,15);
            _closeMenu=MenuPrefs.GetInt("MUM_RagdollCloseMenu",1)==1;
            for(int i=0;i<3;i++)
            {
                float v=MenuPrefs.GetFloat("MUM_RagdollVector"+i,_vector[i]);
                float r=MenuPrefs.GetFloat("MUM_RagdollRotation"+i,0);
                _vector[i]=float.IsNaN(v)||float.IsInfinity(v)?0:v;
                _rotation[i]=float.IsNaN(r)||float.IsInfinity(r)?0:Mathf.Clamp(r,-180,180);
                _vectorDraft[i]=_vector[i].ToString("0.###",CultureInfo.InvariantCulture);
                _rotationDraft[i]=_rotation[i].ToString("0.###",CultureInfo.InvariantCulture);
            }
        }
        void Update()
        {
            if(!_registered&&MenuUI.Instance!=null){MenuUI.Instance.RegisterPage(Title,DrawPage,MenuCategory.Experimental);_registered=true;}
            // Receiving a peer does not require enabling our own ragdoll controls.
            if(!Enabled){_pendingStart=-1;if(_local!=null)RestoreLocal();else if(_publisher!=null)ClearPublished();}
            if(_wasNetwork!=_network){_wasNetwork=_network;Publish();if(!_network)ClearRemote();}
            if(_pendingStart>=0&&Time.unscaledTime>=_pendingStart){_pendingStart=-1;Trigger();}
            if(_local!=null&&Expired(_local))RestoreLocal();
            _expiredRemote.Clear();
            foreach(var pair in _remote)if(Expired(pair.Value))_expiredRemote.Add(pair.Key);
            foreach(int id in _expiredRemote){Dispose(_remote[id]);_remote.Remove(id);}
            if(Time.unscaledTime<_nextPoll)return;
            _nextPoll=Time.unscaledTime+.35f;
            if(_room!=PhotonNetwork.CurrentRoom)
            {
                ClearRemote();_room=PhotonNetwork.CurrentRoom;
                if(_local!=null)Publish();
            }
            PollRemote();
        }
        static bool Expired(Doll d)=>d.Root==null||d.Source==null||Time.unscaledTime>d.Expires||d.Bodies.Count==0||
            d.Bodies[0]==null;
        void LateUpdate()
        {
            if(_local!=null){UpdatePose(_local);ApplyHidden(_local);}
            foreach(var d in _remote.Values){UpdatePose(d);ApplyHidden(d);}
        }
        static void UpdatePose(Doll d)
        {
            // Move every unclassified bone with the freely simulated torso, not the launch container.
            var root=d.RootDriver;
            if(root!=null&&root.Body!=null&&d.VisualRoot!=null)
            {
                d.VisualRoot.SetPositionAndRotation(root.Body.position+root.Body.rotation*d.VisualOffset,
                    root.Body.rotation*d.VisualRotation);
            }
            foreach(var bone in d.FixedBonePositions)if(bone.Key!=null)bone.Key.localPosition=bone.Value;
            foreach(var bone in d.FixedBoneRotations)if(bone.Key!=null)bone.Key.localRotation=bone.Value;
            foreach(var bone in d.FixedBoneScales)if(bone.Key!=null)bone.Key.localScale=bone.Value;
            foreach(var driver in d.Drivers)
            {
                if(driver.Bone==null||driver.Body==null)continue;
                RagdollVisualPose.Apply(driver.Bone,driver.Body.transform,driver.Root,driver.RestPosition);
            }
        }
        static void TrackOriginal(Doll d,Renderer source)
        {
            if(source==null||d.Originals.ContainsKey(source))return;
            d.Originals[source]=source.enabled;d.ForcedRendering[source]=source.forceRenderingOff;
        }
        static void RestoreRendering(Doll d)
        {
            foreach(var source in d.Originals)if(source.Key!=null)
            {source.Key.enabled=source.Value;source.Key.forceRenderingOff=d.ForcedRendering[source.Key];}
        }
        static void ApplyHidden(Doll d)
        {
            if(!d.Hide)return;
            foreach(var original in d.Originals)if(original.Key!=null){original.Key.forceRenderingOff=true;original.Key.enabled=false;}
        }
        void Save()
        {
            MenuPrefs.SetInt("MUM_RagdollCollisionLayer",_collisionMode);
            MenuPrefs.SetInt("MUM_RagdollHide",_hide?1:0);
            MenuPrefs.SetFloat("MUM_RagdollSpeed",_speed);MenuPrefs.SetFloat("MUM_RagdollRandom",_random);
            MenuPrefs.SetFloat("MUM_RagdollStretch",_stretch);MenuPrefs.SetFloat("MUM_RagdollDuration",_duration);
            MenuPrefs.SetFloat("MUM_RagdollDelay",_delay);MenuPrefs.SetInt("MUM_RagdollCloseMenu",_closeMenu?1:0);
        }
        void BeginActivation()
        {
            if(!Enabled)return;
            if(_directionDirty){_status="Ragdoll.ValidateFirst";return;}
            if(_delay>0){_pendingStart=Time.unscaledTime+_delay;_status="";}
            else Trigger();
            if(_closeMenu&&(_pendingStart>=0||_local!=null))MenuUI.Instance?.HideForCapture();
        }
        void Trigger()
        {
            if(!Enabled||_local!=null)return;
            if(_directionDirty){_status="Ragdoll.ValidateFirst";return;}
            var rig=VRRig.LocalRig;
            if(rig==null){_status="Ragdoll.NotReady";return;}
            float seed=new System.Random().Next(1,16000000);
            Quaternion view=rig.head.rigTarget!=null?rig.head.rigTarget.rotation:rig.transform.rotation;
            _local=Build(rig,seed,_speed,_random,_stretch,_hide,_duration,rotation:rig.transform.rotation*Quaternion.Euler(_rotation),worldLaunch:view*_vector);
            if(_local==null){_status="Ragdoll.NoSkeleton";return;}
            _status="";Publish();
        }
        void Publish()
        {
            try
            {
                var player=PhotonNetwork.LocalPlayer;
                if(player==null)return;
                if(_publisher!=null&&_publisher!=player)ClearPublished();
                _publisher=player;
                float[] packet=null;
                if(_network&&_local!=null&&PhotonNetwork.InRoom)
                {
                    Transform root=_local.Root.transform;var p=root.position;var q=root.rotation;
                    packet=(float[])_local.Packet.Clone();packet[12]=_hide?1:0;
                }
                player.SetCustomProperties(new Hashtable{{NetworkKey,packet}});
            }
            catch(Exception e){Log.Err("Ragdoll publish",e);}
        }
        void ClearPublished()
        {
            if(_publisher==null)return;
            try{_publisher.SetCustomProperties(new Hashtable{{NetworkKey,null}});}catch(Exception e){Log.Err("Ragdoll cleanup",e);}
            _publisher=null;
        }
        void PollRemote()
        {
            if(!_network||!PhotonNetwork.InRoom){ClearRemote();return;}
            var alive=new HashSet<int>();
            foreach(var player in PhotonNetwork.PlayerList)
            {
                if(player==null||player.IsLocal||GorillaNetwork.VersionFor(player.ActorNumber).Length==0)continue;
                object value=player.CustomProperties[NetworkKey];
                if(!RagdollPacket.Valid(value))continue;
                float[] p=(float[])value;alive.Add(player.ActorNumber);
                if(_remote.TryGetValue(player.ActorNumber,out var existing)&&existing.Seed==p[1])
                {
                    if(existing.Hide!=(p[12]==1))
                    {RestoreRendering(existing);existing.Hide=p[12]==1;}
                    continue;
                }
                if(existing!=null){Dispose(existing);_remote.Remove(player.ActorNumber);}
                if(_seenRemote.TryGetValue(player.ActorNumber,out float seen)&&seen==p[1])continue;
                if(_remote.Count>=MaxRemote)continue;
                var rig=GorillaGameManager.instance!=null?GorillaGameManager.instance.FindPlayerVRRig(player):null;
                if(rig==null||rig.isOfflineVRRig||rig.Creator==null||rig.Creator.ActorNumber!=player.ActorNumber)continue;
                Vector3 position=new Vector3(p[2],p[3],p[4]);
                if((position-rig.transform.position).sqrMagnitude>100f)continue;
                var d=Build(rig,p[1],p[9],p[10],p[11],p[12]==1,p[14],position,new Quaternion(p[5],p[6],p[7],p[8]),p[13],new Vector3(p[15],p[16],p[17]));
                if(d==null)continue;
                _remote[player.ActorNumber]=d;
                _seenRemote[player.ActorNumber]=p[1];
            }
            foreach(int id in _remote.Keys.ToArray())if(!alive.Contains(id)){Dispose(_remote[id]);_remote.Remove(id);}
            foreach(int id in _seenRemote.Keys.ToArray())if(!alive.Contains(id))_seenRemote.Remove(id);
        }
        static string BoneName(Transform t)=>new string(t.name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        static int Depth(Transform t){int depth=0;for(;t!=null;t=t.parent)depth++;return depth;}
        static void CopyProperties(Renderer source,Renderer target)
        {
            var block=new MaterialPropertyBlock();source.GetPropertyBlock(block);target.SetPropertyBlock(block);
            for(int i=0;i<Mathf.Min(16,source.sharedMaterials.Length);i++)
            {block.Clear();source.GetPropertyBlock(block,i);if(!block.isEmpty)target.SetPropertyBlock(block,i);}
        }
        static bool IsBodyBone(string name)=>RagdollSkeleton.BodyBone(name);
        static void AttachAtBodyAnchor(Transform target,Transform original,Transform originalAnchor,Transform bodyBone,Transform bodySource)
        {
            target.SetParent(bodyBone,false);
            Vector3 anchorScale=originalAnchor.lossyScale,boneScale=bodySource.lossyScale;
            Vector3 ratio=new Vector3(anchorScale.x/Mathf.Max(.001f,Mathf.Abs(boneScale.x)),anchorScale.y/Mathf.Max(.001f,Mathf.Abs(boneScale.y)),anchorScale.z/Mathf.Max(.001f,Mathf.Abs(boneScale.z)));
            Quaternion correction=Quaternion.Inverse(bodySource.rotation)*originalAnchor.rotation;
            target.localPosition=correction*Vector3.Scale(originalAnchor.InverseTransformPoint(original.position),ratio);
            target.localRotation=correction*(Quaternion.Inverse(originalAnchor.rotation)*original.rotation);
            Vector3 originalScale=original.lossyScale;
            target.localScale=new Vector3(originalScale.x/Mathf.Max(.001f,Mathf.Abs(boneScale.x)),originalScale.y/Mathf.Max(.001f,Mathf.Abs(boneScale.y)),originalScale.z/Mathf.Max(.001f,Mathf.Abs(boneScale.z)));
        }
        Doll Build(VRRig rig,float seed,float speed,float random,float stretch,bool hide,float duration,Vector3? position=null,Quaternion? rotation=null,float? scale=null,Vector3? worldLaunch=null)
        {
            var d=new Doll{Source=rig,Seed=seed,Hide=hide,Expires=Time.unscaledTime+duration};
            try
            {
                var worn=new HashSet<Renderer>();
                var outfitRoots=new HashSet<GameObject>();
                var selectedOutfitRoots=new HashSet<GameObject>();
                Action<IEnumerable<GameObject>> addRoots=items=>{if(items!=null)foreach(var item in items)if(item!=null&&item.activeInHierarchy)outfitRoots.Add(item);};
                Action<IEnumerable<GameObject>> addSelected=items=>{if(items!=null)foreach(var item in items)if(item!=null&&item.activeInHierarchy){outfitRoots.Add(item);selectedOutfitRoots.Add(item);}};
                addSelected(rig.activeCosmetics);addRoots(rig.cosmetics);addRoots(rig.overrideCosmetics);
                if(rig.cosmeticsObjectRegistry!=null&&rig.mergedSet!=null&&rig.mergedSet.items!=null)
                    foreach(var item in rig.mergedSet.items)
                    {
                        if(item.isNullItem||string.IsNullOrEmpty(item.itemName))continue;
                        var equipped=rig.cosmeticsObjectRegistry.Cosmetic(item.itemName);
                        if(equipped==null)continue;
                        addSelected(equipped.objects);addSelected(equipped.leftObjects);addSelected(equipped.rightObjects);addSelected(equipped.holdableObjects);
                    }
                foreach(var item in outfitRoots)
                    foreach(var renderer in item.GetComponentsInChildren<Renderer>(true))
                        if(renderer.gameObject.activeInHierarchy&&(renderer.enabled||selectedOutfitRoots.Contains(item)))worn.Add(renderer);
                var skins=worn.OfType<SkinnedMeshRenderer>().Where(s=>s.sharedMesh!=null&&s.bones.Length>0).Take(32).ToArray();
                var skin=rig.mainSkin;
                if(skin==null||skin.sharedMesh==null||skin.bones.Length==0)
                    skin=skins.OrderByDescending(s=>s.bones.Count(b=>b!=null&&IsBodyBone(BoneName(b)))).FirstOrDefault();
                if(skin==null)return null;
                float modelHeight=Mathf.Clamp(skin.bounds.size.y,.3f,5f);
                d.Root=new GameObject("UltimateMenu Ragdoll");d.Root.transform.SetPositionAndRotation(position??rig.transform.position,rotation??rig.transform.rotation);
                d.Root.transform.localScale=scale.HasValue?Vector3.one*scale.Value:rig.transform.lossyScale;d.Origin=d.Root.transform.position;
                var visualRoot=new GameObject("VisualSkeleton").transform;
                visualRoot.SetParent(d.Root.transform,false);
                // The simulation container stays fixed. Moving the visual root must never move all rigidbodies with it.
                var map=new Dictionary<Transform,Transform>{{rig.transform,visualRoot}};
                Func<Transform,Transform> copy=null;
                copy=source=>
                {
                    if(source==null)return null;
                    if(map.TryGetValue(source,out var found))return found;
                    if(map.Count>=1024)return null;
                    if(!source.IsChildOf(rig.transform))
                    {
                        if(source!=rig.faceSkin?.transform&&!outfitRoots.Any(root=>source==root.transform||source.IsChildOf(root.transform)))return null;
                        // Equipped items can be docked outside VRRig. Snapshot their world pose relative to the copied rig.
                        var external=new GameObject(source.name);
                        if(source.parent!=null&&outfitRoots.Any(root=>source.parent==root.transform||source.parent.IsChildOf(root.transform)))
                        {
                            external.transform.SetParent(copy(source.parent),false);
                            external.transform.localPosition=source.localPosition;external.transform.localRotation=source.localRotation;external.transform.localScale=source.localScale;
                        }
                        else
                        {
                            external.transform.SetParent(d.Root.transform,false);
                            external.transform.localPosition=rig.transform.InverseTransformPoint(source.position);
                            external.transform.localRotation=Quaternion.Inverse(rig.transform.rotation)*source.rotation;
                            Vector3 rigScale=rig.transform.lossyScale,s=source.lossyScale;
                            external.transform.localScale=new Vector3(s.x/Mathf.Max(.001f,Mathf.Abs(rigScale.x)),s.y/Mathf.Max(.001f,Mathf.Abs(rigScale.y)),s.z/Mathf.Max(.001f,Mathf.Abs(rigScale.z)));
                        }
                        map[source]=external.transform;return external.transform;
                    }
                    Transform parent=copy(source.parent);if(parent==null)return null;
                    var go=new GameObject(source.name);go.transform.SetParent(parent,false);
                    go.transform.localPosition=source.localPosition;go.transform.localRotation=source.localRotation;go.transform.localScale=source.localScale;
                    map[source]=go.transform;return go.transform;
                };
                // Always include the authoritative body even when many cosmetics are attached.
                foreach(var source in new[]{skin}.Concat(skins.Where(s=>s!=skin)))
                {
                    var target=copy(source.transform);if(target==null)continue;
                    Transform[] bones=source.bones.Select(b=>copy(b)).ToArray();
                    if(bones.Any(b=>b==null)){Log.Info("Ragdoll: cosmetic skipped, unmapped bones: "+source.name);continue;}
                    var renderer=target.gameObject.AddComponent<SkinnedMeshRenderer>();
                    var meshCopy=Instantiate(source.sharedMesh);meshCopy.name=source.sharedMesh.name+"_RagdollSnapshot";
                    d.Meshes.Add(meshCopy);renderer.sharedMesh=meshCopy;
                    renderer.bones=bones;renderer.rootBone=copy(source.rootBone);renderer.sharedMaterials=source.sharedMaterials;
                    renderer.updateWhenOffscreen=true;renderer.localBounds=source.localBounds;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;
                    renderer.quality=source.quality;
                    CopyProperties(source,renderer);
                    for(int i=0;i<Mathf.Min(128,source.sharedMesh.blendShapeCount);i++)renderer.SetBlendShapeWeight(i,source.GetBlendShapeWeight(i));
                    TrackOriginal(d,source);
                }
                if(d.Originals.Count==0){Dispose(d);return null;}
                var sourceBones=skin.bones.Where(b=>b!=null).Distinct().ToList();
                // Tracking jobs scale and position these live bones every frame. Use mesh bind poses, not their controller pose.
                Matrix4x4[] bindPoses=skin.sharedMesh.bindposes;
                var meshWorld=copy(skin.transform).localToWorldMatrix;
                var restWorld=new Dictionary<Transform,Matrix4x4>();
                Transform[] skinBones=skin.bones;
                for(int i=0;i<Mathf.Min(bindPoses.Length,skinBones.Length);i++)
                    if(skinBones[i]!=null)restWorld[skinBones[i]]=meshWorld*bindPoses[i].inverse;
                if(restWorld.Count!=sourceBones.Count)throw new InvalidOperationException("Incomplete body bind poses; cannot detach tracking safely.");
                foreach(var source in sourceBones.OrderBy(Depth))
                {
                    Transform bone=copy(source);Matrix4x4 pose=restWorld[source];
                    bone.SetPositionAndRotation(pose.GetColumn(3),pose.rotation);
                    Vector3 worldScale=pose.lossyScale,parentScale=bone.parent!=null?bone.parent.lossyScale:Vector3.one;
                    bone.localScale=new Vector3(worldScale.x/NonZero(parentScale.x),worldScale.y/NonZero(parentScale.y),worldScale.z/NonZero(parentScale.z));
                }
                // Select the bone carrying torso vertices, not the common IK-target ancestor.
                var weights=new float[skinBones.Length];
                foreach(var w in skin.sharedMesh.boneWeights)
                {
                    if(w.boneIndex0<weights.Length)weights[w.boneIndex0]+=w.weight0;
                    if(w.boneIndex1<weights.Length)weights[w.boneIndex1]+=w.weight1;
                    if(w.boneIndex2<weights.Length)weights[w.boneIndex2]+=w.weight2;
                    if(w.boneIndex3<weights.Length)weights[w.boneIndex3]+=w.weight3;
                }
                Transform torso=sourceBones.Where(b=>!IsBodyBone(BoneName(b))&&!BoneName(b).Contains("finger")&&!BoneName(b).Contains("thumb")&&!BoneName(b).Contains("index"))
                    .OrderByDescending(b=>Enumerable.Range(0,skinBones.Length).Where(i=>skinBones[i]==b).Sum(i=>weights[i])).FirstOrDefault()
                    ??skin.rootBone??sourceBones[0];
                var selected=new List<Transform>{torso};
                foreach(var b in sourceBones)
                {
                    string n=BoneName(b);
                    if(n.Contains("finger")||n.Contains("thumb")||n.Contains("index"))continue;
                    if(IsBodyBone(n))
                        if(!selected.Contains(b)&&selected.Count<MaxBones)selected.Add(b);
                }
                if(selected.Count<3)
                {Log.Info("Ragdoll: unsupported skeleton: "+string.Join(", ",sourceBones.Take(32).Select(b=>b.name)));Dispose(d);return null;}
                var bodies=new Dictionary<Transform,Rigidbody>();
                var parents=new Dictionary<Transform,Transform>();
                foreach(var bone in selected)
                {
                    string wanted=RagdollSkeleton.ParentPart(RagdollSkeleton.Part(BoneName(bone)));
                    Transform anatomical=wanted.Length>0?selected.FirstOrDefault(b=>b!=bone&&RagdollSkeleton.Part(BoneName(b))==wanted):null;
                    parents[bone]=anatomical??torso;
                }
                // The live rig uses independent IK targets. Rebuild anatomical chains on the copy only.
                foreach(var bone in selected)if(bone!=torso)copy(bone)?.SetParent(copy(parents[bone]),true);
                // Independent palm/finger targets must not remain attached to the launch skeleton.
                foreach(var bone in sourceBones)
                {
                    string parentName=RagdollSkeleton.FingerParent(BoneName(bone));
                    if(parentName.Length==0)continue;
                    var parent=sourceBones.FirstOrDefault(b=>BoneName(b)==parentName);
                    if(parent!=null)copy(bone).SetParent(copy(parent),true);
                }
                foreach(var bone in selected)
                {
                    Transform target=copy(bone);if(target==null)continue;
                    var proxy=new GameObject("Physics_"+bone.name);
                    proxy.transform.SetParent(d.Root.transform,true);
                    proxy.transform.SetPositionAndRotation(target.position,target.rotation);
                    var rb=proxy.AddComponent<Rigidbody>();rb.mass=bone==torso?4f:1f;
                    rb.isKinematic=false;rb.useGravity=true;rb.constraints=RigidbodyConstraints.None;
                    rb.drag=.12f;rb.angularDrag=.3f;rb.interpolation=RigidbodyInterpolation.Interpolate;
                    rb.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;rb.maxAngularVelocity=12f;
                    rb.solverIterations=12;rb.solverVelocityIterations=6;
                    rb.maxDepenetrationVelocity=2f;rb.sleepThreshold=.015f;
                    bodies[bone]=rb;d.Bodies.Add(rb);
                    d.Drivers.Add(new BoneDriver{Bone=target,Body=rb,Root=bone==torso,
                        RestPosition=target.localPosition});
                }
                d.RootDriver=d.Drivers.First(driver=>driver.Root);
                d.VisualRoot=visualRoot;
                var torsoBody=bodies[torso];
                d.VisualOffset=Quaternion.Inverse(torsoBody.rotation)*(visualRoot.position-torsoBody.position);
                d.VisualRotation=Quaternion.Inverse(torsoBody.rotation)*visualRoot.rotation;
                d.Drivers.Sort((a,b)=>Depth(a.Bone).CompareTo(Depth(b.Bone)));
                var collisionMesh=new Mesh();
                copy(skin.transform).GetComponent<SkinnedMeshRenderer>().BakeMesh(collisionMesh);
                d.Meshes.Add(collisionMesh);
                var collisionVertices=collisionMesh.vertices;
                var collisionWorld=copy(skin.transform).localToWorldMatrix;
                // Assign each vertex to its nearest simulated ancestor (fingers belong to their hand).
                var owners=new Rigidbody[skinBones.Length];
                for(int i=0;i<skinBones.Length;i++)
                {
                    Transform bone=copy(skinBones[i]);
                    while(bone!=null)
                    {
                        var driver=d.Drivers.FirstOrDefault(x=>x.Bone==bone);
                        if(driver!=null){owners[i]=driver.Body;break;}
                        bone=bone.parent;
                    }
                }
                var fitted=new Dictionary<Rigidbody,Bounds>();
                var meshWeights=skin.sharedMesh.boneWeights;
                for(int i=0;i<Mathf.Min(collisionVertices.Length,meshWeights.Length);i++)
                {
                    var w=meshWeights[i];int index=w.boneIndex0;float weight=w.weight0;
                    if(w.weight1>weight){index=w.boneIndex1;weight=w.weight1;}
                    if(w.weight2>weight){index=w.boneIndex2;weight=w.weight2;}
                    if(w.weight3>weight)index=w.boneIndex3;
                    if(index<0||index>=owners.Length||owners[index]==null)continue;
                    var body=owners[index];
                    Vector3 point=body.transform.InverseTransformPoint(collisionWorld.MultiplyPoint3x4(collisionVertices[i]));
                    if(fitted.TryGetValue(body,out var bounds)){bounds.Encapsulate(point);fitted[body]=bounds;}
                    else fitted[body]=new Bounds(point,Vector3.zero);
                }
                foreach(var entry in bodies)
                {
                    Transform child=selected.FirstOrDefault(b=>b!=entry.Key&&parents[b]==entry.Key&&bodies.ContainsKey(b));
                    Vector3 local=child!=null?entry.Value.transform.InverseTransformPoint(bodies[child].position):Vector3.zero;
                    string part=RagdollSkeleton.Part(BoneName(entry.Key));
                    Collider collider;
                    if((part.StartsWith("upper_")||part.StartsWith("fore_"))&&local.magnitude>.01f)
                    {
                        // Align with the actual segment instead of snapping to the nearest X/Y/Z axis.
                        var shape=new GameObject("LimbCollider");shape.layer=entry.Value.gameObject.layer;
                        shape.transform.SetParent(entry.Value.transform,false);
                        shape.transform.localPosition=local*.5f;
                        shape.transform.localRotation=Quaternion.FromToRotation(Vector3.up,local.normalized);
                        var capsule=shape.AddComponent<CapsuleCollider>();capsule.direction=1;
                        capsule.radius=Mathf.Clamp(local.magnitude*.17f,modelHeight*.025f,modelHeight*.055f);
                        capsule.height=Mathf.Max(capsule.radius*2,local.magnitude);
                        collider=capsule;
                    }
                    else if(fitted.TryGetValue(entry.Value,out var bounds))
                    {
                        var box=entry.Value.gameObject.AddComponent<BoxCollider>();
                        box.center=bounds.center;
                        box.size=Vector3.Max(bounds.size*.96f,Vector3.one*modelHeight*.02f);
                        collider=box;
                    }
                    else
                    {
                        var sphere=entry.Value.gameObject.AddComponent<SphereCollider>();
                        sphere.radius=modelHeight*.045f;collider=sphere;
                    }
                    collider.contactOffset=.002f;
                    entry.Value.ResetCenterOfMass();entry.Value.ResetInertiaTensor();
                }
                foreach(var entry in bodies)
                {
                    if(entry.Key==torso)continue;
                    var connected=bodies.TryGetValue(parents[entry.Key],out var parent)?parent:bodies[torso];
                    var joint=entry.Value.gameObject.AddComponent<ConfigurableJoint>();joint.connectedBody=connected;
                    joint.autoConfigureConnectedAnchor=false;joint.anchor=Vector3.zero;
                    joint.connectedAnchor=connected.transform.InverseTransformPoint(entry.Value.position);
                    joint.xMotion=joint.yMotion=joint.zMotion=stretch<=.001f?ConfigurableJointMotion.Locked:ConfigurableJointMotion.Limited;
                    joint.linearLimit=new SoftJointLimit{limit=stretch};
                    joint.angularXMotion=joint.angularYMotion=joint.angularZMotion=ConfigurableJointMotion.Limited;
                    joint.lowAngularXLimit=new SoftJointLimit{limit=-55};joint.highAngularXLimit=new SoftJointLimit{limit=55};
                    joint.angularYLimit=new SoftJointLimit{limit=45};joint.angularZLimit=new SoftJointLimit{limit=55};
                    joint.projectionMode=JointProjectionMode.PositionAndRotation;joint.projectionDistance=Mathf.Max(.02f,stretch);
                    joint.projectionAngle=25;
                    joint.enablePreprocessing=false;
                    // Adjacent shapes overlap at their anchor: collision there fights the joint every step.
                    joint.enableCollision=false;
                }
                // Accessories are copied as mesh-only objects and follow the nearest simulated bone.
                foreach(var source in worn.OfType<MeshRenderer>().Concat(new[]{rig.faceSkin}).Where(r=>r!=null).Distinct())
                {
                    var filter=source.GetComponent<MeshFilter>();if((source!=rig.faceSkin&&!source.gameObject.activeInHierarchy)||
                        (source!=rig.faceSkin&&!worn.Contains(source))||filter==null||filter.sharedMesh==null)continue;
                    if(source.GetComponent<TMPro.TMP_Text>()!=null)continue;
                    Transform target;
                    if(source==rig.faceSkin)
                    {
                        // The face renderer can share a tracking transform. Give its copy a private node.
                        var headSource=sourceBones.FirstOrDefault(b=>BoneName(b).Contains("head"))??torso;
                        Transform headCopy=copy(headSource);if(headCopy==null)continue;
                        target=new GameObject("RagdollFace").transform;
                        AttachAtBodyAnchor(target,source.transform,source.transform.parent??headSource,headCopy,headSource);
                    }
                    else target=copy(source.transform);
                    if(target==null)continue;
                    var meshCopy=Instantiate(filter.sharedMesh);d.Meshes.Add(meshCopy);
                    target.gameObject.AddComponent<MeshFilter>().sharedMesh=meshCopy;
                    var renderer=target.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterials=source.sharedMaterials;
                    renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;TrackOriginal(d,source);
                    CopyProperties(source,renderer);
                    Transform nearest=source==rig.faceSkin?d.Drivers.FirstOrDefault(driver=>BoneName(driver.Bone).Contains("head"))?.Bone:null;
                    if(nearest==null)nearest=d.Drivers.OrderBy(driver=>(driver.Bone.position-target.position).sqrMagnitude).First().Bone;
                    if(source!=rig.faceSkin&&!worn.Contains(source)&&nearest!=target&&!nearest.IsChildOf(target))target.SetParent(nearest,true);
                }
                // A cosmetic may have its own armature outside the body's skin hierarchy.
                foreach(var item in outfitRoots)
                {
                    if(item==null||!item.activeInHierarchy||!map.TryGetValue(item.transform,out var itemCopy))continue;
                    if(d.Drivers.Any(driver=>!driver.Root&&itemCopy.IsChildOf(driver.Bone)))continue;
                    Transform mount=item.transform.parent;string mountedPart="";
                    while(mount!=null&&mount!=rig.transform)
                    {mountedPart=RagdollSkeleton.Part(BoneName(mount));if(mountedPart.Length>0)break;mount=mount.parent;}
                    Transform mountedBone=mountedPart.Length>0?selected.FirstOrDefault(b=>RagdollSkeleton.Part(BoneName(b))==mountedPart):null;
                    if(mountedBone!=null&&mount!=null)
                    {AttachAtBodyAnchor(itemCopy,item.transform,mount,copy(mountedBone),mountedBone);continue;}
                    var sourceRenderer=item.GetComponentsInChildren<Renderer>().FirstOrDefault(r=>worn.Contains(r));
                    Vector3 center=sourceRenderer!=null?sourceRenderer.bounds.center:item.transform.position;
                    Vector3 cloneCenter=d.Root.transform.TransformPoint(rig.transform.InverseTransformPoint(center));
                    Transform nearest=d.Drivers.OrderBy(driver=>(driver.Bone.position-cloneCenter).sqrMagnitude).First().Bone;
                    if(nearest!=itemCopy&&!nearest.IsChildOf(itemCopy))itemCopy.SetParent(nearest,true);
                }
                foreach(var source in new[]{skin}.Concat(skins).SelectMany(s=>s.bones).Where(b=>b!=null).Distinct())
                {
                    Transform target=copy(source);
                    if(target!=null)
                    {
                        d.FixedBoneScales[target]=target.localScale;
                        if(!selected.Contains(source))
                        {
                            d.FixedBonePositions[target]=target.localPosition;
                            d.FixedBoneRotations[target]=target.localRotation;
                        }
                    }
                }
                foreach(var renderer in d.Root.GetComponentsInChildren<SkinnedMeshRenderer>())
                    if(renderer.bones.Any(b=>b==null||b.IsChildOf(rig.transform)))
                        throw new InvalidOperationException("Ragdoll still references live VR bones.");
                // Include the original face/spectator renderers in the hide operation, even if a camera toggles enabled later.
                foreach(var source in rig.GetComponentsInChildren<Renderer>(true))
                    if(source is MeshRenderer||source is SkinnedMeshRenderer)TrackOriginal(d,source);
                if(rig.spectatorSkin!=null)foreach(var source in rig.spectatorSkin.GetComponentsInChildren<Renderer>(true))TrackOriginal(d,source);
                ApplyCollisionLayer(d);
                var colliders=d.Root.GetComponentsInChildren<Collider>();
                foreach(var collider in colliders){collider.enabled=true;collider.isTrigger=false;}
                var playerColliders=new List<Collider>(rig.GetComponentsInChildren<Collider>(true));
                var player=GorillaLocomotion.GTPlayer.Instance;
                if(player!=null)playerColliders.AddRange(player.GetComponentsInChildren<Collider>(true));
                var joints=d.Root.GetComponentsInChildren<ConfigurableJoint>();
                foreach(var a in colliders)
                {
                    foreach(var b in colliders)if(a!=b)
                    {
                        bool adjacent=joints.Any(j=>!j.enableCollision&&((j.GetComponent<Rigidbody>()==a.attachedRigidbody&&j.connectedBody==b.attachedRigidbody)||
                            (j.GetComponent<Rigidbody>()==b.attachedRigidbody&&j.connectedBody==a.attachedRigidbody)));
                        Physics.IgnoreCollision(a,b,adjacent);
                    }
                    foreach(var b in playerColliders)if(b!=null)Physics.IgnoreCollision(a,b);
                }
                var rng=new System.Random((int)seed);
                Vector3 direction=worldLaunch??d.Root.transform.forward;
                Vector3 launch=direction*speed;
                foreach(var rb in d.Bodies)
                {
                    rb.velocity=launch+new Vector3((float)rng.NextDouble()*2-1,(float)rng.NextDouble(),(float)rng.NextDouble()*2-1)*random;
                    rb.angularVelocity=new Vector3((float)rng.NextDouble()*2-1,(float)rng.NextDouble()*2-1,(float)rng.NextDouble()*2-1)*random*2f;
                }
                var start=d.Root.transform.position;var orientation=d.Root.transform.rotation;
                d.Packet=new[]{1f,seed,start.x,start.y,start.z,orientation.x,orientation.y,orientation.z,orientation.w,speed,random,stretch,hide?1f:0f,
                    Mathf.Clamp(Mathf.Abs(d.Root.transform.lossyScale.x),.1f,5f),duration,direction.x,direction.y,direction.z};
                Log.Info("Ragdoll created: "+skin.name+"; "+d.Bodies.Count+" physical proxies; mesh bind pose restored, private meshes, no live VR bone references; all mesh bones: "+string.Join(", ",sourceBones.Select(b=>b.name))+"; simulated bones: "+string.Join(", ",selected.Select(b=>b.name)));
                ApplyHidden(d);return d;
            }
            catch(Exception e){Log.Err("Ragdoll build",e);Dispose(d);return null;}
        }
        static void Dispose(Doll d)
        {
            if(d==null)return;
            RestoreRendering(d);
            if(d.Root!=null){d.Root.SetActive(false);Destroy(d.Root);}
            foreach(var mesh in d.Meshes)if(mesh!=null)Destroy(mesh);
        }
        static float NonZero(float value)=>Mathf.Abs(value)>.00001f?value:(value<0?-.00001f:.00001f);
        void RestoreLocal(){Dispose(_local);_local=null;ClearPublished();}
        void ClearRemote(){foreach(var d in _remote.Values)Dispose(d);_remote.Clear();_seenRemote.Clear();}
        void ResetAll(){_pendingStart=-1;if(_local!=null)RestoreLocal();else if(_publisher!=null)ClearPublished();if(_remote.Count>0)ClearRemote();}
        internal void StopNetworkSharing(){ClearPublished();ClearRemote();}
        void OnDisable()=>ResetAll();
        void OnDestroy()=>ResetAll();
        static float ValidSpeed(float value,float fallback)=>float.IsNaN(value)||float.IsInfinity(value)||value<0?fallback:value;
        void DrawSpeed(MenuUI ui,string key,ref string draft,ref float value)
        {
            ui.Label(I18n.T(key)+(key=="Ragdoll.Multiplier"?" (×)":" (m/s)"),Theme.Text,10,true);
            string edited=ui.TextField(draft);
            if(edited==draft)return;
            draft=edited;
            if(float.TryParse(edited.Trim().Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out float parsed)&&
                !float.IsNaN(parsed)&&!float.IsInfinity(parsed)&&parsed>=0&&parsed!=value)
            {value=parsed;Save();}
        }
        void DrawInputs(MenuUI ui,string key,string[] values)
        {
            ui.Label(I18n.T(key),Theme.Text,10,true);
            GUILayout.BeginHorizontal();
            for(int i=0;i<3;i++)
            {
                GUILayout.Label(i==0?"X":i==1?"Y":"Z",UI.Sty(Theme.Dim,9),GUILayout.Width(16*UI.Scale));
                string edited=ui.TextField(values[i]);
                if(edited.Length>12)edited=edited.Substring(0,12);
                if(edited!=values[i]){values[i]=edited;_directionDirty=true;}
                if(i<2)GUILayout.Space(8*UI.Scale);
            }
            GUILayout.EndHorizontal();
        }
        void DrawPage(MenuUI ui)
        {
            ui.BeginCard();
            if(ui.Btn(I18n.T(_local!=null?"Ragdoll.Restore":_pendingStart>=0?"Ragdoll.Cancel":"Ragdoll.Trigger"),ui.TA,Theme.Text))
            {if(_local!=null)RestoreLocal();else if(_pendingStart>=0)_pendingStart=-1;else BeginActivation();}
            if(_pendingStart>=0)ui.Label(I18n.T("Ragdoll.Countdown")+" "+Mathf.CeilToInt(_pendingStart-Time.unscaledTime)+"s",Theme.Text,10,true);
            bool hide=ui.Toggle(I18n.T("Ragdoll.Hide"),_hide);
            if(hide!=_hide)
            {
                _hide=hide;
                if(_local!=null){RestoreRendering(_local);_local.Hide=hide;Publish();}
                Save();
            }
            if(_status.Length>0)ui.Label(I18n.T(_status),Theme.Dim,9);
            float delay=ui.IntSlider(I18n.T("Ragdoll.Delay"),(int)_delay,0,15);
            bool close=ui.Toggle(I18n.T("Ragdoll.CloseMenu"),_closeMenu);
            if(delay!=_delay||close!=_closeMenu){_delay=delay;_closeMenu=close;Save();}
            DrawSpeed(ui,"Ragdoll.Multiplier",ref _speedDraft,ref _speed);
            ui.Label("≈ "+(_vector.magnitude*_speed).ToString("0.##",CultureInfo.InvariantCulture)+" m/s",Theme.Dim,9);
            ui.EndCard();
            ui.ExpandableSection(I18n.T("VR.Advanced"),"",ref _advanced,()=>
            {
            ui.BeginCard();
            ui.Label(I18n.T("Ragdoll.CollisionLayer"),Theme.Dim,9);
            int collision=ui.ModePicker(new[]{I18n.T("Ragdoll.Map"),I18n.T("Ragdoll.Player")},_collisionMode);
            if(collision!=_collisionMode)
            {
                _collisionMode=collision;Save();
                if(_local!=null)ApplyCollisionLayer(_local);
                foreach(var doll in _remote.Values)ApplyCollisionLayer(doll);
            }
            DrawInputs(ui,"Ragdoll.Vector",_vectorDraft);
            DrawInputs(ui,"Ragdoll.Rotation",_rotationDraft);
            ui.Label(I18n.T("Ragdoll.AxesHint"),Theme.Dim,8);
            if(ui.Btn(I18n.T("Ragdoll.Validate"),ui.TB3,Theme.Text))
            {
                if(RagdollInput.TryParse(_vectorDraft,float.MaxValue,out float[] v)&&RagdollInput.TryParse(_rotationDraft,180,out float[] r))
                {
                    _vector=new Vector3(v[0],v[1],v[2]);_rotation=new Vector3(r[0],r[1],r[2]);
                    for(int i=0;i<3;i++){MenuPrefs.SetFloat("MUM_RagdollVector"+i,v[i]);MenuPrefs.SetFloat("MUM_RagdollRotation"+i,r[i]);}
                    MenuPrefs.Save();_directionDirty=false;_status="Ragdoll.Validated";
                }
                else _status="Ragdoll.InvalidInput";
            }
            ui.EndCard();
            ui.BeginCard();
            DrawSpeed(ui,"Ragdoll.Random",ref _randomDraft,ref _random);
            float stretch=ui.Slider(I18n.T("Ragdoll.Stretch"),_stretch,0,.15f,"0.00m");
            float duration=ui.Slider(I18n.T("Ragdoll.Duration"),_duration,5,30,"0s");
            if(stretch!=_stretch||duration!=_duration)
            {_stretch=stretch;_duration=duration;Save();}
            ui.EndCard();
            ui.BeginCard();
            ui.Label(I18n.T(_network?"Ragdoll.NetworkHint":"Ragdoll.LocalHint"),Theme.Dim,9);
            ui.Label(I18n.T("Ragdoll.Responsibility"),Theme.Dim,8);
            ui.EndCard();
            });
        }
    }
}
#endregion

#region RagdollInput
namespace UltimateMenu
{
    using System;
    using System.Globalization;

    internal static class RagdollInput
    {
        internal static bool TryParse(string[] text,float limit,out float[] values)
        {
            values=new float[3];
            if(text==null||text.Length!=3)return false;
            for(int i=0;i<3;i++)
            {
                if(text[i]==null||text[i].Length>12||!float.TryParse(text[i].Trim().Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out values[i])||
                    float.IsNaN(values[i])||float.IsInfinity(values[i])||Math.Abs(values[i])>limit)return false;
            }
            return true;
        }
    }
}
#endregion

#region RagdollPacket
namespace UltimateMenu
{
    using System;

    internal static class RagdollPacket
    {
        internal const int Length=18;
        internal static bool Valid(object value)
        {
            if(!(value is float[] p)||p.Length!=Length||p[0]!=1)return false;
            foreach(float f in p)if(float.IsNaN(f)||float.IsInfinity(f))return false;
            if(p[1]<1||p[1]>16000000||p[1]!=(int)p[1])return false;
            for(int i=2;i<5;i++)if(Math.Abs(p[i])>10000)return false;
            float q=p[5]*p[5]+p[6]*p[6]+p[7]*p[7]+p[8]*p[8];

            return q>.8f&&q<1.2f&&p[9]>=0&&p[10]>=0&&p[11]>=0&&p[11]<=.15f&&(p[12]==0||p[12]==1)&&p[13]>=.1f&&p[13]<=5&&p[14]>=5&&p[14]<=30;
        }
    }
}
#endregion

#region RagdollSkeleton
namespace UltimateMenu
{

    internal static class RagdollSkeleton
    {
        internal static string Part(string name)
        {
            if(name.Contains("head"))return "head";
            string side=name.Contains("left")||name.Contains("arml")||name.Contains("handl")||name.Contains("wristl")?"l":
                name.Contains("right")||name.Contains("armr")||name.Contains("handr")||name.Contains("wristr")?"r":"";
            if(side.Length==0)return "";
            if(name.Contains("forearm")||name.Contains("lowerarm")||name.Contains("elbow"))return "fore_"+side;
            if(name.Contains("upperarm")||name=="arml"||name=="armr")return "upper_"+side;
            if(name.Contains("hand")||name.Contains("wrist"))return "hand_"+side;
            return "";
        }
        // Gorilla Tag has separate palm branches; preserve each finger chain under its hand.
        internal static string FingerParent(string name)
        {
            string n=name.EndsWith("new")?name.Substring(0,name.Length-3):name;
            if(n.Length<2)return "";
            string side=n.Substring(n.Length-1);
            if(side!="l"&&side!="r")return "";
            string stem=n.Substring(0,n.Length-1);
            if(stem=="palm01"||stem=="palm02"||stem=="thumb01")return "hand"+side+"new";
            foreach(string finger in new[]{"findex","fmiddle","thumb"})
            {
                if(stem==finger+"02")return finger+"01"+side+"new";
                if(stem==finger+"03")return finger+"02"+side+"new";
            }
            if(stem=="findex01")return "palm01"+side+"new";
            if(stem=="fmiddle01")return "palm02"+side+"new";
            return "";
        }
        internal static string ParentPart(string part)=>part.StartsWith("hand_")?"fore_"+part.Substring(5):part.StartsWith("fore_")?"upper_"+part.Substring(5):"";
        // Normalized names only. Do not mistake an Armature root or cosmetic propeller for a limb.
        internal static bool BodyBone(string n)
        {
            if(string.IsNullOrEmpty(n)||n.Contains("finger")||n.Contains("thumb")||n.Contains("index")||n.Contains("pinky"))return false;
            return n.Contains("head")||n.Contains("chest")||n.Contains("spine")||n.Contains("upperarm")||n.Contains("forearm")||
                n.Contains("lowerarm")||n.Contains("hand")||n.Contains("wrist")||n.Contains("elbow")||n.Contains("hip")||
                n=="arml"||n=="armr"||n=="leftarm"||n=="rightarm"||n=="legl"||n=="legr";
        }
    }
}
#endregion

#region RagdollVisualPose
namespace UltimateMenu
{
    using UnityEngine;

    internal static class RagdollVisualPose
    {
        internal static void Apply(Transform bone,Transform physics,bool root,Vector3 restPosition)
        {
            if(root)bone.SetPositionAndRotation(physics.position,physics.rotation);
            else
            {
                bone.localPosition=restPosition;
                bone.rotation=physics.rotation;
            }
        }
    }
}
#endregion
