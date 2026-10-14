namespace GW2MM.Core.Humanizing;

/// <summary>How solo play is humanized (client Settings → Humanize; Config.txt "Humanize", 0..6).</summary>
/// <remarks>Subtle, Natural and Loose only place notes; Phrased, Expressive and Rubato also shape the tempo of each phrase.</remarks>
public enum HumanizeLevel { Off = 0, Subtle = 1, Natural = 2, Loose = 3, Phrased = 4, Expressive = 5, Rubato = 6 }

/// <summary>
/// The timing values of one humanize level. They are deliberately small: the game already varies note timing by up to a
/// frame, and a Guild Wars 2 key has no velocity, so timing is the one dimension of the performance this app controls.
/// </summary>
/// <param name="Timing">Standard deviation of a chord's own timing deviation, as a fraction of the time to its window's
/// nearest notes. The deviation is correlated from chord to chord (a player drifting slightly around the beat), not
/// independent jitter.</param>
/// <param name="TimingCapMs">Upper limit of that standard deviation in ms.</param>
/// <param name="PlayerOffsetMs">Standard deviation of each game window's constant lead or lag (several windows only).</param>
/// <param name="PlayerLagMs">How far one window may fall behind the others on its own, after an octave change that needed
/// extra time, before the other windows wait for it (several windows only).</param>
/// <param name="CatchUp">Tempo increase used to win back time an octave change took (a lagging window alone uses twice it).</param>
public sealed record HumanizeProfile(double Timing, double TimingCapMs, double PlayerOffsetMs, double PlayerLagMs, double CatchUp)
{
    // Phrase shaping (the expressive levels only); every value is a local tempo change, so it sounds alike at any tempo.

    /// <summary>Tempo arch over each phrase (A): its start about 0.4 A slower, the middle about 0.3 A faster and its end
    /// about 0.7 A slower than written, with the phrase's total length unchanged.</summary>
    public double PhraseArch { get; init; }
    /// <summary>Extra length of a phrase's last note, as a fraction of its time to the next note, times the strength of the
    /// phrase boundary (the note before it gets about a third of that).</summary>
    public double PhraseEnd { get; init; }
    /// <summary>Tempo reached at the very end of the closing ritardando, as a fraction of the song's tempo (1 = none).</summary>
    public double FinalTempo { get; init; } = 1;
    /// <summary>Extra length of a note that starts a bar (an agogic accent).</summary>
    public double DownbeatStretch { get; init; }
    /// <summary>Standard deviation of a slow, smooth tempo fluctuation (correlation time 4 s).</summary>
    public double TempoWander { get; init; }
    /// <summary>How much steadier notes on strong beats are than on weak ones (0..1).</summary>
    public double BeatSteadiness { get; init; }

    /// <summary>Whether the level shapes phrases (and so needs the song's phrasing).</summary>
    public bool ShapesPhrases => PhraseArch > 0 || PhraseEnd > 0 || FinalTempo < 1 || DownbeatStretch > 0 || TempoWander > 0;

    public static HumanizeProfile For(HumanizeLevel level) => level switch
    {
        HumanizeLevel.Subtle => new(Timing: 0.005, TimingCapMs: 1, PlayerOffsetMs: 0.5, PlayerLagMs: 10, CatchUp: 0.02),
        HumanizeLevel.Natural => new(Timing: 0.01, TimingCapMs: 2, PlayerOffsetMs: 1, PlayerLagMs: 15, CatchUp: 0.02),
        HumanizeLevel.Loose => new(Timing: 0.015, TimingCapMs: 4, PlayerOffsetMs: 2, PlayerLagMs: 20, CatchUp: 0.02),
        HumanizeLevel.Phrased => new(Timing: 0.008, TimingCapMs: 1.5, PlayerOffsetMs: 1, PlayerLagMs: 15, CatchUp: 0.02)
        {
            PhraseArch = 0.025, PhraseEnd = 0.04, FinalTempo = 0.90, DownbeatStretch = 0.005, TempoWander = 0.004, BeatSteadiness = 0.4,
        },
        HumanizeLevel.Expressive => new(Timing: 0.01, TimingCapMs: 2, PlayerOffsetMs: 1.5, PlayerLagMs: 15, CatchUp: 0.02)
        {
            PhraseArch = 0.045, PhraseEnd = 0.08, FinalTempo = 0.80, DownbeatStretch = 0.01, TempoWander = 0.007, BeatSteadiness = 0.4,
        },
        HumanizeLevel.Rubato => new(Timing: 0.012, TimingCapMs: 3, PlayerOffsetMs: 2, PlayerLagMs: 20, CatchUp: 0.02)
        {
            PhraseArch = 0.08, PhraseEnd = 0.14, FinalTempo = 0.70, DownbeatStretch = 0.018, TempoWander = 0.011, BeatSteadiness = 0.4,
        },
        // Off: only the octave-change scheduling (used by tests to isolate it; solo play with Off uses the plain arrangement).
        _ => new(0, 0, 0, 0, 0.02),
    };
}
