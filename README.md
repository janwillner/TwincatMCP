# TwinCAT MCP Server

A [Model Context Protocol](https://modelcontextprotocol.io) server for **Beckhoff TwinCAT 3.1 Build 4026**. With it, an AI agent (Claude Code, Claude Desktop, any MCP client) can create, program, build, wire, download and manage a complete TwinCAT project. Nobody has to click through the IDE.

- **201 tools** in 21 areas: IDE session, solution, build, the System Manager tree, PLC projects & ST code, libraries, archives, real-time & tasks, licenses, activation, PLC online, project variants, I/O devices, EtherCAT, variable mapping, motion (NC), TcCOM, ADS online and ADS routes.
- **Two channels:**
  - **Automation Interface** (engineering, through a TcXaeShell started or attached by the server).
  - **ADS** (runtime: symbols, state, files and routes; needs no IDE).
- **Resources:** workflow guide, tree-path cheat sheet, ST naming conventions, live session status and tree view.
- **Prompts:** create a project, fix build errors, deploy to a target, add EtherCAT I/O.

The full tool list is in [docs/tools.md](docs/tools.md). It is generated from the code.

## Requirements

| Component | Version |
|---|---|
| Windows 10/11 x64 | |
| TwinCAT 3.1 XAE | 4026.x (TcXaeShell64, `TcXaeShell.DTE.17.0`) |
| .NET SDK / runtime | 10.0 |
| Target for deployment | any TwinCAT 3.1 runtime reachable via ADS, e.g. a local **Usermode Runtime** |

The server references the Automation Interface (`TCatSysManagerLib` 3.4) installed with TwinCAT; its interop types are embedded. It uses `Beckhoff.TwinCAT.Ads` 7.0.389 from nuget.org.

## Build & register

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1   # -> artifacts\twincat-mcp\
```

`.mcp.json` in the repo root registers the server for Claude Code as **`twincat`**: `dotnet artifacts/twincat-mcp/TwinCatMcp.Server.dll`.

For other MCP clients:

```json
{
  "mcpServers": {
    "twincat": {
      "command": "dotnet",
      "args": ["C:\\path\\to\\TwincatMCP\\artifacts\\twincat-mcp\\TwinCatMcp.Server.dll"]
    }
  }
}
```

| Environment variable | Effect |
|---|---|
| `TWINCAT_MCP_LOGLEVEL` | `Trace` … `Warning` (logs go to stderr) |
| `TWINCAT_MCP_DEBUG=1` | adds stack traces to unexpected tool errors |

## A complete project in one session

```text
xae_start                                   -> dedicated, hidden TcXaeShell process
solution_create  { directory, name }
plc_create_project { name: "Plc1" }
lib_add { libraries: ["Tc2_MC2"] }          ; lib_get_declaration { objects: ["MC_Power"] }
pou_create_dut / pou_create_gvl / pou_create / pou_create_method / pou_write_code
build_solution                              -> errors with code, object and line
task_configure { task: "PlcTask", cycleTime: 5, unit: "ms" }
io_add_device + ecat_add_terminals ["EK1100","EL1008","EL2008"] + mapping_link
motion_create_nc_task + motion_add_axis + motion_link_axis_to_plc
system_set_target -> system_activate_configuration -> plc_online_login -> plc_online_start
ads_read_symbols / ads_write_symbols / ads_monitor
solution_save / archive_save_solution
```

The acceptance test [CompleteProjectTests](tests/TwinCatMcp.Tests/Integration/CompleteProjectTests.cs) runs exactly this flow (minus deployment) against a live TcXaeShell. It only uses MCP calls.

## Design notes

- **One COM apartment.** Every Automation Interface call runs on a dedicated STA thread with a message pump and an OLE message filter. The filter retries "application busy".
- **Own IDE vs. attached IDE.**
  - `xae_start` lets Windows start a new TcXaeShell by COM activation and identifies its process by the main window. `launchMode: "process"` starts the executable itself (`-Embedding`) and attaches by process ID.
  - The server only quits IDEs it started.
  - `xae_attach` connects to a running IDE such as the user's own. On `xae_close` that IDE gets back the SilentMode it had before.
- **Restarts are really awaited.** Activating, restarting and switching to Run mode wait until the target has left Run and come back. Right after the request the target still reports the old Run state. `plc_online_login` checks that the login really happened and retries while the XAE catches up with a restarted runtime.
- **The own PC is protected.** A new TwinCAT project targets `<Local>`, which is the PC's own TwinCAT system and may run another application. Activating, restarting, switching mode, PLC login/start/stop/reset and writing ADS tools refuse to act on it unless the call passes `allowLocalSystem: true`. Set a dedicated target with `system_set_target` instead (for example a Usermode Runtime instance from `rt_umrt_list`). Customer ADS servers (port ≥ 25000) are exempt.
- **Errors are actionable.** Every error is JSON with `error`, an optional `hint` and the TwinCAT error log. Open modal IDE dialogs (for example *license not found*) are returned as `xaeDialogs`. Misspelled or missing arguments are explained, with suggestions and the expected parameters.
- **Hidden IDE quirks are handled:**
  - The Error List stays empty in a hidden IDE, so build diagnostics are parsed from the Build pane.
  - Solutions load asynchronously; `solution_open` waits until the nested PLC projects are ready.
- **TwinCAT 4026 specifics:** these were verified live and documented by each work package in its code and tests:
  - CreateChild sub types and vInfo formats
  - the link path syntax
  - the CoE startup XML
  - the axis simulation flag
  - UmRT handling

Architecture and conventions are in [docs/spec.md](docs/spec.md). The reflected Automation Interface of TwinCAT 4026.22 is in [docs/reference/TCatSysManagerLib-api.txt](docs/reference/TCatSysManagerLib-api.txt).

## Tests

```powershell
dotnet test tests/TwinCatMcp.Tests --filter "FullyQualifiedName~Unit"   # pure logic, seconds
dotnet test tests/TwinCatMcp.Tests                                       # + live TcXaeShell integration
```

- **Unit tests** cover the pure logic: ST text editing, XML builders and parsers, the ESI catalog, ADS value conversion, protocol packets, and build-output parsing.
- **Integration tests** start the real server over stdio and drive a real TcXaeShell 4026. The ADS tests use an in-process symbolic ADS server and the local system service.
- **Runtime tests** (activation, download, PLC start/stop, live symbols) are skipped until a licensed target exists. See [docs/runtime-setup.md](docs/runtime-setup.md):

```powershell
$env:TWINCAT_MCP_RUNTIME_TESTS = '1'
dotnet test tests/TwinCatMcp.Tests --filter "Category=Runtime"
```

## Known limitations

- **No trial license by automation.** A 7-day trial license cannot be generated through automation (captcha). `license_activate_trial` prepares the IDE and lists the manual steps.
- **No hardware tests.** No EtherCAT hardware or drives were attached during development. Scans, online CoE and adapter binding to real NICs fail cleanly but are not verified against real devices.
- **Library declarations:** `lib_get_declaration` builds declarations from library signatures. They contain VAR blocks only, without struct members or FB methods.
- **Unused objects are not compiled:** objects that nothing references are skipped by `build_solution`. Use `plc_check_all_objects` for those.
- **ITcSmCommands is not available** in TcXaeShell 4026 automation. Config/Run mode switching goes through ADS instead.
- **Variant management cannot be removed again.** `variant_define` needs at least one variant.
- **File tools on a Usermode Runtime** run under its service account. It cannot see user profile folders, so use for example `C:\ProgramData\...` or the `boot` root.
- **Interface property accessors have no code.** Reading their code crashes TcXaeShell 4026, so the code tools skip them.
