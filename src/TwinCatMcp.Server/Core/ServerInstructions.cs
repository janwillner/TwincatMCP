namespace TwinCatMcp.Core;

public static class ServerInstructions
{
    public const string Text = """
        TwinCAT 3.1 (Build 4026) engineering + runtime MCP server.

        Two independent channels:
        1. XAE / Automation Interface (engineering): tools prefixed xae_, solution_, tree_, build_, plc_, pou_, lib_, archive_, task_, rt_, io_, ecat_, mapping_, motion_, tccom_, system_, license_, variant_.
           Requires an IDE session: xae_start (new TcXaeShell) or xae_attach (running IDE), then solution_create or solution_open.
        2. ADS (online/runtime, no IDE needed): tools prefixed ads_ and route_. Address = AMS Net ID ("local" = this PC) + port (PLC1 = 851, system service = 10000, NC = 500).

        Tree paths use '^' as separator, e.g. "TIPC^MyPlc^MyPlc Project^POUs^MAIN".
        Root shortcuts: TIRC real-time config, TIRT tasks, TIRS real-time settings, TIPC PLC, TINC motion (NC), TIID I/O devices, TIRR routes, TIAO additional tasks.

        Typical workflow for a complete project:
        xae_start -> solution_create -> plc_create_project -> pou_create / pou_write_code (ST) -> lib_add -> task_* -> build_solution (check errors)
        -> system_set_target -> system_activate_configuration (switches target to Run mode) -> plc_online_login (download) -> plc_online_start
        -> ads_browse_symbols / ads_read_symbols / ads_write_symbols / ads_wait_for_value / ads_monitor.

        I/O: io_add_device (ethercat-master) -> ecat_search_catalog / ecat_add_terminals ["EK1100","EL1008"] -> GVL with AT %I*/%Q* ({attribute 'linkalways'} or used in code) -> build_solution -> mapping_link.
        Motion: motion_create_nc_task -> motion_add_axis -> motion_set_axis_parameters -> motion_link_axis_to_drive (I/O) and motion_link_axis_to_plc (AXIS_REF from Tc2_MC2, must be used in code) -> build_solution.
        Libraries: lib_list_installed (catalog) -> lib_add ["Tc2_MC2"] -> lib_get_declaration ["MC_MoveAbsolute"] to see FB interfaces before using them.
        Packaging: lib_set_project_info -> archive_save_as_library; archive_save_solution (.tnzip) / archive_open.

        Every tool returns JSON. Errors come back with isError=true, a message, an optional hint and the TwinCAT error log.
        After changing ST code always run build_solution (or plc_check_all_objects) and fix reported errors before activating.
        """;
}
