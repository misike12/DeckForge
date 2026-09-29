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
/// <strong>Every key written here is one the catalog declares.</strong> Menu selections — the log
/// level, the error code, the comparison operator — go into <c>fields</c>, because that is where
/// <see cref="Block.Fields"/> is read from, and everything that fills a slot goes into
/// <c>inputs</c>, because that is where the emitter and the canvas look. The first version of this
/// file wrote slot values with <c>WithField</c>, which parses and round-trips perfectly and emits
/// nothing at all: the block is there, its inputs are empty, and the generated C# passes an empty
/// string. <c>BlockCatalogTests</c> now refuses that, by migrating one instance of every legacy kind
/// and checking each key against the descriptor.
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
            script.Body.AddRange(MigrateStatement(statement, ids));
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
            if (block.Kind is not ("var.set" or "var.set-from-parameter" or "var.set-from-host"))
            {
                continue;
            }

            var name = block.InputText("var");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            // The block's type menu is already in the document's vocabulary: MigrateSetVariable put it
            // there through NormaliseType. Normalising again was a real bug - "Numeric" is not one of
            // the legacy spellings, so every numeric local was redeclared as Text.
            yield return new VariableDeclaration
            {
                Name = name,
                Type = DeclaredTypeOf(block),
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

    /// <summary>
    /// The legacy operator set, which is almost the catalog's.
    /// </summary>
    /// <remarks>
    /// The one difference is <c>==</c> against the catalog's <c>=</c>, because the legacy model stored
    /// C# operators and the catalog stores menu keys. Left alone, a migrated condition would carry a
    /// menu value that is not one of the menu's options: the canvas would show a blank dropdown and the
    /// emitter would fall through to its default, silently testing something else.
    /// </remarks>
    private static string NormaliseOperator(string? legacyOperator) => legacyOperator switch
    {
        "==" => "=",
        "!=" or "<" or ">" or "<=" or ">=" or "isEmpty" or "isNotEmpty"
            or "isAvailable" or "isNotAvailable" or "contains" or "notContains" => legacyOperator,
        _ => "=",
    };

    /// <summary>An <c>ActionErrorCodes</c> member name, or null when the legacy value is not one.</summary>
    private static string? NormaliseErrorCode(string? legacyCode) =>
        ErrorCodes.FirstOrDefault(code => string.Equals(code, legacyCode, StringComparison.OrdinalIgnoreCase));

    /// <summary>The nine members of the SDK's <c>ActionErrorCodes</c>, in the order the menu lists them.</summary>
    private static readonly string[] ErrorCodes =
    [
        "NotConfigured", "NotConnected", "PermissionDenied", "ProviderError", "ProviderRejected",
        "InvalidParameter", "NotFound", "Timeout", "Unavailable",
    ];

    /// <summary>Migrates one statement into the blocks it becomes, in order.</summary>
    /// <remarks>
    /// A sequence rather than a single block, because one legacy statement genuinely maps onto two: an
    /// HTTP request that carries a bearer token needs the token set before the call, and the catalog
    /// keeps that as its own block rather than an extra slot on <c>http.get</c>. Returning a list also
    /// leaves room for a future legacy kind that has to be split, without changing every caller again.
    /// </remarks>
    private static IEnumerable<Block> MigrateStatement(BlockStatement statement, IdSource ids)
    {
        switch (statement)
        {
            case LogBlock log:
            {
                // Two variants, like notify: a log line that attaches a parameter is a different call
                // (one extra structured argument), and the catalog keeps them apart so the palette row
                // tells the truth about what the block does.
                var withParameter = log.Parameter is { Length: > 0 };
                var block = new Block
                {
                    Kind = withParameter ? "ui.log-with-param" : "ui.log",
                    Id = ids.Next(),
                }.WithField("level", NormaliseLogLevel(log.Level));

                if (withParameter)
                {
                    block.WithText("template", log.Template).WithText("param", log.Parameter!);
                }
                else
                {
                    block.WithText("template", log.Template);
                }

                yield return block;
                break;
            }

            case SetVariableBlock set:
                yield return MigrateSetVariable(set, ids);
                break;

            case IfBlock branch:
                yield return MigrateIf(branch, ids);
                break;

            case ReturnResultBlock result:
                yield return MigrateReturnResult(result, ids);
                break;

            case DelayBlock delay:
                yield return new Block { Kind = "control.wait-ms", Id = ids.Next() }
                    .WithNumber("ms", delay.Milliseconds);
                break;

            case HttpRequestBlock http:
            {
                if (http.BearerTokenParameter is { Length: > 0 } bearer)
                {
                    yield return new Block { Kind = "http.set-bearer", Id = ids.Next() }
                        .WithText("name", bearer);
                }

                yield return new Block { Kind = "http.get", Id = ids.Next() }
                    .WithText("into", http.IntoVariable is { Length: > 0 } target ? target : "response")
                    .WithText("url", http.Url);
                break;
            }

            case NotifyBlock notify:
                yield return MigrateNotify(notify, ids);
                break;

            case NavigateBlock navigate:
                yield return new Block { Kind = "deck.open-folder", Id = ids.Next() }
                    .WithText("folder", navigate.FolderId);
                break;

            case GoToParentBlock:
                yield return new Block { Kind = "deck.go-to-parent", Id = ids.Next() };
                break;

            case GoBackBlock:
                yield return new Block { Kind = "deck.go-back", Id = ids.Next() };
                break;

            case ChangeProfileBlock profile:
                yield return new Block { Kind = "deck.switch-profile", Id = ids.Next() }
                    .WithText("profile", profile.ProfileId);
                break;

            case RunScriptBlock script:
                yield return script.Inputs is { Length: > 0 }
                    ? new Block { Kind = "sensing.run-script-with-inputs", Id = ids.Next() }
                        .WithText("script", script.ScriptId)
                        .WithText("inputs", script.Inputs)
                    : new Block { Kind = "sensing.run-script", Id = ids.Next() }
                        .WithText("script", script.ScriptId);
                break;

            case PublishEventBlock publish:
                yield return publish.Payload is { Length: > 0 }
                    ? new Block { Kind = "events.publish-with-payload", Id = ids.Next() }
                        .WithText("event", publish.EventId)
                        .WithText("payload", publish.Payload)
                    : new Block { Kind = "events.publish", Id = ids.Next() }
                        .WithText("event", publish.EventId);
                break;

            // The legacy block read a host variable into a local, which is one block in the catalog:
            // a read is a reporter, and something has to hold its value. var.set-from-host is that
            // block, and it declares its local like every other set block does.
            case ReadVariableBlock read:
                yield return new Block { Kind = "var.set-from-host", Id = ids.Next() }
                    .WithText("var", read.IntoVariable is { Length: > 0 } local ? local : "variableValue")
                    .WithText("name", read.VariableName);
                break;

            case SetVariableValueBlock write:
            {
                var block = new Block { Kind = "sensing.set-host-variable", Id = ids.Next() }
                    .WithText("name", write.VariableName);

                // UseParameter meant "the value is a parameter, not a literal", which the catalog
                // expresses by nesting the parameter reader in the slot. That nesting is the whole
                // reason this model has slots at all.
                if (write.UseParameter)
                {
                    block.WithBlock("value", new Block { Kind = "data.read-payload", Id = ids.Next() }
                        .WithText("name", write.Value));
                }
                else
                {
                    block.WithText("value", write.Value);
                }

                yield return block;
                break;
            }

            case ShowModalBlock modal:
                yield return modal.Data is { Length: > 0 }
                    ? new Block { Kind = "ui.show-modal-data", Id = ids.Next() }
                        .WithText("view", modal.ViewId)
                        .WithText("title", modal.Title)
                        .WithText("data", modal.Data)
                    : new Block { Kind = "ui.show-modal", Id = ids.Next() }
                        .WithText("view", modal.ViewId)
                        .WithText("title", modal.Title);
                break;

            case InvalidateIconBlock icon:
                yield return new Block { Kind = "deck.invalidate-icon", Id = ids.Next() }
                    .WithText("action", icon.ActionId);
                break;

            case ThrowBlock thrown:
                yield return new Block { Kind = "control.throw", Id = ids.Next() }
                    .WithText("message", thrown.Message);
                break;

            // Not a statement this build knows. Kept as an inert, visible placeholder rather than
            // dropped: a migration must never be the reason a user's block disappeared.
            default:
                yield return new Block
                {
                    Kind = "legacy.unsupported",
                    Id = ids.Next(),
                    Disabled = true,
                    Comment = $"Not recognised by this build: {statement.GetType().Name}",
                }.WithText("legacyType", statement.GetType().Name);
                break;
        }
    }

    private static Block MigrateSetVariable(SetVariableBlock set, IdSource ids)
    {
        var block = new Block
        {
            Kind = set.FromParameter is not null ? "var.set-from-parameter" : "var.set",
            Id = ids.Next(),
        }.WithVariable("var", set.VariableName);

        if (set.FromParameter is not null)
        {
            // A parameter arrives as an object, so the block has to be told how to read it.
            return block
                .WithField("type", NormaliseType(set.Type))
                .WithText("param", set.FromParameter)
                .WithField("required", set.Required ? "true" : "false");
        }

        // A literal carries its own type: 5, "5" and true are three different BlockInputs. So there is
        // no type menu on this block at all - design C8 says the local is declared by inference - and
        // DeclaredVariables reads the type back off the slot. An extra type field or valueKind field
        // would be a second source of truth for a type, which is one that can disagree with the first.
        return set.Type switch
        {
            "number" => block.WithNumber("value", double.TryParse(
                set.Literal,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var number) ? number : 0),
            "bool" => block.WithBoolean("value", bool.TryParse(set.Literal, out var flag) && flag),
            _ => block.WithText("value", set.Literal ?? string.Empty),
        };
    }

    /// <summary>
    /// The notify kind that matches what the legacy block actually set.
    /// </summary>
    /// <remarks>
    /// The catalog keeps a level, a key and a bare notify apart, because they are three different calls
    /// to the host. The legacy model had one block with all three properties, so the migration has to
    /// choose: a key makes the notification replaceable, a non-default level makes it a warning, and
    /// anything else is the plain form.
    /// </remarks>
    private static Block MigrateNotify(NotifyBlock notify, IdSource ids)
    {
        if (notify.Key is { Length: > 0 } key)
        {
            return new Block { Kind = "ui.notify-key", Id = ids.Next() }
                .WithText("key", key)
                .WithText("title", notify.Title)
                .WithText("message", notify.Message);
        }

        if (notify.Level is { Length: > 0 } level && !string.Equals(level, "Info", StringComparison.OrdinalIgnoreCase))
        {
            return new Block { Kind = "ui.notify-level", Id = ids.Next() }
                .WithField("level", NormaliseNotificationLevel(level))
                .WithText("title", notify.Title)
                .WithText("message", notify.Message);
        }

        return new Block { Kind = "ui.notify", Id = ids.Next() }
            .WithText("title", notify.Title)
            .WithText("message", notify.Message);
    }

    /// <summary>One of the catalog's three notification levels, defaulting to Info.</summary>
    private static string NormaliseNotificationLevel(string legacyLevel) => legacyLevel.ToLowerInvariant() switch
    {
        "warning" or "warn" => "Warning",
        "error" => "Error",
        _ => "Info",
    };

    /// <summary>One of the catalog's five log levels, defaulting to Information.</summary>
    private static string NormaliseLogLevel(string legacyLevel) =>
        LogLevels.FirstOrDefault(level => string.Equals(level, legacyLevel, StringComparison.OrdinalIgnoreCase))
        ?? "Information";

    /// <summary>The five Serilog levels the log block offers, in the order the menu lists them.</summary>
    private static readonly string[] LogLevels =
        ["Verbose", "Debug", "Information", "Warning", "Error"];

    /// <summary>
    /// The type of the local a set block declares.
    /// </summary>
    /// <remarks>
    /// Two sources, because the two set blocks differ on purpose. <c>var.set-from-parameter</c> has a
    /// type menu — a parameter arrives as an object and has to be read as something — while
    /// <c>var.set</c> deliberately has none, because a literal's type is the slot's own value kind.
    /// Reading both from the menu made every migrated numeric literal declare itself as text.
    /// </remarks>
    private static string DeclaredTypeOf(Block block) => block.Kind switch
    {
        "var.set-from-parameter" => block.Field("type") is { Length: > 0 } type ? type : "Text",
        _ => block.Inputs.TryGetValue("value", out var value)
            ? value.Kind switch
            {
                BlockInputKind.Number => "Numeric",
                BlockInputKind.Boolean => "Boolean",
                _ => "Text",
            }
            : "Text",
    };

    private static Block MigrateReturnResult(ReturnResultBlock result, IdSource ids) => result.Outcome switch
    {
        "failed" => new Block { Kind = "control.finish-failed", Id = ids.Next() }
            .WithField("code", NormaliseErrorCode(result.ErrorCode))
            .WithText("message", result.Message),
        "accepted" => new Block { Kind = "control.finish-accepted", Id = ids.Next() }
            .WithText("message", result.Message),
        _ => new Block { Kind = "control.finish-success", Id = ids.Next() },
    };

    private static Block MigrateIf(IfBlock branch, IdSource ids)
    {
        var comparison = new Block { Kind = "ops.compare", Id = ids.Next() }
            .WithField("op", NormaliseOperator(branch.Operator));

        if (branch.RightLiteral is { Length: > 0 })
        {
            comparison.WithText("b", branch.RightLiteral);
        }

        if (branch.LeftVariable is { Length: > 0 } left)
        {
            comparison.WithVariable("a", left);
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
            container.Body("then").AddRange(MigrateStatement(child, ids));
        }

        foreach (var child in branch.Else)
        {
            container.Body("else").AddRange(MigrateStatement(child, ids));
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
