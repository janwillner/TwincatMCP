# TwinCAT MCP Server

A [Model Context Protocol](https://modelcontextprotocol.io) server for **Beckhoff TwinCAT 3.1 Build 4026**. An AI agent such as Claude Code can use it to create, program, build, wire, activate and run a complete TwinCAT project without anyone clicking through the IDE.

The server works through two channels:

- **Automation Interface:** engineering in a TcXaeShell that the server starts or attaches to.
- **ADS:** runtime access to symbols, state, files and routes. This channel needs no IDE.

Actions on the PC's own TwinCAT system (`<Local>`) are refused unless the call passes `allowLocalSystem: true`.

**Rule for agents:** every action that concerns TwinCAT goes through this server's tools:

- Agents do not read or edit project files (`.tsproj`, `.TcPOU`, `.TcGVL`, …) directly.
- Agents do not control TcXaeShell, runtimes or ADS through their own scripts.
- If no tool fits a step, the agent tells the user instead of working around the server.

The server sends this rule to every MCP client in its instructions and in the `twincat://guide/workflow` guide.

## Installation

**Requirements**

- Windows 10/11 x64
- TwinCAT 3.1.4026 or 4024 XAE
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- git

The Automation Interface interop comes from the NuGet package `Beckhoff.TwinCAT.TCatSysManager` (4026.22, types embedded), so the build does not depend on the installed TwinCAT version.

**TwinCAT 3.1.4024**

The server also runs against a 4024 XAE, with limits. The 4024.79 Automation Interface ends at `ITcSysManager15` and `ITcPlcIECProject3`:

- `xae_start` picks `TcXaeShell.DTE.15.0` (the 4024 XAE Shell) when the 4026 shell is not registered.
- Tools that need a newer interface fail with "… is not supported by this TwinCAT XAE" and a hint that names the TwinCAT version needed: the SARIF export and the `.csa` settings file of `plc_run_static_analysis`, encrypted libraries (`archive_save_as_library`) and the axis Simulation mode (`motion_set_axis_simulation`).
- Build diagnostics of a German XAE are parsed too (4024 writes compiler messages without error codes).
- The Usermode Runtime (`rt_umrt_*`, TC1700) exists only from 4026 on.
- The ADS tools (`ads_*`, `route_*`) do not depend on the XAE version.

**1. Clone and build**

```powershell
git clone https://github.com/janwillner/TwincatMCP.git C:\Tools\TwincatMCP
cd C:\Tools\TwincatMCP
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
```

The server is built into `artifacts\twincat-mcp\`.

**2. Register with Claude Code**

```powershell
claude mcp add --scope user twincat -- dotnet C:\Tools\TwincatMCP\artifacts\twincat-mcp\TwinCatMcp.Server.dll
```

Restart Claude Code. `claude mcp get twincat` should then show `Connected`.

The `.mcp.json` in this repo only registers the server when Claude Code is started inside the repo folder.

**Other MCP clients** (such as Claude Desktop) need this entry in their configuration file:

```json
{
  "mcpServers": {
    "twincat": {
      "command": "dotnet",
      "args": ["C:\\Tools\\TwincatMCP\\artifacts\\twincat-mcp\\TwinCatMcp.Server.dll"]
    }
  }
}
```

Optional environment variables:

- `TWINCAT_MCP_LOGLEVEL`: `Trace` to `Warning`; logs go to stderr.
- `TWINCAT_MCP_DEBUG=1`: adds stack traces to unexpected errors.

**Update**

Run `git pull`, then run `scripts\publish.ps1` again.

## Commands

### Prompts

In Claude Code these appear as slash commands:

| Command | Purpose |
|---|---|
| `/mcp__twincat__create_twincat_project` | Create a complete project from a functional description (PLC code, tasks, I/O, build) |
| `/mcp__twincat__fix_build_errors` | Build and fix all compiler errors |
| `/mcp__twincat__deploy_to_target` | Activate the configuration on a target and download and start the PLC |
| `/mcp__twincat__add_ethercat_io` | Add an EtherCAT configuration and link it to PLC variables |

### Tools (201)

You don't need to call the tools yourself: describe the task and the agent picks them. A typical session starts with `xae_start` or `xae_attach`, followed by `solution_open` or `solution_create`.

| Area | Tools |
|---|---|
| IDE session | `xae_start` `xae_attach` `xae_close` `xae_status` `xae_list_instances` `xae_set_visible` `xae_set_silent_mode` `xae_execute_command` |
| Solution | `solution_create` `solution_open` `solution_save` `solution_close` `solution_list_projects` `solution_select_project` `solution_add_twincat_project` |
| Build | `build_solution` `build_project` `build_clean` `build_get_errors` `build_get_output` `build_list_configurations` `build_set_configuration` |
| System Manager tree | `tree_get_item` `tree_get_children` `tree_find` `tree_exists` `tree_create_child` `tree_delete_child` `tree_delete_item` `tree_rename_item` `tree_set_comment` `tree_set_disabled` `tree_produce_xml` `tree_consume_xml` `tree_export_child` `tree_import_child` |
| PLC project | `plc_create_project` `plc_delete_project` `plc_list_projects` `plc_find_elements` `plc_grep` `plc_create_folder` `plc_compile` `plc_check_all_objects` `plc_run_static_analysis` `plc_get_settings` `plc_set_settings` `plc_get_task_reference` `plc_set_task_reference` `plc_generate_boot_project` `plc_export_plcopen` `plc_import_plcopen` |
| POUs & code | `pou_create` `pou_create_method` `pou_create_property` `pou_create_action` `pou_create_transition` `pou_create_interface` `pou_create_dut` `pou_create_gvl` `pou_create_param_list` `pou_create_text_list` `pou_read_code` `pou_get_declaration` `pou_write_code` `pou_replace_code` `pou_multi_replace` `pou_grep` `pou_rename` `pou_move` `pou_delete` |
| Coding guidelines | `guidelines_get` `guidelines_check` |
| PLC online | `plc_online_login` `plc_online_logout` `plc_online_start` `plc_online_stop` `plc_online_reset` `plc_online_state` |
| Libraries | `lib_add` `lib_remove` `lib_list_references` `lib_add_placeholder` `lib_set_resolution` `lib_freeze_placeholders` `lib_get_declaration` `lib_get_signatures` `lib_list_installed` `lib_install` `lib_uninstall` `lib_scan` `lib_list_repositories` `lib_insert_repository` `lib_move_repository` `lib_remove_repository` `lib_get_project_info` `lib_set_project_info` |
| Archives & export | `archive_save_solution` `archive_open` `archive_export_plc_project` `archive_import_plc_project` `archive_save_as_library` |
| System & activation | `system_set_target` `system_get_target` `system_list_targets` `system_set_target_platform` `system_list_target_platforms` `system_activate_configuration` `system_restart` `system_set_run_mode` `system_set_config_mode` `system_get_state` `system_get_info` `system_get_versions` `system_pin_version` |
| Real-time & Usermode Runtime | `rt_get_settings` `rt_set_settings` `rt_umrt_list` `rt_umrt_create` `rt_umrt_start` `rt_umrt_stop` |
| Tasks | `task_list` `task_create` `task_configure` `task_delete` `task_link_plc` |
| Licenses | `license_list` `license_add` `license_activate_trial` |
| Project variants | `variant_list` `variant_define` `variant_get_current` `variant_set_current` |
| I/O devices | `io_add_device` `io_remove_device` `io_list_devices` `io_list_variables` `io_get_variable_info` `io_list_network_adapters` `io_set_device_adapter` `io_scan_devices` `io_scan_boxes` |
| EtherCAT | `ecat_add_box` `ecat_add_terminals` `ecat_search_catalog` `ecat_get_topology` `ecat_get_slave_info` `ecat_get_coe` `ecat_set_startup_parameter` |
| Variable mapping | `mapping_link` `mapping_unlink` `mapping_auto_link` `mapping_list_links` `mapping_check` `mapping_clear` `mapping_produce` `mapping_consume` |
| Motion (NC) | `motion_create_nc_task` `motion_set_nc_task` `motion_add_axis` `motion_remove_axis` `motion_list_axes` `motion_get_axis_parameters` `motion_set_axis_parameters` `motion_get_axis_xml` `motion_set_axis_simulation` `motion_link_axis_to_plc` `motion_unlink_axis_from_plc` `motion_link_axis_to_drive` `motion_find_plc_axis_refs` |
| TcCOM | `tccom_list_classes` `tccom_list_objects` `tccom_create_object` `tccom_delete_object` `tccom_get_parameters` `tccom_set_parameters` `tccom_set_context` |
| ADS online | `ads_connect` `ads_disconnect` `ads_list_connections` `ads_list_plc_runtimes` `ads_read_state` `ads_write_control` `ads_read_device_info` `ads_get_system_info` `ads_browse_symbols` `ads_get_symbol_info` `ads_read_symbols` `ads_write_symbols` `ads_monitor` `ads_wait_for_value` `ads_invoke_rpc` `ads_read_raw` `ads_write_raw` `ads_list_dir` `ads_read_file` `ads_write_file` `ads_delete_file` |
| ADS routes | `route_list` `route_add` `route_add_remote` `route_remove` `route_broadcast_search` |

Descriptions and parameters of every tool are in [docs/tools.md](docs/tools.md).

**No changes while logged in:** while the XAE is logged in to a PLC, nothing may be changed, built or activated. Every tool that is not read-only (except `plc_online_*`, `ads_*`, `route_*` and `xae_start` / `xae_attach` / `xae_set_visible` / `xae_set_silent_mode`) first logs the XAE out of all PLC projects. The result then contains an extra `autoLogout` entry listing them. The PLC keeps running.

The server also provides these resources:

- `twincat://guide/workflow`
- `twincat://guide/tree-paths`
- `twincat://guide/st-conventions` (the solution's style guide if it has coding guidelines)
- `twincat://guidelines/effective`
- `twincat://session/status`
- `twincat://tree/{path}`

## Coding guidelines

A solution can carry its own coding guidelines in `.twincat-mcp.json` next to the `.sln`. Without this file nothing is checked. With it, the `pou_*` write tools check every new or changed name and declaration:

- A rule with severity `error` rejects the write. Nothing is written, and `guidelineViolations` in the result lists the findings with a suggested name.
- A rule with severity `warn` lets the write through and reports the finding in `guidelineViolations`.
- New objects get the header template and required attributes automatically. They are listed in `guidelineFixes`.
- Existing code that already breaks a rule is only reported when it is changed.
- `guidelines_check` audits a whole project, for example after a PLCopen import, and can write a SARIF report.

Every deployment ships two example configs in `artifacts/twincat-mcp/guidelines/`:

| File | Content |
|---|---|
| `.twincat-mcp.Beckhoff.json` | Beckhoff Hungarian notation (`bEnable`, `astAxes`, `FB_Motor`, `E_State`) |
| `.twincat-mcp.Syntegon.json` | Syntegon TCAT guideline 2018 (`vi_b_Init`, `lr_TempAngle`, `_FB_..._Main`, copyright/help header, enum rules) |

Copy one next to your `.sln` as `.twincat-mcp.json`, or extend it and override single rules:

```json
{
  "$schema": "https://raw.githubusercontent.com/janwillner/TwincatMCP/main/schemas/twincat-mcp.schema.json",
  "extends": "builtin:syntegon",
  "exclude": ["ST_Visu*", "E_Visu*", "GVL_Visu"],
  "rules": { "naming.var.input": "error", "decl.method.inputDefaults": "error" }
}
```

A company base can also be a file path, such as `"extends": "../plc-standards/company.json"`.

In code, `{attribute 'naming' := 'off' | 'on' | 'omit'}` and `{attribute 'no-analysis'}` (the same pragmas as TE1200) and `{attribute 'mcp-ignore' := 'naming.var.input'}` exempt single declarations.

The format is described by [schemas/twincat-mcp.schema.json](schemas/twincat-mcp.schema.json), and the decisions behind it are in [docs/adr/0001-coding-guidelines-config.md](docs/adr/0001-coding-guidelines-config.md).

## License

MIT, see [LICENSE](LICENSE). The license covers this project's code only. Building it pulls third-party NuGet packages under their own licenses, among them Beckhoff `TwinCAT.Ads` and `TCatSysManager` (MIT) and Microsoft's Visual Studio interop assemblies (`envdte`), which may not be redistributed.
