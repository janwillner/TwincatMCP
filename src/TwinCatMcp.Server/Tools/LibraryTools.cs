using System.ComponentModel;
using System.Runtime.InteropServices;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TCatSysManagerLib;
using TwinCatMcp.Core;
using TwinCatMcp.Libraries;

namespace TwinCatMcp.Tools;

/// <summary>PLC library references, placeholders, repositories and library signatures (ITcPlcLibraryManager2).</summary>
[McpServerToolType]
public sealed class LibraryTools(XaeSession session)
{
    private const string PlcDesc = PlcLibraryContext.PlcProjectParameterDescription;

    // ------------------------------------------------------------------ references

    [McpServerTool(Name = "lib_list_references", ReadOnly = true), Description(
        "Lists the library references and placeholders of a PLC project (References node) with requested version ('*' = newest), " +
        "effective (resolved) version, distributor, namespace, default resolution and explicit/frozen placeholder redirections. " +
        "Use before lib_add/lib_remove and to check which libraries a project uses.")]
    public Task<CallToolResult> ListReferences(
        [Description(PlcDesc)] string? plcProject = null,
        [Description("Include the resolved dependencies of every reference (display names).")] bool includeDependencies = false)
        => session.RunAsync(s =>
        {
            var ctx = PlcLibraryContext.Resolve(s, plcProject);
            var doc = ctx.ReadReferences(includeDependencies);
            return new
            {
                plcProject = ctx.NestedProjectPath,
                referencesPath = ctx.ReferencesItem.PathName,
                count = doc.References.Count,
                references = doc.References,
                unresolved = doc.References.Where(r => r.Unresolved).Select(r => r.ItemName).ToArray() is { Length: > 0 } u ? u : null,
            };
        });

    [McpServerTool(Name = "lib_add"), Description(
        "Adds one or more library references to a PLC project (ITcPlcLibraryManager.AddLibrary). Each entry is a TwinCAT display name " +
        "'Name, Version (Distributor)', e.g. 'Tc2_MC2, * (Beckhoff Automation GmbH)'; version '*' = always newest installed. " +
        "'Tc2_MC2' alone works too: version defaults to '*' and the distributor is looked up in the installed libraries. " +
        "Adding an already referenced library is reported as alreadyReferenced (no error). Use lib_list_installed to find names.")]
    public Task<CallToolResult> Add(
        [Description("Libraries, e.g. ['Tc2_MC2', 'Tc2_Utilities, * (Beckhoff Automation GmbH)', 'Tc2_Math, 3.5.1.0 (Beckhoff Automation GmbH)'].")] string[] libraries,
        [Description(PlcDesc)] string? plcProject = null)
        => session.RunAsync(s =>
        {
            var ctx = PlcLibraryContext.Resolve(s, plcProject);
            var catalog = new Lazy<List<LibraryId>>(() => Catalog(ctx));
            var results = libraries.Select(text => (object)AddOne(ctx, text, catalog)).ToList();
            ThrowIfAllFailed(results, "No library could be added.", "Check names with lib_list_installed (or lib_scan). Format: 'Name, Version (Distributor)'.");
            return new { plcProject = ctx.NestedProjectPath, results };
        });

