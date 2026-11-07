using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TCatSysManagerLib;
using TwinCatMcp.Core;
using TwinCatMcp.Libraries;

namespace TwinCatMcp.Tools;

/// <summary>Packaging: save a PLC project as library, solution / project archives (.tnzip, .tszip, .tpzip).</summary>
[McpServerToolType]
public sealed class ArchiveTools(XaeSession session)
{
    [McpServerTool(Name = "archive_save_as_library"), Description(
        "Saves a PLC project as library file (ITcPlcIECProject6.SaveAsLibrary2). '.library' = source library, '.compiled-library' = " +
        "protected compiled library. Optionally encrypted (needs a connected software protection / user database) and installed into the " +
        "System repository. Set Company/Title/Version first with lib_set_project_info (required to install).")]
    public Task<CallToolResult> SaveAsLibrary(
        [Description("Target file, e.g. 'C:\\Libs\\AcmeMotion.library' or '...\\AcmeMotion.compiled-library'. The folder is created.")] string filePath,
        [Description("Install the library into the default repository after saving.")] bool install = false,
        [Description("Encrypt the library (only with software protection / user database).")] bool encrypt = false,
        [Description("Replace an existing file.")] bool overwrite = false,
        [Description(PlcLibraryContext.PlcProjectParameterDescription)] string? plcProject = null,
        int timeoutSeconds = 600)
        => session.RunAsync(s =>
        {
            var full = Path.GetFullPath(filePath);
            var ext = Path.GetExtension(full).ToLowerInvariant();
            if (ext is not (".library" or ".compiled-library"))
            {
                throw new TcException($"Invalid extension '{ext}'.") { Hint = "Use '.library' (source) or '.compiled-library' (compiled)." };
            }

            if (File.Exists(full))
            {
                if (!overwrite)
                {
                    throw new TcException($"File already exists: {full}") { Hint = "Pass overwrite=true or choose another file name." };
                }

                File.Delete(full);
            }

            var ctx = PlcLibraryContext.Resolve(s, plcProject);
            var info = LibraryProjectInfoXml.Parse(ctx.NestedProject.ProduceXml(false));
            if (install && (info.GetValueOrDefault("Title") is null || info.GetValueOrDefault("Version") is null || info.GetValueOrDefault("Company") is null))
            {
                throw new TcException("Installing a library requires Company, Title and Version in the project information.")
                {
                    Hint = "Call lib_set_project_info first, e.g. company='Acme', title='AcmeLib', version='1.0.0.0'.",
                };
            }

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            LibraryTools.Wrap(() =>
            {
                if (ctx.NestedProject is ITcPlcIECProject6 p6)
                {
                    p6.SaveAsLibrary2(full, encrypt, install);
                }
                else if (!encrypt)
                {
                    ((ITcPlcIECProject)ctx.NestedProject).SaveAsLibrary(full, install);
                }
                else
                {
                    throw new InvalidOperationException("Encrypted libraries need ITcPlcIECProject6 (TwinCAT 4026+).");
                }
            }, "Saving as library failed.", "Build the project first (build_solution) and fix compile errors.");

            return new
            {
                file = full,
                format = ext.TrimStart('.'),
                size = new FileInfo(full).Length,
                encrypted = encrypt,
                installed = install,
                title = info.GetValueOrDefault("Title"),
                version = info.GetValueOrDefault("Version"),
                company = info.GetValueOrDefault("Company"),
            };
        }, timeoutSeconds);

