using System.ComponentModel;
using ModelContextProtocol.Server;

namespace TwinCatMcp.Prompts;

/// <summary>Reusable workflow prompts that guide an LLM through multi-step TwinCAT tasks.</summary>
[McpServerPromptType]
public sealed class TwinCatPrompts
{
    [McpServerPrompt(Name = "create_twincat_project"), Description("Create a complete TwinCAT project from a functional description (PLC code, tasks, I/O, build).")]
    public static string CreateProject(
        [Description("What the machine/program should do")] string description,
        [Description("Parent directory for the solution")] string directory,
        [Description("Solution/project name")] string name)
        => $"""
            Create a complete TwinCAT 3.1.4026 project with the twincat-mcp tools.

            Functional description:
            {description}

            Steps:
            1. Read resource twincat://guide/workflow and twincat://guide/st-conventions.
            2. xae_start, then solution_create {"{"} directory: "{directory}", name: "{name}" {"}"}.
            3. plc_create_project, add required libraries (lib_add), look up library FB signatures with lib_get_declaration.
            4. Design the data model first (enums, structs, GVLs), then function blocks with methods/properties, then MAIN.
            5. Write the code, then build_solution and fix all errors until errorCount = 0.
            6. Configure tasks (task_configure) and, if I/O is described, add the EtherCAT configuration and link variables.
            7. solution_save and report the created objects and the remaining manual steps (license, target).
            """;

    [McpServerPrompt(Name = "fix_build_errors"), Description("Iteratively build the solution and fix all compiler errors.")]
    public static string FixBuildErrors()
        => """
            Run build_solution. For every error: read the affected POU with pou_read_code, understand the cause
            (unknown identifier -> check libraries with lib_list_references / lib_get_declaration; type mismatch -> fix declaration),
            apply minimal edits with pou_replace_code, and build again. Repeat until errorCount = 0, then summarize the fixes.
            Do not suppress errors with pragmas and do not delete functionality to make the build pass.
            """;

    [McpServerPrompt(Name = "deploy_to_target"), Description("Activate the configuration on a target and download/start the PLC.")]
    public static string DeployToTarget([Description("AMS Net ID of the target, e.g. 5.1.2.3.1.1")] string netId)
        => $"""
            Deploy the open TwinCAT project to target {netId}:
            1. build_solution – must have 0 errors.
            2. route_list – make sure a route to {netId} exists (route_add if not).
            3. system_set_target (target = "{netId}"), then license_list – a valid TC1200 license is required.
            4. system_activate_configuration (restart in run mode). If {netId} is this PC's own TwinCAT system, allowLocalSystem = true is required.
            5. plc_online_login (mode = "download"), plc_online_start, then ads_read_state (netId = "{netId}", port = 851) to verify the PLC is in Run.
            6. Optionally plc_set_settings (bootProjectAutostart = true) and plc_generate_boot_project (activate = true) so the program survives a restart.
            Report each step's result.
            """;

    [McpServerPrompt(Name = "add_ethercat_io"), Description("Add an offline EtherCAT configuration and link it to PLC variables.")]
    public static string AddEtherCatIo([Description("Terminal list, e.g. 'EK1100, 2x EL1008, EL2008, EL3102'")] string terminals)
        => $"""
            Add EtherCAT I/O: io_add_device (EtherCAT master), ecat_search_catalog to resolve product names,
            ecat_add_terminals with: {terminals}.
            Then create a GVL_IO with 'AT %I*' / 'AT %Q*' variables matching the channels, build_solution,
            and link every variable with mapping_link. Finish with mapping_list_links.
            """;
}