    [McpServerTool(Name = "lib_add_placeholder"), Description(
        "Adds a placeholder reference (ITcPlcLibraryManager.AddPlaceholder). Without 'library' the placeholder must be a known default " +
        "placeholder of the system (e.g. 'Tc2_Utilities'); with 'library' a new placeholder resolving to that library is created, " +
        "e.g. placeholderName='Placeholder_NC', library='Tc2_NC, * (Beckhoff Automation GmbH)'.")]
    public Task<CallToolResult> AddPlaceholder(
        [Description("Placeholder name, e.g. 'Tc2_Utilities' or a custom name.")] string placeholderName,
        [Description("Default resolution 'Name, Version (Distributor)' (version '*' = newest). Optional for well-known placeholders.")] string? library = null,
        [Description(PlcDesc)] string? plcProject = null)
        => session.RunAsync(s =>
        {
            var ctx = PlcLibraryContext.Resolve(s, plcProject);
            if (string.IsNullOrWhiteSpace(library))
            {
                Wrap(() => ctx.Manager.AddPlaceholder(placeholderName), $"Adding placeholder '{placeholderName}' failed.",
                    "Unknown placeholders need a 'library' (default resolution).");
            }
            else
            {
                var id = Complete(ctx, LibraryId.Parse(library), null);
                Wrap(() => ctx.Manager.AddPlaceholder(placeholderName, id.Name, id.VersionOrStar, id.Distributor),
                    $"Adding placeholder '{placeholderName}' -> '{id.DisplayName}' failed.", "Check the library with lib_list_installed.");
            }

            return ctx.ReadReferences().References.FirstOrDefault(r => Eq(r.PlaceholderName, placeholderName))
                   ?? (object)new { placeholderName, added = true };
        });

    [McpServerTool(Name = "lib_remove", Destructive = true), Description(
        "Removes library or placeholder references from a PLC project. Pass the library name ('Tc2_MC2'), the display name " +
        "('Tc2_MC2, 3.3.72.0 (Beckhoff Automation GmbH)' - needed if several versions are referenced) or the placeholder name ('Tc2_Utilities').")]
    public Task<CallToolResult> Remove(
        [Description("References to remove, e.g. ['Tc2_MC2', 'Placeholder_NC'].")] string[] references,
        [Description(PlcDesc)] string? plcProject = null)
        => session.RunAsync(s =>
        {
            var ctx = PlcLibraryContext.Resolve(s, plcProject);
            var results = new List<object>();
            foreach (var text in references)
            {
                try
                {
                    var target = ctx.ReadReferences().FindForRemoval(text)
                                 ?? throw new TcException($"'{text}' is not referenced.");
                    if (target.Version is null)
                    {
                        ctx.Manager.RemoveReference(target.Name);
                    }
                    else
                    {
                        ctx.Manager.RemoveReference(target.Name, target.Version, target.Distributor ?? "");
                    }

                    results.Add(new { reference = text, ok = true, removed = target.Version is null ? target.Name : target.DisplayName });
                }
                catch (Exception ex) when ((ex is TcException or COMException or ArgumentException or AmbiguousLibraryException) && !XaeSession.IsSessionFatal(ex))
                {
                    results.Add(new { reference = text, ok = false, error = ex.Message });
                }
            }

            ThrowIfAllFailed(results, "No reference could be removed.", "Use lib_list_references to see the exact names.");
            return new { plcProject = ctx.NestedProjectPath, results };
        });

    [McpServerTool(Name = "lib_set_resolution"), Description(
        "Sets the effective resolution of a placeholder (ITcPlcLibraryManager.SetEffectiveResolution), i.e. which library version it " +
        "points to in this project, e.g. placeholderName='Tc2_Standard', library='Tc2_Standard, 3.4.5.0 (Beckhoff Automation GmbH)'. " +
        "Only placeholders can be redirected (not plain library references).")]
    public Task<CallToolResult> SetResolution(
        [Description("Placeholder name as listed by lib_list_references.")] string placeholderName,
        [Description("Library 'Name, Version (Distributor)'; version '*' = newest.")] string library,
        [Description(PlcDesc)] string? plcProject = null)
        => session.RunAsync(s =>
        {
            var ctx = PlcLibraryContext.Resolve(s, plcProject);
            var id = Complete(ctx, LibraryId.Parse(library), null);
            Wrap(() => ctx.Manager.SetEffectiveResolution(placeholderName, id.Name, id.VersionOrStar, id.Distributor),
                $"Setting the resolution of '{placeholderName}' failed.", "Use lib_list_references for placeholder names and lib_list_installed for versions.");
            var doc = ctx.ReadReferences();
            return new
            {
                placeholderName,
                requested = id.DisplayName,
                redirection = doc.Redirections.GetValueOrDefault(placeholderName),
                reference = doc.References.FirstOrDefault(r => Eq(r.PlaceholderName, placeholderName)),
            };
        });

