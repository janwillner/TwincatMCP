# Tool catalog

Generated from the server assembly – 203 tools. Regenerate with `TwinCatMcp.Server.exe --tool-catalog > docs/tools.md`.

## IDE session (`xae_*`, 8)

| Tool | Description | Parameters |
|---|---|---|
| `xae_attach` | Attaches to an already running Visual Studio / TcXaeShell instance (Running Object Table). The first TwinCAT project of its solution is selected automatically. | `solutionPathOrProcessId`? |
| `xae_close` | Disconnects from the IDE. An IDE started by xae_start is closed; an attached IDE is only closed when quitIde=true. | `saveAll`?, `quitIde`? |
| `xae_execute_command` | Executes a Visual Studio IDE command by name (e.g. 'File.SaveAll', 'Build.RebuildSolution'). Advanced escape hatch. | `command`, `args`? |
| `xae_list_instances` *(read-only)* | Lists running Visual Studio / TcXaeShell instances with their open solution. |  |
| `xae_set_silent_mode` | Enables/disables the Automation Interface SilentMode (suppresses message boxes in the IDE). Enabled by default. | `silent` |
| `xae_set_visible` | Shows or hides the IDE main window. | `visible` |
| `xae_start` | Starts a new TwinCAT XAE IDE instance (TcXaeShell 4026 by default) and connects to it. Takes 20-60 s. Dialogs are suppressed (Automation Interface SilentMode). | `progId`?, `visible`?, `keepOpen`?, `launchMode`? |
| `xae_status` *(read-only)* | Shows the connected IDE, open solution, selected TwinCAT project and target AMS Net ID. |  |

## Solution (`solution_*`, 7)

| Tool | Description | Parameters |
|---|---|---|
| `solution_add_twincat_project` | Adds another TwinCAT XAE project (.tsproj) to the open solution and selects it. | `name` |
| `solution_close` | Closes the open solution. | `save`? |
| `solution_create` | Creates a new solution with an empty TwinCAT XAE project (<directory>\<name>\<name>.sln and <name>\<name>.tsproj). Next step usually: plc_create_project. | `directory`, `name`, `overwrite`? |
| `solution_list_projects` *(read-only)* | Lists all projects in the solution (TwinCAT and others) and marks the selected one. |  |
| `solution_open` | Opens an existing .sln (or a standalone .tsproj) and selects its first TwinCAT project. | `path` |
| `solution_save` | Saves all projects and the solution. |  |
| `solution_select_project` | Selects which TwinCAT project (.tsproj) subsequent tools operate on. | `name` |

## Build (`build_*`, 7)

