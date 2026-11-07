namespace TwinCatMcp.Core;

public static class ServerInstructions
{
    public const string Text = """
        TwinCAT 3.1 (Build 4026) engineering + runtime MCP server.

        Two independent channels:
        1. XAE / Automation Interface (engineering): tools prefixed xae_, solution_, tree_, plc_, pou_, lib_, task_, io_, ecat_, motion_, system_, license_, route_.
           Requires an IDE session: xae_start (new TcXaeShell) or xae_attach (running IDE), then solution_create or solution_open.
        2. ADS (online/runtime, no IDE needed): tools prefixed ads_. Address = AMS Net ID (local: "127.0.0.1.1.1" or the local Net ID) + port (PLC1 = 851, system service = 10000, NC = 500).

        Tree paths use '^' as separator, e.g. "TIPC^MyPlc^MyPlc Project^POUs^MAIN".
        Root shortcuts: TIRC real-time config, TIRT tasks, TIRS real-time settings, TIPC PLC, TINC motion (NC), TIID I/O devices, TIRR routes, TIAO additional tasks.

        Typical workflow for a complete project:
        xae_start -> solution_create -> plc_create_project -> pou_create / pou_write_code (ST) -> lib_add -> task_* -> build_solution (check errors)
        -> system_set_target -> system_activate_configuration (switches target to Run mode) -> plc_login (download) -> plc_start -> ads_read_symbol / ads_write_symbol.

        Every tool returns JSON. Errors come back with isError=true, a message, an optional hint and the TwinCAT error log.
        After changing ST code always run build_solution (or plc_check_all_objects) and fix reported errors before activating.
        """;
}
