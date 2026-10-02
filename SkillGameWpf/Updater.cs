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

        // After a successful swap: reboot the cabinet (true) or just relaunch the app (false).
        public const bool RebootAfterUpdate = true;

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

        /// <summary>Unpack the zip, hand off to an external script that waits for us to exit, swaps the files in,
        /// writes the new version, and reboots. The app shuts down so its files aren't locked during the swap.</summary>
        public static void ApplyAndRestart(string zipPath, UpdateInfo info)
        {
            string staging = Path.Combine(WorkDir, "staging");
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            ZipFile.ExtractToDirectory(zipPath, staging, overwriteFiles: true);

            // The zip may or may not carry a version.txt; make sure the installed version ends up correct either way.
            try { File.WriteAllText(Path.Combine(staging, "version.txt"), info.Version); } catch { }

            string installDir = AppContext.BaseDirectory.TrimEnd('\\');
            string exe = Path.Combine(installDir, "SkillGameWpf.exe");
            int pid = Environment.ProcessId;
            string restart = RebootAfterUpdate
                ? "shutdown /r /t 5 /c \"SkillGame updated - restarting\""
                : $"start \"\" \"{exe}\"";

            string cmd = $@"@echo off
setlocal
echo Installing SkillGame {info.Version}...
:waitloop
tasklist /FI ""PID eq {pid}"" 2>nul | find ""{pid}"" >nul
if not errorlevel 1 (
  timeout /t 1 /nobreak >nul
  goto waitloop
)
xcopy ""{staging}\*"" ""{installDir}\"" /E /Y /I >nul
{restart}
";
            string cmdPath = Path.Combine(WorkDir, "apply_update.cmd");
            File.WriteAllText(cmdPath, cmd, new UTF8Encoding(false));

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{cmdPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = WorkDir,
            });

            Application.Current.Shutdown();
        }
    }
}
