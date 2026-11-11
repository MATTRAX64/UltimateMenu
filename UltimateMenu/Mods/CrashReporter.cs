#region CrashReporter
namespace UltimateMenu
{
    using System;
    using System.Collections;
    using System.Collections.Concurrent;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using UnityEngine;
    using UnityEngine.Networking;
    using UnityEngine.XR;

    public sealed class CrashReporter : MonoBehaviour
    {
        public static CrashReporter Instance { get; private set; }
        const string Pref="MUM_CrashReports";
        readonly ConcurrentQueue<string> _errors=new ConcurrentQueue<string>();
        string _folder,_session,_lastRecorded="",_token="";
        DateTime _tokenExpiry;
        const string Api="https://api.github.com";
        [Serializable] sealed class Installation { public long id; }
        [Serializable] sealed class AccessToken { public string token; }
        [Serializable] sealed class Issue { public string title,body; }
        bool _enabled,_sending,_manualOpen;
        int _manualCategory;
        string _manualText="", _manualStatus="";
        float _nextManualSend;
        static readonly string[] CategoryKeys={"Category.Gameplay","Category.Apparence","Category.Tracking","Category.Technique","Crash.Other"};
        static readonly string[] CategoryNames={"Gameplay","Interface","VR / Tracking","Performance / Technical","Other"};
        readonly System.Collections.Generic.HashSet<string> _seen=new System.Collections.Generic.HashSet<string>();
        float _nextSnapshot,_nextSend,_nextError;
        public string StatusKey {get;private set;}="Crash.Ready";
        [Serializable] public sealed class Report
        {
            public int schema=1;
            public string id,date,player,modVersion,gameVersion,unityVersion,distribution,runtime,cpu,gpu,os,error,kind;
            public int cpuThreads,ramMB,vramMB;
        }
        void Awake()
        {
            Instance=this;
            _folder=Path.Combine(BepInEx.Paths.PluginPath,"UltimateMenu","CrashReports");
            _session=Path.Combine(_folder,"session.json");
            _enabled=MenuPrefs.GetInt(Pref,1)==1;
            if(_enabled)
            {
                Directory.CreateDirectory(_folder);
                if(File.Exists(_session))
                {
                    try { File.Delete(_session); }
                    catch(Exception error){Debug.LogWarning("[UltimateMenu] Crash session: "+error.Message);}
                }
                Snapshot();
            }
            if(_enabled)PurgeOutdated();
            Application.logMessageReceivedThreaded+=Capture;
        }
        public void SetEnabled(bool value)
        {
            if(value==_enabled)return;
            _enabled=value;MenuPrefs.SetInt(Pref,value?1:0);MenuPrefs.Save();
            if(value){Directory.CreateDirectory(_folder);Snapshot();_nextSend=0;}
            else {while(_errors.TryDequeue(out _)){};ClearSession();}
        }
        public bool ReportsEnabled=>_enabled;
        // A report belongs to the version that recorded it (modVersion). Once a newer version runs, older reports are stale (the bug may be fixed), so they are deleted instead of sent.
        // An unreadable report or one without a valid version is useless and counts as outdated. If the running version itself is unknown, nothing is deleted.
        static bool IsOlderThanCurrent(string recorded)
        {
            if(!Version.TryParse(Log.PluginVersion,out var current))return false;
            return !Version.TryParse(recorded,out var old)||old<current;
        }
        void PurgeOutdated()
        {
            try
            {
                foreach(var file in Directory.GetFiles(_folder,"report-*.json*"))
                {
                    string recorded=null;
                    try{recorded=JsonUtility.FromJson<Report>(File.ReadAllText(file))?.modVersion;}catch{}
                    if(!IsOlderThanCurrent(recorded))continue;
                    try{File.Delete(file);}catch{}
                }
            }
            catch(Exception error){Debug.LogWarning("[UltimateMenu] Crash purge: "+error.Message);}
        }
        public static void Record(string context,Exception error)
        {
            var reporter=Instance;
            if(reporter!=null)reporter.Enqueue(context+"\n"+error);
        }
        void Capture(string condition,string stack,LogType type)
        {
            if(type!=LogType.Exception&&type!=LogType.Error&&type!=LogType.Assert)return;
            string text=condition+"\n"+stack;
            if(stack!=null&&stack.IndexOf("UltimateMenu.",StringComparison.Ordinal)>=0)Enqueue(text);
        }
        void Enqueue(string value){if(_enabled&&_errors.Count<16)_errors.Enqueue(Redact(value));}
        void Update()
        {
            if(!_enabled)return;
            if(Time.unscaledTime>=_nextError&&_errors.TryDequeue(out string error))
            {
                _nextError=Time.unscaledTime+30;
                if(error==_lastRecorded||!_seen.Add(error))return;
                _lastRecorded=error;
                var report=Describe();report.kind="exception";report.error=Redact(error);
                Store(report);
            }
            if(Time.unscaledTime>=_nextSnapshot){_nextSnapshot=Time.unscaledTime+30;Snapshot();}
            if(!_sending&&Time.unscaledTime>=_nextSend)
            {
                _nextSend=Time.unscaledTime+60;
                var files=Directory.GetFiles(_folder,"report-*.json");Array.Sort(files,StringComparer.Ordinal);
                if(files.Length>0)StartCoroutine(Send(files[0]));
            }
        }
        Report Describe()
        {
            string path=BepInEx.Paths.GameRootPath;
            string distribution=path.IndexOf("steamapps",StringComparison.OrdinalIgnoreCase)>=0?"Steam":path.IndexOf("Oculus",StringComparison.OrdinalIgnoreCase)>=0?"Meta/Oculus":"Unknown";
            return new Report {id=Guid.NewGuid().ToString("N"),date=DateTimeOffset.Now.ToString("O"),player="anonymous",modVersion=Log.PluginVersion,
                gameVersion=Application.version,unityVersion=Application.unityVersion,distribution=distribution,runtime=XRSettings.loadedDeviceName??"Unknown",
                cpu=SystemInfo.processorType,cpuThreads=SystemInfo.processorCount,gpu=SystemInfo.graphicsDeviceName,ramMB=SystemInfo.systemMemorySize,vramMB=SystemInfo.graphicsMemorySize,os=SystemInfo.operatingSystem};
        }
        void Snapshot()
        {
            try { var report=Describe();report.kind="session";report.error=Redact(_lastRecorded);File.WriteAllText(_session,JsonUtility.ToJson(report)); }
            catch(Exception error){Debug.LogWarning("[UltimateMenu] Crash snapshot: "+error.Message);}
        }
        void Store(Report report)
        {
            try
            {
                report.id=Guid.NewGuid().ToString("N");report.error=Redact(report.error??"");
                Directory.CreateDirectory(_folder);
                if(Directory.GetFiles(_folder,"report-*.json").Length>=20){StatusKey="Crash.Pending";return;}
                File.WriteAllText(Path.Combine(_folder,"report-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+"-"+report.id+".json"),JsonUtility.ToJson(report,true));
                StatusKey="Crash.Pending";
            }
            catch(Exception error){Debug.LogWarning("[UltimateMenu] Crash report: "+error.Message);}
        }
        UnityWebRequest Request(string url,string method,string authentication,string body=null)
        {
            var request=new UnityWebRequest(url,method);request.timeout=15;
            request.downloadHandler=new DownloadHandlerBuffer();
            if(body!=null){request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));request.SetRequestHeader("Content-Type","application/json");}
            request.SetRequestHeader("User-Agent","UltimateMenu/"+Log.PluginVersion);
            request.SetRequestHeader("Accept","application/vnd.github+json");
            request.SetRequestHeader("Authorization","Bearer "+authentication);
            return request;
        }
        IEnumerator Send(string file, Report manual = null)
        {
            _sending=true;StatusKey="Crash.Sending";
            try
            {
                var steps = SendCore(file, manual);
                using var operation = (IDisposable)steps;
                while (true)
                {
                    bool next;
                    try { next=steps.MoveNext(); }
                    catch (Exception) { StatusKey="Crash.AuthError"; break; }
                    if(!next) break;
                    yield return steps.Current;
                }
            }
            finally { _sending=false; }
        }
        IEnumerator SendCore(string file, Report manual)
        {
            Report report = manual;
            if (report == null)
            {
                try { report=JsonUtility.FromJson<Report>(File.ReadAllText(file)); } catch { }
                if(report==null||IsOlderThanCurrent(report.modVersion)) { try{File.Delete(file);}catch{} yield break; }
            }
            if(string.IsNullOrEmpty(_token)||DateTime.UtcNow>=_tokenExpiry)
            {
                string jwt;
                try
                {
                    using var resource=typeof(CrashReporter).Assembly.GetManifestResourceStream("UltimateMenu.Assets.CrashReporting.github-app.pem");
                    using var reader=new StreamReader(resource);
                    jwt=GithubCrashAuth.Jwt(reader.ReadToEnd(),DateTimeOffset.UtcNow);
                }
                catch {StatusKey="Crash.AuthError";_sending=false;yield break;}
                long installation;
                using(var request=Request(Api+"/repos/MATTRAX64/UltimateMenu/installation","GET",jwt))
                {
                    yield return request.SendWebRequest();
                    if(request.result!=UnityWebRequest.Result.Success){StatusKey="Crash.AuthError";_sending=false;yield break;}
                    installation=JsonUtility.FromJson<Installation>(request.downloadHandler.text)?.id ?? 0;
                    if(installation<=0){StatusKey="Crash.AuthError";yield break;}
                }
                using(var request=Request(Api+"/app/installations/"+installation+"/access_tokens","POST",jwt,"{\"repositories\":[\"UltimateMenu\"],\"permissions\":{\"issues\":\"write\"}}"))
                {
                    yield return request.SendWebRequest();
                    if(request.result!=UnityWebRequest.Result.Success){StatusKey="Crash.AuthError";_sending=false;yield break;}
                    _token=JsonUtility.FromJson<AccessToken>(request.downloadHandler.text)?.token;
                    if(string.IsNullOrWhiteSpace(_token)){StatusKey="Crash.AuthError";yield break;}
                    _tokenExpiry=DateTime.UtcNow.AddMinutes(45);
                }
            }
            if(manual==null&&!_enabled)yield break;
            string body="Player: "+report.player+"\nDate: "+report.date+"\nUltimateMenu: "+report.modVersion+"\nGame: "+report.gameVersion+"\nUnity: "+report.unityVersion+
                "\nDistribution: "+report.distribution+"\nVR runtime: "+report.runtime+"\nCPU: "+report.cpu+" ("+report.cpuThreads+" threads)\nGPU: "+report.gpu+
                "\nRAM: "+report.ramMB+" MB\nVRAM: "+report.vramMB+" MB\nOS: "+report.os+"\nType: "+report.kind+"\n\nError:\n"+report.error;
            var issue=new Issue{title="[Auto report] UltimateMenu "+report.modVersion+" — "+report.kind,body=body};
            if (manual!=null)
            {
                issue.title="[Bug] "+report.kind+" — UltimateMenu "+report.modVersion;
                issue.body="Category: "+report.kind+"\nUltimateMenu: "+report.modVersion+"\nGame: "+report.gameVersion+
                    "\nDistribution: "+report.distribution+"\nVR runtime: "+report.runtime+"\n\nDescription:\n```text\n"+
                    report.error.Replace("```", "[code fence]")+"\n```";
            }
            using(var request=Request(Api+"/repos/MATTRAX64/UltimateMenu/issues","POST",_token,JsonUtility.ToJson(issue)))
            {
                yield return request.SendWebRequest();
                if(request.result==UnityWebRequest.Result.Success)
                {StatusKey="Crash.Sent";if(file!=null)try{File.Move(file,file+".sent");}catch{}}
                else {StatusKey="Crash.Pending";if(request.responseCode==401){_token="";_tokenExpiry=DateTime.MinValue;}}
            }
            _sending=false;
        }
        internal static string Redact(string text)=>CrashPrivacy.Redact(text);
        void ClearSession(){try{if(File.Exists(_session))File.Delete(_session);}catch{}}
        void OnApplicationQuit()=>ClearSession();
        void OnDestroy(){Application.logMessageReceivedThreaded-=Capture;if(Instance==this)Instance=null;}
        IEnumerator SendManual(string description, int category)
        {
            _manualStatus="Crash.Sending";
            var report=Describe();report.kind=CategoryNames[category];report.error=Redact(description);
            yield return Send(null, report);
            if(StatusKey=="Crash.Sent")
            {
                _manualStatus="Crash.Sent";
                if(_manualText.Trim()==description)_manualText="";
            }
            else _manualStatus="Crash.ManualFailed";
        }

