namespace DeckForge.Core.Visual;

using System.Globalization;

/// <summary>
/// The document the Visual page shows before a plugin is open.
/// </summary>
/// <remarks>
/// <para>
/// Phase 3 has no persistence yet, so the page needs something to draw. Rather than a hand-written
/// three-block example — which would show nesting on one category and never show a menu, a hexagon or a
/// hat — this builds one script per palette category containing every shipping block that can sit in a
/// stack. It is therefore the page's regression test as well as its content: a new catalog row appears
/// here, and <c>VisualSampleProjectTests</c> fails until someone looks at it.
/// </para>
/// <para>
/// The declarations it needs are collected from the blocks themselves, so the document validates
/// cleanly. A sample that produced a hundred "unknown variable" diagnostics would be a sample nobody
/// could tell was right.
/// </para>
/// </remarks>
public static class VisualSampleProject
{
    /// <summary>The document the page starts from, built fresh each time.</summary>
    public static VisualProject Build()
    {
        var document = new VisualProject
        {
            DocumentId = VisualProject.NewDocumentId(),
            Targets =
            [
                new VisualTarget
                {
                    Id = "action:log-message",
                    Name = "Log message",
                    Kind = TargetKind.Action,
                    TargetFile = "Actions/LogMessageAction.cs",
                    AnchorId = "ExecuteAsync",
                },
            ],
        };

        // One counter for the whole document rather than VisualProject.NextBlockId, which searches what
        // has been built so far. A block is not in the document until its container has been added to
        // it, so asking for the next id while a tree is half-built hands the same one to a parent and its
        // first child — and two blocks with one id share a layout entry, a diagnostic target and a
        // breakpoint.
        var issued = 0;
        Func<string> nextId = () => "b" + (++issued).ToString(CultureInfo.InvariantCulture);

        var target = document.Targets[0];
        var x = 40.0;
        var y = 24.0;

        foreach (var category in BlockCatalog.Categories)
        {
            var rows = BlockCatalog.InCategory(category.Category)
                .Where(block => block.Shape != BlockShape.Hat)
                .ToList();

            if (rows.Count == 0)
            {
                continue;
            }

            var script = new VisualScript
            {
                Id = "script-" + category.Category.ToString().ToLowerInvariant(),
                Name = category.Name,
                X = x,
                Y = y,
            };

            target.Scripts.Add(script);
            script.Hat = HatFor(category.Category, nextId);

            foreach (var row in rows)
            {
                script.Body.Add(Nest(row, nextId));
            }

            // One column, wrapping: a category's column is as wide as its longest label plus the ones
            // nested inside it, which grows to the right and must not run off the canvas.
            x += 420;
            if (x > 1200)
            {
                x = 40;
                y += 460;
            }
        }

        Declare(document);
        MakeLegal(document);
        return document;
    }

    /// <summary>
    /// What the sample refers to outside itself: the action's parameters and the host's variables.
    /// </summary>
    /// <remarks>
    /// The document does not record these — they belong to the action's declaration and the plugin's
    /// manifest — so the page passes them to the validator alongside the project, exactly as it will for
    /// a real one.
    /// </remarks>
    public static VisualValidationContext ValidationContext { get; } =
        new(new HashSet<string>(StringComparer.Ordinal) { "text", "count" },
            new HashSet<string>(StringComparer.Ordinal) { "hostVar" });

    /// <summary>The hat a category's script starts with.</summary>
    private static Block HatFor(BlockCategory category, Func<string> nextId)
    {
        var own = BlockCatalog.InCategory(category).FirstOrDefault(block => block.Shape == BlockShape.Hat);
        var descriptor = own ?? BlockCatalog.Find("hat.script-starts");
        return descriptor is null
            ? new Block { Kind = "hat.script-starts", Id = nextId() }
            : BlockFactory.Create(descriptor, nextId);
    }

    /// <summary>
    /// A fresh block, put inside another block's body when it can go there.
    /// </summary>
    /// <remarks>
    /// Nesting is what Phase 3 is for, and a flat sample would not exercise the recursive template at
    /// all. The rule is deliberately simple: every container in a category takes the next block in the
    /// same category as its body, and everything after that is a sibling. One level of nesting per
    /// block is enough to prove the recursion and enough to see the indent rail; deeper would only make
    /// the column unreadable.
    /// </remarks>
    private static Block Nest(BlockDescriptor descriptor, Func<string> nextId)
    {
        var block = BlockFactory.Create(descriptor, nextId);

        if (descriptor.IsContainer)
        {
            block.Body(descriptor.Bodies![0].Name).Add(Nested(descriptor.Category, nextId));
        }

        return block;
    }