    [McpServerTool(Name = "lib_freeze_placeholders"), Description(
        "Freezes placeholder versions (ITcPlcLibraryManager.FreezePlaceholder): the currently effective version is pinned, so later " +
        "library installations do not change the project. Without placeholderName all placeholders are frozen. Returns all redirections.")]
    public Task<CallToolResult> FreezePlaceholders(
        [Description("Placeholder to freeze; empty = all placeholders.")] string? placeholderName = null,
        [Description(PlcDesc)] string? plcProject = null)
        => session.RunAsync(s =>
        {
            var ctx = PlcLibraryContext.Resolve(s, plcProject);
            Wrap(() =>
            {
                if (string.IsNullOrWhiteSpace(placeholderName))
                {
                    ctx.Manager.FreezePlaceholder();
                }
                else
                {
                    ctx.Manager.FreezePlaceholder(placeholderName);
                }
            }, "Freezing placeholders failed.", "Use lib_list_references for placeholder names.");
            return new { frozen = string.IsNullOrWhiteSpace(placeholderName) ? "all" : placeholderName, redirections = ctx.ReadReferences().Redirections };
        });

    // ------------------------------------------------------------------ repositories

    [McpServerTool(Name = "lib_list_repositories", ReadOnly = true), Description(
        "Lists the PLC library repositories in search order (index 0 first) with their root folders. 'System' is the default repository.")]
    public Task<CallToolResult> ListRepositories([Description(PlcDesc)] string? plcProject = null)
        => session.RunAsync(s => new { repositories = Repositories(PlcLibraryContext.Resolve(s, plcProject)) });

    [McpServerTool(Name = "lib_insert_repository"), Description(
        "Registers an additional library repository folder (ITcPlcLibraryManager.InsertRepository). NOTE: repositories are a machine/user-wide " +
        "TwinCAT setting, not part of the project. The folder is created if missing.")]
    public Task<CallToolResult> InsertRepository(
        [Description("Unique repository name, e.g. 'MyCompanyLibs'.")] string name,
        [Description("Root folder of the repository, e.g. 'C:\\TwinCAT\\Libraries'.")] string folder,
        [Description("Position in the search order (0 = first). -1 = append at the end.")] int index = -1,
        [Description(PlcDesc)] string? plcProject = null)
        => session.RunAsync(s =>
        {
            var ctx = PlcLibraryContext.Resolve(s, plcProject);
            var full = Path.GetFullPath(folder);
            Directory.CreateDirectory(full);
            var count = ctx.Manager.Repositories.Count;
            var at = index < 0 || index > count ? count : index;
            Wrap(() => ctx.Manager.InsertRepository(name, full, at), $"Inserting repository '{name}' failed.", "The name must be unique (lib_list_repositories).");
            return new { inserted = name, folder = full, repositories = Repositories(ctx) };
        });

    [McpServerTool(Name = "lib_move_repository"), Description("Moves a library repository to another position of the search order (0 = first).")]
    public Task<CallToolResult> MoveRepository(
        [Description("Repository name.")] string name,
        [Description("New 0-based index.")] int newIndex,
        [Description(PlcDesc)] string? plcProject = null)
        => session.RunAsync(s =>
        {
            var ctx = PlcLibraryContext.Resolve(s, plcProject);
            Wrap(() => ctx.Manager.MoveRepository(name, newIndex), $"Moving repository '{name}' failed.", "Use lib_list_repositories for names and indexes.");
            return new { moved = name, repositories = Repositories(ctx) };
        });

