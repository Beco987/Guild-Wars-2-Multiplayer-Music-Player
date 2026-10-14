namespace GW2MM.Core.Humanizing;

/// <summary>How much solo play is humanized (client Settings → Humanize; Config.txt "Humanize", 0..3).</summary>
public enum HumanizeLevel { Off = 0, Subtle = 1, Natural = 2, Loose = 3 }

/// <summary>
/// The timing values of one humanize level. They are deliberately small: the game already varies note timing by up to a
/// frame, and a Guild Wars 2 key has no velocity, so a few milliseconds is all a note needs to stop sounding mechanical.
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
    public static HumanizeProfile For(HumanizeLevel level) => level switch
    {
        HumanizeLevel.Subtle => new(Timing: 0.005, TimingCapMs: 1, PlayerOffsetMs: 0.5, PlayerLagMs: 10, CatchUp: 0.02),
        HumanizeLevel.Natural => new(Timing: 0.01, TimingCapMs: 2, PlayerOffsetMs: 1, PlayerLagMs: 15, CatchUp: 0.02),
        HumanizeLevel.Loose => new(Timing: 0.015, TimingCapMs: 4, PlayerOffsetMs: 2, PlayerLagMs: 20, CatchUp: 0.02),
        // Off: only the octave-change scheduling (used by tests to isolate it; solo play with Off uses the plain arrangement).
        _ => new(0, 0, 0, 0, 0.02),
    };
}