        public void Draw(MenuUI ui)
        {
            ui.Section(I18n.T("Crash.Title"));
            ui.BeginCard();
            SetEnabled(ui.Toggle(I18n.T("Crash.Automatic"),_enabled));
            ui.EndCard();
            ui.ExpandableSection(I18n.T("Crash.Manual"), "", ref _manualOpen, () =>
            {
                var labels = Array.ConvertAll(CategoryKeys, key => I18n.T(key));
                _manualCategory=ui.ModePicker(labels,_manualCategory,2);
                ui.Label(I18n.T("Crash.Description"),Theme.Text,10,true);
                _manualText=ui.TextField(_manualText);
                if(_manualText.Length>1000)_manualText=_manualText.Substring(0,1000);
                ui.Label(I18n.T("Crash.Public"),Theme.Dim,9);
                bool ready=!_sending && Time.unscaledTime>=_nextManualSend && !string.IsNullOrWhiteSpace(_manualText);
                if(ui.Btn(I18n.T("Crash.Send"),ready?ui.TA:ui.TB3,ready?Theme.Text:Theme.Dim) && ready)
                {
                    _nextManualSend=Time.unscaledTime+30f;
                    StartCoroutine(SendManual(_manualText.Trim(),_manualCategory));
                }
                if(_manualStatus.Length>0)ui.Label(I18n.T(_manualStatus),Theme.Dim,9);
            }, centerTitle: true);
        }
    }
}
#endregion

