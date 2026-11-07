namespace TwinCatMcp.Resources;

public static class Guides
{
    public const string Workflow = """
        # TwinCAT MCP – complete project workflow

        ## 1. Engineering session
        1. `xae_start` (new TcXaeShell 4026, hidden) or `xae_attach` (running IDE).
        2. `solution_create { directory, name }` – or `solution_open { path }` for an existing .sln/.tsproj.

        ## 2. PLC project and code
        3. `plc_create_project { name }` (Standard PLC Template: MAIN + PlcTask).
        4. `lib_add` for required libraries (e.g. Tc2_Standard, Tc2_Utilities, Tc2_MC2, Tc3_EventLogger). Look up FB signatures with `lib_get_declaration`.
        5. Create objects: `pou_create_dut` (ST_/E_), `pou_create_gvl` (GVL_), `pou_create_interface` (I_), `pou_create` (FB_/F_/PRG), `pou_create_method`, `pou_create_property`.
        6. Write code with `pou_write_code` (complete text) or `pou_replace_code` (targeted edits). Read with `pou_read_code`, search with `plc_grep`.
        7. `build_solution` – fix every error (`build_get_errors`), repeat until errorCount = 0.

        ## 3. Real-time, I/O and motion (optional)
        8. Tasks: `task_list`, `task_configure` (cycle time, priority), `task_link_plc`.
        9. I/O: `io_add_device` (EtherCAT master), `ecat_add_terminals` (EK1100, EL1008 ...), or `io_scan_devices` / `io_scan_boxes` with real hardware; link PLC `AT %I*/%Q*` variables with `mapping_link`.
        10. Motion: `motion_create_nc_task`, `motion_add_axis`, `motion_set_axis_parameters`, `motion_link_axis_to_drive`, `motion_link_axis_to_plc`.

        ## 4. Deploy
        11. `system_set_target { target }` – a route name or AMS Net ID (route must exist: `route_list`, `route_add`). Activating/writing on this PC's own TwinCAT system additionally needs `allowLocalSystem: true`.
        12. `license_list` – a TC1200 (PLC) license (trial or real) must exist on the target.
        13. `system_activate_configuration` → target switches to Run mode.
        14. `plc_online_login` (download), `plc_online_start`; optional boot project: `plc_set_settings { bootProjectAutostart: true }` + `plc_generate_boot_project { activate: true }`.
        15. Verify online with ADS: `ads_read_state`, `ads_browse_symbols`, `ads_read_symbols`, `ads_write_symbols`, `ads_monitor`.

        ## 5. Manage
        - Save: `solution_save`; archive: `archive_save_solution`; export: `plc_export_plcopen`.
        - Changes while running: edit code → `build_solution` → `plc_online_login { mode: onlineChange }`.
        - Errors always contain `hint` – follow it.
        """;

    public const string TreePaths = """
        # System Manager tree paths

        Separator is `^`. Paths are case-sensitive names as shown in the solution explorer.

        | Shortcut | Node |
        |---|---|
        | `TIRC` | SYSTEM – real-time configuration root |
        | `TIRS` | SYSTEM ^ Real-Time settings |
        | `TIRT` | SYSTEM ^ Tasks |
        | `TIRR` | SYSTEM ^ Routes |
        | `TIAO` | SYSTEM ^ Additional tasks / TcCOM objects |
        | `TINC` | MOTION (NC configuration) |
        | `TIPC` | PLC configuration root |
        | `TIID` | I/O ^ Devices |
        | `TIIC` | I/O configuration |

        ## Common paths
        - PLC project (nested project root): `TIPC^<Plc>^<Plc> Project`
        - POUs folder: `TIPC^<Plc>^<Plc> Project^POUs`
        - MAIN program: `TIPC^<Plc>^<Plc> Project^POUs^MAIN`
        - Method of an FB: `TIPC^<Plc>^<Plc> Project^POUs^FB_Motor^M_Start`
        - Library references: `TIPC^<Plc>^<Plc> Project^References`
        - PLC instance (process image for linking): `TIPC^<Plc>^<Plc> Instance`
        - PLC task inputs: `TIPC^<Plc>^<Plc> Instance^PlcTask Inputs^GVL_IO.bSensor`
        - Real-time task: `TIRT^PlcTask`
        - EtherCAT master: `TIID^Device 1 (EtherCAT)`
        - Terminal channel: `TIID^Device 1 (EtherCAT)^Term 1 (EK1100)^Term 2 (EL1008)^Channel 1^Input`
        - NC axis: `TINC^NC-Task 1 SAF^Axes^Axis 1`

        Use `tree_get_children`, `tree_find` or `plc_find_elements` instead of guessing.
        """;

    public const string StConventions = """
        # Structured Text conventions (Beckhoff style)

        | Object | Prefix | Example |
        |---|---|---|
        | Program | `PRG_` or `MAIN` | `PRG_Conveyor` |
        | Function block | `FB_` | `FB_Motor` |
        | Function | `F_` | `F_Scale` |
        | Interface | `I_` | `I_Drive` |
        | Struct | `ST_` | `ST_MotorData` |
        | Enum | `E_` | `E_MachineState` |
        | Union / Alias | `U_` / `T_` | `T_Speed` |
        | GVL | `GVL_` | `GVL_IO` |
        | Method / Property | `M_` / `P_` (optional) | `M_Start`, `P_IsReady` |

        Variable prefixes: `b` BOOL, `n` integers (BYTE/WORD/INT/DINT/UDINT...), `f` REAL/LREAL, `s` STRING, `ws` WSTRING,
        `t` TIME, `d` DATE/DT, `st` struct, `e` enum, `fb` FB instance, `a` array, `p` POINTER, `ref` REFERENCE, `i` interface, `ax` AXIS_REF.
        Scope: `g` globals optional in GVL; inputs/outputs carry no extra prefix.

        - Use `{attribute 'qualified_only'}` on GVLs and `{attribute 'strict'}` on enums.
        - Hardware I/O: `bSensor AT %I* : BOOL;` / `bValve AT %Q* : BOOL;` then link with `mapping_link`.
        - Prefer state machines with `CASE eState OF` and an enum.
        - Methods return explicit values; avoid side effects in properties.
        """;
}
