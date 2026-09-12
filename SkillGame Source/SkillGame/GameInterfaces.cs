namespace SkillGame
{
    /// <summary>Display surface the game rules push state to.</summary>
    public interface IGameView
    {
        string SoundPackage { get; }
        void ShowStatus(int score, int level, string lastSwitch, string status);
        void ShowScoreLevel(int score, int level);
    }

    /// <summary>Sound output the game rules use.</summary>
    public interface IAudioSink
    {
        void PlaySound(string soundPackage, string file);
    }

    /// <summary>Lamp / solenoid output the game rules use.</summary>
    public interface ILampSink
    {
        void TallyScore(int value);
        void Winner();
        void SetLamp(string lamp, bool on);
        /// <summary>Play a brief "crazy" celebration burst on the score lamps, then settle back onto the given score.</summary>
        void Flourish(int finalScore);
        /// <summary>Fire a named lock coil as a brief pulse (energize to retract, then release). Never held — these coils are intermittent-duty.
        /// startDelayMs offsets the pulse so two coils fired together don't overlap on the 5 V rail.</summary>
        void PulseSolenoid(string name, int ms = 250, int startDelayMs = 0);
    }

    /// <summary>Persistent audit counters the game rules feed (games, play time, per-switch hits, high scores).</summary>
    public interface IAuditSink
    {
        void GameStarted();
        void GameEnded(int finalScore);
        void Won();
        void SwitchHit(string switchId);
    }

    /// <summary>Optional show-lighting reactions to game events on the WS2812b strip.</summary>
    public interface ILedSink
    {
        void Coin();
        void Score(int points, int level);
        void Winner();
        void GameOver(bool tilt);
    }
}