#region CrashPrivacy
namespace UltimateMenu
{
    using System.Text.RegularExpressions;

    internal static class CrashPrivacy
    {
        internal static string Redact(string text)
        {
            text=Regex.Replace(text,@"-----BEGIN [^-]*PRIVATE KEY-----.*?-----END [^-]*PRIVATE KEY-----","[private key removed]",RegexOptions.Singleline);
            if(text.Length>16000)text=text.Substring(0,16000);
            text=Regex.Replace(text,@"(?i)[A-Z]:[\\/]Users[\\/][^\\/\s]+",@"C:\Users\[user]");
            text=Regex.Replace(text,@"(?i)(?:gh[pousr]_[a-z0-9_]+|github_pat_[a-z0-9_]+)","[token removed]");
            text=Regex.Replace(text,@"(?i)(authorization|password|secret|token)\s*[:=]\s*[^\s,;]+","$1=[removed]");
            return text;
        }
    }
}
#endregion

#region GithubCrashAuth
namespace UltimateMenu
{
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.RegularExpressions;

    internal static class GithubCrashAuth
    {
        internal static string Jwt(string pem,DateTimeOffset now)
        {
            string header=Encode(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"));
            long seconds=now.ToUnixTimeSeconds();
            string payload=Encode(Encoding.UTF8.GetBytes("{\"iat\":"+(seconds-60)+",\"exp\":"+(seconds+540)+",\"iss\":\"5328536\"}"));
            string body=header+"."+payload;
            using(var rsa=RSA.Create())
            {
                rsa.ImportParameters(ReadKey(pem));
                return body+"."+Encode(rsa.SignData(Encoding.ASCII.GetBytes(body),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1));
            }
        }
        static string Encode(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
        static RSAParameters ReadKey(string pem)
        {
            // GitHub downloads PKCS#1 RSA private keys. Parse DER without a platform-specific PEM API.
            string raw=Regex.Replace(pem,"-----[^-]+-----|\\s","");
            using(var reader=new BinaryReader(new MemoryStream(Convert.FromBase64String(raw))))
            {
                if(reader.ReadByte()!=0x30)throw new InvalidDataException("RSA sequence expected");
                Length(reader);Integer(reader);
                return new RSAParameters {Modulus=Integer(reader),Exponent=Integer(reader),D=Integer(reader),P=Integer(reader),Q=Integer(reader),DP=Integer(reader),DQ=Integer(reader),InverseQ=Integer(reader)};
            }
        }
        static int Length(BinaryReader reader)
        {
            int value=reader.ReadByte();if(value<128)return value;
            int count=value&127;if(count==0||count>4)throw new InvalidDataException("Invalid DER length");
            value=0;for(int i=0;i<count;i++)value=(value<<8)|reader.ReadByte();return value;
        }
        static byte[] Integer(BinaryReader reader)
        {
            if(reader.ReadByte()!=2)throw new InvalidDataException("RSA integer expected");
            int size=Length(reader);if(size<1||size>16384)throw new InvalidDataException("Invalid RSA integer length");
            byte[] bytes=reader.ReadBytes(size);if(bytes.Length!=size)throw new EndOfStreamException();
            if(bytes.Length>1&&bytes[0]==0){var trimmed=new byte[bytes.Length-1];Buffer.BlockCopy(bytes,1,trimmed,0,trimmed.Length);return trimmed;}return bytes;
        }
    }
}
#endregion
