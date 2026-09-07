using Iot.Device.FtCommon;
using NAudio.CoreAudioApi;
using NAudio.Wasapi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Device.Spi;
using System.Linq;
using System.Media;
using System.Text;
using System.Threading.Tasks;

namespace SkillGame
{
    public class AudioFunctions : IAudioSink
    {
        SoundPlayer player = new SoundPlayer();      // short foreground SFX (clicks, scores, coin)
        // Background/theme music uses NAudio's own output, not SoundPlayer, so SFX clicks don't stop the music.
        private WaveOutEvent? _bgOut;
        private AudioFileReader? _bgReader;
        private readonly object _bgGate = new object();      // serialize bg music (nav fires these off-thread)
        // Attract/distract music runs on its own NAudio output so it mixes with the scoring SFX instead of cutting it off.
        private WaveOutEvent? _musicOut;
        private AudioFileReader? _musicReader;
        private readonly object _musicGate = new object();
        AudioFileReader? reader;
        MixingSampleProvider mixers = new MixingSampleProvider(NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
        WaveOutEvent outputDevice = new WaveOutEvent();
        private readonly Random rnd = new Random();

        public AudioFunctions()
        {

        }


        public string GetMasterVolume()
        {
            string volumeLevel = "";
            try
            {
                var deviceEnumerator = new MMDeviceEnumerator();

                var device = deviceEnumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, Role.Multimedia);

                var audioEndpointVolume = device.AudioEndpointVolume;

                float currentVolume = (float)Math.Round(audioEndpointVolume.MasterVolumeLevelScalar, 2);

                if (audioEndpointVolume.Mute)
                {
                    volumeLevel = "*" + (currentVolume * 100).ToString("0");
                }
                else
                {
                    volumeLevel = (currentVolume * 100).ToString("0");
                }
            }

            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while getting the volume: {ex.Message}");
                return "Error";
            }

            return volumeLevel;
        }

        // A single dedicated audio thread plays every clip so callers never block and the SoundPlayer stays single-threaded, draining to the latest request so bursts don't back up.
        private readonly System.Collections.Concurrent.BlockingCollection<string> _clipQueue =
            new System.Collections.Concurrent.BlockingCollection<string>();
        private System.Threading.Thread? _clipThread;
        private readonly object _clipThreadGate = new object();

        private void EnsureClipWorker()
        {
            if (_clipThread != null) return;
            lock (_clipThreadGate)
            {
                if (_clipThread != null) return;
                _clipThread = new System.Threading.Thread(ClipWorker) { IsBackground = true, Name = "SkillGameAudio" };
                _clipThread.Start();
            }
        }

        // Cache a loaded SoundPlayer per wav so repeat plays are instant and scoring sounds stay tight.
        private readonly System.Collections.Generic.Dictionary<string, SoundPlayer> _clipCache = new();
        private void ClipWorker()
        {
            foreach (string next in _clipQueue.GetConsumingEnumerable())
            {
                string path = next;
                // Drain any that piled up while busy; only the newest matters.
                while (_clipQueue.TryTake(out string? newer)) path = newer!;
                try
                {
                    if (!_clipCache.TryGetValue(path, out var pl))
                    {
                        pl = new SoundPlayer(path);
                        try { pl.Load(); } catch { }   // load once; later plays are instant
                        _clipCache[path] = pl;
                    }
                    pl.Play();
                }
                catch (Exception ex) { Log.Error("audio clip failed: " + path, ex); }
            }
        }

        // Play a wav via the shared SoundPlayer off the calling thread; missing file or device just no-ops.
        private void PlayClip(string path)
        {
            EnsureClipWorker();
            try { _clipQueue.Add(path); } catch (Exception ex) { Log.Error("audio enqueue failed: " + path, ex); }
        }

        // Stop only the foreground player; leaves the background music playing.
        public void StopAudio()
        {
            try
            {
                player.Stop();
                outputDevice?.Stop();
                mixers.RemoveAllMixerInputs();
            }
            catch (Exception ex) { Log.Error("StopAudio failed", ex); }
        }

        // Stop the looping background/theme music (its own NAudio output, independent of SFX).
        public void StopBackground() { lock (_bgGate) StopBgLocked(); }