| Tool | Description | Parameters |
|---|---|---|
| `build_clean` | Cleans the solution. |  |
| `build_get_errors` *(read-only)* | Returns the diagnostics of the last build without building again (Error List, or the last build in the Build output pane when the Error List is empty). | `includeWarnings`?, `includeMessages`? |
| `build_get_output` *(read-only)* | Returns the text of an Output window pane (default 'Build'), optionally only the last N lines. | `pane`?, `lastLines`? |
| `build_list_configurations` *(read-only)* | Lists solution configurations/platforms and the active one. |  |
| `build_project` | Builds a single project of the solution (e.g. a PLC project's .plcproj) by its unique name or file name. | `project`, `configuration`?, `includeWarnings`?, `timeoutSeconds`? |
| `build_set_configuration` | Activates a solution configuration, e.g. name='Release', platform='TwinCAT RT (x64)'. | `name`, `platform`? |
| `build_solution` | Builds (or rebuilds) the whole solution incl. all PLC projects and returns the compiler errors/warnings (code, object, file, line, message). This is the authoritative compile check after editing ST code. Note: objects that are not referenced anywhere are not compiled – use plc_check_all_objects for those. | `rebuild`?, `includeWarnings`?, `includeMessages`?, `timeoutSeconds`? |

## System Manager tree (generic) (`tree_*`, 14)

| Tool | Description | Parameters |
|---|---|---|
| `tree_consume_xml` | Applies XML to a tree item (ITcSmTreeItem.ConsumeXml). Only the elements contained in the XML are changed, so a minimal '<TreeItem>...</TreeItem>' fragment with just the changed parameters is enough. | `treePath`, `xml` |
| `tree_create_child` | Creates a child item below a tree item (ITcSmTreeItem.CreateChild). The meaning of subType and info depends on the parent: e.g. TIRT -> subType 0 = task; TIID -> subType 111 = EtherCAT master; EtherCAT master -> subType 9099 + info 'EL1008' = terminal; TINC -> subType 1 = NC task; PLC folder -> see plc_/pou_ tools which wrap this. | `parentPath`, `name`, `subType`?, `before`?, `info`? |
| `tree_delete_child` | Deletes a child item by name below the given parent. | `parentPath`, `childName` |
| `tree_delete_item` | Deletes the tree item at the given path. | `treePath` |
| `tree_exists` *(read-only)* | Checks whether one or more tree paths exist. | `treePaths` |
| `tree_export_child` | Exports a child item to a file (.xti for I/O/tasks/NC, .tcpou etc.). | `parentPath`, `childName`, `filePath` |
| `tree_find` *(read-only)* | Searches the tree below a root for items whose name contains the pattern (case-insensitive). Returns their paths. | `rootPath`, `namePattern`?, `maxDepth`?, `maxResults`? |
| `tree_get_children` *(read-only)* | Lists the direct children of one or more tree items. Use the roots TIRC, TIRT, TIRS, TIPC, TINC, TIID, TIRR to explore a project. | `treePaths` |
| `tree_get_item` *(read-only)* | Describes a tree item (name, full path, item type, sub type, child count). Optionally includes children up to 'depth' levels. | `treePath`, `depth`? |
| `tree_import_child` | Imports a child item from a file (.xti, ...) below the given parent. | `parentPath`, `filePath`, `name`?, `before`?, `reconnect`? |
| `tree_produce_xml` *(read-only)* | Returns the XML description of a tree item (ITcSmTreeItem.ProduceXml). This exposes all parameters of tasks, devices, terminals, axes, PLC projects etc. Modify the XML and send it back with tree_consume_xml. | `treePath`, `recursive`? |
| `tree_rename_item` | Renames a tree item. | `treePath`, `newName` |
| `tree_set_comment` | Sets the comment of a tree item. | `treePath`, `comment` |
| `tree_set_disabled` | Disables or enables a tree item (e.g. an I/O device, box, task or axis). | `treePath`, `disabled` |

## PLC online (XAE) (`plc_online_*`, 8)

| Tool | Description | Parameters |
|---|---|---|
| `plc_online_generate_boot_project` | Generates the PLC boot project (ITcPlcProject.GenerateBootProject). activate=true writes it to the target so the PLC starts from it after a TwinCAT restart (with autostart, see plc_online_set_autostart). | `plcProject`?, `activate`?, `timeoutSeconds`? |
| `plc_online_login` | Logs the XAE into the PLC runtime of the target and downloads the application if needed (no dialogs). mode: 'auto' = online change when possible, otherwise download; 'download' = full download (PLC goes to Stop, values reset); 'onlineChange' = only online change. The target must be in Run mode (system_activate_configuration). Start the PLC with plc_online_start. | `plcProject`?, `mode`?, `rebuild`?, `timeoutSeconds`? |
| `plc_online_logout` | Logs the XAE out of the PLC runtime (the PLC keeps running). | `plcProject`? |
| `plc_online_reset` | Resets the PLC application (requires plc_online_login): 'cold' = all variables except RETAIN/PERSISTENT to initial values, 'origin' = everything incl. RETAIN/PERSISTENT and the application is removed from the runtime (login with download afterwards). | `type`?, `plcProject`? |
| `plc_online_set_autostart` | Enables/disables 'Autostart Boot Project' of a PLC project (ITcPlcProject.BootProjectAutostart). Takes effect with the next activation / boot project. | `enabled`, `plcProject`? |
| `plc_online_start` | Starts the PLC application (requires plc_online_login). | `plcProject`? |
| `plc_online_state` *(read-only)* | Returns the online state of the PLC project in the XAE: logged in, application state (Run/Stop/...), operation state flags (e.g. BootProjectValid, Exception) and the boot project autostart setting. | `plcProject`? |
| `plc_online_stop` | Stops the PLC application (requires plc_online_login). | `plcProject`? |

## PLC project (`plc_*`, 16)

| Tool | Description | Parameters |
|---|---|---|
| `plc_check_all_objects` | Runs 'Check all objects' on a PLC project (ITcPlcIECProject2.CheckAllObjects - compiles every object, also unused ones) and returns ok + compiler errors/warnings (object, code, message). Unlike build_solution it also finds errors in objects that are not used by any task. Use it after every code change. | `project`?, `includeWarnings`?, `includeOutput`?, `timeoutSeconds`? |
| `plc_compile` | Compiles one PLC project (ITcPlcProject.CompileProject, like 'Build <Plc>' - only objects used by tasks) and returns compiler errors/warnings. | `project`?, `includeWarnings`?, `includeOutput`?, `timeoutSeconds`? |
| `plc_create_folder` | Creates a folder in a PLC project, e.g. parentPath='TIPC^Plc1^Plc1 Project^POUs', name='Motion'. name may contain '^' to create nested folders ('Motion^Axes'); existing folders are reused. | `parentPath`, `name` |
| `plc_create_project` | Creates a new PLC project below TIPC from a Beckhoff template (or adds an existing .plcproj / .tpzip). Returns rootPath ('TIPC^Plc1'), projectPath (nested project 'TIPC^Plc1^Plc1 Project' - the parent of POUs/DUTs/GVLs) and instancePath. The Standard template contains MAIN (ST) linked to PlcTask. Next: pou_create / pou_write_code, then build_solution. | `name`, `template`?, `timeoutSeconds`? |
| `plc_delete_project` | Removes a PLC project from the TwinCAT project (files stay on disk). | `project` |
| `plc_export_plcopen` *(read-only)* | Exports PLC objects as PLCopen XML (ITcPlcIECProject.PlcOpenExport). Select top-level objects or folders by tree path (methods/properties/actions are exported with their POU). Empty selection = whole project. | `filePath`, `selection`?, `project`?, `overwrite`?, `timeoutSeconds`? |
| `plc_find_elements` *(read-only)* | Finds PLC objects (POUs, DUTs, GVLs, interfaces, methods, properties, actions, transitions, folders) with kind and full tree path. Use it to discover paths for pou_read_code / pou_write_code. Library references are not listed. | `projects`?, `kinds`?, `namePattern`?, `maxResults`? |
| `plc_generate_boot_project` | Builds the boot project of a PLC project for the current target (ITcPlcProject.GenerateBootProject). activate=true also activates it (it then starts with TwinCAT when bootProjectAutostart is set). | `project`?, `activate`?, `timeoutSeconds`? |
| `plc_get_settings` *(read-only)* | Reads PLC project settings of the root node (ITcPlcProject): bootProjectAutostart, bootProjectEncryption (None\|Target\|Volume), tmcFileCopy. | `project`? |
| `plc_get_task_reference` *(read-only)* | Lists the PLC task references of a project (e.g. 'TIPC^Plc1^Plc1 Project^PlcTask') and the real-time task each is linked to ('TIRT^PlcTask'). | `project`? |
| `plc_grep` *(read-only)* | Regex search over the declaration and implementation of all code objects of a PLC project (POUs, methods, properties, actions, transitions, DUTs, GVLs, interfaces). Returns path, section, 1-based line number and the line. Graphical languages are searched in their XML. | `pattern`, `project`?, `ignoreCase`?, `kinds`?, `section`?, `maxResults`? |
| `plc_import_plcopen` | Imports PLCopen XML into a PLC project (ITcPlcIECProject.PlcOpenImport). conflict: error (default) \| replace \| rename (creates X_1) \| skip. With targetFolder the imported top-level objects are placed into that folder (e.g. 'POUs^Imported'), otherwise the folder structure stored in the file is used relative to the project root. | `filePath`, `project`?, `conflict`?, `targetFolder`?, `keepFolderStructure`?, `selection`?, `timeoutSeconds`? |
| `plc_list_projects` *(read-only)* | Lists all PLC projects with rootPath ('TIPC^Plc1', boot/settings node), projectPath ('TIPC^Plc1^Plc1 Project', parent of POUs/DUTs/GVLs - use it for code tools) and instancePath. Also shows the task references (PlcTask -> linked TIRT task). |  |
| `plc_run_static_analysis` | Runs TwinCAT Static Analysis (TE1200; without license only the light rules) on a PLC project and returns the findings (rule violations are warnings 'SAxxxx'). Optionally writes a SARIF report (TwinCAT >= 3.1.4026.20). | `project`?, `checkAll`?, `sarifPath`?, `settingsFile`?, `includeOutput`?, `timeoutSeconds`? |
| `plc_set_settings` | Changes PLC project settings (only the given values): bootProjectAutostart (start the boot project when TwinCAT starts), bootProjectEncryption (None\|Target\|Volume), tmcFileCopy (copy the .tmc to the target). | `project`?, `bootProjectAutostart`?, `bootProjectEncryption`?, `tmcFileCopy`? |
| `plc_set_task_reference` | Links a PLC task reference to a real-time task (ITcPlcTaskReference.LinkedTask), e.g. linkedTask='TIRT^FastTask'. Create real-time tasks with the task_ tools. | `linkedTask`, `project`?, `taskReference`? |

## POUs & code (`pou_*`, 19)

| Tool | Description | Parameters |
|---|---|---|
| `pou_create` | Creates a PROGRAM, FUNCTION_BLOCK or FUNCTION. Language ST (default), IL, LD, FBD, SFC or CFC. FUNCTION needs returnType. FUNCTION_BLOCK/PROGRAM may use extends (base FB) and implements (interfaces, comma separated). Optional declaration/implementation are written right after creation (ST). Returns the path and the generated declaration. | `name`, `type`?, `parentPath`?, `language`?, `returnType`?, `extends`?, `implements`?, `declaration`?, `implementation`?, `project`? |
| `pou_create_action` | Creates an ACTION below a FUNCTION_BLOCK or PROGRAM (implementation only, no declaration). | `parentPath`, `name`, `language`?, `implementation`? |
| `pou_create_dut` | Creates a data type (DUT): STRUCT, ENUM, UNION or ALIAS. Pass a full declaration (e.g. 'TYPE ST_Data :\nSTRUCT\n\tnValue : INT;\nEND_STRUCT\nEND_TYPE') or for ALIAS just aliasBaseType. Without declaration TwinCAT's default skeleton is created. | `name`, `type`?, `declaration`?, `aliasBaseType`?, `parentPath`?, `project`? |
| `pou_create_gvl` | Creates a global variable list (GVL). Optional declaration, e.g. "{attribute 'qualified_only'}\nVAR_GLOBAL\n\tbStart : BOOL;\nEND_VAR". | `name`, `declaration`?, `parentPath`?, `project`? |
| `pou_create_interface` | Creates an INTERFACE (optionally EXTENDS other interfaces, comma separated). Add members with pou_create_method / pou_create_property. | `name`, `extends`?, `parentPath`?, `project`? |
| `pou_create_method` | Creates a METHOD below a FUNCTION_BLOCK, PROGRAM or INTERFACE. returnType optional, access PUBLIC (default) \| PRIVATE \| PROTECTED \| INTERNAL (ignored for interfaces). Optional declaration/implementation are written after creation. | `parentPath`, `name`, `returnType`?, `access`?, `language`?, `declaration`?, `implementation`? |
| `pou_create_param_list` | Creates a parameter list (VAR_GLOBAL CONSTANT list usable for array bounds in libraries). Optional declaration. | `name`, `declaration`?, `parentPath`?, `project`? |
| `pou_create_property` | Creates a PROPERTY with Get and Set accessors below a FUNCTION_BLOCK, PROGRAM or INTERFACE. readOnly=true creates only Get, writeOnly=true only Set. Optional ST code for the accessors (FB/PROGRAM only), e.g. getImplementation='P_Speed := fSpeed;'. | `parentPath`, `name`, `type`, `access`?, `readOnly`?, `writeOnly`?, `getImplementation`?, `setImplementation`? |
| `pou_create_text_list` | Creates an (empty) text list object. Its entries can only be edited in the IDE or via PLCopen import. | `name`, `parentPath`?, `project`? |
| `pou_create_transition` | Creates a TRANSITION object below a POU (used by SFC charts). Implementation is a boolean expression in ST, e.g. 'bStart AND NOT bError'. | `parentPath`, `name`, `language`?, `implementation`? |
| `pou_delete` | Deletes one or more PLC objects (POUs, methods, properties, actions, DUTs, GVLs, folders incl. content). References to them are not updated. | `paths` |
| `pou_get_declaration` *(read-only)* | Returns the declaration of one or more objects plus a parsed view: kind, name, access, returnType, extends, implements and VAR sections with variables (name, type, init, address, comment). Use it to learn the interface of FBs, functions, methods, DUTs and GVLs. | `paths` |
| `pou_grep` *(read-only)* | Regex search inside one PLC object (and by default its methods, properties, actions, transitions). Returns path, section, line number and line. | `path`, `pattern`, `ignoreCase`?, `includeMembers`?, `section`? |
| `pou_move` | Moves a top-level PLC object (POU, DUT, GVL, interface, folder) into another folder of the same PLC project. Implemented as PLCopen export -> delete -> import into the target folder (object id, members and code are kept; on import failure the object is restored at its original place). | `path`, `targetFolder`, `timeoutSeconds`? |
| `pou_multi_replace` | Applies several exact search & replace edits across one or more PLC objects in one call. Edits on the same object are applied in order. All or nothing: if any edit fails (not found / ambiguous) nothing is written. | `edits` |
| `pou_read_code` *(read-only)* | Reads declaration + implementation + language of one or more PLC objects (POU, method, property Get/Set, action, transition, DUT, GVL, interface). For POUs/interfaces/properties the members (methods, properties, actions, Get/Set) are listed with their paths. Objects without an area return null for it (DUT/GVL: no implementation, action/transition: no declaration). | `paths`, `includeMemberCode`? |
| `pou_rename` | Renames a PLC object (the declaration header is updated by TwinCAT). Usages in other code are NOT refactored - use plc_grep + pou_multi_replace to update them. | `path`, `newName` |
| `pou_replace_code` | Replaces an exact text snippet in the declaration or implementation of one PLC object (like a code editor's search & replace). Fails if oldText is not found or occurs more than once (unless replaceAll) - then add more context. Line endings are handled automatically. Returns the changed line numbers and a small diff. | `path`, `oldText`, `newText`, `section`?, `replaceAll`? |
| `pou_write_code` | Replaces the whole declaration and/or implementation of a PLC object. Pass only the area(s) you want to change. The declaration must start with the matching header (e.g. 'FUNCTION_BLOCK FB_Motor', 'METHOD M_Start : BOOL', 'TYPE ST_Data :', 'VAR_GLOBAL'). For small changes prefer pou_replace_code. Afterwards check with plc_check_all_objects or build_solution. | `path`, `declaration`?, `implementation`? |

## Libraries (`lib_*`, 18)

| Tool | Description | Parameters |
|---|---|---|
| `lib_add` | Adds one or more library references to a PLC project (ITcPlcLibraryManager.AddLibrary). Each entry is a TwinCAT display name 'Name, Version (Distributor)', e.g. 'Tc2_MC2, * (Beckhoff Automation GmbH)'; version '*' = always newest installed. 'Tc2_MC2' alone works too: version defaults to '*' and the distributor is looked up in the installed libraries. Adding an already referenced library is reported as alreadyReferenced (no error). Use lib_list_installed to find names. | `libraries`, `plcProject`? |
| `lib_add_placeholder` | Adds a placeholder reference (ITcPlcLibraryManager.AddPlaceholder). Without 'library' the placeholder must be a known default placeholder of the system (e.g. 'Tc2_Utilities'); with 'library' a new placeholder resolving to that library is created, e.g. placeholderName='Placeholder_NC', library='Tc2_NC, * (Beckhoff Automation GmbH)'. | `placeholderName`, `library`?, `plcProject`? |
| `lib_freeze_placeholders` | Freezes placeholder versions (ITcPlcLibraryManager.FreezePlaceholder): the currently effective version is pinned, so later library installations do not change the project. Without placeholderName all placeholders are frozen. Returns all redirections. | `placeholderName`?, `plcProject`? |
| `lib_get_declaration` *(read-only)* | Returns the IEC 61131-3 declaration (VAR_INPUT/VAR_OUTPUT/VAR_IN_OUT with types and comments) of library objects, e.g. objects=['MC_MoveAbsolute'], library='Tc2_MC2' -> 'FUNCTION_BLOCK MC_MoveAbsolute ...'. Use it to call library FBs/functions correctly. The library must be referenced (lib_add) - its dependencies are found as well. Generated from the library signature. | `objects`, `library`?, `plcProject`? |
| `lib_get_project_info` *(read-only)* | Reads the project information of a PLC project (Company, Title, Version, Author, Description, DefaultNamespace, Released, ...). These properties identify the library when the project is saved/installed as a library (archive_save_as_library). | `plcProject`? |
| `lib_get_signatures` *(read-only)* | Returns the public signatures (function blocks, functions, types, interfaces, GVLs with inputs/outputs/in-outs and comments) of referenced libraries (ITcPlcLibraryManager2.ProduceLibrarySignatures). Use it to discover what a library offers. Includes libraries pulled in as dependencies. Filter with namePattern ('MC_Move*') and kind. | `library`?, `namePattern`?, `kind`?, `includeVariables`?, `maxResults`?, `plcProject`? |
| `lib_insert_repository` | Registers an additional library repository folder (ITcPlcLibraryManager.InsertRepository). NOTE: repositories are a machine/user-wide TwinCAT setting, not part of the project. The folder is created if missing. | `name`, `folder`, `index`?, `plcProject`? |
| `lib_install` | Installs a .library or .compiled-library file into a repository (ITcPlcLibraryManager.InstallLibrary). The library's project information (Title, Version, Company) determines where it lands: <repo>\<Company>\<Title>\<Version>. Afterwards it can be referenced with lib_add. | `filePath`, `repository`?, `overwrite`?, `plcProject`? |
| `lib_list_installed` *(read-only)* | Lists installed PLC libraries (catalog) from the repository folders: name, version, distributor, repository and file kind (library = source, compiled-library = protected). Read-only. Filter by name/distributor/repository, optionally only the newest version per library. Works without an open PLC project (then the default System repository is scanned). | `nameContains`?, `distributor`?, `repository`?, `latestOnly`?, `maxResults`?, `plcProject`? |
| `lib_list_references` *(read-only)* | Lists the library references and placeholders of a PLC project (References node) with requested version ('*' = newest), effective (resolved) version, distributor, namespace, default resolution and explicit/frozen placeholder redirections. Use before lib_add/lib_remove and to check which libraries a project uses. | `plcProject`?, `includeDependencies`? |
| `lib_list_repositories` *(read-only)* | Lists the PLC library repositories in search order (index 0 first) with their root folders. 'System' is the default repository. | `plcProject`? |
| `lib_move_repository` | Moves a library repository to another position of the search order (0 = first). | `name`, `newIndex`, `plcProject`? |
| `lib_remove` | Removes library or placeholder references from a PLC project. Pass the library name ('Tc2_MC2'), the display name ('Tc2_MC2, 3.3.72.0 (Beckhoff Automation GmbH)' - needed if several versions are referenced) or the placeholder name ('Tc2_Utilities'). | `references`, `plcProject`? |
| `lib_remove_repository` | Unregisters a library repository (ITcPlcLibraryManager.RemoveRepository). The folder and its files are NOT deleted. | `name`, `plcProject`? |
| `lib_scan` *(read-only)* | Asks TwinCAT for all libraries available in all repositories (ITcPlcLibraryManager.ScanLibraries) - the authoritative catalog of the Automation Interface (name, version, distributor, display name). Optionally filtered by name substring. | `nameContains`?, `maxResults`?, `plcProject`? |
| `lib_set_project_info` | Sets project information (library properties) of a PLC project. Only passed values change; '' clears a field. For a library, Company, Title and Version are required to install it, e.g. company='Acme', title='AcmeMotion', version='1.0.0.0'. | `company`?, `title`?, `version`?, `author`?, `description`?, `defaultNamespace`?, `released`?, `docFormat`?, `plcProject`? |
| `lib_set_resolution` | Sets the effective resolution of a placeholder (ITcPlcLibraryManager.SetEffectiveResolution), i.e. which library version it points to in this project, e.g. placeholderName='Tc2_Standard', library='Tc2_Standard, 3.4.5.0 (Beckhoff Automation GmbH)'. Only placeholders can be redirected (not plain library references). | `placeholderName`, `library`, `plcProject`? |
| `lib_uninstall` | Uninstalls a library version from a repository (ITcPlcLibraryManager.UninstallLibrary), e.g. library='MyLib, 1.0.0.0 (My Company)'. If the version is omitted and exactly one version is installed, that one is removed. | `repository`, `library`, `plcProject`? |

## Archives & export (`archive_*`, 5)

| Tool | Description | Parameters |
|---|---|---|
| `archive_export_plc_project` | Exports a whole PLC project as .tpzip archive (ITcSmTreeItem.ExportChild on TIPC) - sources, references and library files. Re-import with archive_import_plc_project. | `plcName`, `filePath`, `overwrite`?, `timeoutSeconds`? |
| `archive_import_plc_project` | Adds a PLC project from a .tpzip archive or an existing .plcproj to the TwinCAT project (CreateChild on TIPC). mode: 'copy' (copy into the solution folder, default), 'move', or 'original' (use the project at its location; .plcproj only). | `filePath`, `name`, `mode`?, `timeoutSeconds`? |
| `archive_open` | Opens an archive. .tnzip (solution archive): extracted into targetFolder and opened as the current solution (ITcArchivePersist.LoadFromArchive) - the previously open solution is closed without saving, its first TwinCAT project is selected. .tszip (TwinCAT project archive): added as new TwinCAT project to the open solution (Solution.AddFromTemplate). For .tpzip PLC project archives use archive_import_plc_project. | `archivePath`, `targetFolder`, `projectName`?, `timeoutSeconds`? |
| `archive_save_as_library` | Saves a PLC project as library file (ITcPlcIECProject6.SaveAsLibrary2). '.library' = source library, '.compiled-library' = protected compiled library. Optionally encrypted (needs a connected software protection / user database) and installed into the System repository. Set Company/Title/Version first with lib_set_project_info (required to install). | `filePath`, `install`?, `encrypt`?, `overwrite`?, `plcProject`?, `timeoutSeconds`? |
| `archive_save_solution` | Saves the whole solution as a .tnzip archive (ITcArchivePersist.SaveAsArchive via DTE.GetObject("TcArchivePersist")): all project files plus the referenced libraries - restorable on another PC with archive_open. scope='twincatProject' instead writes only the selected TwinCAT project as .tszip (ITcSysManager.SaveAsArchive). Save first (solution_save). | `filePath`, `scope`?, `overwrite`?, `timeoutSeconds`? |

## System & activation (`system_*`, 13)

| Tool | Description | Parameters |
|---|---|---|
| `system_activate_configuration` | Activates the TwinCAT configuration on the target (ITcSysManager.ActivateConfiguration: writes the boot configuration incl. PLC boot projects) and, with restart=true (default), restarts TwinCAT in Run mode (StartRestartTwinCAT) without any dialog, then waits until the target reports ADS state Run. Build the solution first and make sure the target platform matches (system_get_info). Requires valid licenses on the target (license_list). | `restart`?, `waitSeconds`?, `skipLicenseCheck`?, `timeoutSeconds`? |
| `system_get_info` *(read-only)* | Reads information about a TwinCAT target via ADS (no XAE needed): system state, device name, TwinCAT version, platform (e.g. 'TwinCAT RT (x64)' or 'TwinCAT OS (x64)') and whether it is a Usermode Runtime. Use the platform with system_set_target_platform. | `netId`? |
| `system_get_state` *(read-only)* | Returns the state of the target TwinCAT system: ADS state of the system service (Run, Config, Stop, Invalid ...) and, with an XAE session, ITcSysManager.IsTwinCATStarted. Pass netId to query any target via ADS only (no XAE session needed). | `netId`? |
| `system_get_target` *(read-only)* | Returns the target system of the selected TwinCAT project: AMS Net ID (ITcSysManager.GetTargetNetId) and its route name if known. Use before activating to make sure you deploy to the right runtime. |  |
| `system_get_versions` *(read-only)* | Returns the TwinCAT XAE version in use and all installed Remote Manager versions (ITcRemoteManager), and whether the project is pinned to a TwinCAT version (TcVersionFixed / PinnedTcVersion). |  |
| `system_list_target_platforms` *(read-only)* | Lists the build target platforms of the project (e.g. 'TwinCAT RT (x64)', 'TwinCAT OS (x64)', 'TwinCAT RT (x86)') and the active one. A Usermode Runtime / TwinCAT/BSD target needs 'TwinCAT OS (x64)'. |  |
| `system_list_targets` *(read-only)* | Lists the target systems the XAE knows: the current target and the ADS routes of the local router (TIRR: actual, static and project routes, incl. local Usermode Runtime instances). Pass a route name or Net ID to system_set_target. |  |
| `system_pin_version` | Pins (or unpins) the TwinCAT version of the project ('Pin Version' on the project's General tab). When pinned, the project always opens with the XAE version it was saved with. Save the solution afterwards. | `pin`? |
| `system_restart` | Starts or restarts TwinCAT on the target in Run mode (ITcSysManager.StartRestartTwinCAT) with the configuration last activated on it, then waits for ADS state Run. Does NOT activate the current project - use system_activate_configuration for that. | `waitSeconds`?, `timeoutSeconds`? |
| `system_set_config_mode` | Switches the target TwinCAT system to Config mode (stops real-time, PLC and I/O; needed for I/O scans or to free the target) via ADS (system service port 10000, state Reconfig = XAE 'Restart TwinCAT (Config Mode)') and waits for ADS state Config. Pass netId to address any target without an XAE session. | `netId`?, `waitSeconds`? |
| `system_set_run_mode` | Restarts the target TwinCAT system in Run mode with its currently activated configuration (same as system_restart, but also works without an XAE session via ADS: system service port 10000, state Reset). Does NOT activate the current project. | `netId`?, `waitSeconds`? |
| `system_set_target` | Sets the target system for activation/login (ITcSysManager.SetTargetNetId). Accepts an AMS Net ID (e.g. '199.4.42.250.1.1' for a local Usermode Runtime, '5.12.34.56.1.1' for a remote PLC), a route name as listed by system_list_targets, or 'local'. For remote targets an ADS route must exist (route_ tools). Does not change the runtime itself. | `target` |
| `system_set_target_platform` | Sets the active target platform for compilation (ITcConfigManager.ActiveTargetPlatform), e.g. 'TwinCAT OS (x64)' for a Usermode Runtime or TwinCAT/BSD, 'TwinCAT RT (x64)' for Windows real-time. Rebuild afterwards. | `platform` |

## Real-time (`rt_*`, 6)

| Tool | Description | Parameters |
|---|---|---|
| `rt_get_settings` *(read-only)* | Returns the real-time settings of the project (TIRS): real-time cores (affinity), per-core base time (ms/us and 100 ns ticks), CPU load limit %, latency warning, router memory and the CPU info last read from the target. |  |
| `rt_set_settings` | Changes real-time settings (TIRS RTimeSetDef). Only given values change. Example: cores=[0], baseTime=500, unit='us', loadLimitPercent=60. baseTime/loadLimitPercent/latencyWarningUs apply to every core in 'cores' (or to all current real-time cores if 'cores' is empty). Task cycle times must be multiples of the base time. Activate the configuration afterwards. | `cores`?, `baseTime`?, `unit`?, `loadLimitPercent`?, `latencyWarningUs`?, `maxCpus`?, `routerMemoryKb`? |
| `rt_umrt_create` | Creates a new Usermode Runtime instance by copying the TC1700 template (UmRT_Template) to C:\ProgramData\Beckhoff\TwinCAT\3.1\Runtimes\<name>. Optionally pre-sets its AMS Net ID; otherwise the runtime picks one on first start (default 199.4.42.250.1.1, second byte incremented on collision). Start it with rt_umrt_start. | `name`, `netId`? |
| `rt_umrt_list` *(read-only)* | Lists the Usermode Runtime instances of this PC (folders in C:\ProgramData\Beckhoff\TwinCAT\3.1\Runtimes) with AMS Net ID, running state (process / Windows service), ADS system state and license files (e.g. trial license with expiry). |  |
| `rt_umrt_start` | Starts a Usermode Runtime instance (TcSystemServiceUm.exe, like its Start.bat; minimized console window) and waits until it answers via ADS. It starts in Config mode; then use system_set_target with the returned netId and system_activate_configuration. | `name`, `waitSeconds`? |
| `rt_umrt_stop` | Stops a Usermode Runtime instance that was started as a process (sends 'x' to its console, kills it if it does not exit). An instance running as the Windows service TcSysSrv is not stopped. | `name`, `waitSeconds`? |

## Tasks (`task_*`, 5)

| Tool | Description | Parameters |
|---|---|---|
| `task_configure` | Changes cycle time, priority, autostart, CPU cores or 'I/O at task begin' of an existing real-time task (TaskDef XML). Only given values change. Returns the task as TwinCAT stored it plus warnings when TwinCAT adjusted a value (e.g. cycle time not a multiple of the base time). | `task`, `cycleTime`?, `unit`?, `priority`?, `autoStart`?, `cores`?, `ioAtBegin`? |
| `task_create` | Creates a real-time task below TIRT and configures it in one step. Example: name='FastTask', cycleTime=1, unit='ms', priority=10. The cycle time must be a multiple of the real-time base time (rt_get_settings, default 1 ms). Link a PLC to it with task_link_plc. | `name`, `cycleTime`?, `unit`?, `priority`?, `withImage`?, `autoStart`?, `cores`? |
| `task_delete` | Deletes a real-time task below TIRT. Unlink PLC task references first (task_link_plc). | `task` |
| `task_link_plc` | Links a PLC task reference (the task object inside the PLC project, e.g. 'TIPC^Plc1^Plc1 Project^PlcTask') to a real-time task (ITcPlcTaskReference.LinkedTask). Example: plcTask='PlcTask', plcProject='Plc1', task='FastTask'. | `task`, `plcTask`?, `plcProject`? |
| `task_list` *(read-only)* | Lists all real-time tasks (TIRT) with cycle time (ms/us and raw 100 ns ticks), priority, AMS port, autostart, CPU cores, and which PLC task references are linked to each task. |  |

## Licenses (`license_*`, 3)

| Tool | Description | Parameters |
|---|---|---|
| `license_activate_trial` | Prepares the 7-day trial license for the current target. TwinCAT protects trial generation with a captcha, so the final click must be done by a human: this tool checks whether the target already has a valid trial (local Usermode Runtimes), makes the XAE window visible, selects System > License and returns the exact steps. Afterwards call license_list to verify. |  |
| `license_add` | Adds (or with remove=true removes) a license manually to/from a PLC project (IECProjectDef/LicenseSettings), e.g. for function licenses used by libraries (TF6250 Modbus TCP, TF6310 TCP/IP). The license becomes part of the project's required licenses and therefore of the 7-day trial / license request. Accepts an order number ('TF6250') or a license GUID. Licenses the configuration needs anyway (TC1200 PLC, TC1000 ADS, ...) are detected automatically by TwinCAT. | `license`, `plcProject`?, `remove`? |
| `license_list` *(read-only)* | Shows license information: (1) licenses present on the target when it is a Usermode Runtime of this PC (license files incl. trial expiry), (2) the license request settings of the project (TIRC^License), (3) licenses added manually to PLC projects, and (4) with 'search', matching entries of the TwinCAT license catalog (e.g. 'TC1200' -> TC3 PLC with its license GUID). | `search`? |

## Project variants (`variant_*`, 4)

| Tool | Description | Parameters |
|---|---|---|
| `variant_define` | Defines the project variants (replaces the whole variant configuration). Example: variants=['Small','Large'], groups={'All':['Small','Large']}. Afterwards select one with variant_set_current and use tree_ tools / PlcCompilerDefines per variant. | `variants`, `groups`? |
| `variant_get_current` *(read-only)* | Returns the currently selected project variant (empty = no variant management). |  |
| `variant_list` *(read-only)* | Lists the project variants and variant groups of the TwinCAT project and the current variant. |  |
| `variant_set_current` | Selects the current project variant by name ('Variant1') or a variant group in brackets ('[Group1]'). Rebuild/activate afterwards. | `variant` |

## I/O devices (`io_*`, 9)

| Tool | Description | Parameters |
|---|---|---|
| `io_add_device` | Adds an I/O device below TIID. Types: ethercat-master (111), ethercat-slave (130), ethercat-automation-protocol (112), ethercat-simulation (144), profinet-controller (113), profinet-device (115), ethernetip-scanner (133), ethernetip-adapter (139), realtime-ethernet-adapter (109), virtual-ethernet (45), usb (57), ... or a numeric item sub type. Optionally binds a network adapter (see io_list_network_adapters). Next step for EtherCAT: ecat_add_terminals. | `type`?, `name`?, `adapter`? |
| `io_get_variable_info` *(read-only)* | Details of one or more process image variables: data type, bit size, bit address, direction, ADS index group/offset and links. | `treePaths` |
| `io_list_devices` *(read-only)* | Lists the I/O devices below TIID (EtherCAT masters, Profinet, EtherNet/IP, ...) with type, number of boxes, bound network adapter and AMS Net ID. Start here before adding boxes or linking variables. |  |
| `io_list_network_adapters` *(read-only)* | Lists the local Ethernet adapters with MAC, GUID and the '\DEVICE\{GUID}' name TwinCAT uses for binding. twinCatDriver=true marks adapters with the TwinCAT RT-Ethernet driver installed (required for real-time EtherCAT on this PC). For a remote target use io_scan_devices instead (it reports the target's adapters). | `includeVirtual`? |
| `io_list_variables` *(read-only)* | Lists the process image variables below a box, terminal, channel, device or PLC instance with data type, bit size, direction (input/output) and what each one is linked to. Examples: 'TIID^EtherCAT Master^Term 1 (EK1100)^Term 2 (EL1008)', 'TIPC^Plc1^Plc1 Instance'. Use the returned paths with mapping_link. | `treePath`, `direction`?, `includeSubBoxes`?, `includeLinks`?, `maxVariables`? |
| `io_remove_device` | Removes an I/O device (incl. all its boxes and their links) from TIID. Accepts the device name or full path. | `device` |
| `io_scan_boxes` | Scans the boxes/terminals connected to an I/O device (e.g. EtherCAT slaves of a master, 'Scan' on the device in XAE) and adds them. Requires a bound adapter, the target in Config mode and connected hardware; otherwise returns scanned=false with the reason. | `device`, `timeoutSeconds`? |
| `io_scan_devices` | Scans the target system for I/O devices ('Scan' in XAE, via ProduceXml on TIID). Returns the found devices (type, sub type, adapter). Requires the target in Config mode and real hardware; without hardware an empty list is returned. addFound=true creates the found devices in the configuration with their address info (like XAE does). | `addFound`?, `reload`?, `timeoutSeconds`? |
| `io_set_device_adapter` | Binds an I/O device (e.g. the EtherCAT master) to a network adapter via its XML address info (DeviceDef/AddressInfo/Pnp). Pass 'adapter' to pick a local adapter (name/description/MAC/GUID), or deviceName+mac(+description) for adapters of a remote target (as reported by io_scan_devices). Pass clear=true to unbind. | `device`, `adapter`?, `deviceName`?, `mac`?, `description`?, `clear`? |

## EtherCAT (`ecat_*`, 7)

| Tool | Description | Parameters |
|---|---|---|
| `ecat_add_box` | Adds one EtherCAT box/terminal (EK1100 coupler, ELxxxx terminal, EPxxxx box, drive, ...) below an EtherCAT master or a coupler/box. The product may be a short type 'EL1008' (latest revision from the ESI catalog), a variant 'EL3102-0010', a full product revision 'EL1008-0000-0018' or an identity string 'V00000002_P03F03052_R00120000'. Item sub type is chosen automatically (9099; special terminals like EL6001 = 9101). Default name follows XAE: 'Term 2 (EL1008)'. | `parent`, `product`, `name`?, `before`? |
| `ecat_add_terminals` | Batch-adds EtherCAT boxes/terminals in order, e.g. products=['EK1100','EL1008','EL2008','EL3102','EL7041'] below 'TIID^EtherCAT Master'. A coupler/junction (EK1xxx, CU11xx, BK1120) placed directly below a master becomes the parent of the following terminals, so a complete station can be created in one call. Products are resolved through the ESI catalog (short names get the latest revision). Returns the created items (path, product revision) and stops at the first error (reported with the items created so far). | `parent`, `products`, `names`? |
| `ecat_get_coe` *(read-only)* | Reads the CoE startup list (init commands sent to the slave at state transitions, e.g. PS = Pre-Op->Safe-Op) of an EtherCAT slave. fixed=true entries are generated by TwinCAT (PDO assignment) and cannot be changed. Optionally reads CoE objects ONLINE from a running slave via ADS (needs an activated configuration with connected hardware). | `treePath`, `onlineObjects`?, `onlineDataType`?, `onlineSize`? |
| `ecat_get_slave_info` *(read-only)* | EtherCAT identity and addresses of one or more boxes/terminals: vendor id, product code, revision, serial number, product revision string, EtherCAT (physical) address, auto-increment address, previous port and mailbox protocols (CoE/FoE/...). | `treePaths` |
| `ecat_get_topology` *(read-only)* | Returns the EtherCAT topology as a tree: masters -> couplers/boxes -> terminals, with product revision of every box. details=true adds EtherCAT addresses and previous port (slower: reads the XML of every box). | `master`?, `details`? |
| `ecat_search_catalog` *(read-only)* | Searches the EtherCAT device description (ESI) catalog installed with TwinCAT by product type, description words, group or vendor. Returns per type: description, vendor, product code, all revisions (product revision strings like 'EL1008-0000-0018' for Beckhoff, identity strings 'V..._P..._R...' for other vendors) and the latest one. The first call parses the ESI files (cached afterwards). Examples: query='EL1008', query='4Ch analog input 0-10V', query='EK1100', vendor='Beckhoff'. | `query`?, `vendor`?, `maxResults`?, `refresh`? |
| `ecat_set_startup_parameter` | Adds, replaces or removes a CoE startup parameter (entry of the slave's CoE startup list, written at a state transition). Example: EL7041 max current 0x8010:01 = 1500 (UINT, mA) at transition PS. An existing user entry with the same index, subindex and transition is replaced. Values are encoded little-endian according to dataType (or pass dataType='HEX' with raw bytes). | `treePath`, `index`, `subIndex`, `value`?, `dataType`?, `transition`?, `comment`?, `remove`?, `completeAccess`? |

## Variable mapping (`mapping_*`, 8)

| Tool | Description | Parameters |
|---|---|---|
| `mapping_auto_link` | Links several PLC variables to I/O channels in one call using a name map. Paths are relative: plcVariable is searched in the process image of the PLC instance (e.g. 'GVL_IO.bIn1' under 'PlcTask Inputs'/'PlcTask Outputs'), ioVariable is relative to ioParent (e.g. 'Term 2 (EL1008)^Channel 1^Input' under 'TIID^EtherCAT Master^Term 1 (EK1100)'). Example map: {"GVL_IO.bIn1": "Term 2 (EL1008)^Channel 1^Input", "GVL_IO.bOut1": "Term 3 (EL2008)^Channel 1^Output"}. | `plcInstance`, `ioParent`, `map` |
| `mapping_check` *(read-only)* | Checks all links: verifies that both ends of every link still exist in the tree and reports bit size mismatches (e.g. after renaming PLC variables and rebuilding). A PLC BOOL linked to an I/O BIT is fine. The XAE recomputes the process image mappings itself during build_solution / activation. |  |
| `mapping_clear` | Deletes ALL variable links of the project (ITcSysManager.ClearMappingInfo). Consider mapping_produce first to keep a backup. |  |
| `mapping_consume` | Applies mapping XML (ITcSysManager.ConsumeMappingInfo). WARNING: this REPLACES ALL existing links of the project with the given set. Typical use: restore XML previously returned by mapping_produce. Format: <VarLinks><OwnerA Name="TIPC^Plc1^Plc1 Instance"><OwnerB Name="TIID^EtherCAT Master^Term 1 (EK1100)^Term 3 (EL2008)"><Link VarA="PlcTask Outputs^GVL_IO.bOut1" VarB="Channel 1^Output"/></OwnerB></OwnerA></VarLinks>. | `xml` |
| `mapping_link` | Links variables (ITcSysManager.LinkVariables), e.g. a PLC input 'TIPC^Plc1^Plc1 Instance^PlcTask Inputs^GVL_IO.bIn1' to an I/O channel 'TIID^EtherCAT Master^Term 1 (EK1100)^Term 2 (EL1008)^Channel 1^Input'. Batch: pass several pairs. The PLC must have been built (build_solution) so its process image (PlcTask Inputs/Outputs) exists; PLC variables need 'AT %I*' (inputs) or 'AT %Q*' (outputs). Use io_list_variables to find valid paths. Each pair reports success/error individually. | `links` |
| `mapping_list_links` *(read-only)* | Lists all variable links of the project as a compact list of {a, b} full tree paths (parsed from ProduceMappingInfo). Optionally filtered by a path substring (e.g. 'Term 2 (EL1008)' or 'Plc1 Instance'). | `filter`? |
| `mapping_produce` *(read-only)* | Returns the raw mapping XML of the whole project (ITcSysManager.ProduceMappingInfo). Use mapping_list_links for a compact view; the raw XML can be saved and restored later with mapping_consume. |  |
| `mapping_unlink` | Removes links (ITcSysManager.UnlinkVariables). Pass {a, b} to remove one link or only {a} to remove all links of variable a. | `links` |

## Motion (NC) (`motion_*`, 13)

| Tool | Description | Parameters |
|---|---|---|
| `motion_add_axis` | Adds one or more NC axes to an NC task. A new axis has a simulation encoder and is not linked to I/O, i.e. it can be moved without hardware. axisType: continuous (default, servo/stepper via drive), discrete (two speed), stepper (low cost stepper, digital I/O), encoder (encoder only), hydraulic, timeGenerator - or the numeric sub type. Example: names=['X','Y'], axisType='continuous'. | `names`, `ncTask`?, `axisType`? |
| `motion_create_nc_task` | Creates an NC task below MOTION (TINC). TwinCAT appends ' SAF' to the name (e.g. 'NC-Task 1' -> 'TINC^NC-Task 1 SAF') and creates the 'Axes' folder; the SVB task is created implicitly (cycle 10 ms). Default SAF cycle time is 2000 us, priority 4. A project can contain only one NC task. Next step: motion_add_axis. | `name`?, `cycleTimeUs`?, `priority`? |
| `motion_find_plc_axis_refs` *(read-only)* | Lists PLC AXIS_REF variables that can be linked to NC axes (PLC instance I/O items '<symbol>.NcToPlc' / '.PlcToNc'). The PLC project must have been built (build_solution) after declaring the AXIS_REF (library Tc2_MC2). | `plc`? |
| `motion_get_axis_parameters` *(read-only)* | Returns the most important axis parameters in compact, LLM-readable form (units: position unit 'unit', s): maxVelocity, acceleration, deceleration, jerk, manual/homing velocities, scalingFactor [unit/INC], encoderMode, moduloFactor, software limits, position lag monitoring, referenceVelocity, positionControlKp, target position monitoring. Use motion_set_axis_parameters with the same names to change them. | `axes`, `includeSimulationMode`? |
| `motion_get_axis_xml` *(read-only)* | Returns the raw NcAxisDef XML of an axis (ProduceXml) for parameters not covered by motion_get_axis_parameters. section: full (default) \| AxisParam \| Encoder \| Drive \| Controller. Change raw values with tree_consume_xml. | `axis`, `section`? |
| `motion_link_axis_to_drive` | Links an NC axis to an I/O drive box/terminal (like 'Link To I/O...' in XAE), e.g. an EL7041 stepper terminal 'TIID^Device 1 (EtherCAT)^Term 1 (EK1100)^Term 2 (EL7041)' or an AX5000 servo drive. TwinCAT then selects matching encoder/drive sub types (e.g. EL7041 -> encoder MDP 511, drive MDP 703; AX5000 -> SoE/Sercos drive) and creates all process data links. Works offline. channel: drive channel for multi-axis devices (0 = first, AX5203 has 0 and 1). Pass ioPath='' to remove the I/O link. | `axis`, `ioPath`, `channel`?, `axisIoType`? |
| `motion_link_axis_to_plc` | Links an NC axis to a PLC AXIS_REF variable (like 'Link To PLC...' in XAE): axis 'Outputs^ToPlc' <-> '<symbol>.NcToPlc' and axis 'Inputs^FromPlc' <-> '<symbol>.PlcToNc'. plcAxisRef is the symbol as shown in the PLC instance, e.g. 'GVL_Motion.axX' or 'MAIN.fbAxis.Axis' (see motion_find_plc_axis_refs). Build the PLC project first. | `axis`, `plcAxisRef`, `plc`? |
| `motion_list_axes` *(read-only)* | Lists all NC tasks with their axes: axis type, encoder/drive/controller sub types, I/O link and object id. Use it to discover axis paths for the other motion_ tools. | `ncTask`? |
| `motion_remove_axis` | Deletes NC axes (and their variable links). Accepts full paths or unique axis names. | `axes` |
| `motion_set_axis_parameters` | Changes axis parameters (partial update via ConsumeXml - only the given ones are touched). Names as returned by motion_get_axis_parameters, e.g. {"maxVelocity": 500, "acceleration": 2000, "deceleration": 2000, "jerk": 20000, "scalingFactor": 0.0001, "softLimitMaxEnabled": true, "softLimitMax": 800, "positionLagMax": 2}. Instead of scalingFactor you may pass feedPerRevolution + incrementsPerRevolution (scalingFactor = feed / increments). Returns the values read back after the update; 'notApplied' lists values TwinCAT did not accept. | `axis`, `parameters`?, `feedPerRevolution`?, `incrementsPerRevolution`? |
| `motion_set_axis_simulation` | Switches the TwinCAT 4026 axis 'Simulation' mode (Settings tab) on or off. In simulation mode the I/O link is kept but ignored and a simulated drive + encoder are used, so a configured axis can run without hardware. Implemented by exporting the axis (.xti), setting SimulationMode and re-importing it in place with links reconnected. Note: an axis that was never linked to I/O already runs simulated (simulation encoder, no drive). | `axes`, `enabled`? |
| `motion_set_nc_task` | Changes cycle time and/or priority of an NC SAF task. Example: ncTask='NC-Task 1 SAF', cycleTimeUs=1000. | `ncTask`?, `cycleTimeUs`?, `priority`? |
| `motion_unlink_axis_from_plc` | Removes the links between an NC axis and its PLC AXIS_REF. | `axis` |

## TcCOM (`tccom_*`, 7)

| Tool | Description | Parameters |
|---|---|---|
| `tccom_create_object` | Creates a TcCOM object instance from an installed module class (by classId GUID or class name, see tccom_list_classes), e.g. classIdOrName='{272A98C0-4C87-4243-BED6-3BB69E29F02C}' or 'TcJobPool'. The tree shows it as '<name> (<class>)'. Optionally assigns the cyclic task context right away (taskPath e.g. 'TIRT^Task 1'). | `name`, `classIdOrName`, `parentPath`?, `taskPath`? |
| `tccom_delete_object` | Deletes TcCOM object instances (path or instance name). | `objects` |
| `tccom_get_parameters` *(read-only)* | Returns class, object id, parameters (name, type, current value), contexts (task assignment) and data areas of a TcCOM object. object: full tree path or the instance name below 'TIRC^TcCOM Objects' (e.g. 'SoftDrive1'). | `obj` |
| `tccom_list_classes` *(read-only)* | Lists TcCOM module classes installed on this PC (read from the TMC files in TwinCAT Config\Modules, CustomConfig\Modules and the module repository C:\ProgramData\Beckhoff\TwinCAT\3.1\Repository). Returns name, classId, class factory, group, parameter names. Use name or classId with tccom_create_object. Example filter: 'SoftDrive', 'Kinematic', 'Xts'. | `filter`?, `maxResults`?, `includeParameters`? |
| `tccom_list_objects` *(read-only)* | Lists TcCOM object instances (ITcModuleManager): tree path, class name, classId, object id (OTCID) and parent object id. By default only objects below 'TIRC^TcCOM Objects'; includeAll=true also lists tasks, NC, PLC and I/O modules. | `includeAll`? |
| `tccom_set_context` | Assigns a task to a context of a TcCOM object (ITcModuleInstance2.SetModuleContext), i.e. which task calls the module cyclically. Pass taskPath (e.g. 'TIRT^Task 1') or taskObjectId (e.g. '#x02010010'). contextId: the module's context id as listed by tccom_get_parameters (usually 1); default = first context. | `obj`, `taskPath`?, `taskObjectId`?, `contextId`? |
| `tccom_set_parameters` | Sets TcCOM object parameter values (startup values, via ConsumeXml ParameterValues). Values are given as text in TwinCAT notation, e.g. {"PoolId": "3", "MqttTopic": "line1", "MqttClient": "#x01010020"}. Returns the values read back. | `obj`, `values` |

## ADS online (`ads_*`, 21)

| Tool | Description | Parameters |
|---|---|---|
| `ads_browse_symbols` *(read-only)* | Lists the PLC's symbols (variables) via ADS - the way to discover valid names for ads_read_symbols / ads_write_symbols. Filter by prefix ('MAIN.'), glob ('*.bStart*') or regex ('re:^GVL\.n'). depth>0 also lists struct/FB members (and array elements with expandArrays). Paged: use offset/limit; 'hasMore' tells if more exist. Example: {filter:'MAIN.', depth:1}. | `filter`?, `depth`?, `expandArrays`?, `includeTypes`?, `offset`?, `limit`?, `refresh`?, `netId`?, `port`? |
| `ads_connect` | Opens (or reuses) an ADS connection to netId:port and checks it by reading device info and ADS state. Connections are also opened implicitly by all ads_ tools; use this to verify a target is reachable. Example: {netId:'local', port:851} for PLC 1 on this PC, {netId:'5.12.34.56.1.1', port:10000} for a remote system service. | `netId`?, `port`?, `timeoutMs`? |
| `ads_delete_file` | Deletes a file - or, with isDirectory=true, an EMPTY directory - on the target through the TwinCAT system service. | `path`, `root`?, `isDirectory`?, `netId`? |
| `ads_disconnect` | Closes the cached ADS connection (and symbol table) to netId:port. | `netId`?, `port`? |
| `ads_get_symbol_info` *(read-only)* | Detailed info for one or more symbols: data type, category, size, index group/offset, comment, attributes and the type layout (struct members with offsets, enum values, array dimensions, RPC methods). Example: {symbols:['MAIN.stData','GVL.eMode']}. | `symbols`, `netId`?, `port`? |
| `ads_get_system_info` *(read-only)* | TwinCAT system information of a target via its system service (port 10000): TwinCAT version/build, ADS/system state (run/config), platform (x64/ARM...), OS, target type, host name, Net ID, whether it is a Usermode Runtime, name of the active configuration project. | `netId`? |
| `ads_invoke_rpc` | Calls a method of a PLC function block instance via ADS RPC. The method must be marked with {attribute 'TcRpcEnable'} in the PLC. Arguments as JSON object by parameter name (or array by position); primitive and string parameters are supported. Returns returnValue and out parameters. Use ads_get_symbol_info on the FB instance to see its rpcMethods. Example: {symbol:'MAIN.fbAxis', method:'MoveTo', args:{fPos:100.0, fVelo:50.0}}. | `symbol`, `method`, `args`?, `netId`?, `port`? |
| `ads_list_connections` *(read-only)* | Lists the ADS connections cached by this server. |  |
| `ads_list_dir` *(read-only)* | Lists a directory on the target through the TwinCAT system service: names, directory flag, size, modification time. Example: {path:'', root:'boot'} lists the TwinCAT boot folder; {path:'C:\\TwinCAT'} an absolute folder. | `path`?, `root`?, `maxEntries`?, `netId`? |
| `ads_list_plc_runtimes` *(read-only)* | Probes the PLC runtime ports of a target (TwinCAT 3: 851..860, TwinCAT 2: 801/811/821/831 or custom) and lists the ones that answer, with ADS state (Run/Stop), device name and the PLC project/application name (TwinCAT_SystemInfoVarList._AppInfo). | `netId`?, `ports`?, `timeoutMs`? |
| `ads_monitor` *(read-only)* | Samples one or more symbols over time and returns a time series per symbol plus statistics (min, max, number of changes, first/last value). mode 'poll' (default) reads all symbols every intervalMs with a sum command; mode 'notification' registers ADS on-change notifications (cycle time = intervalMs) and records every change. Use it to observe how values evolve, e.g. after setting a start bit. Example: {symbols:['MAIN.fActPos','MAIN.eState'], durationSeconds:5, intervalMs:100}. | `symbols`, `durationSeconds`?, `intervalMs`?, `mode`?, `onlyChanges`?, `maxSamples`?, `netId`?, `port`? |
| `ads_read_device_info` *(read-only)* | Reads name and version of an ADS device (e.g. 'TwinCAT System' 3.1.4026 on port 10000, 'Plc30 App' on 851). | `netId`?, `port`? |
| `ads_read_file` *(read-only)* | Reads a file on the target through the TwinCAT system service (works remotely over an ADS route, no file share needed). Text is returned as UTF-8 (encoding 'text') or the bytes as base64. Example: {path:'CurrentConfig.xml', root:'boot'}. | `path`, `root`?, `encoding`?, `maxBytes`?, `netId`? |
| `ads_read_raw` *(read-only)* | Reads raw bytes by index group / index offset (ADS Read, or ADS ReadWrite when writeData is given). Numbers accept decimal, 0x.. or 16#.. (e.g. indexGroup '0x4020' = PLC %M area, '0xF020' = %I, '0xF030' = %Q). Returns hex and base64. | `indexGroup`, `indexOffset`, `length`, `writeData`?, `format`?, `netId`?, `port`? |
| `ads_read_state` *(read-only)* | Reads the ADS state (Run, Stop, Config, Error, ...) and device state of an ADS device. Port 851 = PLC 1 (Run/Stop), port 10000 = TwinCAT system (Run = run mode, Config = config mode). | `netId`?, `port`? |
| `ads_read_symbols` *(read-only)* | Reads current values of PLC symbols in ONE ADS sum command and returns them as typed JSON: structs/FBs as objects, arrays as (nested) arrays, enums as {name, value}, STRING/WSTRING as text, TIME/LTIME/DATE/DT/TOD as IEC literals (T#1s500ms, DT#2024-01-15-12:30:00). Members and elements can be addressed directly: MAIN.stData.fTemp, MAIN.aValues[3]. Example: {symbols:['MAIN.nCounter','GVL.stConfig']}. | `symbols`, `netId`?, `port`? |
| `ads_wait_for_value` *(read-only)* | Polls a symbol until it satisfies a condition or the timeout elapses - for test automation (e.g. set MAIN.bStart, then wait until MAIN.eState = 'Done'). operator '==' (default) / '!=' compares typed values (enum names, T#.. literals, partial struct objects allowed); '>', '>=', '<', '<=' compare numerically. Returns reached=true/false, the last value and the elapsed time (not an error on timeout). Example: {symbol:'MAIN.nStep', value:100, operator:'>=', timeoutSeconds:30}. | `symbol`, `value`, `operator`?, `timeoutSeconds`?, `intervalMs`?, `netId`?, `port`? |
| `ads_write_control` | Sets the ADS state of a device (ADS WriteControl). PLC port (851..): 'run' starts, 'stop' stops, 'reset' resets the PLC application. WARNING: on port 10000 (system service) this changes the WHOLE TwinCAT system of that PC: 'run'/'restart' restarts TwinCAT in run mode, 'config' switches to config mode - all PLCs, NC and I/O stop. This requires confirmSystemService=true. Numeric ADS states are accepted too (5 = Run, 6 = Stop, 2 = Reset, 16 = Reconfig). | `state`, `netId`?, `port`?, `deviceState`?, `confirmSystemService`? |
| `ads_write_file` | Writes (creates/overwrites or appends) a file on the target through the TwinCAT system service; missing directories are created. Content as UTF-8 text or base64. Example: {path:'C:\\Temp\\recipe.csv', content:'a;b\n1;2'}. | `path`, `content`, `encoding`?, `append`?, `root`?, `netId`? |
| `ads_write_raw` | Writes raw bytes by index group / index offset (ADS Write). Data as hex ('0A000000') or base64. Little endian, exactly as the target expects. Prefer ads_write_symbols for PLC variables. | `indexGroup`, `indexOffset`, `data`, `format`?, `netId`?, `port`? |
| `ads_write_symbols` | Writes values to PLC symbols (one ADS sum command). The JSON value is converted to the symbol's type: numbers (or '16#FF'), true/false, strings, enum names ('Running') or values, TIME as 'T#2s' (or ms), DT as 'DT#2024-01-15-12:30:00'. Structs take an object - only the given members are changed; arrays take an array - a shorter array changes only the leading elements; single members/elements by path (MAIN.stData.nCount, MAIN.aValues[4]). Example: {values:{'MAIN.bStart':true,'GVL.stCfg':{'fGain':1.5},'MAIN.eMode':'Auto'}}. Changes the running machine - be careful. | `values`, `readBack`?, `netId`?, `port`? |

## ADS routes (`route_*`, 5)

| Tool | Description | Parameters |
|---|---|---|
| `route_add` | Adds an ADS route on a TwinCAT system (default: this PC) via its system service: from that system to the target 'routeNetId' at 'address' (IP or host name). temporary=true keeps it only until TwinCAT restarts; otherwise it is persisted in StaticRoutes.xml. For a working connection the remote side usually needs a route back to this PC too - use route_add_remote for that. Example: {name:'CX-1234', address:'192.168.1.20', routeNetId:'5.12.34.56.1.1'}. | `name`, `address`, `routeNetId`, `temporary`?, `netId`? |
| `route_add_remote` | Registers a route on a REMOTE TwinCAT system back to this PC using the TwinCAT UDP route protocol (port 48899) - the same as 'Add Route' with 'Remote Route' in the XAE. Needs the remote OS credentials (e.g. Administrator / 1 on a CX with default image); the password is sent in clear text by this protocol. Afterwards add the local route with route_add (or set alsoAddLocal=true). Example: {remoteHost:'192.168.1.20', password:'1', alsoAddLocal:true, remoteNetId:'5.12.34.56.1.1'}. | `remoteHost`, `password`, `userName`?, `routeName`?, `localNetId`?, `localAddress`?, `alsoAddLocal`?, `remoteNetId`?, `timeoutMs`? |
| `route_broadcast_search` *(read-only)* | Searches the local networks for TwinCAT systems (UDP broadcast to port 48899, like 'Broadcast Search' in the XAE). Returns IP, AMS Net ID, host name, TwinCAT version and OS of every system that answers. Optionally query specific hosts (unicast). | `timeoutMs`?, `hosts`?, `broadcast`? |
| `route_list` *(read-only)* | Lists the ADS routes of a TwinCAT system. source 'ads' (default) enumerates the active routes through the system service (also works for remote systems); source 'file' reads StaticRoutes.xml of this PC (persistent routes, read-only). | `netId`?, `source`?, `filePath`? |
| `route_remove` | Removes an ADS route (by route name, see route_list) from a TwinCAT system (default: this PC) via its system service. | `name`, `netId`? |

## Resources

- `twincat://guide/workflow` – Step-by-step workflow for creating, programming, building and deploying a complete TwinCAT 3.1.4026 project with this server.
- `twincat://guide/tree-paths` – Cheat sheet: System Manager tree path syntax, root shortcuts and common item paths/sub types.
- `twincat://guide/st-conventions` – Beckhoff Structured Text naming conventions and code style to use when generating PLC code.
- `twincat://session/status` – Live status of the XAE session (IDE, solution, selected TwinCAT project, target).
- `twincat://tree/{path}` – A System Manager tree item with two levels of children. 'path' uses '^' as separator, e.g. TIPC or TIRT.

## Prompts

- `create_twincat_project` – Create a complete TwinCAT project from a functional description (PLC code, tasks, I/O, build).
- `fix_build_errors` – Iteratively build the solution and fix all compiler errors.
- `deploy_to_target` – Activate the configuration on a target and download/start the PLC.
- `add_ethercat_io` – Add an offline EtherCAT configuration and link it to PLC variables.
