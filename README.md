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
| `SysmlStudio.Editing` — graphical edits as text patches | not started |
| `SysmlStudio.Diagrams` — the five diagram kinds, MSAGL layout | not started |
| `SysmlStudio.App` — the Avalonia application | not started |

There is no application to run yet. What exists can be seen with the parse
checker, which loads a folder and prints what it found:

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
| UI | [Avalonia](https://avaloniaui.net), `Dock.Avalonia`, `Nodify.Avalonia`, `AvaloniaEdit` |

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
| `test` | the xUnit suite |
| `model` | loads a real model — `$SYSML_STUDIO_MODEL`, or `../os/docs/sysml` when that checkout is beside this one — and fails if a file does not parse |

Judge a gate by its exit status, not by its last line. `scripts/gate.sh` stops
at the first gate that fails.

## Commits

A commit message is a subject, a blank line, and a body that argues the why.
No `Co-authored-by:` trailer, no "Generated with" line, no tool signature —
one author per commit.
