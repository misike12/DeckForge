namespace DeckForge.Core.Settings;

/// <summary>
/// What the user has asked about motion, as three answers rather than two.
/// </summary>
/// <remarks>
/// <para>
/// A bool would have been the obvious shape and it is the wrong one. <c>true</c>/<c>false</c> cannot
/// express "whatever the operating system says", and that third answer is the one almost everybody
/// wants: a user who has turned animations off in Windows has already made the decision, and asking
/// them again on the Settings page invites a second answer that can contradict the first.
/// </para>
/// <para>
/// The member names are the ones Part 20's table uses - system, always, never - because a settings key
/// written in a document and a member written in code should be recognisably the same thing. The
/// ambiguity of <c>Always</c> is resolved in every doc comment below rather than in the name, because
/// renaming it to <c>AlwaysReduce</c> would leave a reader comparing the JSON on disk with the
/// document.
/// </para>
/// </remarks>
public enum MotionPreference
{
    /// <summary>
    /// Follow the operating system: reduced motion when the system asks for it, full motion otherwise.
    /// </summary>
    /// <remarks>
    /// The default, and the only member that can change without the user touching DeckForge. See
    /// <see cref="MotionPolicy"/> for what the system answer actually is read from - it is a cached
    /// WPF property, not a live query, which is why the App re-reads it whenever it re-applies.
    /// </remarks>
    System,

    /// <summary>Always reduce motion, whatever the operating system says.</summary>
    /// <remarks>
    /// For the user who wants the app to stop moving and has not switched the whole machine off - a
    /// shared machine, or an OS setting they do not want to change for everything else.
    /// </remarks>
    Always,

    /// <summary>Never reduce motion, whatever the operating system says.</summary>
    /// <remarks>
    /// Exists because the other two need it: a three-way question where one answer cannot be
    /// expressed is a question whose default is doing the user's thinking for them.
    /// </remarks>
    Never,
}

/// <summary>
/// Turns a <see cref="MotionPreference"/> and what the system asked for into the one answer the UI acts
/// on.
/// </summary>
/// <remarks>
/// <para>
/// A separate class rather than a property on the enum or a method on the settings model, for one
/// reason: this is the rule, and the rule is the part worth testing. There is no window in
/// <c>DeckForge.Core</c>, so anything that needed one could not be tested at all - and "does the third
/// option actually do what it says" is precisely the kind of question a bool-shaped setting answers
/// without anyone noticing.
/// </para>
/// <para>
/// The system answer is passed in rather than read here. Reading it needs WPF, and this is the half of
/// reduced motion that a test can reach.
/// </para>
/// </remarks>
public static class MotionPolicy
{
    /// <summary>
    /// Whether motion is off for this session.
    /// </summary>
    /// <param name="preference">What the user asked for.</param>
    /// <param name="systemAsksToReduce">
    /// Whether the operating system asks for reduced motion. Ignored unless
    /// <paramref name="preference"/> is <see cref="MotionPreference.System"/>.
    /// </param>
    /// <returns>
    /// True when the App should stop the motion it governs. Only two of the sixteen settings keys reach
    /// this method today, and only one of those does anything visible - see the App-side motion
    /// controller for the honest list of what is and is not covered.
    /// </returns>
    /// <remarks>
    /// <see cref="MotionPreference.Always"/> and <see cref="MotionPreference.Never"/> are checked
    /// before the system value is looked at, so an explicit answer is never overruled. The default arm
    /// is <c>_</c> rather than a named case so that adding a member later resolves to following the
    /// system, which is the safe direction: motion a user did not ask to stop.
    /// </remarks>
    public static bool IsReduced(MotionPreference preference, bool systemAsksToReduce) =>
        preference switch
        {
            MotionPreference.Always => true,
            MotionPreference.Never => false,
            _ => systemAsksToReduce,
        };

    /// <summary>
    /// A sentence for the Settings page saying what the answer actually came out as.
    /// </summary>
    /// <param name="preference">What the user asked for.</param>
    /// <param name="systemAsksToReduce">Whether the operating system asks for reduced motion.</param>
    /// <returns>
    /// A sentence naming the resolved answer and, where it was the system that decided, saying so.
    /// </returns>
    /// <remarks>
    /// The page shows the raw enum member next to this, and that was the whole problem: three radio
    /// buttons reading "System", "Always" and "Never" tell a user nothing about which one is selected
    /// in effect, and "Always" beside a system that says motion is fine looks like it has been ignored.
    /// The sentence is what makes the resolution visible.
    /// </remarks>
    public static string Describe(MotionPreference preference, bool systemAsksToReduce) =>
        (preference, MotionPolicy.IsReduced(preference, systemAsksToReduce)) switch
        {
            (MotionPreference.Always, _) =>
                "Reduced. DeckForge's own transitions are off, whatever the system says.",
            (MotionPreference.Never, _) =>
                "Full motion, whatever the system says.",
            (_, true) =>
                "Reduced, because the system asks for reduced motion.",
            _ =>
                "Full motion, because the system does not ask for reduced motion.",
        };

    /// <summary>
    /// The three choices, with the labels the Settings page shows.
    /// </summary>
    /// <remarks>
    /// Declared here rather than in the view model so that the three labels are tested alongside the
    /// three behaviours. A label is the part a user reads and the part nobody re-checks; "Never" on its
    /// own is a complete non-sentence to somebody who has not read Part 20.
    /// </remarks>
    public static IReadOnlyList<MotionOption> Options { get; } =
    [
        new(
            MotionPreference.System,
            "Follow the system",
            "Reduced motion when Windows asks for it, full motion otherwise."),
        new(
            MotionPreference.Always,
            "Always reduce motion",
            "Transitions are off in DeckForge even if Windows still allows them."),
        new(
            MotionPreference.Never,
            "Never reduce motion",
            "Transitions stay on in DeckForge even if Windows asks for them off."),
    ];

    /// <summary>The choice matching a preference, for the page to preselect.</summary>
    /// <param name="preference">The stored preference.</param>
    /// <returns>
    /// The matching option. Falls back to the first one for a value that is not in the table, which can
    /// only come from a hand-edited file, and falling back to <see cref="MotionPreference.System"/> is
    /// the answer that follows the user rather than overruling them.
    /// </returns>
    public static MotionOption OptionFor(MotionPreference preference) =>
        Options.FirstOrDefault(option => option.Value == preference) ?? Options[0];
}

/// <summary>One of the three motion choices, with the wording the Settings page shows.</summary>
/// <param name="Value">The stored preference this option selects.</param>
/// <param name="Label">The radio button's text.</param>
/// <param name="Description">The sentence under the row, explaining what choosing it means.</param>
public sealed record MotionOption(MotionPreference Value, string Label, string Description);