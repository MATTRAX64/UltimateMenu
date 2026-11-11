#region GorillaNames
namespace UltimateMenu
{
    using UltimateMenu;
    using UnityEngine;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Collections;
    using TMPro;

    public class GorillaNames : MonoBehaviour
    {
        public static GorillaNames Instance { get; private set; }

        public bool Enabled => MenuUI.Instance != null && MenuUI.Instance.IsModEnabled("Names.Title");

        readonly Dictionary<string, string> saved = new Dictionary<string, string>();
        object outfitTMP;
        PropertyInfo outfitProp;
        string currentOutfit = "";
        object _computer; FieldInfo _computerName,_savedComputerName;
        Coroutine _nameSync;
        readonly List<TMP_Text> _outfitLabels=new List<TMP_Text>();
        readonly Dictionary<TMP_Text,string> _writtenLabels=new Dictionary<TMP_Text,string>();
        float _nextLabelScan, _nextCheck;

        void Awake() { Instance = this; Load(); }
        void Start() { Invoke(nameof(Init), 3f); MenuUI.Instance?.RegisterPage("Names.Title", Draw, MenuCategory.Gameplay); }

        void Init()
        {
            outfitTMP = FindOutfitTMP();
            if (outfitTMP != null) outfitProp = outfitTMP.GetType().GetProperty("text") ?? outfitTMP.GetType().GetProperty("Text");
            if (outfitTMP is TMP_Text label && IsOutfitLabel(label)) currentOutfit = TopLabel(label.text);
            ScanOutfitLabels();
        }

