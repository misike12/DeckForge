using System.Globalization;
using System.Text.RegularExpressions;

namespace DeckForge.Core.Visual;

/// <summary>
/// Reads a generated block region back into blocks, without a C# compiler and without a C# parser.
/// </summary>
/// <remarks>
/// <para>
/// Part 24.1.5 asks for a property test: parse a region, emit it again, and get the region byte for byte.
/// That property is not claimed here, and the reason is worth stating plainly rather than discovering
/// later: byte-for-byte re-emission means reproducing the emitter's <em>lowering</em> backwards, not
/// parsing C#. A comparison, for instance, does not appear in the region as a comparison — the emitter
/// writes two temporaries and a <c>switch</c> over the operand, so reading it back means re-deriving the
/// reporter that produced it, and every row in the catalogue would need its lowering inverted.
/// </para>
/// <para>
/// So the subset here is not "what the emitter produces" but "<strong>what the emitter produces that can be
/// read back without inverting anything</strong>". Conditions are the honest boundary: an <c>if</c>, a
/// <c>while</c> and a <c>do</c> all take one, so they — and everything nested inside them — degrade to
/// preserved lines. Everything else is recognised: the two guards, the cancellation check, a
/// <c>for</c> with a literal count, <c>while (true)</c>, the three finishes, <c>break</c> and
/// <c>continue</c>, the log, two of the deck calls, a comment, a procedure and its calls, and the
/// <c>// script:</c> headers.
/// </para>
/// <para>
/// <strong>Nothing is dropped and almost nothing is refused.</strong> A line this parser does not
/// recognise becomes an opaque block holding its own text, in the same position in the same body, and a
/// <c>vis-opaque-lines</c> warning names how many lines went that way. Part 24.2's promise — "regions
/// written by a much older DeckForge may not parse; they degrade to opaque blocks with a full diagnostic,
/// never to a refusal" — is kept by making preservation the default rather than a decision, so an
/// unreadable construct costs the user a grey tile and loses nothing. The only refusals are the structural
/// ones in <see cref="BlockRegion.Extract"/>: a file whose region cannot be identified at all.
/// </para>
/// <para>
/// <strong>Deterministic by construction.</strong> Ids come from a counter in parse order, there is no clock
/// and no hash of anything, and every decision is a regex match on one line — so the same source produces
/// the same document on every machine and every run, which is what lets a test compare a parsed region
/// rather than merely assert that one was produced.
/// </para>
/// </remarks>
public static partial class BlockRegionReader
{
    /// <summary>The <c>vis-</c> code a preserved run is reported as.</summary>
    /// <remarks>
    /// Appendix F's own row: <c>vis-opaque-lines</c>, a warning, "round-trip could not recognise some
    /// lines", hinted "They are preserved and written back unchanged". Taken from the table rather than
    /// invented, because a code the catalogue does not declare is a code nothing can check, and
    /// <c>VisualValidatorTests</c> fails the build over an undeclared one.
    /// </remarks>
    public const string OpaqueCode = "vis-opaque-lines";

    /// <summary>
    /// Builds a document from the block region of <paramref name="csharpSource"/>.
    /// </summary>
    /// <param name="csharpSource">A generated action file, or just its region.</param>
    /// <param name="target">
    /// The target the region belongs to, or null for a synthetic one. A region on its own says nothing about
    /// which action it was written into — that lives in the document, not the file — so a caller reading a
    /// file it has not opened yet gets a document it can look at rather than an error about it.
    /// </param>
    /// <remarks>
    /// Every <c>// script: name</c> comment the emitter wrote starts a new script, so a region holding two
    /// scripts comes back as two. A region with none — a hand-edited one, or one written by a much older
    /// build — becomes a single script called <c>main</c>, which is the shape the emitter would have
    /// produced for it.
    /// </remarks>
    public static RegionReadResult Read(string? csharpSource, VisualTarget? target = null)
    {
        var extraction = BlockRegion.Extract(csharpSource);
        if (!extraction.Found)
        {
            return RegionReadResult.Refused(extraction.Problem!, extraction.Code!);
        }

        var project = new VisualProject
        {
            DocumentId = VisualProject.NewDocumentId(),
            Targets = [target ?? SyntheticTarget()],
        };

        var parser = new Parser(extraction.Text!);
        parser.Run(project);

        return RegionReadResult.Read(project, parser.Opaque);
    }