    [McpServerTool(Name = "lib_remove_repository", Destructive = true), Description(
        "Unregisters a library repository (ITcPlcLibraryManager.RemoveRepository). The folder and its files are NOT deleted.")]
    public Task<CallToolResult> RemoveRepository(
        [Description("Repository name.")] string name,
        [Description(PlcDesc)] string? plcProject = null)
        => session.RunAsync(s =>
        {
            var ctx = PlcLibraryContext.Resolve(s, plcProject);
            if (string.Equals(name, "System", StringComparison.OrdinalIgnoreCase))
            {
                throw new TcException("The 'System' repository must not be removed.");
            }

            Wrap(() => ctx.Manager.RemoveRepository(name), $"Removing repository '{name}' failed.", "Use lib_list_repositories.");
            return new { removed = name, repositories = Repositories(ctx) };
        });

    // ------------------------------------------------------------------ install / catalog

    [McpServerTool(Name = "lib_install"), Description(
        "Installs a .library or .compiled-library file into a repository (ITcPlcLibraryManager.InstallLibrary). The library's project " +
        "information (Title, Version, Company) determines where it lands: <repo>\\<Company>\\<Title>\\<Version>. Afterwards it can be referenced with lib_add.")]
    public Task<CallToolResult> Install(
        [Description("Full path of the .library / .compiled-library file.")] string filePath,
        [Description("Target repository name (see lib_list_repositories). Default 'System'.")] string repository = "System",
        [Description("Overwrite an already installed library with the same name/version/company.")] bool overwrite = false,
        [Description(PlcDesc)] string? plcProject = null)
        => session.RunAsync(s =>
        {
            var full = Path.GetFullPath(filePath);
            if (!File.Exists(full))
            {
                throw new TcException($"Library file not found: {full}");
            }

            var ctx = PlcLibraryContext.Resolve(s, plcProject);
            var repo = FindRepository(ctx, repository);
            var before = LibraryRepositoryScanner.Scan(repo.Name, repo.Folder).Select(l => l.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Wrap(() => ctx.Manager.InstallLibrary(repo.Name, full, overwrite), $"Installing '{full}' into '{repo.Name}' failed.",
                "Set overwrite=true to replace an installed version. The library needs Title, Version and Company (lib_set_project_info before saving).");
            var after = LibraryRepositoryScanner.Scan(repo.Name, repo.Folder);
            var installed = after.Where(l => !before.Contains(l.File)).ToList();
            if (installed.Count == 0)
            {
                // overwrite of an existing version: report entries whose file name matches
                installed = after.Where(l => string.Equals(Path.GetFileNameWithoutExtension(l.File), Path.GetFileNameWithoutExtension(full), StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return new { repository = repo.Name, installed = installed.Select(Compact).ToList() };
        }, TimeSpan.FromMinutes(5));

    [McpServerTool(Name = "lib_uninstall", Destructive = true), Description(
        "Uninstalls a library version from a repository (ITcPlcLibraryManager.UninstallLibrary), e.g. library='MyLib, 1.0.0.0 (My Company)'. " +
        "If the version is omitted and exactly one version is installed, that one is removed.")]
    public Task<CallToolResult> Uninstall(
        [Description("Repository name, e.g. 'System'.")] string repository,
        [Description("Library 'Name, Version (Distributor)'.")] string library,
        [Description(PlcDesc)] string? plcProject = null)
        => session.RunAsync(s =>
        {
            var ctx = PlcLibraryContext.Resolve(s, plcProject);
            var repo = FindRepository(ctx, repository);
            var id = LibraryId.Parse(library);
            var matches = LibraryRepositoryScanner.Scan(repo.Name, repo.Folder)
                .Where(l => Eq(l.Name, id.Name) && (id.Distributor is null || Eq(l.Distributor, id.Distributor)))
                .Where(l => LibraryVersion.IsLatest(id.Version) || LibraryVersion.Compare(l.Version, id.Version) == 0)
                .ToList();
            if (matches.Count == 0)
            {
                // The Automation Interface throws a NullReferenceException for unknown libraries - report it properly instead.
                throw new TcException($"'{id.DisplayName}' is not installed in repository '{repo.Name}'.") { Hint = "Use lib_list_installed with repository filter." };
            }

            if (matches.Count > 1)
            {
                throw new TcException($"Several versions match: {string.Join("; ", matches.Select(m => m.DisplayName))}.") { Hint = "Pass the exact version." };
            }

            var lib = matches[0];
            Wrap(() => ctx.Manager.UninstallLibrary(repo.Name, lib.Name, lib.Version, lib.Distributor), $"Uninstalling '{lib.DisplayName}' failed.", null);
            return new { repository = repo.Name, uninstalled = lib.DisplayName };
        }, TimeSpan.FromMinutes(5));

    [McpServerTool(Name = "lib_list_installed", ReadOnly = true), Description(
        "Lists installed PLC libraries (catalog) from the repository folders: name, version, distributor, repository and file kind " +
        "(library = source, compiled-library = protected). Read-only. Filter by name/distributor/repository, optionally only the newest " +
        "version per library. Works without an open PLC project (then the default System repository is scanned).")]
    public Task<CallToolResult> ListInstalled(
        [Description("Substring of the library name, e.g. 'MC2' or 'Tc2_'.")] string? nameContains = null,
        [Description("Substring of the distributor, e.g. 'Beckhoff'.")] string? distributor = null,
        [Description("Only this repository (name).")] string? repository = null,
        [Description("Only the newest version of each library.")] bool latestOnly = false,
        int maxResults = 300,
        [Description(PlcDesc)] string? plcProject = null)
        => session.RunAsync(s =>
        {
            var ctx = PlcLibraryContext.TryResolve(s, plcProject);
            var repos = ctx is not null
                ? Repositories(ctx)
                : [new LibraryRepository(0, "System", LibraryRepositoryScanner.DefaultSystemRepositoryFolder)];
            if (!string.IsNullOrWhiteSpace(repository))
            {
                repos = repos.Where(r => Eq(r.Name, repository)).ToList();
                if (repos.Count == 0)
                {
                    throw new TcException($"Repository '{repository}' not found.") { Hint = "Use lib_list_repositories." };
                }
            }

            var all = repos.SelectMany(r => LibraryRepositoryScanner.Scan(r.Name, r.Folder));
            var filtered = LibraryRepositoryScanner.Filter(all, nameContains, distributor, latestOnly);
            return new
            {
                repositories = repos.Select(r => r.Name).ToArray(),
                total = filtered.Count,
                truncated = filtered.Count > maxResults ? true : (bool?)null,
                libraries = filtered.Take(Math.Max(1, maxResults)).Select(Compact).ToList(),
            };
        });

    [McpServerTool(Name = "lib_scan", ReadOnly = true), Description(
        "Asks TwinCAT for all libraries available in all repositories (ITcPlcLibraryManager.ScanLibraries) - the authoritative catalog " +
        "of the Automation Interface (name, version, distributor, display name). Optionally filtered by name substring.")]
    public Task<CallToolResult> Scan(
        [Description("Substring of the library name.")] string? nameContains = null,
        int maxResults = 300,
        [Description(PlcDesc)] string? plcProject = null)
        => session.RunAsync(s =>
        {
            var ctx = PlcLibraryContext.Resolve(s, plcProject);
            var list = Catalog(ctx)
                .Where(l => string.IsNullOrWhiteSpace(nameContains) || l.Name.Contains(nameContains.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(l => l.Version, Comparer<string?>.Create(LibraryVersion.Compare))
                .ToList();
            return new
            {
                total = list.Count,
                libraries = list.Take(Math.Max(1, maxResults)).Select(l => new { name = l.Name, version = l.Version, distributor = l.Distributor, displayName = l.DisplayName }).ToList(),
            };
        }, TimeSpan.FromMinutes(5));

    // ------------------------------------------------------------------ signatures / declarations

    [McpServerTool(Name = "lib_get_signatures", ReadOnly = true), Description(
        "Returns the public signatures (function blocks, functions, types, interfaces, GVLs with inputs/outputs/in-outs and comments) of " +
        "referenced libraries (ITcPlcLibraryManager2.ProduceLibrarySignatures). Use it to discover what a library offers. " +
        "Includes libraries pulled in as dependencies. Filter with namePattern ('MC_Move*') and kind.")]
    public Task<CallToolResult> GetSignatures(
        [Description("Library name or placeholder (e.g. 'Tc2_MC2'); empty = all referenced libraries.")] string? library = null,
        [Description("Exact name or wildcard pattern with * and ?, e.g. 'MC_Move*'.")] string? namePattern = null,
        [Description("FunctionBlock, Function, Type, Interface or VarGlobal.")] string? kind = null,
        [Description("Include inputs/outputs/in-outs (false = names and kinds only).")] bool includeVariables = true,
        int maxResults = 100,
        [Description(PlcDesc)] string? plcProject = null)
        => session.RunAsync(s =>
        {
            var ctx = PlcLibraryContext.Resolve(s, plcProject);
            var libs = Signatures(ctx, library);
            var hits = LibrarySignatures.Find(libs, namePattern, kind);
            return new
            {
                libraries = libs.Select(l => new LibraryId(l.LibraryName, l.Version, l.Distributor).DisplayName).ToArray(),
                total = hits.Count,
                truncated = hits.Count > maxResults ? true : (bool?)null,
                entries = hits.Take(Math.Max(1, maxResults)).Select(h => includeVariables
                    ? (object)new
                    {
                        library = h.Library.LibraryName,
                        kind = h.Entry.Kind,
                        name = h.Entry.Name,
                        comment = h.Entry.Comment,
                        returnType = h.Entry.ReturnType,
                        inputs = h.Entry.Inputs,
                        outputs = h.Entry.Outputs,
                        inOuts = h.Entry.InOuts,
                        constants = h.Entry.Constants,
                    }
                    : new { library = h.Library.LibraryName, kind = h.Entry.Kind, name = h.Entry.Name }).ToList(),
            };
        }, TimeSpan.FromMinutes(5));

    [McpServerTool(Name = "lib_get_declaration", ReadOnly = true), Description(
        "Returns the IEC 61131-3 declaration (VAR_INPUT/VAR_OUTPUT/VAR_IN_OUT with types and comments) of library objects, e.g. " +
        "objects=['MC_MoveAbsolute'], library='Tc2_MC2' -> 'FUNCTION_BLOCK MC_MoveAbsolute ...'. Use it to call library FBs/functions " +
        "correctly. The library must be referenced (lib_add) - its dependencies are found as well. Generated from the library signature.")]
    public Task<CallToolResult> GetDeclaration(
        [Description("Object names, e.g. ['MC_MoveAbsolute', 'MC_Power'] or ['TON'].")] string[] objects,
        [Description("Library name or placeholder to search in; empty = all referenced libraries.")] string? library = null,
        [Description(PlcDesc)] string? plcProject = null)
        => session.RunAsync(s =>
        {
            var ctx = PlcLibraryContext.Resolve(s, plcProject);
            var libs = Signatures(ctx, library);
            var declarations = new List<object>();
            var missing = new List<string>();
            foreach (var name in objects)
            {
                var hit = LibrarySignatures.Find(libs, name).FirstOrDefault();
                if (hit.Entry is null)
                {
                    missing.Add(name);
                    continue;
                }

                declarations.Add(new
                {
                    name = hit.Entry.Name,
                    library = hit.Library.LibraryName,
                    libraryVersion = hit.Library.Version,
                    kind = hit.Entry.Kind,
                    declaration = LibrarySignatures.ToIec(hit.Entry),
                });
            }

            if (declarations.Count == 0)
            {
                var similar = objects.SelectMany(o => LibrarySignatures.Find(libs, $"*{o}*")).Take(15).Select(h => h.Entry.Name).Distinct().ToArray();
                throw new TcException($"Not found in {(library is null ? "the referenced libraries" : $"'{library}'")}: {string.Join(", ", objects)}.")
                {
                    Hint = similar.Length > 0
                        ? $"Similar names: {string.Join(", ", similar)}."
                        : "Reference the library first (lib_add) or browse with lib_get_signatures.",
                };
            }

            return new { declarations, notFound = missing.Count > 0 ? missing : null };
        }, TimeSpan.FromMinutes(5));

    // ------------------------------------------------------------------ project info (library properties)

    [McpServerTool(Name = "lib_get_project_info", ReadOnly = true), Description(
        "Reads the project information of a PLC project (Company, Title, Version, Author, Description, DefaultNamespace, Released, ...). " +
        "These properties identify the library when the project is saved/installed as a library (archive_save_as_library).")]
    public Task<CallToolResult> GetProjectInfo([Description(PlcDesc)] string? plcProject = null)
        => session.RunAsync(s =>
        {
            var ctx = PlcLibraryContext.Resolve(s, plcProject);
            return new { plcProject = ctx.NestedProjectPath, info = LibraryProjectInfoXml.Parse(ctx.NestedProject.ProduceXml(false)) };
        });

    [McpServerTool(Name = "lib_set_project_info"), Description(
        "Sets project information (library properties) of a PLC project. Only passed values change; '' clears a field. " +
        "For a library, Company, Title and Version are required to install it, e.g. company='Acme', title='AcmeMotion', version='1.0.0.0'.")]
    public Task<CallToolResult> SetProjectInfo(
        [Description("Company / distributor, e.g. 'Acme GmbH'.")] string? company = null,
        [Description("Library title (= library name), e.g. 'AcmeMotion'.")] string? title = null,
        [Description("Version 'major.minor.build.revision', e.g. '1.0.0.0'.")] string? version = null,
        [Description("Author shown in the library information.")] string? author = null,
        [Description("Free-text library description shown in the Library Manager.")] string? description = null,
        [Description("Default namespace of the library (default: the title).")] string? defaultNamespace = null,
        [Description("Mark the project as released (protection against modification).")] bool? released = null,
        [Description("Documentation format, e.g. 'TcDocGen'.")] string? docFormat = null,
        [Description(PlcDesc)] string? plcProject = null)
        => session.RunAsync(s =>
        {
            var ctx = PlcLibraryContext.Resolve(s, plcProject);
            var values = new Dictionary<string, string?>
            {
                ["Company"] = company,
                ["Title"] = title,
                ["Version"] = version,
                ["DefaultNamespace"] = defaultNamespace,
                ["Author"] = author,
                ["Description"] = description,
                ["Released"] = released is null ? null : released.Value ? "true" : "false",
                ["DocFormat"] = docFormat,
            };
            if (values.Values.All(v => v is null))
            {
                throw new TcException("Nothing to set.") { Hint = "Pass at least one of company, title, version, author, description, defaultNamespace, released, docFormat." };
            }

            ctx.NestedProject.ConsumeXml(LibraryProjectInfoXml.BuildConsumeXml(values));
            string? xmlError = null;
            try { xmlError = ((ITcSmTreeItem3)ctx.NestedProject).GetLastXmlError(); } catch { /* not available */ }
            return new
            {
                plcProject = ctx.NestedProjectPath,
                info = LibraryProjectInfoXml.Parse(ctx.NestedProject.ProduceXml(false)),
                lastXmlError = string.IsNullOrWhiteSpace(xmlError) ? null : xmlError,
            };
        });

    // ------------------------------------------------------------------ helpers (STA)

    private static object AddOne(PlcLibraryContext ctx, string text, Lazy<List<LibraryId>> catalog)
    {
        LibraryId? id = null;
        try
        {
            id = Complete(ctx, LibraryId.Parse(text), catalog);
            ctx.Manager.AddLibrary(id.Name, id.VersionOrStar, id.Distributor);
            return new { library = text, ok = true, added = id.DisplayName };
        }
        catch (ArgumentException ex) when (ex.Message.Contains("already contained", StringComparison.OrdinalIgnoreCase))
        {
            return new { library = text, ok = true, added = id?.DisplayName, alreadyReferenced = true };
        }
        catch (Exception ex) when ((ex is TcException or COMException or ArgumentException or FormatException) && !XaeSession.IsSessionFatal(ex))
        {
            return new { library = text, ok = false, error = ex.Message };
        }
    }

    /// <summary>Fills in a missing distributor from the installed catalog (newest version of that name).</summary>
    private static LibraryId Complete(PlcLibraryContext ctx, LibraryId id, Lazy<List<LibraryId>>? catalog)
    {
        if (id.Distributor is not null)
        {
            return id;
        }

        var list = catalog?.Value ?? Catalog(ctx);
        var newest = LibraryVersion.PickNewest(list, id.Name)
                     ?? throw new TcException($"Library '{id.Name}' is not installed.")
                     {
                         Hint = "Use lib_list_installed / lib_scan to find the exact name, or pass 'Name, Version (Distributor)'.",
                     };
        return id with { Name = newest.Name, Distributor = newest.Distributor };
    }

    private static List<LibraryId> Catalog(PlcLibraryContext ctx)
    {
        var result = new List<LibraryId>();
        foreach (ITcPlcLibRef r in ctx.Manager.ScanLibraries())
        {
            if (r is ITcPlcLibrary lib)
            {
                result.Add(new LibraryId(string.IsNullOrEmpty(lib.LibraryName) ? lib.Name : lib.LibraryName, lib.Version, lib.Distributor));
            }
        }

        return result;
    }

    private static List<LibraryRepository> Repositories(PlcLibraryContext ctx)
    {
        var repos = ctx.Manager.Repositories;
        var list = new List<LibraryRepository>();
        for (var i = 0; i < repos.Count; i++)
        {
            var r = repos[i]; // 0-based (verified on 4026)
            list.Add(new LibraryRepository(i, r.Name, r.Folder));
        }

        return list;
    }

    private static LibraryRepository FindRepository(PlcLibraryContext ctx, string name)
        => Repositories(ctx).FirstOrDefault(r => Eq(r.Name, name))
           ?? throw new TcException($"Repository '{name}' not found.") { Hint = "Use lib_list_repositories (or lib_insert_repository)." };

    /// <summary>Signatures of all referenced libraries (incl. dependencies), optionally only one library (name or placeholder).</summary>
    private static List<LibrarySignatureSet> Signatures(PlcLibraryContext ctx, string? library)
    {
        var all = LibrarySignatures.Parse(ctx.Manager.ProduceAllLibrarySignatures());
        if (string.IsNullOrWhiteSpace(library))
        {
            return all;
        }

        var wanted = LibraryId.Parse(library).Name;
        var placeholder = ctx.ReadReferences().References.FirstOrDefault(r => Eq(r.PlaceholderName, wanted));
        var libName = placeholder?.LibraryName ?? wanted;
        var match = all.Where(l => Eq(l.LibraryName, libName)).ToList();
        if (match.Count == 0)
        {
            throw new TcException($"Library '{library}' is not referenced by {ctx.NestedProjectPath} (nor a dependency).")
            {
                Hint = $"Add it with lib_add first. Referenced: {string.Join(", ", all.Select(l => l.LibraryName))}.",
            };
        }

        return match;
    }

    private static object Compact(InstalledLibrary l)
        => new { name = l.Name, version = l.Version, distributor = l.Distributor, repository = l.Repository, fileKind = l.FileKind, displayName = l.DisplayName };

    private static void ThrowIfAllFailed(List<object> results, string message, string hint)
    {
        var failed = results.Where(r => r.GetType().GetProperty("ok")?.GetValue(r) is false).ToList();
        if (results.Count > 0 && failed.Count == results.Count)
        {
            var details = string.Join(" | ", failed.Select(f => f.GetType().GetProperty("error")?.GetValue(f)));
            throw new TcException($"{message} {details}") { Hint = hint };
        }
    }

    /// <summary>Runs an Automation Interface call and turns its exception into a TcException with a hint.</summary>
    internal static void Wrap(Action action, string message, string? hint)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or NullReferenceException or InvalidOperationException or IOException)
        {
            throw new TcException($"{message} {ex.Message}", ex) { Hint = hint };
        }
    }

    private static bool Eq(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