    /// <summary>The block a container holds: the next one in the same category, or a leaf of its own.</summary>
    private static Block Nested(BlockCategory category, Func<string> nextId)
    {
        var candidates = BlockCatalog.InCategory(category)
            .Where(block => block.Shape is not BlockShape.Hat)
            .Skip(1)
            .ToList();

        var descriptor = candidates.FirstOrDefault(block => !block.IsContainer) ?? candidates.FirstOrDefault();
        return descriptor is null
            ? new Block { Kind = "control.yield", Id = nextId() }
            : BlockFactory.Create(descriptor, nextId);
    }

    /// <summary>
    /// Moves the blocks that cannot sit where they were put into somewhere they can.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three blocks in the catalog are illegal at the top level of an action's script: <c>break</c> and
    /// <c>continue</c> need a loop around them, and <c>return</c> needs a procedure. Every other block is
    /// fine there, and putting the other hundred and fifty next to them is the point of the sample — so
    /// rather than hand-ordering the Control column, the two loops go inside the first loop in their own
    /// script and the <c>return</c> goes into the procedure the document declares.
    /// </para>
    /// <para>
    /// A sample that opens with three errors is a sample a user cannot tell is working: the diagnostics
    /// pane is on this page from the first phase, and it would be showing them.
    /// </para>
    /// </remarks>
    private static void MakeLegal(VisualProject document)
    {
        foreach (var script in document.Targets.SelectMany(target => target.Scripts))
        {
            var host = script.Body.FirstOrDefault(block => BlockCatalog.Find(block.Kind) is { IsContainer: true });
            if (host is null)
            {
                continue;
            }

            var descriptor = BlockCatalog.Find(host.Kind)!;
            var body = host.Body(descriptor.Bodies![0].Name);

            foreach (var block in script.Body
                         .Where(block => block.Kind is "control.break" or "control.continue")
                         .ToList())
            {
                script.Body.Remove(block);
                body.Add(block);
            }
        }

        var procedure = document.Procedures.FirstOrDefault();
        if (procedure is null)
        {
            return;
        }

        foreach (var script in document.Targets.SelectMany(target => target.Scripts))
        {
            foreach (var block in script.Body.Where(block => block.Kind == "control.return").ToList())
            {
                script.Body.Remove(block);
                procedure.Body.Add(block);
            }
        }
    }

    /// <summary>
    /// Declares every variable, list and procedure the sample refers to.
    /// </summary>
    /// <remarks>
    /// Collected from the document rather than written out, so a block that names something new is
    /// covered without this file being edited. Types come from the document's own three — Text, Numeric,
    /// Boolean — which is the SDK's vocabulary rather than C#'s (see Part 6.1).
    /// </remarks>
    private static void Declare(VisualProject document)
    {
        var variables = new HashSet<string>(StringComparer.Ordinal);
        var lists = new HashSet<string>(StringComparer.Ordinal);

        foreach (var block in document.Blocks())
        {
            foreach (var (slotName, input) in block.Inputs)
            {
                if (input.Variable is not { } name)
                {
                    continue;
                }

                var descriptor = BlockCatalog.Find(block.Kind);
                var type = descriptor?.Slot(slotName)?.Type ?? SlotType.Any;

                if (type is SlotType.Variable)
                {
                    variables.Add(name);
                }
                else if (type is SlotType.List)
                {
                    lists.Add(name);
                }
            }
        }

        foreach (var name in variables.OrderBy(name => name, StringComparer.Ordinal))
        {
            document.Variables.Add(new VariableDeclaration { Name = name, Type = "Text" });
        }

        foreach (var name in lists.OrderBy(name => name, StringComparer.Ordinal))
        {
            document.Lists.Add(new ListDeclaration { Name = name, ItemType = "Text" });
        }

        var procedure = new ProcedureDeclaration { Name = "myProcedure" };
        procedure.Parameters.Add(new ProcedureParameter { Name = "text", Type = "Any" });
        document.Procedures.Add(procedure);
    }
}