        static bool IsOutfitLabel(TMP_Text text)
        {
            if(text==null || string.IsNullOrEmpty(text.text) || text.text.Length>256) return false;
            string label=TopLabel(text.text).ToLowerInvariant();
            bool numbered=false;foreach(char c in label)if(char.IsDigit(c)){numbered=true;break;}
            if(!numbered || !(label.Contains("outfit")||label.Contains("tenue")||label.Contains("pack")))return false;
            for(Transform parent=text.transform;parent!=null;parent=parent.parent)
            {
                string name=parent.name.ToLowerInvariant();
                if(name.Contains("wardrobe")||name.Contains("outfit")||name.Contains("outfitpack"))return true;
            }
            return false;
        }
        static string TopLabel(string text)
        {
            if(string.IsNullOrEmpty(text))return "";
            int end=text.IndexOf('\n');string top=(end<0?text:text.Substring(0,end)).Trim();
            return System.Text.RegularExpressions.Regex.Replace(top,"<[^>]{0,64}>","");
        }
        void ScanOutfitLabels()
        {
            _nextLabelScan=Time.unscaledTime+3f;
            _outfitLabels.RemoveAll(text=>text==null);
            foreach(var dead in _writtenLabels.Keys.Where(text=>text==null).ToList())_writtenLabels.Remove(dead);
            if(_computer==null)
                foreach(var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type=assembly.GetType("GorillaComputer")??assembly.GetType("GorillaNetworking.GorillaComputer");
                    if(type==null)continue;
                    _computer=FindObjectOfType(type);
                    _computerName=type.GetField("currentName",BindingFlags.Public|BindingFlags.Instance);
                    _savedComputerName=type.GetField("savedName",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
                    if(_computer!=null)break;
                }
            foreach(var text in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if(_outfitLabels.Count>=64)break;
                if(!IsOutfitLabel(text)||_outfitLabels.Contains(text))continue;
                _outfitLabels.Add(text);
                _writtenLabels[text]=text.text;
                if(string.IsNullOrEmpty(currentOutfit)&&text.gameObject.activeInHierarchy)currentOutfit=TopLabel(text.text);
            }
        }

        void Update()
        {
            if(!Enabled || Time.unscaledTime<_nextCheck)return;
            _nextCheck=Time.unscaledTime+.15f;
            if(Time.unscaledTime>=_nextLabelScan)ScanOutfitLabels();
            string previousOutfit=currentOutfit;
            string top=currentOutfit;
            foreach(var label in _outfitLabels)
            {
                if(label==null||!label.gameObject.activeInHierarchy)continue;
                string value=label.text;
                if(_writtenLabels.TryGetValue(label,out string written)&&value==written)continue;
                if(IsOutfitLabel(label)){top=TopLabel(value);break;}
            }
            string currentName = GetPlayerName();
            if(!string.IsNullOrEmpty(top)&&top!=currentOutfit)
            {
            if (!string.IsNullOrEmpty(currentOutfit)) { saved[currentOutfit] = currentName; Save(); }

            if (!saved.TryGetValue(top, out string preset)) { preset = currentName; saved[top] = preset; Save(); }

            ApplyPreset(preset);
            currentOutfit = top;
            currentName=GetPlayerName();
            }
            if(string.IsNullOrEmpty(currentOutfit))return;
            if(!saved.ContainsKey(currentOutfit)){saved[currentOutfit]=currentName;Save();}
            foreach(var label in _outfitLabels)
            {
                if(label==null)continue;
                string labelOutfit=TopLabel(label.text);
                if(labelOutfit!=previousOutfit && labelOutfit!=currentOutfit)continue;
                string original=label.text;
                int lineBreak=original.IndexOf('\n');
                string heading=lineBreak<0?original:original.Substring(0,lineBreak);
                string content=heading+"\n"+currentName;
                if(original!=content)label.text=content;
                _writtenLabels[label]=content;
            }
        }

        void ApplyPreset(string name)
        {
            name=CleanName(name??"");
            if (string.IsNullOrEmpty(name)) name = "Gorilla";
            if(_nameSync!=null){StopCoroutine(_nameSync);_nameSync=null;}
            Log.Info("Names requested: "+name);
            CommitName(name);
            _nameSync=StartCoroutine(FinishNameChange(name));
        }

        void CommitName(string name)
        {
            // Wardrobe code also reads savedName: leaving it stale can restore the previous preset.
            if(_computer!=null)
            {
                _computerName?.SetValue(_computer,name);
                _savedComputerName?.SetValue(_computer,name);
            }
            var network=NetworkSystem.Instance;
            if(network!=null)
            {
                if(network.GetMyNickName()!=name)network.SetMyNickName(name);
            }
            else if(Photon.Pun.PhotonNetwork.NickName!=name)Photon.Pun.PhotonNetwork.NickName=name;
            MenuPrefs.SetString("playerName",name);MenuPrefs.Save();
            if(Photon.Pun.PhotonNetwork.InRoom&&(network==null||network.GetType().Name=="NetworkSystemPUN"))
                Photon.Pun.PhotonNetwork.SendAllOutgoingCommands();
            var rig=VRRig.LocalRig;
            if(rig!=null)rig.UpdateName();
        }

        IEnumerator FinishNameChange(string requested)
        {
            // Let the wardrobe finish its own click handler, then reconcile only the latest request.
            yield return new WaitForEndOfFrame();
            yield return new WaitForSecondsRealtime(.2f);
            if(!Enabled){_nameSync=null;yield break;}
            CommitName(requested);
            yield return new WaitForSecondsRealtime(.35f);
            _nameSync=null;
            if(!Enabled)yield break;
            var network=NetworkSystem.Instance;
            string actual=network!=null?network.GetMyNickName():Photon.Pun.PhotonNetwork.NickName;
            Log.Info("Names network check: requested="+requested+"; actual="+actual+"; inRoom="+(network!=null&&network.InRoom));
            if(actual!=requested)yield break;
            // The normal game name-change path refreshes the rig after publishing the nickname.
            // Delay it until the property update has been sent, instead of refreshing the old name.
            if(network!=null&&network.InRoom&&GorillaTagger.Instance!=null&&_computer!=null)
            {
                try
                {
                    var tagger=GorillaTagger.Instance;
                    object view=tagger.GetType().GetProperty("myVRRig",BindingFlags.Public|BindingFlags.Instance)?.GetValue(tagger);
                    var flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
                    object red=_computer.GetType().GetField("redValue",flags)?.GetValue(_computer);
                    object green=_computer.GetType().GetField("greenValue",flags)?.GetValue(_computer);
                    object blue=_computer.GetType().GetField("blueValue",flags)?.GetValue(_computer);
                    if(view!=null&&red!=null&&green!=null&&blue!=null)
                    {
                        var send=view.GetType().GetMethod("SendRPC",new[]{typeof(string),typeof(Photon.Pun.RpcTarget),typeof(object[])});
                        if(send!=null)
                        {
                            send.Invoke(view,new object[]{"RPC_InitializeNoobMaterial",Photon.Pun.RpcTarget.All,new object[]{red,green,blue}});
                            Log.Info("Names rig refresh sent: "+requested);
                        }
                    }
                }
                catch(System.Exception error){Log.Err("Names network refresh",error);}
            }
            var rig=VRRig.LocalRig;if(rig!=null)rig.UpdateName();
        }
        void OnDisable()
        {
            if(_nameSync!=null){StopCoroutine(_nameSync);_nameSync=null;}
        }

        string GetPlayerName()
        {
            string value=_computer!=null?_computerName?.GetValue(_computer)?.ToString():null;
            if(!string.IsNullOrEmpty(value))return CleanName(value);
            return CleanName(MenuPrefs.GetString("playerName", "Gorilla"));
        }
        static string CleanName(string name)=>NameContent.Plain(name);

        string TMP(object o, PropertyInfo p) { try { return p?.GetValue(o)?.ToString() ?? ""; } catch { return ""; } }
        string Top(string s) => TopLabel(s);

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
                if (g.activeInHierarchy && g.name.ToLower().Contains("outfit"))
                {
                    var tmp = g.GetComponents<Component>().FirstOrDefault(c => c.GetType().Name.Contains("TextMeshPro"));
                    if (tmp != null) return tmp;
                }
            return null;
        }

