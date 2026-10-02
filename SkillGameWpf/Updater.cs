using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace SkillGameWpf
{
    /// <summary>What the server advertises for the latest build.</summary>
    public sealed class UpdateInfo
    {
        public string Version { get; set; } = "";
        public string Notes { get; set; } = "";
        public string Zip { get; set; } = "";       // absolute URL of the update zip
        public string Sha256 { get; set; } = "";     // optional; verified when present
        public long Size { get; set; }
        public string Date { get; set; } = "";
    }

    /// <summary>Checks the WezeBull server for a newer SkillGame, downloads it, and swaps the files in on reboot.</summary>
    public static class Updater
    {
        // The only thing to change to move hosting: point this at wherever version.json lives.
        public const string ManifestUrl = "https://raw.githubusercontent.com/dlbuller/SkillGame/main/update/version.json";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        /// <summary>This build's version — read from version.txt beside the exe, falling back to the assembly version.</summary>
        public static Version Current
        {
            get
            {
                try
                {
                    var f = Path.Combine(AppContext.BaseDirectory, "version.txt");
                    if (File.Exists(f) && Version.TryParse(File.ReadAllText(f).Trim(), out var v)) return v;
                }
                catch { }
                return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            }
        }

        /// <summary>Hit the server. Returns the update when one is newer than this build, otherwise null.</summary>
        public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
        {
            // Cache-buster so the host's CDN can't serve a stale manifest during testing.
            string url = ManifestUrl + (ManifestUrl.Contains('?') ? "&" : "?") + "t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string json = await Http.GetStringAsync(url, ct);
            var info = JsonSerializer.Deserialize<UpdateInfo>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (info == null || !Version.TryParse(info.Version, out var remote)) return null;
            return remote > Current ? info : null;
        }

        private static string WorkDir
        {
            get
            {
                var d = Path.Combine(Path.GetTempPath(), "SkillGameUpdate");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        /// <summary>Download the update zip with progress (0..1) and verify its hash. Returns the local zip path.</summary>
        public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct = default)
        {
            string zipPath = Path.Combine(WorkDir, "update.zip");
            using (var resp = await Http.GetAsync(info.Zip, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? info.Size;
                using var src = await resp.Content.ReadAsStreamAsync(ct);
                using var dst = File.Create(zipPath);
                var buf = new byte[81920];
                long done = 0; int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    if (total > 0) progress?.Report(Math.Min(1.0, (double)done / total));
                }
            }

            if (!string.IsNullOrWhiteSpace(info.Sha256))
            {
                using var fs = File.OpenRead(zipPath);
                string got = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
                if (!string.Equals(got, info.Sha256.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                    throw new InvalidDataException("Downloaded file failed its checksum — not installing.");
            }
            return zipPath;
        }

        /// <summary>Unpack the update next to the app and relaunch into it. It's just unzip + copy + restart the app —
        /// no cmd.exe, no temp script, no PC reboot (that "drop a script in temp and reboot" pattern looks like malware
        /// to endpoint security like Cortex). When the package carries new binaries we hand off to a staged copy of
        /// ourselves to do the copy (the running exe/DLLs are locked until we exit); a notes-only update applies in place.</summary>
        public static void ApplyAndRestart(string zipPath, UpdateInfo info)
        {
            string installDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
            string staged = Path.Combine(installDir, "_staged");
            if (Directory.Exists(staged)) Directory.Delete(staged, true);
            Directory.CreateDirectory(staged);
            ZipFile.ExtractToDirectory(zipPath, staged, overwriteFiles: true);
            try { File.WriteAllText(Path.Combine(staged, "version.txt"), info.Version); } catch { }

            string stagedExe = Path.Combine(staged, "SkillGameWpf.exe");
            string installExe = Path.Combine(installDir, "SkillGameWpf.exe");

            if (File.Exists(stagedExe))
            {
                // Full build: the staged exe runs from _staged, so the install files are free to overwrite once we exit.
                Process.Start(new ProcessStartInfo
                {
                    FileName = stagedExe,
                    Arguments = $"--finish-update \"{installDir}\"",
                    UseShellExecute = false,
                    WorkingDirectory = staged,
                });
            }
            else
            {
                // Notes-only / no locked binaries: back up the current version, apply in place, relaunch ourselves.
                try { string bak = Path.Combine(installDir, "_backup"); if (Directory.Exists(bak)) Directory.Delete(bak, true); CopyOver(installDir, bak); } catch { }
                CopyOver(staged, installDir);
                try { Directory.Delete(staged, true); } catch { }
                Process.Start(new ProcessStartInfo { FileName = installExe, UseShellExecute = false, WorkingDirectory = installDir });
            }
            Application.Current.Shutdown();
        }

        /// <summary>Run by the staged copy (App sees --finish-update): the old instance has exited, so copy the staged
        /// build over the install dir and relaunch the updated app. No external processes — just file copies.</summary>
        public static void FinishUpdate(string installDir)
        {
            string staged = AppContext.BaseDirectory.TrimEnd('\\', '/');   // we're running from _staged
            installDir = installDir.TrimEnd('\\', '/');
            // Keep the version we're replacing so the operator can roll back to it.
            try { string bak = Path.Combine(installDir, "_backup"); if (Directory.Exists(bak)) Directory.Delete(bak, true); CopyOver(installDir, bak, retries: 8); } catch { }
            CopyOver(staged, installDir, retries: 15);                      // the old instance may take a moment to release files
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(installDir, "SkillGameWpf.exe"),
                    UseShellExecute = false,
                    WorkingDirectory = installDir,
                });
            }
            catch { }
            Application.Current?.Shutdown();
        }

        /// <summary>Remove a leftover _staged folder from a completed update (called on a normal startup).</summary>
        public static void CleanupStaging()
        {
            try
            {
                string staged = Path.Combine(AppContext.BaseDirectory.TrimEnd('\\', '/'), "_staged");
                if (Directory.Exists(staged)) Directory.Delete(staged, true);
            }
            catch { }
        }

        // ---- rollback: the previous version is kept in _backup so the operator can pick old vs new ----
        private static string BackupDir => Path.Combine(AppContext.BaseDirectory.TrimEnd('\\', '/'), "_backup");
        public static bool CanRollback => File.Exists(Path.Combine(BackupDir, "SkillGameWpf.exe")) || File.Exists(Path.Combine(BackupDir, "version.txt"));
        public static string PreviousVersion
        {
            get { try { var f = Path.Combine(BackupDir, "version.txt"); return File.Exists(f) ? File.ReadAllText(f).Trim() : "previous"; } catch { return "previous"; } }
        }

        /// <summary>Restore the backed-up previous version and relaunch into it (same gentle swap, source = _backup).</summary>
        public static void Rollback()
        {
            string installDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
            string backupExe = Path.Combine(BackupDir, "SkillGameWpf.exe");
            if (File.Exists(backupExe))
                Process.Start(new ProcessStartInfo { FileName = backupExe, Arguments = $"--restore \"{installDir}\"", UseShellExecute = false, WorkingDirectory = BackupDir });
            else
            {
                CopyOver(BackupDir, installDir);
                Process.Start(new ProcessStartInfo { FileName = Path.Combine(installDir, "SkillGameWpf.exe"), UseShellExecute = false, WorkingDirectory = installDir });
            }
            Application.Current.Shutdown();
        }

        /// <summary>Run by the backup copy (App sees --restore): copy the backup over the install dir and relaunch. No re-backup.</summary>
        public static void RestoreUpdate(string installDir)
        {
            installDir = installDir.TrimEnd('\\', '/');
            CopyOver(AppContext.BaseDirectory.TrimEnd('\\', '/'), installDir, retries: 15);   // we're running from _backup
            try { Process.Start(new ProcessStartInfo { FileName = Path.Combine(installDir, "SkillGameWpf.exe"), UseShellExecute = false, WorkingDirectory = installDir }); } catch { }
            Application.Current?.Shutdown();
        }

        // ---- background "update available" checks (on app boot + every few hours) ----
        public static UpdateInfo? Available { get; private set; }
        public static event Action? Changed;
        private static System.Windows.Threading.DispatcherTimer? _poll;

        public static void StartBackgroundChecks()
        {
            _ = CheckInBackground();
            _poll = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(6) };
            _poll.Tick += (s, e) => _ = CheckInBackground();
            _poll.Start();
        }
        private static async Task CheckInBackground()
        {
            try { var info = await CheckAsync(); if (info != null) { Available = info; Changed?.Invoke(); } }
            catch { }
        }

        // Copy every file from src over dst (overwrite), skipping a nested _staged. Retries ride out a file the
        // just-exited instance hasn't released yet; a file we still can't replace is skipped, not fatal.
        private static void CopyOver(string src, string dst, int retries = 1)
        {
            foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(src, file);
                if (rel.StartsWith("_staged", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("_backup", StringComparison.OrdinalIgnoreCase)) continue;
                string target = Path.Combine(dst, rel);
                for (int attempt = 0; ; attempt++)
                {
                    try { Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target, true); break; }
                    catch when (attempt < retries) { Thread.Sleep(300); }
                    catch { break; }
                }
            }
        }
    }
}
