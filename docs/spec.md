# TwinCAT MCP Server – Spec

Goal: an MCP server for **TwinCAT 3.1 Build 4026** with which an agent can create, program, build, download and manage a complete TwinCAT project without touching the IDE by hand.

Stack: C# / .NET 10 (`net10.0-windows`, x64), `ModelContextProtocol` 2.4.0 (stdio), TwinCAT Automation Interface (`TCatSysManagerLib` 3.4, embedded interop), EnvDTE 17.14, `Beckhoff.TwinCAT.Ads` 7.0.389.

## Architecture

| Piece | File | Rule |
|---|---|---|
| COM apartment | `Core/StaComThread.cs` | **Every** COM call (DTE, ITcSysManager, tree items) runs on this STA thread. Never touch COM objects from another thread. |
| XAE session | `Core/XaeSession.cs` | One IDE + selected `.tsproj`. Tools call `session.RunAsync(s => ..., timeout)`; it marshals to the STA, serializes the return value as JSON and converts exceptions into MCP error results incl. `GetLastErrorMessages()`. |
| ADS | `Ads/AdsSessionManager.cs` | Cached `AdsClient` per `netId:port`. ADS tools use `ads.RunAsync(...)` / `ads.Run(...)`. No STA needed. |
| Errors | `Core/TcException.cs`, `Core/ToolResult.cs` | Throw `TcException(message) { Hint = "how to recover" }` for expected failures. Never return raw stack traces. |
| Tree helpers | `Core/TreeInfo.cs` | `Describe`, `Children`, `Descendants`. |
| Lookup | `XaeSession.Lookup(path)` | Throws a helpful `TcException` when the path does not exist. |

## Tool conventions

- One `[McpServerToolType]` class per area in `Tools/<Area>Tools.cs`, primary-constructor DI (`XaeSession`, `AdsSessionManager`). Registration is automatic (`WithToolsFromAssembly`).
- Tool names: `snake_case`, **area prefix owned by exactly one work package** (table below). Never reuse another package's prefix.
- Every tool and every parameter gets a `[Description]` that tells an LLM *when* to use it, valid values and an example. Set `ReadOnly = true` / `Destructive = true` / `Idempotent = true` where it applies.
- Return a small anonymous object or record – it is serialized as camelCase JSON. Keep output compact (no giant XML unless asked for).
- Tree paths always use `^` and are full paths (`TIPC^Plc1^Plc1 Project^POUs^MAIN`).
- Prefer batch-capable signatures (`string[] paths`) where an agent typically needs several items.
- Long operations take `int timeoutSeconds` (default via `RunAsync`).
- Domain logic that is pure (text editing, XML building, value conversion) lives in a separate static/helper class so it can be unit tested without COM.

## Testing (TDD seams – agreed)

- **Seam A – MCP tool surface (integration):** `tests/TwinCatMcp.Tests/Integration`. Tests talk to the real server over stdio via `McpServerFixture` / `XaeProjectFixture` (starts TcXaeShell, creates solution `McpIt`). Mark with `[Trait("Category","Integration")]`. Each package adds its own test class(es); if a package needs a different setup it creates its own fixture subclass + `[CollectionDefinition]`.
- **Seam B – pure logic (unit):** `tests/TwinCatMcp.Tests/Unit`. Fast, no COM.
- Red → green, one vertical slice at a time. Expected values come from TwinCAT docs / known-good literals, never recomputed the way the code does.
- Run a single test class with `dotnet test tests/TwinCatMcp.Tests --filter "FullyQualifiedName~<Class>"`.

## Work packages

| # | Package | Prefixes | Scope |
|---|---|---|---|
| 0 | Core (lead) | `xae_`, `solution_`, `tree_`, `build_` | done in skeleton |
| 1 | PLC project & code | `plc_`, `pou_` | PLC project create (Standard/Empty template), find elements, read/write/replace/grep ST code (batch), create POU/FB/FUNCTION/PROGRAM (language, return type, EXTENDS/IMPLEMENTS), DUT (STRUCT/ENUM/UNION/ALIAS), GVL, INTERFACE, METHOD, PROPERTY (Get/Set), ACTION, TRANSITION, folders, delete/rename/move, PLCopenXML import/export, check all objects, static analysis, PLC project settings (boot project autostart, generate boot project), PLC task reference |
| 2 | Libraries & packaging | `lib_`, `archive_` | references list/add/remove, placeholders, effective resolution, freeze, repositories, install/uninstall, scan, library IEC declaration/signatures, save as library / compiled library, solution archive (.tnzip) / SaveAsArchive, export of whole PLC project |
| 3 | System, runtime & deployment | `system_`, `task_`, `rt_`, `license_`, `plc_online_`, `variant_` | target Net ID, real-time settings (cores, base time), tasks (create, cycle, priority, core), link PLC to task, activate configuration, restart TwinCAT, config/run mode, real-time status, licenses (list/add), PLC online via XAE (login w/ download/online change, logout, start, stop, reset cold/origin, state), boot project, project variants, TwinCAT version pinning, **Usermode Runtime setup for tests**. Only this package may change the local TwinCAT system state. |
| 4 | I/O & EtherCAT | `io_`, `ecat_`, `mapping_` | devices (EtherCAT master, virtual/other), boxes/terminals by product type, ESI catalog search, scan (where hardware exists), network adapter, process image variables, LinkVariables/UnlinkVariables, mapping info produce/consume/clear, CoE / startup parameters, EtherCAT slave identity |
| 5 | Motion & TcCOM | `motion_`, `tccom_` | NC task, axes (create, type, parameters via XML), encoder/drive, link axis to I/O drive and to PLC `AXIS_REF`, TcCOM module instances (create, parameters, task context) |
| 6 | ADS online | `ads_`, `route_` | connect/state/device info, set ADS state, read/write symbols (typed, structs/arrays as JSON), browse symbols, sum read/write, index group/offset access, RPC methods, sampling/monitor over time, routes (list/add), file access via system service, TwinCAT/runtime info |

## Constraints for parallel work

- Work only inside your package's files: `Tools/<Area>Tools.cs`, `<Area>/**` helpers, `tests/**/<Area>*`. Do not edit shared core files; if core needs a change, describe it in your final report (the lead integrates).
- Only package 3 may activate configurations, restart TwinCAT or switch Config/Run mode on this PC.
- Start IDE instances only through the test fixtures (they quit their own instance). Never kill TcXaeShell processes you did not start.
- Research Beckhoff documentation (InfoSys) instead of guessing; the reflected API of this exact installation is in `docs/reference/TCatSysManagerLib-api.txt`.