    /// <summary>A target for a region read without one, named after what it is.</summary>
    private static VisualTarget SyntheticTarget() => new()
    {
        Id = "action:recovered",
        Name = "recovered",
        Kind = TargetKind.Action,
        AnchorId = "ExecuteAsync",
    };

    /// <summary>
    /// One pass over the region's lines.
    /// </summary>
    /// <remarks>
    /// A private class rather than a dozen static methods taking an index, because the parse is a walk with
    /// a stack in it — a loop header, its brace and its body are three lines that only mean anything
    /// together — and threading that through parameters produces a signature nobody can read. It holds no
    /// state that outlives one <see cref="Read"/>.
    /// </remarks>
    private sealed partial class Parser
    {
        private readonly List<string> _lines;
        private readonly IdSource _ids = new();

        /// <summary>
        /// Every procedure the region declares, known before the walk begins.
        /// </summary>
        /// <remarks>
        /// Collected up front because the emitter hoists procedures above the scripts but does not order
        /// them against each other, so a call inside the first procedure to the second would otherwise be
        /// read before the second exists and would degrade to preserved lines — a document that loses a
        /// call purely because of the order two independent procedures happened to be written in.
        /// </remarks>
        private readonly HashSet<string> _declaredProcedures = new(StringComparer.Ordinal);

        private int _index;

        /// <summary>How many <c>// script:</c> headers have been met, which is how the first is recognised.</summary>
        private int _headers;

        public Parser(string region)
        {
            // Normalised once, here, so nothing downstream has to think about CRLF. A file written on
            // Windows and read elsewhere has to produce the same document, and a stray \r left in the
            // preserved text is a difference between two machines that have no business differing.
            _lines = region.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n')
                .ToList();

            foreach (var line in _lines)
            {
                var signature = ProcedureStart().Match(line.Trim());
                if (signature.Success)
                {
                    _declaredProcedures.Add(Unsuffix(signature.Groups["name"].Value));
                }
            }
        }

        /// <summary>The runs of lines that were not recognised, in the order they were met.</summary>
        public List<OpaqueRegion> Opaque { get; } = [];

        public void Run(VisualProject project)
        {
            var owner = project.Targets[0];

            // A script is opened before anything is read rather than created lazily, so a region of
            // nothing but scaffolding still produces a script with a hat. An empty canvas is a document,
            // and "the region had no statements" is not a refusal.
            owner.Scripts.Add(NewScript("main"));

            while (_index < _lines.Count)
            {
                var line = _lines[_index].Trim();
                if (line.Length == 0)
                {
                    _index++;
                    continue;
                }

                if (ScriptHeader().IsMatch(line))
                {
                    // The index is advanced here rather than by the body walk, which stops at a script
                    // header and leaves it for this loop: two readers of the same marker, and only one of
                    // them may consume it.
                    StartScript(owner, line[ScriptPrefix.Length..]);
                    _index++;
                    continue;
                }

                owner.Scripts[^1].Body.AddRange(ReadBody(project, 0));
            }
        }

        /// <summary>
        /// Opens a script, or renames the placeholder one when nothing has been read into it yet.
        /// </summary>
        /// <remarks>
        /// The default script exists so a region of nothing but scaffolding still has a hat, which means the
        /// first <c>// script:</c> would otherwise leave an empty "main" above the script the region really
        /// describes. Renaming it keeps the ids it was given and drops a script that was never in the file.
        /// </remarks>
        private void StartScript(VisualTarget owner, string name)
        {
            if (owner.Scripts.Count == 1 && owner.Scripts[0].Body.Count == 0 && _headers == 0)
            {
                owner.Scripts[0].Name = name.Trim().Length == 0 ? "main" : name.Trim();
            }
            else
            {
                owner.Scripts.Add(NewScript(name));
            }

            _headers++;
        }

