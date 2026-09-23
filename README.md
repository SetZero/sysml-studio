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
| `SysmlStudio.App` — the Avalonia application | ribbon, dockable panes, five diagram kinds, right-click menus on the tree and the canvas, edit dialogs with a preview of what will be written, a relation tool, undo/redo, source tabs; Save writes the changed files |

Open a model folder:

```
dotnet run --project src/SysmlStudio.App -- ../os/docs/sysml
```

The project browser lists every package and element with its maturity bar;
select one and the ribbon's Diagram kind group lights the diagrams that can be
drawn of it. Right-click an element — in the tree or on a diagram — to add
inside it, rename it, type it, mark it or delete it; each dialog shows the
lines it will write before it writes them. Toolbox relations draw between two
clicked boxes. Every edit is a patch over the text, re-parsed before it is
accepted, kept in memory with undo until Save writes the changed files. With
no folder given, the Start page offers the sample model in `samples/vehicle`.

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
| UI | [Avalonia](https://avaloniaui.net) 12, `Dock.Avalonia` (panes), `Nodify.Avalonia` (canvas), `AvaloniaEdit` (source), `FluentIcons.Avalonia` |
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
