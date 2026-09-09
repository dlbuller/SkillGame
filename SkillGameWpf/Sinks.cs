using SkillGame;

namespace SkillGameWpf
{
    // No-op sinks that let the engine run with no hardware attached.
    internal sealed class NoAudioSink : IAudioSink
    {
        public void PlaySound(string soundPackage, string file) { }
    }

    internal sealed class NoLampSink : ILampSink
    {
        public void TallyScore(int value) { }
        public void Winner() { }
        public void SetLamp(string lamp, bool on) { }
        public void Flourish(int finalScore) { }
        public void PulseSolenoid(string name, int ms = 250) { }
    }

    internal sealed class NoLedSink : ILedSink
    {
        public void Coin() { }
        public void Score(int points, int level) { }
        public void Winner() { }
        public void GameOver(bool tilt) { }
    }

    // Scoring audio, gated by the operator's SOUND FX toggle.
    internal sealed class SoundFxGate : IAudioSink
    {
        private readonly AudioFunctions _audio;
        public SoundFxGate(AudioFunctions audio) { _audio = audio; }
        public void PlaySound(string soundPackage, string file)
        {
            if (AppState.SoundFxOn) _audio.PlaySound(soundPackage, file);
        }
    }

    // Demo audio that lets the view time the scoring and end sounds to the coin animation.
    internal sealed class DeferredScoreSink : IAudioSink
    {
        private readonly AudioFunctions _audio;
        public DeferredScoreSink(AudioFunctions audio) { _audio = audio; }
        public void PlaySound(string soundPackage, string file)
        {
            if (!AppState.SoundFxOn) return;
            // The view plays these itself in sync with the coin, so drop the engine's early calls. Tilt plays
            // on every nudge (warning and final), so let it through.
            if (file != null && (file.Contains("pts") || file == "Game Over")) return;
            _audio.PlaySound(soundPackage, file);
        }
    }
}
