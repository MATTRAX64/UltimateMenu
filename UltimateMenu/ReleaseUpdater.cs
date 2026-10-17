using System;
using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;

namespace UltimateMenu
{
    internal static class ReleaseUpdater
    {
        internal const int MaxBytes = 16 * 1024 * 1024;
        internal static string PluginDllPath = typeof(Log).Assembly.Location;
        internal static string DataFolder => Path.Combine(BepInEx.Paths.PluginPath, "UltimateMenu");
        static void MergeData(string source, string destination)
        {
            if (!Directory.Exists(source) || string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) return;
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) return;
            Directory.CreateDirectory(destination);
            foreach (string directory in Directory.GetDirectories(source))
                MergeData(directory, Path.Combine(destination, Path.GetFileName(directory)));
            foreach (string file in Directory.GetFiles(source))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                string target = Path.Combine(destination, Path.GetFileName(file));
                if (File.Exists(target))
                {
                    if (Hash(file) == Hash(target)) { File.Delete(file); continue; }
                    // Preserve both differing files; repeated migrations reuse the same hash suffix.
                    target = Path.Combine(destination, Path.GetFileNameWithoutExtension(file) + ".recovered-" + Hash(file).Substring(0, 12) + Path.GetExtension(file));
                    if (File.Exists(target))
                    {
                        if (Hash(file) == Hash(target)) File.Delete(file);
                        continue;
                    }
                }
                File.Move(file, target);
            }
            if (Directory.GetFileSystemEntries(source).Length == 0) Directory.Delete(source);
        }
        static void PrepareDataFolders(string loadedDirectory)
        {
            Directory.CreateDirectory(DataFolder);
            foreach (string source in new[] { loadedDirectory, BepInEx.Paths.PluginPath, Path.GetDirectoryName(BepInEx.Paths.PluginPath) })
                foreach (string name in new[] { "GorillaFaces", "GorillaMocap", "GorillaSpawners", "GorillaStreams", "CrashReports" })
                    try { MergeData(Path.Combine(source, name), Path.Combine(DataFolder, name)); }
                    catch (Exception e) { Log.Err("Data migration: " + name, e); }
        }
        static void StartWorker(string script)
        {
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            using (var worker = Process.Start(new ProcessStartInfo(powershell,
                "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)))
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }))
                if (worker == null) throw new IOException("Update worker did not start");
        }

        internal static void EnsurePluginFolder()
        {
            string current = Path.GetFullPath(typeof(Log).Assembly.Location);
            string directory = Path.GetDirectoryName(current);
            PrepareDataFolders(directory);
            string folder = DataFolder;
            if (string.Equals(Path.GetFullPath(directory), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase)) return;
            Directory.CreateDirectory(folder);
            string destination = Path.Combine(folder, Path.GetFileName(current));
            if (!File.Exists(destination)) File.Copy(current, destination);
            else if (ReadVersion(destination) <= ReadVersion(current) && Hash(destination) != Hash(current))
            {
                File.Copy(current, destination, true);
            }
            PluginDllPath = destination;
            // Only retire this exact source once the verified copy exists and the game exits.
            string script = "$ErrorActionPreference='Stop'\n" + WorkerHashFunction +
                "try { $game=[System.Diagnostics.Process]::GetProcessById(" + Process.GetCurrentProcess().Id + "); $game.WaitForExit() } catch [System.ArgumentException] {}\n" +
                "$source=" + Quote(current) + "\n$destination=" + Quote(destination) + "\n" +
                "if ((Test-Path -LiteralPath $source) -and (Test-Path -LiteralPath $destination) -and " +
                "(FileHash $source) -eq " + Quote(Hash(current)) + " -and " +
                "(FileHash $destination) -eq " + Quote(Hash(destination)) + ") {\n" +
                "[System.IO.File]::Delete($source)\n}\n";
            StartWorker(script);
        }
        internal static Version ReadVersion(string path)
        {
            // Inspect metadata without loading or executing the downloaded assembly.
            using (var assembly = AssemblyDefinition.ReadAssembly(path))
                foreach (var type in assembly.MainModule.Types)
                    foreach (var attribute in type.CustomAttributes)
                        if (attribute.AttributeType.FullName == "BepInEx.BepInPlugin" &&
                            attribute.ConstructorArguments.Count == 3 &&
                            (string)attribute.ConstructorArguments[0].Value == "com.mattrax.ultimatemenu" &&
                            Log.TryReleaseVersion((string)attribute.ConstructorArguments[2].Value, out var version))
                            return version;
            throw new InvalidDataException("Not an UltimateMenu plugin");
        }

        const string WorkerHashFunction = "function FileHash([string]$path) { $sha=[System.Security.Cryptography.SHA256]::Create(); try { [BitConverter]::ToString($sha.ComputeHash([System.IO.File]::ReadAllBytes($path))).Replace('-','') } finally { $sha.Dispose() } }\n";
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        internal static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }

        internal static string BuildInstallScript(string current, string pending, string sourceHash, string targetHash, int processId)
        {
            current = Path.GetFullPath(current); pending = Path.GetFullPath(pending);
            if (!string.Equals(Path.GetDirectoryName(current), Path.GetDirectoryName(pending), StringComparison.OrdinalIgnoreCase) ||
                !pending.EndsWith(".pending", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Update paths must share the plugin folder");
            return "$ErrorActionPreference='Stop'\n" + WorkerHashFunction +
                "$target=" + Quote(current) + "\n$pending=" + Quote(pending) + "\n" +
                "try {\n" +
                "try { $game=[System.Diagnostics.Process]::GetProcessById(" + processId + "); $game.WaitForExit() } catch [System.ArgumentException] {}\n" +
                // Do not replace a file already changed by a manual installation.
                "if (!(Test-Path -LiteralPath $pending)) { exit }\n" +
                "if ((FileHash $pending) -ne " + Quote(sourceHash) + ") { throw 'Pending hash mismatch' }\n" +
                "if ((FileHash $target) -ne " + Quote(targetHash) + ") { throw 'Installed DLL changed; update cancelled' }\n" +
                "$done=$false\nfor ($attempt=0; $attempt -lt 30; $attempt++) {\n" +
                "try { [System.IO.File]::Replace($pending,$target,$null); $done=$true; break } catch { Start-Sleep -Milliseconds 500 }\n}\n" +
                "if (!$done) { throw 'DLL replacement failed; original retained' }\n" +
                "} catch { exit 1 }\n";
        }

        internal static void Stage(byte[] data, Version published)
        {
            if (data == null || data.Length < 1024 || data.Length > MaxBytes) throw new InvalidDataException("Invalid DLL size");
            string current = PluginDllPath;
            string pending = current + ".pending";
            if (File.Exists(pending)) throw new IOException("An update is already staged");
            try
            {
                File.WriteAllBytes(pending, data);
                Version downloaded = ReadVersion(pending);
                if (downloaded != published || !Log.TryReleaseVersion(Log.PluginVersion, out var installed) || downloaded <= installed || downloaded <= ReadVersion(current))
                    throw new InvalidDataException("Downloaded version is not the announced newer version");
                string script = BuildInstallScript(current, pending, Hash(pending), Hash(current), Process.GetCurrentProcess().Id);
                StartWorker(script);
            }
            catch { if (File.Exists(pending)) File.Delete(pending); throw; }
        }

        internal static bool ResumePending()
        {
            string current = PluginDllPath, pending = current + ".pending";
            if (!File.Exists(pending)) return false;
            if (new FileInfo(pending).Length > MaxBytes || ReadVersion(pending) <= ReadVersion(current))
                throw new InvalidDataException("Pending update is invalid or older");
            StartWorker(BuildInstallScript(current, pending, Hash(pending), Hash(current), Process.GetCurrentProcess().Id));
            return true;
        }

        internal static void RetireOlderCopies()
        {
            string active = Path.GetFullPath(PluginDllPath);
            Version newest = ReadVersion(active);
            string keeper = active;
            var candidates = new System.Collections.Generic.Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(Path.GetDirectoryName(active), "*.dll"))
            {
                try
                {
                    if (new FileInfo(file).Length > MaxBytes) continue;
                    Version version = ReadVersion(file);
                    candidates[file] = version;
                    if (version > newest) { newest = version; keeper = file; }
                }
                catch { /* Unrelated or invalid DLL: leave it untouched. */ }
            }
            string keeperHash = Hash(keeper);
            var script = new StringBuilder("$ErrorActionPreference='Stop'\n" + WorkerHashFunction +
                "try { $game=[System.Diagnostics.Process]::GetProcessById(" + Process.GetCurrentProcess().Id +
                "); $game.WaitForExit() } catch [System.ArgumentException] {}\n");
            int deferred = 0;
            foreach (var candidate in candidates)
            {
                string file = candidate.Key;
                if (string.Equals(file, keeper, StringComparison.OrdinalIgnoreCase)) continue;
                // Do not remove a different build sharing the newest version number.
                if (candidate.Value == newest && Hash(file) != keeperHash) continue;
                string oldHash = Hash(file);
                try
                {
                    if (Hash(keeper) != keeperHash || Hash(file) != oldHash) continue;
                    File.Delete(file);
                    Log.Info("Removed older UltimateMenu: " + Path.GetFileName(file));
                    continue;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                script.Append("$old=").Append(Quote(file)).Append("\nif ([System.IO.File]::Exists($old) -and [System.IO.File]::Exists(")
                    .Append(Quote(keeper)).Append(") -and (FileHash $old) -eq ").Append(Quote(oldHash))
                    .Append(" -and (FileHash ").Append(Quote(keeper)).Append(") -eq ").Append(Quote(keeperHash))
                    .Append(") { [System.IO.File]::Delete($old) }\n");
                deferred++;
            }
            if (deferred > 0)
            {
                Log.Info("Locked UltimateMenu copies will be removed after game exit: " + deferred);
                StartWorker(script.ToString());
            }
        }

    }
}
