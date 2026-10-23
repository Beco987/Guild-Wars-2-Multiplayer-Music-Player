namespace GW2MM.Tests;

/// <summary>
/// Tests that measure real elapsed time run one class at a time and never alongside other test classes, so CPU
/// load from parallel tests cannot masquerade as playback lateness.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingCollection
{
    public const string Name = "Timing-sensitive";
}
