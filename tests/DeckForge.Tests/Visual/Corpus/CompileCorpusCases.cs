using DeckForge.Core.Visual;

namespace DeckForge.Tests.Visual.Corpus;

/// <summary>
/// One document in the compile corpus: a name, what it is for, and a builder that produces it.
/// </summary>
/// <param name="Name">The file name stem of its golden file.</param>
/// <param name="Covers">One line saying which shape this document exists to pin, copied into the golden.</param>
/// <param name="Build">Builds the document. Called once per comparison, never cached, so a case cannot be half-evaluated.</param>
internal sealed record CorpusCase(string Name, string Covers, Func<VisualProject> Build)
{
    /// <summary>
    /// Whether the emitter's output for this document is written into a plugin and built for real.
    /// </summary>
    /// <remarks>
    /// True for every case that <em>can</em> compile. A case that cannot sets it false and says why,
    /// and <c>The_corpus_says_why_a_document_is_not_compiled</c> holds that reason to the same standard
    /// as the coverage test holds the block list: an exclusion nobody can read is an exclusion nobody
    /// can challenge, and a silently-skipped document is a document that stopped being tested.
    /// </remarks>
    public bool CompiledForReal { get; init; } = true;

    /// <summary>Why this document is snapshot-only. Empty exactly when <see cref="CompiledForReal"/> is true.</summary>
    public string NotCompiledReason { get; init; } = string.Empty;

    /// <summary>
    /// Whether the emitter is expected to report at least one problem for this document.
    /// </summary>
    /// <remarks>
    /// Separate from the golden file on purpose. A golden file is rewritten by a human who decided the
    /// change was correct, so a new diagnostic appearing in one is exactly what the golden diff is for.
    /// This flag is the other half: it says "this document is expected to be clean", and the test
    /// enforces that without any human in the loop.
    /// </remarks>
    public bool ExpectsProblems { get; init; }
}

/// <summary>
/// A document under construction, with ids handed out in document order.
/// </summary>
/// <remarks>
/// <para>
/// Ids are <c>b1</c>, <c>b2</c>, … rather than GUIDs for the reason <see cref="VisualProject.NextBlockId"/>
/// gives: the corpus is a thing a human reads, and its goldens are reviewed as diffs. A GUID in every
/// block would make a three-line change look like a rewrite.
/// </para>
/// <para>
/// <see cref="Project"/>'s declarations - variables, lists and procedures - are set up explicitly per
/// case, because whether the emitter reads them is itself part of what a snapshot pins down.
/// </para>
/// </remarks>
internal sealed class CorpusDoc
{
    private readonly VisualProject _project = new() { DocumentId = "corpus" };
    private readonly VisualTarget _target = new() { Id = "action:corpus", Name = "Corpus action" };
    private int _next = 1;

    public CorpusDoc()
    {
        _project.Targets.Add(_target);
    }

    /// <summary>The document, once <see cref="Finish"/> has been called.</summary>
    public VisualProject Project => _project;

    /// <summary>The target every script in this document lives in.</summary>
    public VisualTarget Target => _target;

    /// <summary>The document's procedures, so a case can reach the one it just declared.</summary>
    public List<ProcedureDeclaration> Procedures => _project.Procedures;

    /// <summary>Adds a script to the target and returns it, so a case can build several.</summary>
    public VisualScript Script(string hat = "hat.action-runs", string name = "main", bool disabled = false)
    {
        var script = new VisualScript
        {
            Id = $"script-{_target.Scripts.Count + 1}",
            Name = name,
            Disabled = disabled,
            Hat = new Block { Kind = hat, Id = $"hat-{_target.Scripts.Count + 1}" },
        };

        _target.Scripts.Add(script);
        return script;
    }

    /// <summary>Adds a second target to the document, which is how two actions share one document.</summary>
    public VisualTarget AddTarget(string id, string name)
    {
        var target = new VisualTarget { Id = id, Name = name, Kind = TargetKind.Action };
        _project.Targets.Add(target);
        return target;
    }

    /// <summary>Appends a statement to a script.</summary>
    public Block Add(VisualScript script, string kind, Action<Block>? fill = null)
    {
        var block = Make(kind, fill);
        script.Body.Add(block);
        return block;
    }

    /// <summary>Appends a statement to a script's first target, which is what most cases want.</summary>
    public Block Add(string kind, Action<Block>? fill = null) => Add(_target.Scripts[0], kind, fill);

    /// <summary>
    /// A block that is not attached to anything yet: for a slot, a wrapped body, or a tree that is
    /// about to be nested. Ids are assigned when <see cref="Finish"/> runs, not here, so the order
    /// ids come out in is the document's shape and not the order the case happened to write it in.
    /// </summary>
    public Block Make(string kind, Action<Block>? fill = null)
    {
        var block = new Block { Kind = kind };
        fill?.Invoke(block);
        return block;
    }