        /// <summary>Statements until the enclosing brace closes, or until the region runs out.</summary>
        /// <param name="project">The document being built, for procedures and their calls.</param>
        /// <param name="depth">Nesting depth, carried so a nested body is filled by the same walk.</param>
        private List<Block> ReadBody(VisualProject project, int depth)
        {
            var statements = new List<Block>();

            while (_index < _lines.Count)
            {
                var line = _lines[_index].Trim();

                if (line == "}")
                {
                    _index++;
                    return statements;
                }

                if (line.Length == 0)
                {
                    _index++;
                    continue;
                }

                // A script comment inside a body means the region is not one the emitter wrote — it closes
                // every container before the next script header. Left where it is, and the caller's next
                // pass opens the next script on top of it.
                if (ScriptHeader().IsMatch(line))
                {
                    return statements;
                }

                if (TryStatement(line, project, depth, statements))
                {
                    continue;
                }

                statements.Add(PreservedRun());
            }

            return statements;
        }

        /// <summary>
        /// One statement, or false when the line is not one this parser knows.
        /// </summary>
        /// <remarks>
        /// Every branch either advances <see cref="_index"/> past exactly the lines it consumed or returns
        /// false having advanced nothing. That is the whole contract, and it is what lets the caller's
        /// fallback start an opaque run at the right line without having to undo anything.
        /// </remarks>
        private bool TryStatement(
            string line,
            VisualProject project,
            int depth,
            List<Block> into)
        {
            if (line == "{")
            {
                _index++;
                return true;
            }

            if (TryScaffolding(line))
            {
                return true;
            }

            var match = Guard().Match(line);
            if (match.Success)
            {
                _index++;
                SkipBracedRun();
                return true;
            }

            match = ForLoop().Match(line);
            if (match.Success)
            {
                _index++;
                into.Add(Container("control.repeat", project, depth, NumberInside(match.Groups["count"].Value)));
                return true;
            }

            if (ForeverLoop().IsMatch(line))
            {
                _index++;
                into.Add(Container("control.forever", project, depth, null));
                return true;
            }

            match = Logger().Match(line);
            if (match.Success)
            {
                _index++;
                into.Add(new Block { Id = _ids.Next(), Kind = "ui.log" }
                    .WithField("level", match.Groups["level"].Value)
                    .WithText("template", Unwrap(match.Groups["template"].Value)));
                return true;
            }

            match = Delay().Match(line);
            if (match.Success)
            {
                _index++;
                into.Add(Delay(match.Groups["expr"].Value));
                return true;
            }

            match = Finish().Match(line);
            if (match.Success)
            {
                _index++;
                into.Add(Finish(match));
                return true;
            }

            match = DeckCall().Match(line);
            if (match.Success)
            {
                _index++;
                into.Add(new Block
                {
                    Id = _ids.Next(),
                    Kind = match.Groups["member"].Value == "GoToParentAsync"
                        ? "deck.go-to-parent"
                        : "deck.go-back",
                });
                return true;
            }

            match = BreakOrContinue().Match(line);
            if (match.Success)
            {
                _index++;
                into.Add(new Block
                {
                    Id = _ids.Next(),
                    Kind = match.Groups["word"].Value == "break" ? "control.break" : "control.continue",
                });
                return true;
            }

            if (ProcedureStart().IsMatch(line))
            {
                ReadProcedure(project);
                return true;
            }

            match = ProcedureCall().Match(line);
            if (match.Success && _declaredProcedures.Contains(match.Groups["name"].Value))
            {
                _index++;
                into.Add(new Block { Id = _ids.Next(), Kind = "proc.call" }
                    .WithText("name", match.Groups["name"].Value)
                    .WithText("args", CallArguments(match.Groups["args"].Value)));
                return true;
            }

            return false;
        }

