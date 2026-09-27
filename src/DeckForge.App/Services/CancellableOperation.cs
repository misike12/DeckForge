using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DeckForge.App.Services;

/// <summary>
/// Tracks a view model's long-running commands so one Cancel button can stop whichever is running.
/// </summary>
/// <remarks>
/// The generator does create each command from a <c>Func&lt;CancellationToken, Task&gt;</c>, so the
/// toolkit wires a token and <c>IAsyncRelayCommand.Cancel()</c> works. What it does not generate is
/// a command to call it: a <c>CancelBuildCommand</c>, a <c>CancelShipCommand</c>, and so on. Every
/// one of those commands therefore had a reachable <c>catch (OperationCanceledException)</c> that no
/// user could ever trigger, and a build or a pack had to be waited out.
/// <para>
/// One shared button is the right shape here rather than a cancel command per button: the toolkit's
/// async commands refuse concurrent execution anyway, so there is never more than one to cancel.
/// </para>
/// </remarks>
public sealed class CancellableOperation : ObservableObject
{
    private readonly List<IAsyncRelayCommand> _commands = [];

    /// <summary>Registers the commands this tracker can cancel. Call once, from the constructor.</summary>
    public CancellableOperation Track(params IAsyncRelayCommand[] commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        foreach (var command in commands)
        {
            _commands.Add(command);

            // IsRunning is the only thing that makes the button enable and disable itself, and the
            // toolkit raises it on a background continuation - so the notification has to be
            // marshalled or the bound button never updates.
            command.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(IAsyncRelayCommand.IsRunning))
                {
                    OnPropertyChanged(nameof(IsRunning));
                    CancelCommand.NotifyCanExecuteChanged();
                }
            };
        }

        return this;
    }

    /// <summary>True while any tracked command is running.</summary>
    public bool IsRunning
    {
        get
        {
            foreach (var command in _commands)
            {
                if (command.IsRunning)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private RelayCommand? _cancelCommand;

    /// <summary>Cancels whichever tracked command is running.</summary>
    public RelayCommand CancelCommand =>
        _cancelCommand ??= new RelayCommand(Cancel, () => IsRunning);

    private void Cancel()
    {
        foreach (var command in _commands)
        {
            if (command.IsRunning)
            {
                command.Cancel();
            }
        }
    }
}