    /// <summary>A block with every slot and every menu filled with a representative value.</summary>
    public Block Filled(string kind)
    {
        var block = Make(kind);
        Fill(block, BlockCatalog.Find(kind)!);
        return block;
    }

    /// <summary>Declares a variable on the document.</summary>
    public CorpusDoc Variable(string name, string type = "Text", string? initial = null,
        VariableScope scope = VariableScope.Local)
    {
        _project.Variables.Add(new VariableDeclaration
        {
            Name = name,
            Type = type,
            Initial = initial,
            Scope = scope,
        });

        return this;
    }

    /// <summary>Declares a list on the document.</summary>
    public CorpusDoc List(string name, string itemType = "Text", params string[] initial)
    {
        _project.Lists.Add(new ListDeclaration { Name = name, ItemType = itemType, Initial = [.. initial] });
        return this;
    }

    /// <summary>Declares a procedure on the document.</summary>
    public ProcedureDeclaration Procedure(string name, bool returns = false,
        params ProcedureParameter[] parameters)
    {
        var procedure = new ProcedureDeclaration
        {
            Id = $"proc{_project.Procedures.Count + 1}",
            Name = name,
            Returns = returns,
            Parameters = [.. parameters],
        };

        _project.Procedures.Add(procedure);
        return procedure;
    }

    /// <summary>Adds a statement to a procedure body.</summary>
    public Block Body(ProcedureDeclaration procedure, string kind, Action<Block>? fill = null)
    {
        var block = Make(kind, fill);
        procedure.Body.Add(block);
        return block;
    }

    /// <summary>Fills every slot and every menu of a block from its descriptor.</summary>
    /// <param name="block">The block to fill.</param>
    /// <param name="descriptor">Its catalog row.</param>
    /// <remarks>
    /// A <see cref="SlotType.List"/> slot is filled with <see cref="ListName"/>, because the emitter
    /// writes that slot as a bare identifier and a document that uses one has to declare it before it
    /// is read. Every case that fills a list slot therefore opens with a <c>list.define</c>.
    /// </remarks>
    public void Fill(Block block, BlockDescriptor descriptor)
    {
        foreach (var slot in descriptor.Slots ?? [])
        {
            FillSlot(block, slot);
        }

        foreach (var menu in descriptor.Menus ?? [])
        {
            // The default, which the catalog asserts is one of the options.
            block.WithField(menu.Name, menu.Default);
        }
    }

    /// <summary>Fills one slot from its descriptor.</summary>
    public void FillSlot(Block block, SlotDescriptor slot)
    {
        switch (slot.Type)
        {
            case SlotType.Number:
                block.WithNumber(slot.Name, 1);
                break;
            case SlotType.Boolean:
                block.WithBoolean(slot.Name, true);
                break;
            case SlotType.List:
                block.WithText(slot.Name, ListName);
                break;
            case SlotType.Procedure:
                // A procedure slot becomes the local function's name, so a document that fills one has
                // to declare the procedure; the cases that do open with `doc.Procedure(...)`.
                block.WithText(slot.Name, ProcedureName);
                break;
            default:
                block.WithText(slot.Name, slot.Name);
                break;
        }
    }

    /// <summary>The name every list slot in a filled document uses, and every list it declares.</summary>
    public const string ListName = "items";

    /// <summary>The response local a document that reads a finished request refers to.</summary>
    public const string ResponseLocal = "response";

    /// <summary>The name every procedure slot in a filled document uses, declared by <see cref="Procedure"/>.</summary>
    public const string ProcedureName = "helper";

    /// <summary>Numbers every block in the document, in document order, and returns the project.</summary>
    public VisualProject Finish()
    {
        foreach (var target in _project.Targets)
        {
            foreach (var script in target.Scripts)
            {
                Number(script.Hat);
                foreach (var statement in script.Body)
                {
                    Number(statement);
                }
            }
        }

        foreach (var procedure in _project.Procedures)
        {
            foreach (var statement in procedure.Body)
            {
                Number(statement);
            }
        }

        _project.EnsureProcedureIds();
        return _project;
    }

    /// <summary>One id per block, depth first, so a golden file's ids are a readable sequence.</summary>
    private void Number(Block block)
    {
        if (block.Id.Length == 0)
        {
            block.Id = $"b{_next++}";
        }

        foreach (var input in block.Inputs.Values)
        {
            if (input.Block is { } nested)
            {
                Number(nested);
            }
        }

        foreach (var body in block.Bodies.Values)
        {
            foreach (var child in body)
            {
                Number(child);
            }
        }
    }
}