        /// <summary>
        /// The lines the emitter writes around blocks rather than for them: the guards, the cancellation
        /// check, a comment, and a procedure's parameter binding.
        /// </summary>
        /// <remarks>
        /// Reading any of these as blocks would put tiles on the canvas that the emitter then writes back
        /// as code, so the region would grow a guard and a cancellation check on every round trip. They are
        /// consumed rather than discarded — the lines still exist, and the region is still read line by
        /// line — because "not a block" and "not in the region" are different claims and only one of them is
        /// true.
        /// </remarks>
        private bool TryScaffolding(string line)
        {
            if (CancellationCheck().IsMatch(line)
                || VisualRuntimeSet().IsMatch(line)
                || LineComment().IsMatch(line))
            {
                _index++;
                return true;
            }

            return false;
        }

        /// <summary>A loop: read its header, then everything inside its braces.</summary>
        private Block Container(string kind, VisualProject project, int depth, double? count)
        {
            var block = new Block { Id = _ids.Next(), Kind = kind };
            if (count is { } times)
            {
                block.WithNumber("count", times);
            }

            // The brace is optional in what this reads: the emitter writes one on the line after the
            // header, and a hand-edited region may not. ReadBody stops at whichever closing brace comes
            // first, so a missing one costs the container its body rather than costing the parse its shape.
            if (_index < _lines.Count && _lines[_index].Trim() == "{")
            {
                _index++;
            }

            block.Body("body").AddRange(ReadBody(project, depth + 1));
            return block;
        }

        /// <summary>
        /// A hoisted local function: its name, its parameters, and the statements inside it.
        /// </summary>
        /// <remarks>
        /// Recognised by the shape the emitter produces — <c>async Task NameAsync(object? …)</c> on a line
        /// of its own — rather than by any keyword a hand-written helper might also use. §24.2 lists that
        /// as a limit rather than a bug: a wrapper the user wrote is a different function from a block, and
        /// reading it as a procedure would be an inference about intent dressed up as a parse.
        /// </remarks>
        private void ReadProcedure(VisualProject project)
        {
            var signature = ProcedureStart().Match(_lines[_index].Trim());
            _index++;

            var procedure = new ProcedureDeclaration
            {
                Id = "proc" + (project.Procedures.Count + 1).ToString(CultureInfo.InvariantCulture),
                Name = Unsuffix(signature.Groups["name"].Value),
                Returns = signature.Groups["value"].Success,
            };

            foreach (var parameter in SplitParameters(signature.Groups["parameters"].Value))
            {
                procedure.Parameters.Add(new ProcedureParameter { Name = parameter, Type = "Any" });
            }

            if (_index < _lines.Count && _lines[_index].Trim() == "{")
            {
                _index++;
            }

            // Added before its body is read, not after: the emitter hoists every declaration above every
            // call site, so a procedure that calls a sibling declared later in the region is ordinary — and
            // registering the declaration late would make the forward call look like an unknown one.
            project.Procedures.Add(procedure);
            procedure.Body.AddRange(ReadBody(project, 1));
        }

