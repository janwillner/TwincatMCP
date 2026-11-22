# ADR-0001: Coding guidelines as a per-solution config the server enforces

Status: accepted (2026-11-22)

## Context

Agents write PLC code through this server, and companies have their own coding guidelines, for example `src/TwinCatMcp.Server/GuidelineExamples/plc-coding-guidelines.Syntegon.md` (Syntegon). The only guidance was the fixed Beckhoff text in `twincat://guide/st-conventions`, which a client may never read. Skills in an agent plugin help only that one client. TE1200 Static Analysis checks naming only with the full license (Light has no naming conventions), only on demand, and only for the whole project. Its naming model also does not fit `vi_b_Init`-style names: TE1200 concatenates prefixes without separators.

## Decision

- **The server enforces the rules, and the rules are also shown as guidance.** The config is the single source for every MCP client. Skills may point to it, never override it.
- **The config is `.twincat-mcp.json` next to the `.sln`.** It is loaded on demand, keyed on `Dte.Solution.FullName`, and re-read when the config chain, the guide or a header template changes. Without the file nothing is checked, so existing users see no change.
- **Layers come from an explicit `extends`.** It names a path or `builtin:<name>`, which resolves to the example `.twincat-mcp.<Name>.json` shipped in `guidelines/`. There is no implicit machine-level file, so results are the same on every machine and in CI. Objects merge key by key, and arrays are replaced. A project may override its base; `guidelines_get` shows which file set each value.
- **The format is JSON with comments.** It has a published schema, and prose lives in Markdown (`guide`). Unknown keys, unknown rule keys and invalid severities make the config invalid. With an invalid config the write tools refuse to work and no rules are silently dropped; read tools keep working.
- **The agent can only read the config.** There is no tool that writes it. The instructions forbid editing it or adding bypass pragmas unless the user asks.
- **Rules have readable keys** such as `naming.var.input`, `decl.enum.explicitValues` and `header.copyright`.
  - Each rule has a severity `off | warn | error`. The shipped examples map the guideline's **(rule)** to `error` and its other recommendations to `warn`.
  - `error` rejects the write before anything is changed. `pou_multi_replace` stays all-or-nothing.
  - `warn` returns `guidelineViolations` with the written result.
- **Only new or changed identifiers are checked** (name, type and init value compared with the text before the write). Legacy code stays editable. `guidelines_check` audits everything and can write SARIF.
- **Naming uses token templates, not the TE1200 model.**
  - A template is built from `{type}`, `{Name}`, `{name}`, `{NAME}`, `{*}` and literal text, with `@regex` as an escape hatch.
  - Type prefixes come from `types` (exact names, then wildcard patterns), then `typePrefixes`, then the type-name heuristic `typeKinds` → `kindPrefixes`. ARRAY/POINTER/REFERENCE prefixes compose.
  - A type the config does not know is not prefix-checked; `naming.unknownType` can switch on a warning.
  - Context parts such as `MODx_UNITx_` are not derived. They are the author's responsibility (`{*}`).
- **Declaration rules are a fixed, parameterized catalog** (required attributes, explicit enum values and their range, no defaults on method inputs). There is no rule DSL.
- **Header templates are inserted on create**, and missing required attributes are added on create. Existing code is never fixed automatically, and names are never changed automatically. A suggestion is given when it follows unambiguously from the template.
- **Exceptions:** `exclude` globs, global or per rule; the TE1200 pragmas `{attribute 'naming'}` and `{attribute 'no-analysis'}`; and `{attribute 'mcp-ignore' := '<rule keys>'}`.
- **Checked:** every `pou_*` write and create tool, `pou_rename`, and `tree_rename_item` on PLC nodes. **Not checked:** bulk or raw imports (`plc_import_plcopen`, `archive_*`, `tree_consume_xml`, `xae_execute_command`); `guidelines_check` audits after them.
- **v1 scope:** naming, declarations and headers.
  - **v2:** formatting, metrics, code rules (e.g. no writes to `vi_`), project structure, I/O/task names.
  - **Never:** language checks and visualisation layout.

## Consequences

- TE1200 is complemented, not replaced. `staticAnalysis.settingsFile` points `plc_run_static_analysis` at a `.csa` file.
- The parser (`StDeclaration`) now keeps pragmas (per declaration and per object), knows `naming` off/on regions, parses ENUM values and strips `/* */` comments.
- The heuristic can miss violations of library types that are not listed in `types`. This was chosen over false errors that block an agent.
- The `tc-naming-conventions` skill of the agent plugin must defer to `guidelines_get` (follow-up issue).
