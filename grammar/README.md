# The SysML v2 grammar

`SysMLv2Lexer.g4` and `SysMLv2Parser.g4` are vendored from
[antlr/grammars-v4](https://github.com/antlr/grammars-v4/tree/master/sysml-v2),
commit `e1c222f3f0e7c1b2fec799e94e34fc388b03f887`, MIT licensed. They are
generated from the OMG SysML v2 specification's KEBNF grammar (release
2026-05) by https://github.com/daltskin/sysml-v2-grammar.

`SysmlStudio.Syntax` generates the C# lexer and parser from them at build time
with `Antlr4BuildTasks`, which needs a JDK on PATH (it downloads one if there
is none).

## Local patches

None yet. Any change made here is listed in this section with the reason, so
that a later re-vendor can re-apply it.
