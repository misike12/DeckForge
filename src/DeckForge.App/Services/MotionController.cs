using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using DeckForge.Core.Settings;
using Wpf.Ui.Animations;
using Wpf.Ui.Controls;

namespace DeckForge.App.Services;

/// <summary>What one application of the motion setting actually did.</summary>
/// <param name="Preference">What the settings file said.</param>
/// <param name="SystemAsksToReduce">Whether Windows asks for reduced motion.</param>
/// <param name="Reduced">The resolved answer the controls were set from.</param>
/// <param name="Summary">A sentence naming the resolved answer.</param>
/// <param name="ControlsChanged">
/// How many live controls this application had to change. Zero is a legitimate answer and a useful one:
/// it means there was no navigation view on screen to stop, not that nothing happened.
/// </param>
/// <remarks>
/// A record rather than a bool because "reduced motion is on" and "reduced motion did something" are
/// different claims, and only one of them is what a user is asking for. The count is here so the claim
/// can be checked rather than believed.
/// </remarks>
public sealed record MotionReport(
    MotionPreference Preference,
    bool SystemAsksToReduce,
    bool Reduced,
    string Summary,
    int ControlsChanged)
{
    /// <summary>Whether this application changed at least one control.</summary>
    public bool DidSomething => ControlsChanged > 0;

    /// <summary>
    /// A sentence naming what reduced motion governs here, in full, for the Settings page to show.
    /// </summary>
    /// <remarks>
    /// Long, and deliberately so. This is the string a contributor reads when they want to know what
    /// <c>VisualReduceMotion</c> actually does, and the answer has three parts that are all easy to
    /// over-claim: what it stops, what it cannot reach, and what was never animated to begin with. A
    /// shorter sentence would be a tidier one and a wrong one.
    /// </remarks>
    public const string Scope =
        "Applies to: the slide and fade between sidebar pages, the drop animation of the combo boxes and "
        + "tooltips already on screen, and the window manager's own open, close and snap animations. "
        + "Does not apply to: the block canvas, which draws nothing in motion - Part 19's duration table "
        + "has no constants in this build because there is no animation to give them to - or WPF-UI's own "
        + "control animations, which live inside its compiled theme and expose no switch.";
}

/// <summary>
/// Applies the reduced-motion setting to the window, and takes it back when the user changes their mind.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is for.</b> Part 19 asks for reduced motion to collapse a table of animation durations,
/// and §20.1 records that none of those durations exist: this application has no animation of its own, so
/// there is nothing to shorten. The honest version of the feature is therefore not a duration table - it is
/// the motion that <em>is</em> real, which is the motion the framework and the UI toolkit produce on
/// their own.
/// </para>
/// <para>
/// <b>What was checked, and what was found.</b> WPF-UI 4.3.0's public surface was read out of
/// <c>Wpf.Ui.dll</c> with a metadata reader (the shape <c>tools/SdkInventory</c> takes for the Macro Deck
/// SDK). It exposes <c>Wpf.Ui.Animations.Transition</c>,
/// <c>Wpf.Ui.Animations.TransitionAnimationProvider.ApplyTransition</c>, and per-instance
/// <c>Transition</c> and <c>TransitionDuration</c> on <c>INavigationView</c> - and no global switch
/// anywhere: no "animations disabled" resource, no application-level flag, nothing on
/// <c>ApplicationThemeManager</c>. It also ships a CsWin32 <c>Windows.Win32.PInvoke.DwmSetWindowAttribute</c>,
/// which is how the window-manager half below was found. WPF itself offers nothing either:
/// <c>SystemParameters</c>'s animation properties are read-only, so
/// <c>SystemParameters.ClientAreaAnimation</c> can be read as the operating system's answer and can never
/// be set from managed code.
/// </para>
/// <para>
/// <b>Therefore three things are changed and three are not.</b> Changed: the sidebar's page transitions,
/// the popups already on screen, and the window manager's window transitions. Not changed: WPF-UI's own
/// control hover and press animations, which are storyboards inside its compiled theme with no public way
/// in; and nothing inside the block canvas, which does not animate. Both gaps are stated to the user
/// through <see cref="MotionReport.Scope"/> rather than only here.
/// </para>
/// <para>
/// <b>No resource dictionary is involved, on purpose.</b> The obvious way to stop every popup at once is an
/// implicit <c>Style</c> for <see cref="Popup"/> in <c>Application.Resources</c>, and it is the wrong tool:
/// an implicit style found there <em>replaces</em> the theme's own Popup style, and that style carries the
/// template the popup is drawn with. Setting <c>PopupAnimation</c> that way would satisfy "no motion"
/// perfectly by making every combo box, menu and tooltip invisible.
/// </para>
/// </remarks>
public sealed class MotionController
{
    /// <summary>
    /// The transition each navigation view had before the controller touched it.
    /// </summary>
    /// <remarks>
    /// Recorded once per element and never overwritten. Overwriting would save the value this class had
    /// just written, so turning the setting off would leave every page with no transition at all rather
    /// than with the theme's own - which is what a reduced-motion switch that cannot be undone looks like
    /// from the outside.
    /// </remarks>
    private readonly Dictionary<INavigationView, SavedTransition> _savedTransitions =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>The animation each popup had before the controller touched it.</summary>
    private readonly Dictionary<Popup, PopupAnimation> _savedPopupAnimations = [];