        private void StopBgLocked()
        {
            try { _bgOut?.Stop(); _bgOut?.Dispose(); } catch { }
            try { _bgReader?.Dispose(); } catch { }
            _bgOut = null; _bgReader = null;
        }

        // Stop everything — used when returning to the Game screen (which is quiet).
        public void StopAll() { StopAudio(); StopBackground(); StopMusicClip(); }

        // Play a background track once on its own NAudio output, off the calling thread, replacing any current track.
        private void PlayBgMusic(string path)
        {
            Task.Run(() =>
            {
                lock (_bgGate)
                {
                    try
                    {
                        StopBgLocked();
                        if (!System.IO.File.Exists(path)) return;
                        _bgReader = new AudioFileReader(path);
                        _bgOut = new WaveOutEvent();
                        _bgOut.Init(_bgReader);   // play once; restarts on the next nav click
                        _bgOut.Play();
                    }
                    catch (Exception ex) { Log.Error("bg music failed: " + path, ex); }
                }
            });
        }

        public void PlayStartupSound()
        {
            PlayClip(@"C:\SkillGame\Sounds\Startup\startup" + Convert.ToString(rnd.Next(1, 7)) + ".wav");
        }

        public async Task PlayThemeMusic(string music)
        {
            try
            {
                mixers.ReadFully = true;
                outputDevice.Init(mixers);
                outputDevice.Play();

                string? file = music switch
                {
                    "Settings" => @"C:\SkillGame\Sounds\Settings\settingsTheme.wav",
                    "Diagnostics" => @"C:\SkillGame\Sounds\Settings\diagnosticsTheme.wav",
                    "Audits" => @"C:\SkillGame\Sounds\Settings\auditsTheme.wav",
                    "Admin" => @"C:\SkillGame\Sounds\Settings\adminTheme.wav",
                    _ => null,
                };
                if (file != null)
                {
                    reader = new AudioFileReader(file);
                    mixers.AddMixerInput((ISampleProvider)reader);
                }
            }
            catch (Exception ex) { Log.Error("PlayThemeMusic failed", ex); }
            await Task.CompletedTask;
        }

        public async Task PlayAttractMusic()
        {
            PlayMusicClip(@"C:\SkillGame\Sounds\Attract\attract" + Convert.ToString(rnd.Next(1, 31)) + ".wav");
            await Task.CompletedTask;
        }

        public async Task PlayDistractMusic()
        {
            PlayMusicClip(@"C:\SkillGame\Sounds\Distract\distract" + Convert.ToString(rnd.Next(1, 11)) + ".wav", loop: true);
            await Task.CompletedTask;
        }

        // Attract/distract music on NAudio so it plays over the scoring SFX, replacing any still-playing track.
        // Distract loops so it runs the whole time the show is up, until the next switch stops it.
        private void PlayMusicClip(string path, bool loop = false)
        {
            Task.Run(() =>
            {
                lock (_musicGate)
                {
                    try
                    {
                        StopMusicLocked();
                        if (!System.IO.File.Exists(path)) return;
                        _musicReader = new AudioFileReader(path);
                        _musicOut = new WaveOutEvent();
                        _musicOut.Init(loop ? new LoopStream(_musicReader) : (IWaveProvider)_musicReader);
                        _musicOut.Play();
                    }
                    catch (Exception ex) { Log.Error("music clip failed: " + path, ex); }
                }
            });
        }

        private void StopMusicLocked()
        {
            try { _musicOut?.Stop(); _musicOut?.Dispose(); } catch { }
            try { _musicReader?.Dispose(); } catch { }
            _musicOut = null; _musicReader = null;
        }

        public void StopMusicClip() { lock (_musicGate) StopMusicLocked(); }

        // Halt music output now (reset flushes the buffer instantly); dispose the objects off-thread.
        public void StopMusicNow()
        {
            var o = _musicOut;
            try { o?.Stop(); } catch { }
            Task.Run(StopMusicClip);
        }

        // True while an attract/distract clip is still playing on the music output.
        public bool IsMusicPlaying { get { lock (_musicGate) return _musicOut?.PlaybackState == PlaybackState.Playing; } }

