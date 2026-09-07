namespace SkillGame
{
    /// <summary>Game rules — score, level, and switch behavior — with no UI or hardware dependencies so it stays unit-testable.</summary>
    public class GameEngine
    {
        private readonly IGameView _view;
        private readonly IAudioSink _audio;
        private readonly ILampSink _lamps;
        private readonly IAuditSink? _audits;
        private readonly ILedSink? _led;

        public int Score { get; private set; }
        public int Level { get; private set; }
        public bool GameInProgress { get; private set; }

        /// <summary>Operator setting: when false, the tilt switch is ignored entirely.</summary>
        public bool TiltEnabled { get; set; } = true;

        /// <summary>Operator setting: free tilt nudges before game over (0 = the first tilt ends it, N = N warnings first).</summary>
        public int TiltsAllowed { get; set; } = 0;

        private int _tilts;   // tilts taken this game (reset on coin)

        /// <summary>When false, nothing this game does is written to the audits — used by the demo/attract game,
        /// which drives the engine in software and must never count as physical play (games, switch wear, winners, scores).</summary>
        public bool RecordAudits { get; set; } = true;
        private IAuditSink? Aud => RecordAudits ? _audits : null;

        public GameEngine(IGameView view, IAudioSink audio, ILampSink lamps,
                          IAuditSink? audits = null, ILedSink? led = null)
        {
            _view = view; _audio = audio; _lamps = lamps; _audits = audits; _led = led;
        }

        private void Status(string lastSwitch, string status) => _view.ShowStatus(Score, Level, lastSwitch, status);

        /// <summary>Coin Up (S0): arm a new game at level 1.</summary>
        public void Coin(bool high)
        {
            if (!GameInProgress) ClearGame();
            if (Level == 0 && high)
            {
                GameInProgress = true;
                Level += 1;
                Score = 0;
                _tilts = 0;
                Aud?.GameStarted();
                _led?.Coin();
                Status("S0 - Coin Up", "Game In Progress");
            }
        }

        /// <summary>A scoring hole: only counts when the ball is on that switch's row.</summary>
        public void Hit(SwitchDef def, bool high)
        {
            if (!high || !GameInProgress || Level != def.Level) return;

            Aud?.SwitchHit(def.Id);
            _audio.PlaySound(_view.SoundPackage, def.Points + " pts");   // spaced key matches AudioFunctions
            Score += def.Points;
            if (def.Points >= 50) _lamps.Flourish(Score);   // big hit: crazy lamp burst, then settle on the score
            else _lamps.TallyScore(Score);
            Level += 1;
            _led?.Score(def.Points, Level);

            if (def.IsWinner)
            {
                Aud?.Won();
                _led?.Winner();
                Status(def.Label, "Winner!");   // distinct from a plain loss, so the UI can celebrate a win
                _lamps.Winner();
                GameInProgress = false;         // a winner ends the game
                Aud?.GameEnded(Score);      // record play time + the winning score in the high-score table
            }
            else
            {
                Status(def.Label, "Game In Progress");
            }
        }

        /// <summary>Gobble hole (S23): drains the ball, ends the game.</summary>
        public void Gobble(bool high)
        {
            if (!high || !GameInProgress) return;   // ignore a drain when no game is running (e.g. right after a winner)
            _audio.PlaySound(_view.SoundPackage, "Game Over");
            _lamps.SetLamp("GameOver", true);
            _led?.GameOver(false);
            Status("S23 - Gobble", "Game Over!");
            GameInProgress = false;
            Aud?.GameEnded(Score);
        }

        /// <summary>Tilt (S27): light the tilt + game-over lamps and end the game.</summary>
        public void Tilt(bool high)
        {
            if (!high || !TiltEnabled || !GameInProgress) return;   // can't tilt a game that isn't running
            _tilts++;
            _audio.PlaySound(_view.SoundPackage, "Tilt");           // dedicated tilt clip on every tilt
            // TiltsAllowed = free tilt nudges before the game tilts: 0 ends it on the first tilt, N gives N warnings.
            if (_tilts > System.Math.Max(0, TiltsAllowed))
            {
                _lamps.SetLamp("Tilt", true);
                _lamps.SetLamp("GameOver", true);
                _led?.GameOver(true);
                Status("S27 - Tilt", "Game Over");
                GameInProgress = false;
                Aud?.GameEnded(Score);
            }
            else
            {
                // Warning only — the game keeps going, light the tilt lamp as a cue.
                _lamps.SetLamp("Tilt", true);
                Status($"S27 - Tilt WARNING {_tilts}/{TiltsAllowed}", "Game In Progress");
            }
        }

        /// <summary>Reset score/level and clear all lamps.</summary>
        public void ClearGame()
        {
            Score = 0;
            Level = 0;
            _tilts = 0;
            _lamps.TallyScore(0);   // clears all score lamps + solenoids
            _view.ShowScoreLevel(0, 0);
        }
    }
}
