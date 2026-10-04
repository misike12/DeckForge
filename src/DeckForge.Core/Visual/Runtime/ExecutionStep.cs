namespace DeckForge.Core.Visual.Runtime;

/// <summary>What kind of thing happened.</summary>
public enum ExecutionStepKind
{
    /// <summary>A block is about to run.</summary>
    BlockEntered,

    /// <summary>A block has finished.</summary>
    BlockExited,

    /// <summary>A reporter produced a value.</summary>
    ValueComputed,

    /// <summary>The block asked the host to do something.</summary>
    HostCall,

    /// <summary>Something was written to the log.</summary>
    LogEmitted,

    /// <summary>Time passed.</summary>
    WaitElapsed,

    /// <summary>A breakpoint stopped the run.</summary>
    BreakpointHit,

    /// <summary>
    /// A pause stopped the run, and this is the block it had just finished.
    /// </summary>
    /// <remarks>
    /// Its own kind rather than a reused <see cref="BreakpointHit"/>, because the two say opposite things
    /// about the block they name. A breakpoint is recorded <em>before</em> the block runs and the block has
    /// not happened; a pause is recorded <em>after</em> it, so the block has run and the trace must not
    /// imply otherwise. Reusing one kind would put a line in the trace that reads "breakpoint at X" beside a
    /// block that had already executed, and the next thing anybody does with that trace is believe it.
    /// </remarks>
    PausedAfter,

    /// <summary>The run stopped, and this is why.</summary>
    Error,
}

/// <summary>One thing that happened, as the trace shows it.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="BlockId">The block it happened to, or null for a run-level step.</param>
/// <param name="Label">The block's label with its holes filled, or a sentence.</param>
/// <param name="Value">The value computed or returned, when there was one.</param>
/// <param name="Surface">The host surface, for a host call.</param>
/// <param name="Member">The member called, for a host call.</param>
/// <param name="Millisecond">How far into the run it happened, on the simulated clock.</param>
public sealed record ExecutionStep(
    ExecutionStepKind Kind,
    string? BlockId = null,
    string? Label = null,
    string? Value = null,
    string? Surface = null,
    string? Member = null,
    double Millisecond = 0)
{
    /// <summary>One line of the trace, as the timeline shows it.</summary>
    public string Describe() => Kind switch
    {
        ExecutionStepKind.BlockEntered => $"enter {Label}",
        ExecutionStepKind.BlockExited => $"exit  {Label}",
        ExecutionStepKind.ValueComputed => $"{Label} = {Value}",
        ExecutionStepKind.HostCall => $"{Surface}.{Member}({Value})",
        ExecutionStepKind.LogEmitted => $"log  {Label}",
        ExecutionStepKind.WaitElapsed => $"wait {Value} ms",
        ExecutionStepKind.BreakpointHit => $"breakpoint at {Label}",
        ExecutionStepKind.PausedAfter => $"paused after {Label}",
        _ => Label ?? "error",
    };
}