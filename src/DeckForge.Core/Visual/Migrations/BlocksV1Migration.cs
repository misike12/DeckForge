using DeckForge.Core.Blocks;

namespace DeckForge.Core.Visual.Migrations;

/// <summary>
/// Turns a legacy <see cref="BlockProgram"/> — one flat statement list aimed at one action — into a
/// visual document with a target, a script and the matching blocks.
/// </summary>
/// <remarks>
/// <para>
/// This is the only reason the retired Blocks page does not cost anyone their work. Every one of the
/// eighteen legacy kinds maps onto a shipping block, and each mapping is listed in Part 7.15 of
/// <c>visual.md</c> so the two can be checked against each other rather than trusted.
/// </para>
/// <para>
/// A statement type this build does not know becomes a disabled <c>legacy.unsupported</c> block that
/// records its type name and compiles to nothing, rather than being dropped. The whole point of a
/// migration is that the user's work survives a version they cannot see, so the failure mode has to be
/// "visible and inert", never "gone".
/// </para>
/// </remarks>
public static class BlocksV1Migration
{
    /// <summary>
    /// Migrates a legacy program.
    /// </summary>
    /// <param name="program">The legacy document, as read from <c>&lt;action&gt;.blocks.json</c>.</param>
    /// <returns>A visual document with one action target and one script.</returns>
    public static VisualProject Migrate(BlockProgram program)
    {
        var ids = new IdSource();
        var actionId = string.IsNullOrWhiteSpace(program.TargetActionId) ? "log-message" : program.TargetActionId;

        var script = new VisualScript
        {
            Id = "s1",
            Name = "main",
            X = 60,
            Y = 40,
            Hat = new Block { Kind = "hat.action-runs", Id = ids.Next() },
        };

        foreach (var statement in program.Statements)
        {
            if (MigrateStatement(statement, ids) is { } migrated)
            {
                script.Body.Add(migrated);
            }
        }

        var project = new VisualProject
        {
            DocumentId = VisualProject.NewDocumentId(),
            Targets =
            [
                new VisualTarget
                {
                    Id = "action:" + actionId,
                    Name = actionId,
                    Kind = TargetKind.Action,
                    TargetFile = string.IsNullOrWhiteSpace(program.TargetFile)
                        ? "LogMessageAction.cs"
                        : program.TargetFile,
                    AnchorId = "ExecuteAsync",
                    Scripts = [script],
                },
            ],
        };

        // A legacy program that read a parameter into a local declared that local implicitly. The
        // document declares it explicitly, so the canvas can show it in the palette of variables
        // instead of only discovering it by reading the blocks.
        foreach (var declaration in DeclaredVariables(script))
        {
            if (!project.Variables.Any(existing =>
                    string.Equals(existing.Name, declaration.Name, StringComparison.Ordinal)))
            {
                project.Variables.Add(declaration);
            }
        }

        return project;
    }

    /// <summary>Every local a migrated script declares through a set block.</summary>
    private static IEnumerable<VariableDeclaration> DeclaredVariables(VisualScript script)
    {
        foreach (var block in script.Body.SelectMany(statement => statement.Walk()))
        {
            if (block.Kind is not ("var.set" or "var.set-from-parameter"))
            {
                continue;
            }

            var name = block.Field("var");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            // The block's type field is already in the document's vocabulary: MigrateSetVariable put it
            // there through NormaliseType. Normalising again was a real bug - "Numeric" is not one of
            // the legacy spellings, so every numeric local was redeclared as Text.
            yield return new VariableDeclaration
            {
                Name = name,
                Type = block.Field("type") is { Length: > 0 } type ? type : "Text",
                Scope = VariableScope.Local,
            };
        }
    }

    /// <summary>
    /// The SDK's three variable types, from the legacy compiler's <c>string</c>/<c>number</c>/<c>bool</c>.
    /// </summary>
    /// <remarks>
    /// The vocabularies differ on purpose: the old model's types described a C# local, and the
    /// document's describe an SDK <c>VariableType</c>, which is <c>Text</c>/<c>Numeric</c>/<c>Boolean</c>.
    /// Keeping both spellings would mean translating in two places for ever.
    /// </remarks>
    private static string NormaliseType(string? legacyType) => legacyType switch
    {
        "number" => "Numeric",
        "bool" => "Boolean",
        _ => "Text",
    };