        public void PlaySettingsSound(string file)
        {
            string? name = file switch
            {
                "settings" => "settings", "select" => "select", "volume" => "volumeupdown", "coin" => "coin",
                "mutebutton" => "mutebutton", "muteoff" => "muteoff", "menuselect" => "menuselect",
                "home" => "home", "ledbutton" => "ledbutton", "ledbuttonoff" => "ledbuttonoff",
                "solenoidon" => "solenoidon", "solenoidoff" => "solenoidoff",
                "ledpatternon" => "ledpatternon", "ledpatternoff" => "ledpatternoff",
                "solidledon" => "solidledon", "solidledoff" => "solidledoff",
                "brightnessup" => "brightnessup", "brightnessdown" => "brightnessdown", "brightnessoff" => "brightnessoff",
                "soundfxon" => "soundfxon", "soundfxoff" => "soundfxoff",
                "attracton" => "attracton", "attractoff" => "attractoff",
                "distracton" => "distracton", "distractoff" => "distractoff",
                "poweroff" => "poweroff", "closebutton" => "closebutton", "reboot" => "reboot",
                "flipper" => "flipper",
                _ => null,
            };
            if (name != null) PlayClip(@"C:\SkillGame\Sounds\Settings\" + name + ".wav");
        }

        /// <summary>Loop a screen's theme on the dedicated background player so clicking buttons never stops the music.</summary>
        public void PlayScreenTheme(string screen)
        {
            string? f = screen switch
            {
                "Settings" => "settingsTheme",
                "Diagnostics" => "diagnosticsTheme",
                "Audits" => "auditsTheme",
                "Admin" => "adminTheme",
                _ => null,
            };
            if (f != null) PlayBgMusic(@"C:\SkillGame\Sounds\Settings\" + f + ".wav");
            else StopBackground();
        }

        public void PlaySound(string soundPackage, string file)
        {
            // Tilt has its own dedicated clip (shared, not per-package) — play it instead of a generic game-over.
            if (file == "Tilt") { PlayClip(@"C:\SkillGame\Sounds\Settings\tilt.wav"); return; }
            string? path = ResolveSoundPath(soundPackage, file);
            if (path != null) PlayClip(path);
        }

        // Resolve a (package, spoken-file) pair to its wav path — one place, shared by PlaySound + ClipLengthMs.
        private static string? ResolveSoundPath(string soundPackage, string file)
        {
            string? name = file switch
            {
                "10 pts" => "10pts", "20 pts" => "20pts", "30 pts" => "30pts", "40 pts" => "40pts",
                "50 pts" => "50pts", "60 pts" => "60pts", "70 pts" => "70pts", "80 pts" => "80pts",
                "Game Over" => "gameover",
                _ => null,
            };
            return name == null ? null : @"C:\SkillGame\Sounds\" + soundPackage + @"\" + name + ".wav";
        }

        /// <summary>Length of a scoring clip in milliseconds (0 if missing), so a test cycle can wait for it to finish.</summary>
        public double ClipLengthMs(string soundPackage, string file)
        {
            string? path = ResolveSoundPath(soundPackage, file);
            return path == null ? 0 : WavSeconds(path) * 1000.0;
        }

        // Read a PCM wav's duration from its RIFF header; returns 0 on any problem.
        public static double WavSeconds(string path)
        {
            try
            {
                using var fs = System.IO.File.OpenRead(path);
                using var br = new System.IO.BinaryReader(fs);
                if (new string(br.ReadChars(4)) != "RIFF") return 0;
                br.ReadInt32();                                   // overall RIFF size
                if (new string(br.ReadChars(4)) != "WAVE") return 0;
                int byteRate = 0; long dataLen = 0;
                while (fs.Position + 8 <= fs.Length)
                {
                    string id = new string(br.ReadChars(4));
                    int size = br.ReadInt32();
                    if (id == "fmt ")
                    {
                        byte[] fmt = br.ReadBytes(size);
                        if (fmt.Length >= 12) byteRate = BitConverter.ToInt32(fmt, 8);   // avg bytes/sec
                        if ((size & 1) == 1 && fs.Position < fs.Length) fs.Position += 1; // word-align pad
                    }
                    else if (id == "data") { dataLen = size; break; }
                    else { fs.Position += size + (size & 1); }
                }
                return byteRate > 0 ? (double)dataLen / byteRate : 0;
            }
            catch { return 0; }
        }

        /// <summary>Raise the master volume of the default audio endpoint.</summary>
        public void SetMasterVolumeUp()
        {
            try
            {
                var deviceEnumerator = new MMDeviceEnumerator();


                var device = deviceEnumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, Role.Multimedia);

                var audioEndpointVolume = device.AudioEndpointVolume;

                float currentVolume = (float)Math.Round(audioEndpointVolume.MasterVolumeLevelScalar, 2);

                audioEndpointVolume.Mute = false;

                if (currentVolume < 1.0f)
                {
                    if (currentVolume % 0.05f == 0 && currentVolume <= 0.95f)
                    {
                        audioEndpointVolume.MasterVolumeLevelScalar = currentVolume + 0.05f;
                    }
                    if (currentVolume % 0.05f != 0 && currentVolume > 0.95f)
                    {
                        audioEndpointVolume.MasterVolumeLevelScalar = 1.0f;
                    }
                    if (currentVolume % 0.05f != 0 && currentVolume <= 0.95f)
                    {
                        audioEndpointVolume.MasterVolumeLevelScalar = currentVolume + 0.05f;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while setting the volume: {ex.Message}");
            }
        }

        /// <summary>Lower the master volume of the default audio endpoint.</summary>
        public void SetMasterVolumeDown()
        {
            try
            {
                var deviceEnumerator = new MMDeviceEnumerator();


                var device = deviceEnumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, Role.Multimedia);

                var audioEndpointVolume = device.AudioEndpointVolume;

                float currentVolume = (float)Math.Round(audioEndpointVolume.MasterVolumeLevelScalar, 2);


                if (currentVolume > 0.0f)
                {
                    if (currentVolume % 0.05f == 0 && currentVolume >= 0.04f)
                    {
                        audioEndpointVolume.MasterVolumeLevelScalar = currentVolume - 0.05f;
                    }
                    if (currentVolume % 0.05f != 0 && currentVolume <= 0.04f)
                    {
                        audioEndpointVolume.MasterVolumeLevelScalar = 0.0f;
                    }
                    if (currentVolume % 0.05f != 0 && currentVolume >= 0.04f)
                    {
                        audioEndpointVolume.MasterVolumeLevelScalar = currentVolume - 0.05f;
                    }
                }

                currentVolume = (float)Math.Round(audioEndpointVolume.MasterVolumeLevelScalar, 2);

                // Mute if the level is 0
                if (currentVolume == 0.0f)
                {
                    audioEndpointVolume.Mute = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while setting the volume: {ex.Message}");
            }
        }

        /// <summary>Set the master volume to a percent (0-100), used by quiet hours.</summary>
        public void SetMasterVolumePercent(int percent)
        {
            try
            {
                var device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, Role.Multimedia);
                device.AudioEndpointVolume.Mute = false;
                device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Max(0f, Math.Min(1f, percent / 100f));
            }
            catch (Exception ex) { Console.WriteLine($"An error occurred setting quiet-hours volume: {ex.Message}"); }
        }

        public bool MuteVolume(bool muted)
        {
            bool isMuted = false;
            try
            {
                var deviceEnumerator = new MMDeviceEnumerator();
                var device = deviceEnumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, Role.Multimedia);
                var audioEndpointVolume = device.AudioEndpointVolume;

                if (muted) 
                {
                    audioEndpointVolume.Mute = false;
                    isMuted = false;
                }
                else
                {
                    audioEndpointVolume.Mute = true;
                    isMuted = true;
                }
                return isMuted;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while muting the volume: {ex.Message}");
                return isMuted;
            }
        }
    }

    /// <summary>Wraps a WaveStream so it loops forever, seeking back to the start at the end.</summary>
    internal sealed class LoopStream : WaveStream
    {
        private readonly WaveStream _source;
        public LoopStream(WaveStream source) { _source = source; }
        public override WaveFormat WaveFormat => _source.WaveFormat;
        public override long Length => _source.Length;
        public override long Position { get => _source.Position; set => _source.Position = value; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = _source.Read(buffer, offset + total, count - total);
                if (n == 0)
                {
                    if (_source.Position == 0) break;   // empty/zero-length source — avoid a tight loop
                    _source.Position = 0;               // loop back to the start
                }
                total += n;
            }
            return total;
        }
    }
}