        object GetTMP(string path) => GameObject.Find(path)?.GetComponents<Component>().FirstOrDefault(c => c.GetType().Name.Contains("TextMeshPro"));

        void Draw(MenuUI ui)
        {
            ui.BeginCard();
            ui.InfoRow(I18n.T("Names.Outfit"),string.IsNullOrEmpty(currentOutfit)?"—":currentOutfit);
            ui.InfoRow(I18n.T("Names.Pseudo"),GetPlayerName());
            ui.Label(I18n.T("UI.OutfitCopies")+" "+_outfitLabels.Count,Theme.Dim,9);
            ui.EndCard();
            ui.Section(I18n.T("Names.Presets")+" · "+saved.Count);
            if(saved.Count==0)ui.Label(I18n.T("Names.NoPresets"),Theme.Dim,9);
            foreach(var entry in saved.OrderBy(x=>NumericKey(x.Key)).ThenBy(x=>x.Key,System.StringComparer.OrdinalIgnoreCase).ToList())
                DrawPresetRow(ui,entry.Key,entry.Value,Enabled);
        }
        int NumericKey(string outfit)
        {
            var digits = new string(outfit.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
            return int.TryParse(digits, out int n) ? n : int.MaxValue;
        }

        void DrawPresetRow(MenuUI ui,string outfit,string presetName,bool enabled)
        {
            ui.BeginCard();
            ui.Label(outfit,Theme.Text,11,true);
            GUILayout.BeginHorizontal();
            string edited=CleanName(ui.TextField(presetName));
            bool remove=ui.SmallBtn(I18n.T("Spawner.Delete"),ui.TB3,Theme.Text);
            GUILayout.EndHorizontal();
            if(remove){saved.Remove(outfit);Save();}
            else if(edited!=presetName)
            {saved[outfit]=edited;Save();if(enabled&&outfit==currentOutfit)ApplyPreset(edited);}
            ui.EndCard();
        }
        static System.Runtime.Serialization.Json.DataContractJsonSerializer Serializer()=>
            new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Dictionary<string,string>),
                new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings{UseSimpleDictionaryFormat=true});
        void Save()
        {
            using(var stream=new System.IO.MemoryStream())
            {
                Serializer().WriteObject(stream,saved);
                MenuPrefs.SetString("MUM_names",System.Text.Encoding.UTF8.GetString(stream.ToArray()));
                MenuPrefs.Save();
            }
        }
        void Load()
        {
            saved.Clear();
            try
            {
                using(var stream=new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(MenuPrefs.GetString("MUM_names","{}"))))
                    foreach(var item in (Dictionary<string,string>)Serializer().ReadObject(stream))
                        if(!string.IsNullOrEmpty(item.Key)&&item.Value!=null)saved[item.Key]=CleanName(item.Value);
            }
            catch(System.Exception error){Log.Err("Names.Load",error);}
        }
    }
}
#endregion

#region NameContent
namespace UltimateMenu
{
    using System.Text;

    internal static class NameContent
    {
        // Strip formatting before limiting visible characters; never save half a rich-text tag.
        internal static string Plain(string value)
        {
            if(string.IsNullOrEmpty(value))return "";
            var result=new StringBuilder(32);
            bool tag=false;
            for(int i=0;i<value.Length&&i<16384&&result.Length<32;i++)
            {
                char c=value[i];
                if(c=='<'){tag=true;continue;}
                if(c=='>'){tag=false;continue;}
                if(tag)continue;
                if(c=='\r'||c=='\n')c=' ';
                else if(char.IsControl(c))continue;
                result.Append(c);
            }
            return result.ToString().Trim();
        }
    }
}
#endregion