    /// <summary>Migrates one statement, or null when it produced nothing.</summary>
    private static Block? MigrateStatement(BlockStatement statement, IdSource ids) => statement switch
    {
        LogBlock log => new Block { Kind = "ui.log", Id = ids.Next() }
            .WithField("level", log.Level)
            .WithText("template", log.Template)
            .Apply(block => log.Parameter is { Length: > 0 }
                ? block.WithText("param", log.Parameter)
                : block),

        SetVariableBlock set => MigrateSetVariable(set, ids),

        IfBlock branch => MigrateIf(branch, ids),

        ReturnResultBlock result => new Block
        {
            Kind = result.Outcome switch
            {
                "failed" => "control.finish-failed",
                "accepted" => "control.finish-accepted",
                _ => "control.finish-success",
            },
            Id = ids.Next(),
        }
            .WithField("code", result.ErrorCode)
            .WithField("message", result.Message),

        DelayBlock delay => new Block { Kind = "control.wait-ms", Id = ids.Next() }
            .WithNumber("ms", delay.Milliseconds),

        HttpRequestBlock http => new Block { Kind = "http.get", Id = ids.Next() }
            .WithField("into", http.IntoVariable)
            .WithField("bearer", http.BearerTokenParameter)
            .WithText("url", http.Url),

        NotifyBlock notify => new Block { Kind = "ui.notify", Id = ids.Next() }
            .WithField("level", notify.Level)
            .WithField("title", notify.Title)
            .WithField("key", notify.Key)
            .WithText("message", notify.Message),

        NavigateBlock navigate => new Block { Kind = "deck.open-folder", Id = ids.Next() }
            .WithText("folder", navigate.FolderId),

        GoToParentBlock => new Block { Kind = "deck.go-to-parent", Id = ids.Next() },

        GoBackBlock => new Block { Kind = "deck.go-back", Id = ids.Next() },

        ChangeProfileBlock profile => new Block { Kind = "deck.switch-profile", Id = ids.Next() }
            .WithText("profile", profile.ProfileId),

        RunScriptBlock script => new Block { Kind = "sensing.run-script", Id = ids.Next() }
            .WithField("script", script.ScriptId)
            .WithField("inputs", script.Inputs),

        PublishEventBlock publish => new Block { Kind = "events.publish", Id = ids.Next() }
            .WithField("event", publish.EventId)
            .WithField("payload", publish.Payload),

        ReadVariableBlock read => new Block { Kind = "sensing.get-host-variable", Id = ids.Next() }
            .WithField("name", read.VariableName)
            .WithField("into", read.IntoVariable),

        SetVariableValueBlock write => new Block { Kind = "sensing.set-host-variable", Id = ids.Next() }
            .WithField("name", write.VariableName)
            .WithText("value", write.Value),

        ShowModalBlock modal => new Block { Kind = "ui.show-modal", Id = ids.Next() }
            .WithField("view", modal.ViewId)
            .WithField("title", modal.Title)
            .WithField("data", modal.Data),

        InvalidateIconBlock icon => new Block { Kind = "deck.invalidate-icon", Id = ids.Next() }
            .WithField("action", icon.ActionId),

        ThrowBlock thrown => new Block { Kind = "control.throw", Id = ids.Next() }
            .WithText("message", thrown.Message),

        // Not a statement this build knows. Kept as an inert, visible placeholder rather than dropped:
        // a migration must never be the reason a user's block disappeared.
        _ => new Block
        {
            Kind = "legacy.unsupported",
            Id = ids.Next(),
            Disabled = true,
            Comment = $"Not recognised by this build: {statement.GetType().Name}",
        }.WithField("legacyType", statement.GetType().Name),
    };

    private static Block MigrateSetVariable(SetVariableBlock set, IdSource ids)
    {
        var block = new Block
        {
            Kind = set.FromParameter is not null ? "var.set-from-parameter" : "var.set",
            Id = ids.Next(),
        }.WithField("var", set.VariableName)
         .WithField("type", NormaliseType(set.Type));

        if (set.FromParameter is not null)
        {
            return block
                .WithField("param", set.FromParameter)
                .WithField("required", set.Required ? "true" : "false");
        }

        return set.Type switch
        {
            "number" => block.WithField("valueKind", "number")
                .WithNumber("value", double.TryParse(
                    set.Literal,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var number) ? number : 0),
            "bool" => block.WithField("valueKind", "bool")
                .WithBoolean("value", bool.TryParse(set.Literal, out var flag) && flag),
            _ => block.WithField("valueKind", "text").WithText("value", set.Literal ?? string.Empty),
        };
    }

    private static Block MigrateIf(IfBlock branch, IdSource ids)
    {
        var comparison = new Block { Kind = "ops.compare", Id = ids.Next(), }
            .WithField("op", branch.Operator);

        if (branch.RightLiteral is { Length: > 0 })
        {
            comparison.WithText("b", branch.RightLiteral);
        }

        if (branch.LeftVariable is { Length: > 0 })
        {
            comparison.WithVariable("a", branch.LeftVariable);
        }
        else
        {
            comparison.WithText("a", string.Empty);
        }

        var container = new Block
        {
            Kind = branch.Else.Count > 0 ? "control.if-else" : "control.if",
            Id = ids.Next(),
        }.WithBlock("condition", comparison);

        foreach (var child in branch.Then)
        {
            if (MigrateStatement(child, ids) is { } migrated)
            {
                container.Body("then").Add(migrated);
            }
        }

        foreach (var child in branch.Else)
        {
            if (MigrateStatement(child, ids) is { } migrated)
            {
                container.Body("else").Add(migrated);
            }
        }

        return container;
    }

    /// <summary>
    /// Hands out <c>b1</c>, <c>b2</c>, … in the order the document is built.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="VisualProject.NextBlockId"/>: the project is still being assembled
    /// while blocks are created, so a scan of it would find nothing and hand out <c>b1</c> to every
    /// block — an id collision that only shows up as one block's breakpoint lighting up another's.
    /// </remarks>
    private sealed class IdSource
    {
        private int _next;

        public string Next() => "b" + (++_next).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>Small fluent helper so the migration's mapping table stays a table.</summary>
internal static class BlockFluentExtensions
{
    /// <summary>Applies a change, for the odd case that needs a condition inside a chain.</summary>
    public static Block Apply(this Block block, Func<Block, Block> change) => change(block);
}