    [McpServerTool(Name = "archive_save_solution"), Description(
        "Saves the whole solution as a .tnzip archive (ITcArchivePersist.SaveAsArchive via DTE.GetObject(\"TcArchivePersist\")): all " +
        "project files plus the referenced libraries - restorable on another PC with archive_open. scope='twincatProject' instead writes " +
        "only the selected TwinCAT project as .tszip (ITcSysManager.SaveAsArchive). Save first (solution_save).")]
    public Task<CallToolResult> SaveSolution(
        [Description("Target file, e.g. 'C:\\Archives\\Machine.tnzip' (.tszip for scope='twincatProject'). The folder is created.")] string filePath,
        [Description("'solution' (.tnzip, default) or 'twincatProject' (.tszip).")] string scope = "solution",
        [Description("Replace an existing file.")] bool overwrite = false,
        int timeoutSeconds = 600)
        => session.RunAsync(s =>
        {
            var full = Path.GetFullPath(filePath);
            var project = scope.Equals("twincatProject", StringComparison.OrdinalIgnoreCase);
            if (!project && !scope.Equals("solution", StringComparison.OrdinalIgnoreCase))
            {
                throw new TcException($"Unknown scope '{scope}'.") { Hint = "Use 'solution' or 'twincatProject'." };
            }

            var expected = project ? ".tszip" : ".tnzip";
            if (!string.Equals(Path.GetExtension(full), expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new TcException($"The archive file must have the extension {expected}: {full}");
            }

            if (File.Exists(full))
            {
                if (!overwrite)
                {
                    throw new TcException($"File already exists: {full}") { Hint = "Pass overwrite=true or choose another file name." };
                }

                File.Delete(full);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            LibraryTools.Wrap(() =>
            {
                if (project)
                {
                    s.SysManager.SaveAsArchive(full);
                }
                else
                {
                    ((ITcArchivePersist)s.Dte.GetObject("TcArchivePersist")).SaveAsArchive(full);
                }
            }, "Saving the archive failed.", "Save the solution first (solution_save).");
            return new { file = full, scope = project ? "twincatProject" : "solution", size = new FileInfo(full).Length };
        }, timeoutSeconds);

    [McpServerTool(Name = "archive_open"), Description(
        "Opens an archive. .tnzip (solution archive): extracted into targetFolder and opened as the current solution " +
        "(ITcArchivePersist.LoadFromArchive) - the previously open solution is closed without saving, its first TwinCAT project is selected. " +
        ".tszip (TwinCAT project archive): added as new TwinCAT project to the open solution (Solution.AddFromTemplate). " +
        "For .tpzip PLC project archives use archive_import_plc_project.")]
    public Task<CallToolResult> Open(
        [Description("Archive file (.tnzip or .tszip).")] string archivePath,
        [Description("Folder to extract into (created if missing, should be empty).")] string targetFolder,
        [Description("Project name for a .tszip (default: archive file name).")] string? projectName = null,
        int timeoutSeconds = 600)
        => session.RunAsync(s =>
        {
            var archive = Path.GetFullPath(archivePath);
            if (!File.Exists(archive))
            {
                throw new TcException($"Archive not found: {archive}");
            }

            var folder = Path.GetFullPath(targetFolder);
            Directory.CreateDirectory(folder);
            var ext = Path.GetExtension(archive).ToLowerInvariant();
            switch (ext)
            {
                case ".tnzip":
                {
                    if (Directory.EnumerateFileSystemEntries(folder).Any())
                    {
                        throw new TcException($"Target folder is not empty: {folder}") { Hint = "Use a new or empty folder." };
                    }

                    LibraryTools.Wrap(() => ((ITcArchivePersist)s.Dte.GetObject("TcArchivePersist")).LoadFromArchive(archive, folder),
                        "Opening the archive failed.", null);
                    var tc = s.EnumerateTwinCatProjects().FirstOrDefault()
                             ?? throw new TcException("The archive was opened but contains no TwinCAT project.");
                    s.SelectProject(tc.Name);
                    return new
                    {
                        solution = s.Dte.Solution.FullName,
                        selectedProject = tc.Name,
                        twinCatProjects = s.EnumerateTwinCatProjects().Select(p => p.FullName).ToArray(),
                    };
                }

                case ".tszip":
                {
                    if (!s.Dte.Solution.IsOpen)
                    {
                        throw new TcException("A .tszip is added to the open solution, but no solution is open.") { Hint = "solution_create or solution_open first." };
                    }

                    var name = string.IsNullOrWhiteSpace(projectName) ? Path.GetFileNameWithoutExtension(archive) : projectName;
                    LibraryTools.Wrap(() => s.Dte.Solution.AddFromTemplate(archive, Path.Combine(folder, name), name),
                        "Adding the TwinCAT project archive failed.", "The project name must be unique in the solution.");
                    var tc = s.EnumerateTwinCatProjects().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                             ?? s.EnumerateTwinCatProjects().Last();
                    s.SelectProject(tc.Name);
                    return new { solution = s.Dte.Solution.FullName, selectedProject = tc.Name, project = tc.FullName };
                }

                default:
                    throw new TcException($"Unsupported archive type '{ext}'.") { Hint = "Use .tnzip / .tszip here, archive_import_plc_project for .tpzip." };
            }
        }, timeoutSeconds);

    [McpServerTool(Name = "archive_export_plc_project"), Description(
        "Exports a whole PLC project as .tpzip archive (ITcSmTreeItem.ExportChild on TIPC) - sources, references and library files. " +
        "Re-import with archive_import_plc_project.")]
    public Task<CallToolResult> ExportPlcProject(
        [Description("PLC project name below TIPC, e.g. 'Plc1'.")] string plcName,
        [Description("Target .tpzip file. The folder is created.")] string filePath,
        bool overwrite = false,
        int timeoutSeconds = 600)
        => session.RunAsync(s =>
        {
            var full = Path.GetFullPath(filePath);
            if (!string.Equals(Path.GetExtension(full), ".tpzip", StringComparison.OrdinalIgnoreCase))
            {
                throw new TcException($"The archive file must have the extension .tpzip: {full}");
            }

            if (File.Exists(full))
            {
                if (!overwrite)
                {
                    throw new TcException($"File already exists: {full}") { Hint = "Pass overwrite=true or choose another file name." };
                }

                File.Delete(full);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            var tipc = s.Lookup("TIPC");
            s.Lookup("TIPC^" + plcName);
            LibraryTools.Wrap(() => tipc.ExportChild(plcName, full), $"Exporting PLC project '{plcName}' failed.", null);
            return new { plcName, file = full, size = new FileInfo(full).Length };
        }, timeoutSeconds);

    [McpServerTool(Name = "archive_import_plc_project"), Description(
        "Adds a PLC project from a .tpzip archive or an existing .plcproj to the TwinCAT project (CreateChild on TIPC). " +
        "mode: 'copy' (copy into the solution folder, default), 'move', or 'original' (use the project at its location; .plcproj only).")]
    public Task<CallToolResult> ImportPlcProject(
        [Description("Path of the .tpzip or .plcproj file.")] string filePath,
        [Description("Name of the new PLC project (ignored for mode 'original').")] string name,
        [Description("'copy' (default), 'move' or 'original'.")] string mode = "copy",
        int timeoutSeconds = 600)
        => session.RunAsync(s =>
        {
            var full = Path.GetFullPath(filePath);
            if (!File.Exists(full))
            {
                throw new TcException($"File not found: {full}");
            }

            var subType = mode.ToLowerInvariant() switch
            {
                "copy" => 0,
                "move" => 1,
                "original" => 2,
                _ => throw new TcException($"Unknown mode '{mode}'.") { Hint = "Use copy, move or original." },
            };
            ITcSmTreeItem? created = null;
            LibraryTools.Wrap(() => created = s.Lookup("TIPC").CreateChild(subType == 2 ? "" : name, subType, "", full),
                $"Importing '{full}' failed.", "The name must be unique below TIPC.");
            return TreeInfo.Describe(created!);
        }, timeoutSeconds);
}
