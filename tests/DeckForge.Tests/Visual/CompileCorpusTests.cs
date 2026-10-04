using System.Globalization;
using System.Text;
using DeckForge.CodeGen.Generation;
using DeckForge.Core.Visual;
using DeckForge.Tests.Visual.Corpus;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// The compile corpus, snapshotted: about forty documents, the C# the emitter produces for each, and a
/// golden file per document in <c>Visual/Corpus/Golden</c>.
/// </summary>
/// <remarks>
/// <para>
/// §28 asks for "a ~40-document compile corpus and golden-file snapshots" and §28.5 records that it was
/// not written. The documents are in <c>Corpus/CompileCorpusDocuments.cs</c>; this is what they are for.
/// </para>
/// <para>
/// <b>The goldens are written deliberately, never by a failing test.</b> There is a rewrite - a test
/// marked <see cref="ExplicitAttribute"/> and additionally gated on
/// <c>DECKFORGE_REWRITE_CORPUS_GOLDENS=1</c>, so neither a normal run nor the name of the fixture is
/// enough to reach it - and it exists because the alternative is a test that rewrites its own
/// expectation and passes for ever. When the emitter changes on purpose, someone runs that one command,
/// reads the diff, and commits it.
/// </para>
/// <para>
/// <b>They are plain text, one document per file, in emission order.</b> Somebody who has just changed a
/// catalog row should be able to open the folder, see which documents moved and by how much, and judge
/// each one. Nothing is minified, hashed, or folded into a table in this source file: a golden nobody
/// can read is a golden nobody reviews.
/// </para>
/// <para>
/// <b>What this proves:</b> that the emitter's output for each of these documents is exactly what is
/// committed, and that the corpus touches every block, menu option, category and wrapped body the
/// catalog declares. <b>What it does not prove:</b> that the output compiles -
/// <see cref="CompileCorpusCompilesTests"/> builds it, and records which documents it could not build.
/// </para>
/// </remarks>
[TestFixture]
public sealed class CompileCorpusTests
{
    /// <summary>The environment variable that unlocks the deliberate rewrite.</summary>
    private const string RewriteVariable = "DECKFORGE_REWRITE_CORPUS_GOLDENS";

    [Test]
    [Explicit("Run only to rewrite the corpus goldens: " + RewriteVariable
              + "=1 dotnet test tests/DeckForge.Tests --filter \"Name~Rewrite_the_golden_files\"")]
    public void Rewrite_the_golden_files_deliberately()
    {
        // Two independent gates, so neither an ordinary run nor the habit of naming the fixture can do
        // this. A golden is an expectation, and an expectation nobody chose is not one.
        if (Environment.GetEnvironmentVariable(RewriteVariable) != "1")
        {
            Assert.Ignore(
                "The goldens are rewritten only when " + RewriteVariable + "=1 is set. Read the diff "
                + "before committing it: a golden that moves because the emitter changed is a decision, "
                + "and a golden that moves because it was regenerated proves nothing.");
        }

        var directory = GoldenDirectory();
        foreach (var corpusCase in CompileCorpus.All)
        {
            File.WriteAllText(GoldenPath(directory, corpusCase.Name), Snapshot(corpusCase));
        }

        TestContext.Out.WriteLine(
            $"Rewrote {CompileCorpus.All.Count} golden files in {directory}. Read the diff before committing.");
    }

    [Test]
    public void Every_corpus_document_emits_exactly_what_its_golden_file_says()
    {
        var directory = GoldenDirectory();
        var failures = new List<string>();

        foreach (var corpusCase in CompileCorpus.All)
        {
            var expectedPath = GoldenPath(directory, corpusCase.Name);
            if (!File.Exists(expectedPath))
            {
                failures.Add(
                    $"{corpusCase.Name}: there is no golden file at {expectedPath}. A document with no "
                    + "golden is not tested; run the deliberate rewrite and read what it wrote.");
                continue;
            }

            var expected = Normalize(File.ReadAllText(expectedPath));
            var actual = Normalize(Snapshot(corpusCase));

            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                failures.Add($"{corpusCase.Name}: the emitter's output has changed.\n{Diff(expected, actual)}");
            }
        }

