using System.ComponentModel;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TCatSysManagerLib;
using TwinCatMcp.Core;
using TwinCatMcp.Plc;

namespace TwinCatMcp.Tools;

/// <summary>PLC project level tools (prefix plc_): projects, element search, grep, folders, checks, PLCopen, settings, task reference.</summary>
[McpServerToolType]
public sealed class PlcTools(XaeSession session)
{
    private const string ProjectParam =
        "PLC project: name ('Plc1'), root path ('TIPC^Plc1') or nested project path ('TIPC^Plc1^Plc1 Project'). Empty = the only PLC project.";

    // ------------------------------------------------------------------ projects

    [McpServerTool(Name = "plc_create_project"), Description(
        "Creates a new PLC project below TIPC from a Beckhoff template (or adds an existing .plcproj / .tpzip). " +
        "Returns rootPath ('TIPC^Plc1'), projectPath (nested project 'TIPC^Plc1^Plc1 Project' - the parent of POUs/DUTs/GVLs) and instancePath. " +
        "The Standard template contains MAIN (ST) linked to PlcTask. Next: pou_create / pou_write_code, then build_solution.")]
    public Task<CallToolResult> CreateProject(
        [Description("Project name, e.g. 'Plc1' or 'MachineControl'.")] string name,
        [Description("'Standard PLC Template' (default, with MAIN + PlcTask), 'Empty PLC Template', or a full path to a .plcproj/.tpzip to copy into the solution.")] string template = "Standard PLC Template",
        int timeoutSeconds = 300)
        => session.RunAsync(s =>
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new TcException("name must not be empty.");
            }

            if (PlcTree.Projects(s).Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new TcException($"A PLC project '{name}' already exists.") { Hint = "Use plc_list_projects or choose another name." };
            }

            var tpl = template.Trim();
            if (tpl.Contains('\\') || tpl.Contains('/'))
            {
                tpl = Path.GetFullPath(tpl);
                if (!File.Exists(tpl))
                {
                    throw new TcException($"Template/project file not found: {tpl}");
                }
            }