        /// <summary>Consumes a <c>{ … }</c> run without reading what is in it.</summary>
        private void SkipBracedRun()
        {
            var depth = 0;

            while (_index < _lines.Count)
            {
                var line = _lines[_index].Trim();
                _index++;

                if (line == "{")
                {
                    depth++;
                }
                else if (line == "}")
                {
                    depth--;
                    if (depth <= 0)
                    {
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// A run of consecutive lines nothing recognised, kept verbatim and placed as one opaque block.
        /// </summary>
        /// <remarks>
        /// Gathered into a single block rather than one per line, because a preserved run is one thing to a
        /// reader and one thing to write back: five grey tiles in a row is five chances for a user to delete
        /// one of them and lose a line.
        /// </remarks>
        private Block PreservedRun()
        {
            var start = _index;
            var run = new List<string>();
            var depth = 0;
            var expectingBody = false;

            while (_index < _lines.Count)
            {
                var raw = _lines[_index];
                var line = raw.Trim();

                // A recognised line ends the run — unless a preserved brace is still open, where nothing
                // ends it but the brace. That is the difference between preserving a construct and
                // preserving its text: hoisting the statements out of a `foreach` the user wrote and
                // leaving the header behind would keep every line and lose the shape.
                if (depth == 0 && !expectingBody && (line.Length == 0 || IsRecognised(line)))
                {
                    break;
                }

                run.Add(raw);
                _index++;
                expectingBody = false;

                if (line == "{")
                {
                    depth++;
                }
                else if (line == "}")
                {
                    depth = Math.Max(0, depth - 1);
                }

                // The emitter puts a container's brace on the line after its header, so this is the only
                // shape worth guessing at. Guessing further would swallow blank lines into the chunk, and a
                // preserved run that loses a line is the one thing it may not do.
                expectingBody = depth == 0 && _index < _lines.Count && _lines[_index].Trim() == "{";
            }

            if (run.Count == 0)
            {
                // Reachable, and not only through a defect: a call to a procedure this region does not
                // declare matches the call pattern — so the scan stops before it — while the statement reader
                // declines it, because turning somebody else's helper into a `proc.call` would be an inference
                // about intent. Consuming the line keeps it: one preserved line is the honest answer, and a
                // zero-length run would have the caller walking the same line for ever.
                run.Add(_lines[_index]);
                _index++;
            }

            var chunk = new OpaqueRegion(start + 1, run);
            Opaque.Add(chunk);

            // `legacy.unsupported` rather than a new kind. It is the catalogue's one shape for "a thing a
            // document holds that the palette never offers and the emitter skips", which is exactly what
            // these lines are, and adding a second placeholder for them would move three counts the
            // specification pins in tests — the palette size, the per-category sizes, and the list of
            // placeholders — to describe a distinction no reader can act on. The original text goes in the
            // `legacyType` slot, the one text slot that placeholder declares.
            return new Block { Id = _ids.Next(), Kind = "legacy.unsupported" }
                .WithText("legacyType", chunk.Text);
        }

        /// <summary>
        /// Whether a line is one this parser can consume, so a preserved run stops at the first line it can.
        /// </summary>
        /// <remarks>
        /// Every pattern that <see cref="TryStatement"/> accepts has to appear here as well, and the two
        /// lists drifting apart is the defect this shape makes expensive: an omission costs a recognised
        /// line being swallowed into the previous opaque run, which reads as a parser bug and is exactly
        /// that. A bare brace is treated as recognised for the same reason — it is structure, and folding it
        /// into a preserved run would put a stray <c>{</c> inside a grey tile and unbalance the container
        /// the run sits in. The sweep in the tests is what holds the two lists together.
        /// </remarks>
        private bool IsRecognised(string line) =>
            line.Length > 0
            && (line == "{"
                || line == "}"
                || ScriptHeader().IsMatch(line)
                || CancellationCheck().IsMatch(line)
                || LineComment().IsMatch(line)
                || VisualRuntimeSet().IsMatch(line)
                || Guard().IsMatch(line)
                || ForLoop().IsMatch(line)
                || ForeverLoop().IsMatch(line)
                || Logger().IsMatch(line)
                || Delay().IsMatch(line)
                || Finish().IsMatch(line)
                || DeckCall().IsMatch(line)
                || BreakOrContinue().IsMatch(line)
                || ProcedureStart().IsMatch(line)
                || ProcedureCall().IsMatch(line));

        private VisualScript NewScript(string name) => new()
        {
            Id = "s" + _ids.NextScript().ToString(CultureInfo.InvariantCulture),
            Name = name.Trim().Length == 0 ? "main" : name.Trim(),
            X = 60,
            Y = 40,
            Hat = new Block { Id = _ids.Next(), Kind = "hat.action-runs" },
        };

        /// <summary>
        /// A wait, told apart by the arithmetic the emitter used to get there.
        /// </summary>
        /// <remarks>
        /// The two rows both emit a <c>Task.Delay</c> and nothing else in the text says which one it was —
        /// the only difference is that seconds is multiplied by 1000 on the way out. So the number inside
        /// <c>VisualRuntime.ToNumber(…)</c> is already the value the block holds, seconds or milliseconds,
        /// and reading both as milliseconds would turn a wait of two seconds into a wait of two
        /// milliseconds, repeated two thousand times as often.
        /// </remarks>
        private Block Delay(string expression)
        {
            var amount = NumberInside(expression);

            return expression.Contains("* 1000", StringComparison.Ordinal)
                ? new Block { Id = _ids.Next(), Kind = "control.wait-seconds" }
                    .WithNumber("seconds", amount)
                : new Block { Id = _ids.Next(), Kind = "control.wait-ms" }
                    .WithNumber("ms", amount);
        }

        private Block Finish(Match match) => match.Groups["shape"].Value switch
        {
            "Success" => new Block { Id = _ids.Next(), Kind = "control.finish-success" },
            "Accepted" => new Block { Id = _ids.Next(), Kind = "control.finish-accepted" }
                .WithText("message", Unwrap(match.Groups["argument"].Value)),
            _ => new Block { Id = _ids.Next(), Kind = "control.finish-failed" }
                .WithField("code", ErrorCode(match.Groups["code"].Value))
                .WithText("message", Unwrap(match.Groups["argument"].Value)),
        };

        /// <summary>
        /// The error code, without the <c>ActionErrorCodes.</c> the emitter writes in front of it.
        /// </summary>
        /// <remarks>
        /// The menu holds the bare SDK member — <c>Timeout</c>, not <c>ActionErrorCodes.Timeout</c> — because
        /// that is what <c>MenuDescriptor</c>'s options are and what the emitter substitutes the constant's
        /// own prefix around. Storing the qualified spelling would make every recovered <c>finish: failed</c>
        /// report <c>vis-menu-key-unknown</c> against its own row, which is a diagnostic about a block that
        /// is perfectly correct.
        /// </remarks>
        private static string ErrorCode(string qualified)
        {
            const string prefix = "ActionErrorCodes.";

            return qualified.StartsWith(prefix, StringComparison.Ordinal)
                ? qualified[prefix.Length..]
                : qualified;
        }

        /// <summary>
        /// A number out of a <c>VisualRuntime.ToNumber(…)</c> wrapper, or zero.
        /// </summary>
        /// <remarks>
        /// Invariant parsing, like every other number this project writes. A machine whose culture puts a
        /// decimal comma into a literal would otherwise read <c>1,5</c> as one-and-a-half in a region
        /// DeckForge wrote elsewhere, and quietly save a different wait back.
        /// </remarks>
        private static double NumberInside(string expression)
        {
            const string wrapper = "VisualRuntime.ToNumber(";

            var open = expression.IndexOf(wrapper, StringComparison.Ordinal);
            if (open < 0)
            {
                return 0;
            }

            var from = open + wrapper.Length;
            var close = expression.IndexOf(')', from);

            return close > 0
                && double.TryParse(
                    expression[from..close].Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value)
                ? value
                : 0;
        }

        /// <summary>Strips the <c>Async</c> the emitter appends to every procedure's identifier.</summary>
        private static string Unsuffix(string emitted) =>
            emitted.EndsWith("Async", StringComparison.Ordinal) && emitted.Length > 5
                ? emitted[..^5]
                : emitted;

        /// <summary>Strips the <c>VisualRuntime.ToText(…)</c> wrapper the emitter puts round a message.</summary>
        private static string Unwrap(string argument)
        {
            const string wrapper = "VisualRuntime.ToText(";

            var trimmed = argument.Trim();

            return trimmed.StartsWith(wrapper, StringComparison.Ordinal) && trimmed.EndsWith(')')
                ? trimmed[wrapper.Length..^1].Trim().Trim('"')
                : trimmed.Trim('"');
        }

        /// <summary>The call's <c>name=value</c> lines, which is what a <c>proc.call</c> block carries.</summary>
        /// <remarks>
        /// Reconstructed from the positional arguments rather than read, because the emitter writes a call's
        /// values positionally in declaration order and drops the names: <c>AnnounceAsync("Ada")</c> is the
        /// only evidence in the region that the value belonged to <c>who</c>. Naming them
        /// <c>arg0</c>, <c>arg1</c> keeps the count and the order, which is what survives the round trip
        /// without claiming to know the caller's intent — a value bound to the wrong parameter is a document
        /// the user can see and fix, and a value bound to a name we guessed is one they cannot.
        /// </remarks>
        private static string CallArguments(string arguments) =>
            string.Join(
                "\n",
                SplitArguments(arguments)
                    .Select((value, index) => "arg" + index.ToString(CultureInfo.InvariantCulture) + "=" + value.Trim('"')));

        /// <summary>
        /// The procedure's parameter names, without the types the emitter declares them with.
        /// </summary>
        /// <remarks>
        /// The emitter writes every parameter as <c>object? name</c>, so a call site never has to know a
        /// parameter's declared type to pass text that parses. Splitting on the space rather than storing the
        /// whole declaration is what makes the block's <c>name</c> slot hold <c>who</c> and not
        /// <c>object? who</c> — and a parameter named "object? who" would be a name no call could bind and
        /// no rename could find.
        /// </remarks>
        private static List<string> SplitParameters(string parameters) =>
            [.. SplitArguments(parameters).Select(part =>
            {
                var space = part.LastIndexOf(' ');
                return space < 0 ? part : part[(space + 1)..];
            })];

        /// <summary>
        /// Splits an argument list on the commas that separate arguments.
        /// </summary>
        /// <remarks>
        /// Not <c>Split(',')</c>: an argument can be a string containing a comma, and
        /// <c>await AnnounceAsync("Hello, world")</c> split naively becomes two arguments, which renames
        /// every parameter bound after the first. The quote state is tracked rather than escaped with a
        /// regular expression because the emitter only ever puts commas and string literals in an argument
        /// list — no nested generics, no collection initialisers — and a scanner that is right for the
        /// language the emitter writes is smaller than one that pretends to be a C# lexer.
        /// </remarks>
        private static List<string> SplitArguments(string arguments)
        {
            var parts = new List<string>();
            var quoted = false;
            var start = 0;

            for (var i = 0; i < arguments.Length; i++)
            {
                var c = arguments[i];

                if (c == '"')
                {
                    quoted = !quoted;
                }
                else if (c == ',' && !quoted)
                {
                    parts.Add(arguments[start..i].Trim());
                    start = i + 1;
                }
            }

            if (start < arguments.Length)
            {
                parts.Add(arguments[start..].Trim());
            }

            return [.. parts.Where(part => part.Length > 0)];
        }

        // ---- the subset -----------------------------------------------------------------------------

        /// <summary>The prefix the emitter writes before a script's name.</summary>
        private const string ScriptPrefix = "// script:";

        [GeneratedRegex(@"^//\s*script:\s*(.*)$")]
        private static partial Regex ScriptHeader();

        [GeneratedRegex(@"^context\.CancellationToken\.ThrowIfCancellationRequested\(\);$")]
        private static partial Regex CancellationCheck();

        [GeneratedRegex(@"^//\s?(.*)$")]
        private static partial Regex LineComment();

        [GeneratedRegex(@"^VisualRuntime\.Set\(")]
        private static partial Regex VisualRuntimeSet();

        [GeneratedRegex(
            @"^if \(_integration is null\)$|^if \(context\.Ui is null \|\| context\.Interactions is null\)$")]
        private static partial Regex Guard();

        [GeneratedRegex(@"^for \(var _i\d+ = 0; _i\d+ < (?<count>.+); _i\d+\+\+\)$")]
        private static partial Regex ForLoop();

        [GeneratedRegex(@"^while \(true\)$")]
        private static partial Regex ForeverLoop();

        [GeneratedRegex(
            @"^_logger\.(?<level>Verbose|Debug|Information|Warning|Error)\(VisualRuntime\.LogTemplate\((?<template>.+), context\.Parameters\)\);$")]
        private static partial Regex Logger();

        [GeneratedRegex(@"^await Task\.Delay\((?<expr>.+), context\.CancellationToken\);$")]
        private static partial Regex Delay();

        [GeneratedRegex(
            @"^return ActionResult\.(?<shape>Success|Failed|Accepted)\((?<code>ActionErrorCodes\.\w+)?\s*,?\s*(?<argument>.+?)?\);$")]
        private static partial Regex Finish();

        [GeneratedRegex(
            @"^await _integration\.Deck\.(?<member>GoToParentAsync|GoBackAsync)\(context\.OriginClientId, context\.CancellationToken\);$")]
        private static partial Regex DeckCall();

        [GeneratedRegex(@"^(?<word>break|continue);$")]
        private static partial Regex BreakOrContinue();

        [GeneratedRegex(@"^async\s+Task(?<value><object\?>)?\s+(?<name>\w+?)Async\s*\((?<parameters>.*)\)$")]
        private static partial Regex ProcedureStart();

        [GeneratedRegex(@"^await\s+(?<name>\w+?)Async\((?<args>.*)\);$")]
        private static partial Regex ProcedureCall();

        /// <summary>
        /// Ids in parse order, so the same region always produces the same document.
        /// </summary>
        /// <remarks>
        /// A counter rather than a GUID, for the reason <see cref="VisualProject.NextBlockId"/> gives: the
        /// sidecar is meant to be read and diffed, and hexadecimal noise in every id of a recovered
        /// document makes three changed lines look like a rewrite. Scripts are counted separately from
        /// blocks because the two id spaces are different — a <c>BodyRef</c> carries both — and sharing one
        /// counter would produce a script called <c>s2</c> whose hat is <c>b2</c>.
        /// </remarks>
        private sealed class IdSource
        {
            private int _block;
            private int _script;

            public string Next() => "b" + (++_block).ToString(CultureInfo.InvariantCulture);

            public int NextScript() => ++_script;
        }
    }
}

/// <summary>
/// What a region read produced: a document, the runs that stayed as lines, and a refusal when there was no
/// region at all.
/// </summary>
/// <param name="Project">The document, or null when the read was refused.</param>
/// <param name="Diagnostics">
/// One <c>vis-opaque-lines</c> warning per preserved run. On the result as well as inside the document,
/// because a warning the user has to go looking for inside a recovered canvas is a warning nobody reads.
/// </param>
/// <param name="Opaque">The preserved runs, anchored by the region line each starts on.</param>
/// <param name="Problem">Why the read was refused, or null.</param>
/// <param name="Code">The <c>vis-</c> code for the refusal, or null.</param>
/// <remarks>
/// <para>
/// The document alone is never the whole answer: a run of lines this parser could not read is in
/// <paramref name="Opaque"/> and in the document as an opaque block, but the run's <em>text</em> only
/// exists in the former. Both are returned because they answer different questions — a caller building a
/// canvas wants the document, and a caller telling a user what it could not open wants the lines.
/// </para>
/// <para>
/// Every refusal carries a <c>vis-</c> code in the one namespace the validator and <see cref="DropPlan"/>
/// use, for the reason <see cref="DropPlan"/> gives: two vocabularies of codes is one too many for a caller
/// to handle, and a wrong code in one of them was invisible.
/// </para>
/// </remarks>
public sealed record RegionReadResult(
    VisualProject? Project,
    IReadOnlyList<VisualDiagnostic> Diagnostics,
    IReadOnlyList<OpaqueRegion> Opaque,
    string? Problem,
    string? Code)
{
    /// <summary>Whether a document was produced.</summary>
    public bool Ok => Project is not null;

    /// <summary>A read that produced a document.</summary>
    public static RegionReadResult Read(VisualProject project, IReadOnlyList<OpaqueRegion> opaque)
    {
        var diagnostics = new List<VisualDiagnostic>();

        foreach (var chunk in opaque)
        {
            diagnostics.Add(new VisualDiagnostic(
                BlockRegionReader.OpaqueCode,
                VisualSeverity.Warning,
                null,
                $"{chunk.Count} line{(chunk.Count == 1 ? string.Empty : "s")} from the region, starting at "
                + $"line {chunk.Line}, could not be opened as blocks.",
                "They are preserved and written back unchanged."));
        }

        return new RegionReadResult(project, diagnostics, opaque, null, null);
    }

    /// <summary>A read that produced nothing, with the code that says why.</summary>
    public static RegionReadResult Refused(string problem, string code) =>
        new(null, [], [], problem, code);
}