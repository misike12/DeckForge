using NUnit.Framework;

namespace DeckForge.Tests;

/// <summary>
/// One temporary directory for one test, removed when the test is done with it.
/// </summary>
/// <remarks>
/// <para>
/// Sixteen test files used to grow their own copy of <c>Path.Combine(Path.GetTempPath(), "..." +
/// Guid.NewGuid())</c>, with three different teardowns between them and one that never removed
/// anything. This is the one copy: it creates a directory no other fixture can be holding, and it
/// removes it without ever failing the test that made it.
/// </para>
/// <para>
/// Two rules are why it is a type rather than a helper method.
/// </para>
/// <para>
/// <b>Never share a directory.</b> The name carries a full 128-bit GUID, so a second fixture - in this
/// process, or in a parallel run of the suite - cannot land on the same path, and neither can a run
/// that died holding its directory. The one call site that used a fixed name had no GUID at all, so
/// two parallel runs wrote into one directory and deleted each other's files underneath the test.
/// </para>
/// <para>
/// <b>Never fail on removal.</b> A locked file - a <c>dotnet build</c> that has not exited, an indexer
/// holding an assembly - would otherwise turn a passing test into a failing one during teardown, which
/// is the worst possible moment to be told about it. A failure here is reported to the test output and
/// remembered, so it is visible without being fatal.
/// </para>
/// </remarks>
internal sealed class TempDirectory : IDisposable
{
    private bool _disposed;

    /// <summary>Creates a fresh, empty directory under the system temp path.</summary>
    /// <param name="prefix">
    /// A readable stem, so a directory left behind by a crash can be attributed to a test. Not part of
    /// the uniqueness guarantee - the GUID is.
    /// </param>
    public TempDirectory(string prefix = "deckforge")
    {
        Root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            prefix + "-" + Guid.NewGuid().ToString("N"));

        // The constructor's contract is "a directory this fixture owns, and it is empty", not "this path
        // has never been used". A full GUID makes a stale path unreachable in practice, and CreateDirectory
        // would quietly reuse it if one ever appeared, so the stale directory is removed rather than
        // inherited: a test that finds yesterday's files is a test measuring the wrong thing.
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }

        Directory.CreateDirectory(Root);
    }

    /// <summary>The directory's full path.</summary>
    public string Root { get; }

    /// <summary>A path inside the directory. Nothing is created.</summary>
    public string File(string relative) => System.IO.Path.Combine(Root, relative);

    /// <summary>Why the directory could not be removed, or null when it was.</summary>
    /// <remarks>
    /// Kept rather than only logged so that a test can assert on it. Nothing in the suite does today;
    /// a test that needs to prove a directory survived disposal can.
    /// </remarks>
    public string? RemovalFailure { get; private set; }

    /// <summary>
    /// Removes the directory, and never throws.
    /// </summary>
    /// <remarks>
    /// Two attempts, because the usual cause is a handle the runtime has not finalized yet and a
    /// collection between attempts is often enough to release it. After the second attempt the
    /// directory is left where it is and the reason is reported - a teardown that throws turns a green
    /// run red for something the test itself did not do.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!Directory.Exists(Root))
        {
            return;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                Directory.Delete(Root, recursive: true);
                RemovalFailure = null;
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                RemovalFailure = ex.Message;
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        TestContext.Out.WriteLine(
            $"[TempDirectory] could not remove {Root} after two attempts: {RemovalFailure}. "
            + "It is left in place, and nothing failed because of it.");
    }
}

/// <summary>
/// Tests for the fixture every file-writing test in this suite now shares.
/// </summary>
/// <remarks>
/// Three properties, and the migrated call sites depend on all of them. A fixture that handed two
/// parallel runs the same directory, or that failed a test because a file was locked, would be worse
/// than the sixteen helpers it replaced.
/// </remarks>
[TestFixture]
public sealed class TempDirectoryTests
{
    [Test]
    public void Two_fixtures_never_share_a_directory()
    {
        using var first = new TempDirectory("share-probe");
        using var second = new TempDirectory("share-probe");

        Assert.That(
            second.Root,
            Is.Not.EqualTo(first.Root),
            "Two live fixtures share one directory, so one disposing removes the other's files "
            + "underneath the other. The name has to carry a fresh GUID for every instance.");
    }

    [Test]
    public void A_fixture_does_not_see_another_fixtures_files()
    {
        // The hazard the fixed "docsroot" name had, stated as a property: two runs, one directory,
        // one deleting the other's work while the other is still reading it.
        using var first = new TempDirectory("isolation-probe");
        using var second = new TempDirectory("isolation-probe");
        File.WriteAllText(Path.Combine(first.Root, "written-by-first.txt"), "x");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(second.Root, "written-by-first.txt")), Is.False);
            Assert.That(Directory.Exists(first.Root), Is.True,
                "the first fixture's directory is gone while it is still in use");
        });
    }

    [Test]
    public void Disposing_removes_the_directory_and_everything_in_it()
    {
        var directory = new TempDirectory("remove-probe");
        var root = directory.Root;
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        File.WriteAllText(Path.Combine(root, "nested", "a.txt"), "content");

        directory.Dispose();

        Assert.That(Directory.Exists(root), Is.False, "The directory survived its own Dispose().");
    }

    [Test]
    public void Disposing_a_directory_that_is_already_gone_is_not_a_failure()
    {
        // The reachable half of the "removal must never fail" rule. The other half - a genuinely
        // locked file - cannot be produced reliably from managed code on Windows, which is why this test
        // covers the case that can be produced and the report says the other is not covered.
        var directory = new TempDirectory("vanished-probe");
        Directory.Delete(directory.Root, recursive: true);

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(directory.Dispose,
                "a directory something else removed is not a reason for a test to fail");
            Assert.That(directory.RemovalFailure, Is.Null,
                "nothing went wrong, so there is nothing to report");
        });
    }

    [Test]
    public void Disposing_twice_is_not_an_error()
    {
        var directory = new TempDirectory("double-dispose-probe");
        directory.Dispose();

        Assert.DoesNotThrow(directory.Dispose,
            "Teardown runs from more than one path in this suite, so a second Dispose has to be a no-op.");
    }
}