            var tipc = s.Lookup("TIPC");
            tipc.CreateChild(name, 0, "", tpl);
            var p = PlcTree.ResolveProject(s, name);
            return DescribeProject(p);
        }, timeoutSeconds);

    [McpServerTool(Name = "plc_list_projects", ReadOnly = true), Description(
        "Lists all PLC projects with rootPath ('TIPC^Plc1', boot/settings node), projectPath ('TIPC^Plc1^Plc1 Project', parent of POUs/DUTs/GVLs - " +
        "use it for code tools) and instancePath. Also shows the task references (PlcTask -> linked TIRT task).")]
    public Task<CallToolResult> ListProjects()
        => session.RunAsync(s => new { projects = PlcTree.Projects(s).Select(DescribeProject).ToArray() });

    [McpServerTool(Name = "plc_delete_project", Destructive = true), Description("Removes a PLC project from the TwinCAT project (files stay on disk).")]
    public Task<CallToolResult> DeleteProject([Description(ProjectParam)] string project)
        => session.RunAsync(s =>
        {
            var p = PlcTree.ResolveProject(s, project);
            var root = p.RootPath;
            try
            {
                s.Lookup("TIPC").DeleteChild(p.Name);
            }
            catch (System.Runtime.InteropServices.COMException ex) when (unchecked((uint)ex.HResult) == 0x98510007 && !s.SysManager.TestItemPath(root))
            {
                // TSM_E_ITEMDELETED: seen live on 4026 although the project was removed successfully.
            }

            return new { deleted = root, exists = s.SysManager.TestItemPath(root) };
        });

    private static object DescribeProject(PlcProjectRef p) => new
    {
        name = p.Name,
        rootPath = p.RootPath,
        projectPath = p.ProjectPath,
        instancePath = p.InstancePath,
        tasks = TaskReferences(p).Select(t => new { path = t.PathName, linkedTask = PlcTree.Safe(() => ((ITcPlcTaskReference)t).LinkedTask) }).ToArray(),
    };

    private static IEnumerable<ITcSmTreeItem> TaskReferences(PlcProjectRef p)
        => TreeInfo.Children(p.Nested).Where(c => PlcTree.TypeOf(c) == PlcObjects.Task);

    // ------------------------------------------------------------------ search

    [McpServerTool(Name = "plc_find_elements", ReadOnly = true), Description(
        "Finds PLC objects (POUs, DUTs, GVLs, interfaces, methods, properties, actions, transitions, folders) with kind and full tree path. " +
        "Use it to discover paths for pou_read_code / pou_write_code. Library references are not listed.")]
    public Task<CallToolResult> FindElements(
        [Description("PLC projects to search (names or paths). Empty = all PLC projects.")] string[]? projects = null,
        [Description("Kind filter, any of: pou, program, function_block (fb), function, dut, struct, enum, union, alias, gvl, interface (itf), method, property, action, transition, folder, text_list, task. Empty = all.")] string[]? kinds = null,
        [Description("Case-insensitive substring of the object name, or a regex when it starts with '/' (e.g. '/^FB_'). Empty = all.")] string? namePattern = null,
        int maxResults = 500)
        => session.RunAsync(s =>
        {
            PlcObjects.ValidateKindFilters(kinds);
            var match = NameMatcher(namePattern);
            var targets = projects is { Length: > 0 } ? projects.Select(p => PlcTree.ResolveProject(s, p)).ToList() : PlcTree.Projects(s);
            var list = new List<object>();
            var truncated = false;
            foreach (var p in targets)
            {
                foreach (var item in PlcTree.Elements(p.Nested))
                {
                    var type = PlcTree.TypeOf(item);
                    if (!PlcObjects.MatchesKind(type, kinds) || !match(item.Name))
                    {
                        continue;
                    }

                    if (list.Count >= maxResults)
                    {
                        truncated = true;
                        break;
                    }

                    list.Add(new { name = item.Name, kind = PlcObjects.KindOf(type), path = item.PathName, project = p.Name });
                }
            }

            return new { count = list.Count, truncated, elements = list };
        });

    [McpServerTool(Name = "plc_grep", ReadOnly = true), Description(
        "Regex search over the declaration and implementation of all code objects of a PLC project (POUs, methods, properties, actions, " +
        "transitions, DUTs, GVLs, interfaces). Returns path, section, 1-based line number and the line. Graphical languages are searched in their XML.")]
    public Task<CallToolResult> Grep(
        [Description(".NET regular expression applied per line, e.g. 'nCount\\s*:=' or 'FB_Motor'.")] string pattern,
        [Description(ProjectParam)] string? project = null,
        [Description("Case-insensitive regex matching (default true).")] bool ignoreCase = true,
        [Description("Optional kind filter (see plc_find_elements), e.g. ['pou','method'].")] string[]? kinds = null,
        [Description("Search only 'declaration' or 'implementation'. Empty = both.")] string? section = null,
        int maxResults = 200)
        => session.RunAsync(s =>
        {
            PlcObjects.ValidateKindFilters(kinds);
            var regex = BuildRegex(pattern, ignoreCase);
            var only = StCode.ParseSection(section);
            var p = PlcTree.ResolveProject(s, project);
            var hits = new List<object>();
            var truncated = false;
            foreach (var item in PlcTree.Elements(p.Nested))
            {
                var type = PlcTree.TypeOf(item);
                if (!PlcObjects.IsCode(type) || !PlcObjects.MatchesKind(type, kinds))
                {
                    continue;
                }

                foreach (var hit in GrepItem(item, regex, only))
                {
                    if (hits.Count >= maxResults)
                    {
                        truncated = true;
                        break;
                    }

                    hits.Add(hit);
                }

                if (truncated)
                {
                    break;
                }
            }

            return new { project = p.Name, count = hits.Count, truncated, matches = hits };
        });

    internal static IEnumerable<object> GrepItem(ITcSmTreeItem item, Regex regex, string? onlySection)
    {
        var text = PlcTree.ReadText(item);
        var path = item.PathName;
        if (text.Declaration is not null && onlySection is null or StCode.Declaration)
        {
            foreach (var h in StCode.Grep(text.Declaration, regex))
            {
                yield return new { path, section = StCode.Declaration, line = h.Line, text = h.Text };
            }
        }

        if (text.Implementation is not null && onlySection is null or StCode.Implementation)
        {
            foreach (var h in StCode.Grep(text.Implementation, regex))
            {
                yield return new { path, section = StCode.Implementation, line = h.Line, text = h.Text };
            }
        }
    }

    internal static Regex BuildRegex(string pattern, bool ignoreCase)
    {
        try
        {
            return new Regex(pattern, (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None) | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        }
        catch (ArgumentException ex)
        {
            throw new TcException($"Invalid regex: {ex.Message}") { Hint = "Escape special characters like ( ) [ ] . * + ? with a backslash." };
        }
    }

    private static Func<string, bool> NameMatcher(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return _ => true;
        }

        if (pattern.StartsWith('/'))
        {
            var r = BuildRegex(pattern.Trim('/'), true);
            return n => r.IsMatch(n ?? "");
        }

        return n => (n ?? "").Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ folders

    [McpServerTool(Name = "plc_create_folder"), Description(
        "Creates a folder in a PLC project, e.g. parentPath='TIPC^Plc1^Plc1 Project^POUs', name='Motion'. " +
        "name may contain '^' to create nested folders ('Motion^Axes'); existing folders are reused.")]
    public Task<CallToolResult> CreateFolder(
        [Description("Parent folder or nested project path (relative paths like 'POUs' work when there is one PLC project).")] string parentPath,
        [Description("Folder name, or several levels separated by '^'.")] string name)
        => session.RunAsync(s =>
        {
            var parent = PlcTree.ResolveItem(s, parentPath);
            var created = new List<string>();
            foreach (var part in name.Split('^', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var existing = TreeInfo.Children(parent).FirstOrDefault(c => string.Equals(c.Name, part, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    if (PlcTree.TypeOf(existing) != PlcObjects.Folder)
                    {
                        throw new TcException($"'{existing.PathName}' exists and is not a folder.");
                    }

                    parent = existing;
                    continue;
                }

                parent = parent.CreateChild(part, PlcObjects.Folder, "", null);
                created.Add(parent.PathName);
            }

            return new { path = parent.PathName, created };
        });

    // ------------------------------------------------------------------ checks / compile

    [McpServerTool(Name = "plc_check_all_objects"), Description(
        "Runs 'Check all objects' on a PLC project (ITcPlcIECProject2.CheckAllObjects - compiles every object, also unused ones) " +
        "and returns ok + compiler errors/warnings (object, code, message). Unlike build_solution it also finds errors in objects " +
        "that are not used by any task. Use it after every code change.")]
    public Task<CallToolResult> CheckAllObjects(
        [Description(ProjectParam)] string? project = null,
        bool includeWarnings = true,
        [Description("Also return the raw compiler output text.")] bool includeOutput = false,
        int timeoutSeconds = 600)
        => session.RunAsync(s =>
        {
            var p = PlcTree.ResolveProject(s, project);
            var ok = false;
            var result = CaptureWithDetails(s, () => ok = ((ITcPlcIECProject2)p.Nested).CheckAllObjects(), () => !ok);
            return Diagnostics(p, ok && result.ErrorCount == 0, result, includeWarnings, includeOutput);
        }, timeoutSeconds);

    [McpServerTool(Name = "plc_compile"), Description(
        "Compiles one PLC project (ITcPlcProject.CompileProject, like 'Build <Plc>' - only objects used by tasks) and returns compiler errors/warnings.")]
    public Task<CallToolResult> Compile(
        [Description(ProjectParam)] string? project = null,
        bool includeWarnings = true,
        [Description("Also return the raw compiler output text.")] bool includeOutput = false,
        int timeoutSeconds = 600)
        => session.RunAsync(s =>
        {
            var p = PlcTree.ResolveProject(s, project);
            string? failure = null;
            var result = CaptureWithDetails(s, () =>
            {
                try
                {
                    failure = null;
                    ((ITcPlcProject)p.Root).CompileProject();
                }
                catch (System.Runtime.InteropServices.COMException ex)
                {
                    failure = ex.Message; // thrown when the compile fails
                }
            }, () => failure is not null);
            if (failure is not null && result.Errors.Count == 0)
            {
                result.Errors.Add(new PlcDiagnostic("error", null, failure, null, null, null));
            }

            return Diagnostics(p, failure is null && result.ErrorCount == 0, result with { ErrorCount = Math.Max(result.ErrorCount, result.Errors.Count) }, includeWarnings, includeOutput);
        }, timeoutSeconds);

    /// <summary>
    /// The PLC compiler prints an error line only once per unchanged object (incremental compile); a later run
    /// only shows "Compile complete -- 1 errors". In that case the solution is cleaned and the action repeated
    /// so that the error details are printed again.
    /// </summary>
    private static PlcBuildResult CaptureWithDetails(XaeSession s, Action action, Func<bool>? failed = null)
    {
        var result = PlcBuildOutput.Capture(s.Dte, action);
        var coded = result.Errors.Count(e => e.Code is not null);
        if (result.ErrorCount > coded || (coded == 0 && failed?.Invoke() == true))
        {
            s.Dte.Solution.SolutionBuild.Clean(true);
            var again = PlcBuildOutput.Capture(s.Dte, action);
            if (again.Errors.Count >= result.Errors.Count)
            {
                result = again;
            }
        }

        return result;
    }

    /// <summary>
    /// Diagnostics are parsed from the Build output pane: the DTE Error List stays empty in a hidden TcXaeShell.
    /// </summary>
    private static object Diagnostics(PlcProjectRef p, bool ok, PlcBuildResult r, bool includeWarnings, bool includeOutput, string? sarifFile = null) => new
    {
        project = p.Name,
        ok,
        errorCount = r.ErrorCount,
        warningCount = r.WarningCount,
        errors = r.Errors,
        warnings = includeWarnings ? r.Warnings : null,
        sarifFile,
        output = includeOutput ? r.Output : null,
    };

    [McpServerTool(Name = "plc_run_static_analysis"), Description(
        "Runs TwinCAT Static Analysis (TE1200; without license only the light rules) on a PLC project and returns the findings " +
        "(rule violations are warnings 'SAxxxx'). Optionally writes a SARIF report (TwinCAT >= 3.1.4026.20).")]
    public Task<CallToolResult> RunStaticAnalysis(
        [Description(ProjectParam)] string? project = null,
        [Description("true = 'Run static analysis [Check all objects]' (also unused objects).")] bool checkAll = true,
        [Description("Optional path of a SARIF file (*.sarif.json) to write the results to (runs the analysis itself).")] string? sarifPath = null,
        [Description("Optional .csa settings file to load before the analysis.")] string? settingsFile = null,
        [Description("Also return the raw output text.")] bool includeOutput = false,
        int timeoutSeconds = 900)
        => session.RunAsync(s =>
        {
            var p = PlcTree.ResolveProject(s, project);
            if (!string.IsNullOrWhiteSpace(settingsFile))
            {
                ((ITcPlcIECProject4)p.Nested).LoadStaticAnalysisSettings(Path.GetFullPath(settingsFile));
            }

            string? sarif = null;
            var result = PlcBuildOutput.Capture(s.Dte, () =>
            {
                if (!string.IsNullOrWhiteSpace(sarifPath))
                {
                    sarif = Path.GetFullPath(sarifPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(sarif)!);
                    ((ITcPlcIECProject7)p.Nested).ExportToSarif(sarif);
                }
                else
                {
                    ((ITcPlcIECProject3)p.Nested).RunStaticAnalysis(checkAll);
                }
            });

            return Diagnostics(p, result.ErrorCount == 0, result, true, includeOutput, sarif is not null && File.Exists(sarif) ? sarif : null);
        }, timeoutSeconds);

    // ------------------------------------------------------------------ PLCopen XML

    [McpServerTool(Name = "plc_export_plcopen", ReadOnly = true), Description(
        "Exports PLC objects as PLCopen XML (ITcPlcIECProject.PlcOpenExport). Select top-level objects or folders by tree path " +
        "(methods/properties/actions are exported with their POU). Empty selection = whole project.")]
    public Task<CallToolResult> ExportPlcOpen(
        [Description("Target .xml file path.")] string filePath,
        [Description("Objects/folders to export as tree paths ('TIPC^Plc1^Plc1 Project^POUs^FB_Motor') or relative ('POUs^FB_Motor'). Empty = whole project.")] string[]? selection = null,
        [Description(ProjectParam + " Only needed when selection is empty and there are several PLC projects.")] string? project = null,
        [Description("Overwrite an existing file (TwinCAT refuses to export to an existing file).")] bool overwrite = true,
        int timeoutSeconds = 300)
        => session.RunAsync(s =>
        {
            var full = Path.GetFullPath(filePath);
            var (p, sel) = Selection(s, project, selection);
            PrepareTarget(full, overwrite);
            ((ITcPlcIECProject)p.Nested).PlcOpenExport(full, sel);
            return new { project = p.Name, file = full, size = new FileInfo(full).Length, selection = sel, objects = PlcOpenXml.ListObjects(File.ReadAllText(full)) };
        }, timeoutSeconds);

    [McpServerTool(Name = "plc_import_plcopen"), Description(
        "Imports PLCopen XML into a PLC project (ITcPlcIECProject.PlcOpenImport). conflict: error (default) | replace | rename (creates X_1) | skip. " +
        "With targetFolder the imported top-level objects are placed into that folder (e.g. 'POUs^Imported'), otherwise the folder " +
        "structure stored in the file is used relative to the project root.")]
    public Task<CallToolResult> ImportPlcOpen(
        [Description("PLCopen XML file to import.")] string filePath,
        [Description(ProjectParam)] string? project = null,
        [Description("error | replace | rename | skip - what to do when an object with the same name exists.")] string conflict = "error",
        [Description("Optional folder (relative to the nested project, e.g. 'POUs^Imported' or 'DUTs') to put the imported objects into.")] string? targetFolder = null,
        [Description("Keep the folder structure stored in the file (bSubTree). Ignored when targetFolder is set.")] bool keepFolderStructure = true,
        [Description("Optional subset of objects in the file to import (PLCopen selection names, e.g. 'FB_A').")] string[]? selection = null,
        int timeoutSeconds = 300)
        => session.RunAsync(s =>
        {
            var p = PlcTree.ResolveProject(s, project);
            var full = Path.GetFullPath(filePath);
            if (!File.Exists(full))
            {
                throw new TcException($"File not found: {full}");
            }

            var option = PlcOpenXml.ParseImportOption(conflict);
            var source = full;
            string? temp = null;
            var subTree = keepFolderStructure;
            if (!string.IsNullOrWhiteSpace(targetFolder))
            {
                var chain = targetFolder.Replace(p.ProjectPath + "^", "", StringComparison.OrdinalIgnoreCase)
                    .Split('^', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                EnsureFolders(p, chain);
                temp = Path.Combine(Path.GetTempPath(), "twincat-mcp-" + Guid.NewGuid().ToString("N") + ".xml");
                File.WriteAllText(temp, PlcOpenXml.PlaceInFolder(File.ReadAllText(full), chain));
                source = temp;
                subTree = true;
            }

            try
            {
                ((ITcPlcIECProject)p.Nested).PlcOpenImport(source, option, selection is { Length: > 0 } ? string.Join(";", selection) : "", subTree);
            }
            finally
            {
                if (temp is not null)
                {
                    try { File.Delete(temp); } catch { /* ignore */ }
                }
            }

            return new { project = p.Name, imported = PlcOpenXml.ListObjects(File.ReadAllText(full)), conflict, targetFolder };
        }, timeoutSeconds);

    internal static (PlcProjectRef Project, string Selection) Selection(XaeSession s, string? project, string[]? selection)
    {
        if (selection is not { Length: > 0 })
        {
            return (PlcTree.ResolveProject(s, project), "");
        }

        var items = selection.Select(x => PlcTree.ResolveItem(s, x)).ToList();
        var p = string.IsNullOrWhiteSpace(project) ? PlcTree.ProjectOf(s, items[0].PathName) : PlcTree.ResolveProject(s, project);
        foreach (var item in items)
        {
            var type = PlcTree.TypeOf(item);
            if (!PlcObjects.TopLevelTypes.Contains(type))
            {
                throw new TcException($"'{item.PathName}' ({PlcObjects.KindOf(type)}) cannot be exported on its own.")
                {
                    Hint = "Export the owning POU/interface instead - methods, properties, actions and transitions are included with it.",
                };
            }
        }

        return (p, string.Join(";", items.Select(i => PlcObjects.ToSelection(p.ProjectPath, i.PathName))));
    }

    internal static void PrepareTarget(string full, bool overwrite)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        if (File.Exists(full))
        {
            if (!overwrite)
            {
                throw new TcException($"File exists: {full}") { Hint = "Pass overwrite=true." };
            }

            File.Delete(full);
        }
    }

    internal static ITcSmTreeItem EnsureFolders(PlcProjectRef p, IEnumerable<string> chain)
    {
        var parent = p.Nested;
        foreach (var part in chain)
        {
            var existing = TreeInfo.Children(parent).FirstOrDefault(c => string.Equals(c.Name, part, StringComparison.OrdinalIgnoreCase));
            parent = existing ?? parent.CreateChild(part, PlcObjects.Folder, "", null);
        }

        return parent;
    }

    // ------------------------------------------------------------------ settings / boot project / task

    [McpServerTool(Name = "plc_get_settings", ReadOnly = true), Description(
        "Reads PLC project settings of the root node (ITcPlcProject): bootProjectAutostart, bootProjectEncryption (None|Target|Volume), tmcFileCopy.")]
    public Task<CallToolResult> GetSettings([Description(ProjectParam)] string? project = null)
        => session.RunAsync(s => Settings(PlcTree.ResolveProject(s, project)));

    [McpServerTool(Name = "plc_set_settings"), Description(
        "Changes PLC project settings (only the given values): bootProjectAutostart (start the boot project when TwinCAT starts), " +
        "bootProjectEncryption (None|Target|Volume), tmcFileCopy (copy the .tmc to the target).")]
    public Task<CallToolResult> SetSettings(
        [Description(ProjectParam)] string? project = null,
        [Description("Start the boot project automatically when TwinCAT starts (null = unchanged).")] bool? bootProjectAutostart = null,
        [Description("None | Target | Volume")] string? bootProjectEncryption = null,
        [Description("Copy the TMC file to the target on activation (null = unchanged).")] bool? tmcFileCopy = null)
        => session.RunAsync(s =>
        {
            var p = PlcTree.ResolveProject(s, project);
            var root = (ITcPlcProject)p.Root;
            if (bootProjectAutostart is not null)
            {
                root.BootProjectAutostart = bootProjectAutostart.Value;
            }

            if (!string.IsNullOrWhiteSpace(bootProjectEncryption))
            {
                root.BootProjectEncryption = bootProjectEncryption.Trim().ToLowerInvariant() switch
                {
                    "none" => TcEncryptionType.TcEncryptionType_None,
                    "target" => TcEncryptionType.TcEncryptionType_Target,
                    "volume" => TcEncryptionType.TcEncryptionType_Volume,
                    _ => throw new TcException($"Unknown encryption '{bootProjectEncryption}'.") { Hint = "Use None, Target or Volume." },
                };
            }

            if (tmcFileCopy is not null)
            {
                root.TmcFileCopy = tmcFileCopy.Value;
            }

            return Settings(p);
        });

    private static object Settings(PlcProjectRef p)
    {
        var root = (ITcPlcProject)p.Root;
        return new
        {
            project = p.Name,
            rootPath = p.RootPath,
            bootProjectAutostart = root.BootProjectAutostart,
            bootProjectEncryption = root.BootProjectEncryption switch
            {
                TcEncryptionType.TcEncryptionType_Target => "Target",
                TcEncryptionType.TcEncryptionType_Volume => "Volume",
                _ => "None",
            },
            tmcFileCopy = root.TmcFileCopy,
        };
    }

    [McpServerTool(Name = "plc_generate_boot_project"), Description(
        "Builds the boot project of a PLC project for the current target (ITcPlcProject.GenerateBootProject). " +
        "activate=true also activates it (it then starts with TwinCAT when bootProjectAutostart is set).")]
    public Task<CallToolResult> GenerateBootProject([Description(ProjectParam)] string? project = null, [Description("Also activate the boot project on the target (writes the target boot folder). Default false = generate locally only.")] bool activate = false, int timeoutSeconds = 600)
        => session.RunAsync(s =>
        {
            var p = PlcTree.ResolveProject(s, project);
            var result = PlcBuildOutput.Capture(s.Dte, () => ((ITcPlcProject)p.Root).GenerateBootProject(activate));
            return new { project = p.Name, generated = result.ErrorCount == 0, activated = activate && result.ErrorCount == 0, result.ErrorCount, result.Errors };
        }, timeoutSeconds);

    [McpServerTool(Name = "plc_get_task_reference", ReadOnly = true), Description(
        "Lists the PLC task references of a project (e.g. 'TIPC^Plc1^Plc1 Project^PlcTask') and the real-time task each is linked to ('TIRT^PlcTask').")]
    public Task<CallToolResult> GetTaskReference([Description(ProjectParam)] string? project = null)
        => session.RunAsync(s =>
        {
            var p = PlcTree.ResolveProject(s, project);
            return new
            {
                project = p.Name,
                tasks = TaskReferences(p).Select(t => new
                {
                    path = t.PathName,
                    linkedTask = PlcTree.Safe(() => ((ITcPlcTaskReference)t).LinkedTask),
                    programs = TreeInfo.Children(t).Select(c => c.Name).ToArray(),
                }).ToArray(),
            };
        });

    [McpServerTool(Name = "plc_set_task_reference"), Description(
        "Links a PLC task reference to a real-time task (ITcPlcTaskReference.LinkedTask), e.g. linkedTask='TIRT^FastTask'. " +
        "Create real-time tasks with the task_ tools.")]
    public Task<CallToolResult> SetTaskReference(
        [Description("Tree path of the real-time task, e.g. 'TIRT^PlcTask'.")] string linkedTask,
        [Description(ProjectParam)] string? project = null,
        [Description("Name or path of the PLC task reference (default: the only one, usually 'PlcTask').")] string? taskReference = null)
        => session.RunAsync(s =>
        {
            var p = PlcTree.ResolveProject(s, project);
            s.Lookup(linkedTask); // validate
            var refs = TaskReferences(p).ToList();
            ITcSmTreeItem target;
            if (string.IsNullOrWhiteSpace(taskReference))
            {
                target = refs.Count == 1
                    ? refs[0]
                    : throw new TcException($"The project has {refs.Count} task references.") { Hint = "Pass taskReference (see plc_get_task_reference)." };
            }
            else
            {
                target = refs.FirstOrDefault(r => string.Equals(r.Name, taskReference, StringComparison.OrdinalIgnoreCase)
                                                  || string.Equals(r.PathName, taskReference, StringComparison.OrdinalIgnoreCase))
                         ?? throw new TcException($"Task reference '{taskReference}' not found.") { Hint = "See plc_get_task_reference." };
            }

            ((ITcPlcTaskReference)target).LinkedTask = linkedTask;
            return new { path = target.PathName, linkedTask = ((ITcPlcTaskReference)target).LinkedTask };
        });
}
