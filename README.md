# SysML Studio

A graphical viewer and editor for SysML v2 models in the textual notation —
the Enterprise Architect idea, on .NET, cross-platform, with the `.sysml` files
themselves as the source of truth.

Open a folder of `.sysml` files, browse the model as a tree, read it as
diagrams, change it, and save. Saving writes a minimal patch over the text, so
comments, doc blocks and formatting survive an edit and a diff shows only what
actually changed.

## State

| Piece | State |
|---|---|
| `SysmlStudio.Syntax` — parse SysML v2 text, keep tokens and comments | works |
| `SysmlStudio.Model` — element tree, relations, name resolution | works |
| `SysmlStudio.Diagrams` — the five diagram kinds, MSAGL layout | works |
| `SysmlStudio.Editing` — edits as text patches | rename (every reference, across files), delete, add, set type, specialize, maturity, doc comment, and drawing connect / flow / succession / transition / satisfy / dependency / allocate / specialization / composition; each re-parsed before it is accepted |
| `SysmlStudio.App` — the Avalonia application | model tree, diagram tabs with a kind switcher, inspector, problems and usages, right-click menus on the tree and the canvas, edit dialogs that say what they will change, a relation tool, undo/redo, source tabs, SysML v2 JSON and XMI export; Save writes the changed files |

Open a model folder:

```
dotnet run --project src/SysmlStudio.App -- ../os/docs/sysml
```

The model tree on the left lists every package and element; select one and
the switcher in the title bar offers the diagram kinds that can be drawn of it
(Definition, Interconnection, Requirements, Action, State), and the inspector
on the right shows what it specializes and holds, its maturity and its doc
comment. Right-click an element — in the tree or on a diagram — to add inside
it, rename it, type it, mark it or delete it; each dialog says what it will
change before it writes. The toolbar under a diagram adds elements and draws
relations between two clicked boxes. Every edit is a patch over the text,
re-parsed before it is accepted, kept in memory with undo until Save writes
the changed files. Export writes the whole workspace as SysML v2 JSON or XMI.
Nearly every command has a keyboard shortcut; the cogwheel at the bottom left
opens the settings, where each one can be changed (click it, press the new
keys) and the theme chosen. They are kept in `settings.json` in the user's
application data.
With no folder given, the window offers a folder picker, the recent folders
and the sample model in `samples/vehicle`.

What the model holds can also be printed without the window:

```
dotnet run --project tools/ParseCheck -- ../os/docs/sysml
```

## What it is built from

Nothing here reimplements what a library already does.

| Need | Dependency |
|---|---|
| SysML v2 grammar | [`grammars-v4/sysml-v2`](https://github.com/antlr/grammars-v4/tree/master/sysml-v2), vendored in `grammar/` (MIT), generated to C# at build time by `Antlr4BuildTasks` |
| Lossless edits | ANTLR's `TokenStreamRewriter` over the token stream |
| Model interchange | [`SysML2.NET`](https://github.com/STARIONGROUP/SysML2.NET) for JSON/XMI export |
| Graph layout | [MSAGL](https://github.com/microsoft/automatic-graph-layout) |
| UI | [Avalonia](https://avaloniaui.net) 12, `Nodify.Avalonia` (canvas), `AvaloniaEdit` (source), `FluentIcons.Avalonia` |
| Type | IBM Plex Sans Condensed and IBM Plex Mono, bundled under the SIL Open Font Licence (`src/SysmlStudio.App/Assets/Fonts/OFL.txt`) |

## Building

Needs the .NET 10 SDK and a JDK (ANTLR generates the parser at build time;
`Antlr4BuildTasks` downloads a JDK if there is none).

```
dotnet build SysmlStudio.slnx
dotnet test SysmlStudio.slnx
```

## Gates

Every gate CI runs, cheapest first, in one command:

```
scripts/gate.sh          # or: .\scripts\gate.ps1
scripts/gate.sh format   # one gate by name
```

| Gate | What it is |
|---|---|
| `format` | `dotnet format --verify-no-changes --severity info`: whitespace, using order and the code style `.editorconfig` fixes |
| `build` | the build with the .NET analyzers, Roslynator and SonarAnalyzer on and **every warning an error** |
| `test` | the xUnit suites, including a headless run of the real window: every diagram kind, a source tab with an error, the dark theme. `SYSML_STUDIO_SHOTS=<folder>` saves what each test rendered |
| `sample` | the sample model parses |
| `model` | loads a real model — `$SYSML_STUDIO_MODEL`, or `../os/docs/sysml` when that checkout is beside this one — and fails if a file does not parse |

Judge a gate by its exit status, not by its last line. `scripts/gate.sh` stops
at the first gate that fails.

## Commits

A commit message is a subject, a blank line, and a body that argues the why.
No `Co-authored-by:` trailer, no "Generated with" line, no tool signature —
one author per commit.