        Assert.That(
            failures,
            Is.Empty,
            "The corpus and the emitter disagree. Every difference below is a change somebody has to "
            + "judge: if it is intended, run the deliberate rewrite and commit the diff; if it is not, "
            + "this is a defect in the emitter.\n\n" + string.Join("\n\n", failures));
    }

    [Test]
    public void Every_golden_file_has_a_document_behind_it()
    {
        // The other direction. A golden nobody regenerates is one that quietly stops being checked the
        // day a document is deleted or renamed.
        var directory = GoldenDirectory();
        var documented = CompileCorpus.All
            .Select(corpusCase => Path.GetFileName(GoldenPath(directory, corpusCase.Name)))
            .ToHashSet(StringComparer.Ordinal);

        var orphans = Directory.GetFiles(directory, "*.cs.txt")
            .Select(Path.GetFileName)
            .Where(name => name is not null && !documented.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.That(
            orphans,
            Is.Empty,
            "These golden files have no document behind them: " + string.Join(", ", orphans));
    }

    [Test]
    public void The_corpus_covers_every_block_the_palette_offers()
    {
        // "The corpus exercises the whole catalogue" is the claim worth making, so it is checked rather
        // than asserted. A new block with no document fails here, which is the moment to write one.
        var covered = Covered().Select(block => block.Kind).ToHashSet(StringComparer.Ordinal);

        var missing = BlockCatalog.Blocks
            .Select(block => block.Kind)
            .Where(kind => !covered.Contains(kind))
            .OrderBy(kind => kind, StringComparer.Ordinal)
            .ToList();

        Assert.That(
            missing,
            Is.Empty,
            "The corpus has no document containing " + string.Join(", ", missing)
            + ". §28.5 asks for a corpus that exercises the catalogue, and a block nothing in the corpus "
            + "names is a block whose emission this suite does not pin.");
    }

    [Test]
    public void The_corpus_covers_every_menu_option_the_catalog_offers()
    {
        // Every option, not every menu: an option added to a menu in a later build has to move a golden
        // file, which is the only way anyone finds out that a dropdown grew.
        var chosen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var block in Covered())
        {
            foreach (var field in block.Fields)
            {
                chosen.Add(field.Key + "=" + field.Value);
            }
        }

        var unseen = new List<string>();
        foreach (var descriptor in BlockCatalog.Blocks)
        {
            foreach (var menu in descriptor.Menus ?? [])
            {
                foreach (var option in menu.Options)
                {
                    if (!chosen.Contains(menu.Name + "=" + option))
                    {
                        unseen.Add($"{descriptor.Kind}.{menu.Name}={option}");
                    }
                }
            }
        }

        Assert.That(
            unseen,
            Is.Empty,
            "These menu options are in no corpus document, so adding or renaming one would not move a "
            + "golden file: " + string.Join(", ", unseen));
    }

    [Test]
    public void The_corpus_covers_every_category_the_rail_shows()
    {
        var covered = Covered()
            .Select(block => BlockCatalog.Find(block.Kind)?.Category)
            .Where(category => category is not null)
            .Select(category => category!.Value)
            .ToHashSet();

        var missing = BlockCatalog.Categories
            .Select(category => category.Category)
            .Where(category => !covered.Contains(category))
            .ToList();

        Assert.That(
            missing,
            Is.Empty,
            "No corpus document uses " + string.Join(", ", missing)
            + ". BlockCategory.Media is deliberately absent: §7.16 dropped every media block because the "
            + "SDK offers no host caller for one, so there is nothing in it to cover.");
    }

    [Test]
    public void The_corpus_fills_every_wrapped_body_the_catalog_declares()
    {
        // "The body exists" and "the body was filled with something" are different claims, and only the
        // second one exercises the emitter's per-body indentation and cancellation check.
        var filled = Covered()
            .SelectMany(block => block.Bodies.Keys.Select(name => block.Kind + "." + name))
            .ToHashSet(StringComparer.Ordinal);

        var missing = new List<string>();
        foreach (var descriptor in BlockCatalog.Blocks)
        {
            foreach (var body in descriptor.Bodies ?? [])
            {
                if (!filled.Contains(descriptor.Kind + "." + body.Name))
                {
                    missing.Add($"{descriptor.Kind}.{body.Name}");
                }
            }
        }

        Assert.That(
            missing,
            Is.Empty,
            "These wrapped bodies are declared but never filled by the corpus: " + string.Join(", ", missing));
    }

    [Test]
    public void A_document_the_corpus_calls_clean_reports_no_problems()
    {
        // The half of the snapshot that needs no human. A golden file is rewritten by somebody who
        // decided the change was right, so a new diagnostic appearing in one is exactly what the diff
        // is for. This is the other half - "this document emits without complaint" - checked on every
        // run with nobody in the loop.
        var offenders = new List<string>();
        foreach (var corpusCase in CompileCorpus.All.Where(corpusCase => !corpusCase.ExpectsProblems))
        {
            foreach (var problem in Emit(corpusCase).Problems)
            {
                offenders.Add($"{corpusCase.Name}: {problem.BlockId}: {problem.Message}");
            }
        }

        Assert.That(
            offenders,
            Is.Empty,
            "Documents the corpus expects to emit cleanly now report problems:\n"
            + string.Join("\n", offenders));
    }

    [Test]
    public void A_document_the_corpus_calls_dirty_goes_on_reporting_a_problem()
    {
        // And the other direction, so "expects problems" cannot quietly become true for everything.
        var offenders = CompileCorpus.All
            .Where(corpusCase => corpusCase.ExpectsProblems)
            .Where(corpusCase => Emit(corpusCase).Problems.Count == 0)
            .Select(corpusCase => corpusCase.Name)
            .ToList();

        Assert.That(
            offenders,
            Is.Empty,
            "These documents exist for the problems they report and now report none, so each is just a "
            + "duplicate of a clean one: " + string.Join(", ", offenders));
    }

    [Test]
    public void Every_document_left_out_of_the_real_build_says_why_and_names_the_diagnostic()
    {
        // A reason that does not quote the compiler is a guess. Each exclusion is checked to name a CS
        // code, because "this does not compile" is not a reason a reviewer can challenge and a reason
        // that cannot be challenged is a decision that quietly becomes permanent.
        var offenders = CompileCorpus.All
            .Where(corpusCase => !corpusCase.CompiledForReal)
            .Where(corpusCase => !corpusCase.NotCompiledReason.Contains("CS", StringComparison.Ordinal))
            .Select(corpusCase => $"{corpusCase.Name}: {corpusCase.NotCompiledReason}")
            .ToList();

        Assert.That(
            offenders,
            Is.Empty,
            "These documents are snapshotted but never built, and their reason does not quote a compiler "
            + "diagnostic: " + string.Join(" | ", offenders));
    }

    [Test]
    public void The_corpus_is_the_size_the_criterion_names()
    {
        // "roughly forty", written down so the corpus cannot quietly become nine documents and so adding
        // one is a visible decision rather than drift.
        Assert.That(
            CompileCorpus.All,
            Has.Count.InRange(36, 60),
            "§28.5 asks for roughly forty documents, and this corpus has " + CompileCorpus.All.Count);
    }

    [Test]
    public void Every_document_emits_the_same_thing_twice()
    {
        // Determinism over the whole corpus rather than over one hand-written project. A case whose
        // builder leaked an id counter or depended on dictionary order would pass its golden on a warm
        // run and fail on a cold one, which is the shape of a defect people call a flake.
        foreach (var corpusCase in CompileCorpus.All)
        {
            var first = Normalize(Snapshot(corpusCase));
            var second = Normalize(Snapshot(corpusCase));

            Assert.That(
                second,
                Is.EqualTo(first),
                corpusCase.Name + " emitted two different things from two builds of the same document.");
        }
    }

    // ---- helpers ------------------------------------------------------------------------------------

    /// <summary>Every block of every corpus document, built once per call.</summary>
    private static IEnumerable<Block> Covered() =>
        CompileCorpus.All.SelectMany(corpusCase => corpusCase.Build().Blocks());

    /// <summary>What one document emits, and what the emitter said about it.</summary>
    private static (string Code, IReadOnlyList<VisualCompileProblem> Problems) Emit(CorpusCase corpusCase)
    {
        var project = corpusCase.Build();
        return VisualEmitter.CompileTarget(project.Targets[0], project);
    }

    /// <summary>The whole snapshot of one document: a header, the region, and the diagnostics under it.</summary>
    private static string Snapshot(CorpusCase corpusCase)
    {
        var (code, problems) = Emit(corpusCase);

        var text = new StringBuilder();
        text.Append(CompileCorpus.Header(corpusCase));
        text.Append(code);

        if (problems.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("// ==== problems the emitter reported ====");
            foreach (var problem in problems)
            {
                text.AppendLine($"// {problem.BlockId}: {problem.Message}");
            }
        }

        return text.ToString();
    }

    /// <summary>Line endings are not part of what this fixture checks.</summary>
    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>The golden folder, found from the test assembly rather than copied beside it.</summary>
    /// <remarks>
    /// Read from the source tree, not the output folder, so there is exactly one copy of a golden and
    /// the deliberate rewrite writes to the same place the comparison reads from. Walking up to the
    /// solution file is the convention the other source-tree tests here already use.
    /// </remarks>
    private static string GoldenDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DeckForge.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException(
                "DeckForge.slnx was not found above " + AppContext.BaseDirectory
                + ", so the corpus has no golden folder to compare against. This test would otherwise "
                + "pass by having nothing to check.");
        }

        var golden = Path.Combine(directory.FullName, "tests", "DeckForge.Tests", "Visual", "Corpus", "Golden");
        Directory.CreateDirectory(golden);
        return golden;
    }

    private static string GoldenPath(string directory, string name) => Path.Combine(directory, name + ".cs.txt");

    /// <summary>
    /// A diff a person can act on: the first differing region with line numbers, and a note where the
    /// output was cut short.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written rather than taken from a library because the failure it serves is a reviewer deciding
    /// whether an emitter change was correct, and that decision needs the surrounding lines and the
    /// numbers - not a character-level soup.
    /// </para>
    /// <para>
    /// The common prefix and suffix are trimmed first, so the part worth reading is what gets printed
    /// rather than the hundred identical lines in front of it.
    /// </para>
    /// </remarks>
    private static string Diff(string expected, string actual, int context = 3, int maxLines = 100)
    {
        var golden = Split(expected);
        var emitted = Split(actual);

        var start = 0;
        while (start < golden.Length && start < emitted.Length && golden[start] == emitted[start])
        {
            start++;
        }

        var goldenEnd = golden.Length;
        var emittedEnd = emitted.Length;
        while (goldenEnd > start && emittedEnd > start && golden[goldenEnd - 1] == emitted[emittedEnd - 1])
        {
            goldenEnd--;
            emittedEnd--;
        }

        var report = new StringBuilder();
        report.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  first difference at line {start + 1}; golden {golden.Length} lines, emitted {emitted.Length} lines"));

        var printed = 0;
        var truncated = false;

        void Line(int number, char marker, string text)
        {
            if (printed++ >= maxLines)
            {
                truncated = true;
                return;
            }

            report.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    {number,5} {marker} {text}"));
        }

        for (var line = Math.Max(0, start - context); line < start; line++)
        {
            Line(line + 1, ' ', golden[line]);
        }

        for (var line = start; line < goldenEnd; line++)
        {
            Line(line + 1, '-', golden[line]);
        }

        for (var line = start; line < emittedEnd; line++)
        {
            Line(line + 1, '+', emitted[line]);
        }

        var tail = Math.Max(goldenEnd, emittedEnd);
        for (var line = tail; line < tail + context && line < golden.Length; line++)
        {
            Line(line + 1, ' ', golden[line]);
        }

        if (truncated)
        {
            report.AppendLine($"    ... cut short at {maxLines} lines; the change is larger than that.");
        }

        return report.ToString();
    }

    private static string[] Split(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
}