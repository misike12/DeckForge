namespace DeckForge.App.Pages;

/// <summary>
/// A page that has to re-read the open workspace when it comes into view.
/// </summary>
/// <remarks>
/// The shell used to type-test each page in a thirteen-branch else-if chain to call this. A page
/// added to <see cref="PageRegistry"/> without also being added to that chain simply never
/// refreshed, and nothing said so - the page just showed whatever it had from last time. An
/// interface makes forgetting impossible: the branch is one <c>is</c> test.
/// </remarks>
public interface IRefreshOnNavigate
{
    void RefreshOnNavigate();
}

/// <summary>
/// A page that can be sent to a document inside itself, by a tag of <c>page::path</c>.
/// </summary>
/// <remarks>
/// Separate from <see cref="IRefreshOnNavigate"/> because only the docs browser has a target, and
/// folding it into the same interface would mean every page grew a no-op.
/// </remarks>
public interface INavigateWithin
{
    void NavigateWithin(string path);
}