    /// <summary>The last application, for the Settings page and for a bug report.</summary>
    public MotionReport Current { get; private set; } = Initial(MotionPreference.System);

    /// <summary>
    /// Whether Windows is asking for reduced motion.
    /// </summary>
    /// <returns>
    /// True when the system asks for reduced motion, and false whenever the answer cannot be read - a
    /// failure to read an accessibility preference must not quietly turn motion off for a user who did
    /// not ask for that.
    /// </returns>
    /// <remarks>
    /// <c>SystemParameters.ClientAreaAnimation</c> is WPF's view of the operating system's "animate
    /// windows" preference and is the only public thing WPF exposes on the subject. It is read-only, which
    /// is why it can answer the question and cannot answer it back; and WPF caches it rather than polling
    /// it, so this method is called again on every apply instead of being read once at startup and
    /// remembered. The same shape, and the same limitation, applies to the high-contrast read in
    /// <c>BlockTheme.ReadSystemContrast</c>.
    /// </remarks>
    public static bool SystemAsksToReduce()
    {
        try
        {
            return !SystemParameters.ClientAreaAnimation;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // A session with no window station, which is what a service or a headless test host has.
            // Motion on is the documented default, so this is not worth surfacing.
            return false;
        }
    }

    /// <summary>
    /// Applies the setting from the settings file, undoing whatever the last application changed.
    /// </summary>
    /// <param name="settings">The current settings.</param>
    /// <returns>What was applied.</returns>
    /// <remarks>
    /// Called at startup and again on every settings change, which is what makes the setting take effect
    /// without a restart. Undoing first and re-applying second rather than branching on the answer is
    /// deliberate: it is the only ordering in which a navigation view created since the last application -
    /// and pages are created as they are navigated to - is caught by the walk without this class having to
    /// know when new ones appear.
    /// </remarks>
    public MotionReport Apply(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Apply(settings.VisualReduceMotion, SystemAsksToReduce());
    }

    /// <summary>Applies an already-resolved answer, for a caller that has read the system value.</summary>
    /// <param name="preference">What the user asked for.</param>
    /// <param name="systemAsksToReduce">Whether Windows asks for reduced motion.</param>
    /// <returns>What was applied.</returns>
    public MotionReport Apply(MotionPreference preference, bool systemAsksToReduce)
    {
        var reduced = MotionPolicy.IsReduced(preference, systemAsksToReduce);

        Restore();

        var changed = reduced ? Suppress() : 0;

        if (!reduced)
        {
            foreach (var window in Windows())
            {
                EnableWindowTransitions(window);
            }
        }

        Current = new MotionReport(
            preference,
            systemAsksToReduce,
            reduced,
            MotionPolicy.Describe(preference, systemAsksToReduce),
            changed);

        return Current;
    }

    /// <summary>Puts back everything a previous application changed.</summary>
    /// <remarks>
    /// Runs before every apply, including the first. That costs nothing on the first call - both
    /// dictionaries are empty - and it removes the one path on which this class's idea of what it changed
    /// and the windows' idea can disagree, which is how a switch ends up stuck on after being turned off.
    /// </remarks>
    private void Restore()
    {
        foreach (var (view, saved) in _savedTransitions)
        {
            view.Transition = saved.Transition;
            view.TransitionDuration = saved.Duration;
        }

        _savedTransitions.Clear();

        foreach (var (popup, animation) in _savedPopupAnimations)
        {
            popup.PopupAnimation = animation;
        }

        _savedPopupAnimations.Clear();
    }

    /// <summary>Stops the motion that exists, on every window that is open.</summary>
    /// <returns>How many live controls were changed.</returns>
    private int Suppress()
    {
        var changed = 0;

        foreach (var window in Windows())
        {
            // 1. The sidebar's page transitions. The NavigationView in MainWindow does not run the
            //    animation itself - NavigationViewContentPresenter, inside its template, does - so both
            //    are walked. Setting only the view would leave the presenter animating at the theme's
            //    duration, which is the exact failure of a setting that reports success.
            foreach (var view in Descendants(window).OfType<INavigationView>())
            {
                if (!_savedTransitions.TryAdd(view, new SavedTransition(view.Transition, view.TransitionDuration)))
                {
                    continue;
                }

                view.Transition = Transition.None;
                view.TransitionDuration = 0;
                changed++;
            }

            // 2. Popups that are already in the tree, set locally. A local value beats the theme's, which
            //    is what makes this work on a ComboBox whose template binds PopupAnimation from the
            //    ComboBox rather than from a style this class could reach.
            foreach (var popup in Descendants(window).OfType<Popup>())
            {
                if (!_savedPopupAnimations.TryAdd(popup, popup.PopupAnimation))
                {
                    continue;
                }

                popup.PopupAnimation = PopupAnimation.None;
                changed++;
            }

            // 3. The window manager's own transitions: what Windows plays when this window opens, closes,
            //    maximises or snaps. Per-window and reversible, and the only motion here the operating
            //    system owns rather than DeckForge.
            DisableWindowTransitions(window);
            changed++;
        }

        return changed;
    }

    /// <summary>The windows that exist and are on screen.</summary>
    /// <returns>
    /// Every open window, or an empty list when the caller is not on the dispatcher thread. An empty list
    /// rather than a marshalled call, because an apply that crossed threads would block the settings save
    /// that triggered it.
    /// </returns>
    private static IEnumerable<Window> Windows()
    {
        var application = Application.Current;
        if (application is null || !application.Dispatcher.CheckAccess())
        {
            return [];
        }

        return application.Windows.OfType<Window>().Where(window => window.IsLoaded).ToList();
    }

    /// <summary>Every element in a window's logical tree.</summary>
    /// <param name="root">Where to start.</param>
    /// <returns>The root, then each descendant, breadth-last.</returns>
    /// <remarks>
    /// The logical tree rather than the visual one, and that is not a detail. A <see cref="Popup"/> is not
    /// in its owner's visual tree at all - it lives in a window of its own - which is precisely why a
    /// visual-tree walk finds no popups and reports "reduced motion applied" while changing nothing that
    /// fades. A Popup is a logical child of what it shows, so it is here.
    /// </remarks>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var pending = new Queue<DependencyObject>();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            yield return current;

            foreach (var child in LogicalChildren(current))
            {
                pending.Enqueue(child);
            }
        }
    }

    /// <summary>One node's logical children, or none if it cannot be asked.</summary>
    /// <param name="node">The node.</param>
    /// <returns>Its logical children.</returns>
    /// <remarks>
    /// Wrapped because <see cref="LogicalTreeHelper"/> refuses some node kinds outright - a
    /// <see cref="ContentElement"/> has no logical tree - and this walk runs inside a settings save. An
    /// exception here would escape into <c>SettingsService.Save</c>, and a settings file that cannot be
    /// saved because a tooltip is a <c>Run</c> is a spectacular way to lose a preference.
    /// </remarks>
    private static IEnumerable<DependencyObject> LogicalChildren(DependencyObject node)
    {
        if (node is ContentElement)
        {
            return [];
        }

        try
        {
            return LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>().ToList();
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>Asks the window manager to stop animating one window.</summary>
    /// <param name="window">The window.</param>
    /// <remarks>
    /// Guarded on the OS version because the attribute arrived in Windows 10 10240. On anything older the
    /// window animates exactly as it always did, which is the correct outcome rather than a failure.
    /// </remarks>
    private static void DisableWindowTransitions(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10240))
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        SetWindowTransitionAttribute(handle, disabled: true);
    }

    /// <summary>Gives a window its window-manager animations back.</summary>
    /// <param name="window">The window.</param>
    private static void EnableWindowTransitions(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10240))
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        SetWindowTransitionAttribute(handle, disabled: false);
    }

    /// <summary>Sets or clears <c>DWMWA_TRANSITIONS_FORCEDISABLED</c> on one window.</summary>
    /// <param name="handle">The window's HWND.</param>
    /// <param name="disabled">Whether to suppress the window's own animations.</param>
    /// <remarks>
    /// <c>PreserveSig</c> on purpose: this returns an HRESULT rather than raising, and a non-zero value
    /// means the OS declined. A refusal is not worth a dialog - the animations simply continue, which is
    /// what happened before the setting existed. Declared here rather than called through WPF-UI's
    /// <c>Windows.Win32.PInvoke</c>, which is a CsWin32 type that happens to be public inside their
    /// assembly: depending on another library's generated interop namespace is a coupling that breaks
    /// silently the day they regenerate it, and it would be the only place in this application that
    /// reaches into a dependency for a system call it could make itself.
    /// </remarks>
    [SupportedOSPlatform("windows10.0.10240.0")]
    private static void SetWindowTransitionAttribute(IntPtr handle, bool disabled)
    {
        var value = disabled ? 1 : 0;

        try
        {
            DwmSetWindowAttribute(handle, TransitionsForcedDisabled, ref value, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // No dwmapi, which means no composition and therefore no window animations to suppress.
        }
    }

    /// <summary>Attribute 3: suppress the window's own open, close and snap animations.</summary>
    private const int TransitionsForcedDisabled = 3;

    [DllImport("dwmapi.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int value,
        int size);

    /// <summary>The answer the default preference produces, read once so a fresh controller is honest.</summary>
    /// <param name="preference">The default preference.</param>
    /// <returns>A report describing the default before anything has been applied.</returns>
    private static MotionReport Initial(MotionPreference preference)
    {
        var system = SystemAsksToReduce();
        return new MotionReport(
            preference,
            system,
            MotionPolicy.IsReduced(preference, system),
            MotionPolicy.Describe(preference, system),
            ControlsChanged: 0);
    }

    /// <summary>A navigation view's transition before the controller changed it.</summary>
    /// <param name="Transition">The <c>Transition</c> member it had.</param>
    /// <param name="Duration">The duration in milliseconds it had.</param>
    private readonly record struct SavedTransition(Transition Transition, int Duration);
}