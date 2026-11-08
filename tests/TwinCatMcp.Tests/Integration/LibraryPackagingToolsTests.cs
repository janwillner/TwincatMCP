using System.IO.Compression;
using System.Text.Json;

namespace TwinCatMcp.Tests.Integration;

/// <summary>Repositories, install/uninstall, project info, save as library and archives (lib_ / archive_ tools).</summary>
[Collection(LibraryCollection.Name)]
[Trait("Category", "Integration")]
public sealed class LibraryPackagingToolsTests(LibraryFixture fx)
{
    [Fact]
    public async Task Lists_the_System_repository()
    {
        var repos = await fx.Call("lib_list_repositories");

        var system = repos.GetProperty("repositories").EnumerateArray().First();
        Assert.Equal("System", system.GetProperty("name").GetString());
        Assert.EndsWith(@"PlcEngineering\Managed Libraries", system.GetProperty("folder").GetString());
    }

    [Fact]
    public async Task Lists_installed_libraries_from_the_repositories()
    {
        var installed = await fx.Call("lib_list_installed", new { nameContains = "Tc2_MC2", distributor = "Beckhoff", latestOnly = true });

        var mc2 = installed.GetProperty("libraries").EnumerateArray().Single(l => l.GetProperty("name").GetString() == "Tc2_MC2");
        Assert.Equal("Beckhoff Automation GmbH", mc2.GetProperty("distributor").GetString());
        Assert.Equal("System", mc2.GetProperty("repository").GetString());
        Assert.Matches(@"^3\.\d+\.\d+\.\d+$", mc2.GetProperty("version").GetString());
    }

    [Fact]
    public async Task Scan_returns_the_library_catalog_of_the_Automation_Interface()
    {
        var scan = await fx.Call("lib_scan", new { nameContains = "Tc2_Standard" });

        Assert.Contains(scan.GetProperty("libraries").EnumerateArray(), l => l.GetProperty("name").GetString() == "Tc2_Standard" && l.GetProperty("distributor").GetString() == "Beckhoff Automation GmbH");
    }

    [Fact]
    public async Task Project_info_can_be_set_and_read()
    {
        await fx.Call("lib_set_project_info", new { company = "Mcp Test", title = "McpTestLib", version = "1.2.3.4", author = "it", description = "integration test library" });

        var info = await fx.Call("lib_get_project_info");

        Assert.Equal("Mcp Test", info.GetProperty("info").GetProperty("Company").GetString());
        Assert.Equal("McpTestLib", info.GetProperty("info").GetProperty("Title").GetString());
        Assert.Equal("1.2.3.4", info.GetProperty("info").GetProperty("Version").GetString());
    }

    [Fact]
    public async Task Save_as_library_and_install_into_a_temporary_repository()
    {
        await fx.Call("lib_set_project_info", new { company = "Mcp Test", title = "McpTestLib", version = "1.2.3.4" });
        var libFile = fx.NewPath("McpTestLib.library");
        var compiledFile = fx.NewPath("McpTestLib.compiled-library");

        var saved = await fx.Call("archive_save_as_library", new { filePath = libFile });
        var savedCompiled = await fx.Call("archive_save_as_library", new { filePath = compiledFile });

        Assert.True(File.Exists(libFile));
        Assert.True(File.Exists(compiledFile));
        Assert.Equal("McpTestLib", saved.GetProperty("title").GetString());
        Assert.Equal("compiled-library", savedCompiled.GetProperty("format").GetString());

        // overwrite protection
        var (_, isError) = await fx.CallRaw("archive_save_as_library", new { filePath = libFile });
        Assert.True(isError);

        var repoName = "McpIt_" + Guid.NewGuid().ToString("N")[..6];
        var repoDir = Path.Combine(fx.OutDir, repoName);
        await fx.Call("lib_insert_repository", new { name = repoName, folder = repoDir });
        try
        {
            var repos = await fx.Call("lib_list_repositories");
            Assert.Contains(repos.GetProperty("repositories").EnumerateArray(), r => r.GetProperty("name").GetString() == repoName);

            var install = await fx.Call("lib_install", new { filePath = libFile, repository = repoName });
            Assert.Equal("McpTestLib, 1.2.3.4 (Mcp Test)", install.GetProperty("installed")[0].GetProperty("displayName").GetString());

            var installed = await fx.Call("lib_list_installed", new { repository = repoName });
            var lib = installed.GetProperty("libraries").EnumerateArray().Single();
            Assert.Equal("McpTestLib", lib.GetProperty("name").GetString());
            Assert.Equal("1.2.3.4", lib.GetProperty("version").GetString());
            Assert.Equal("Mcp Test", lib.GetProperty("distributor").GetString());

            await fx.Call("lib_uninstall", new { repository = repoName, library = "McpTestLib, 1.2.3.4 (Mcp Test)" });

            var empty = await fx.Call("lib_list_installed", new { repository = repoName });
            Assert.Empty(empty.GetProperty("libraries").EnumerateArray());
        }
        finally
        {
            await fx.CallRaw("lib_remove_repository", new { name = repoName });
        }

        var reposAfter = await fx.Call("lib_list_repositories");
        Assert.DoesNotContain(reposAfter.GetProperty("repositories").EnumerateArray(), r => r.GetProperty("name").GetString() == repoName);
    }

    [Fact]
    public async Task Move_repository_changes_its_position()
    {
        var repoName = "McpIt_" + Guid.NewGuid().ToString("N")[..6];
        await fx.Call("lib_insert_repository", new { name = repoName, folder = Path.Combine(fx.OutDir, repoName), index = 1 });
        try
        {
            var count = (await fx.Call("lib_list_repositories")).GetProperty("repositories").GetArrayLength();
            var moved = await fx.Call("lib_move_repository", new { name = repoName, newIndex = count - 1 });

            var last = moved.GetProperty("repositories").EnumerateArray().Last();
            Assert.Equal(repoName, last.GetProperty("name").GetString());
        }
        finally
        {
            await fx.CallRaw("lib_remove_repository", new { name = repoName });
        }

        var after = await fx.Call("lib_list_repositories");
        Assert.DoesNotContain(after.GetProperty("repositories").EnumerateArray(), r => r.GetProperty("name").GetString() == repoName);
    }

    [Fact]
    public async Task Uninstall_of_a_missing_library_is_a_clean_tool_error()
    {
        var (json, isError) = await fx.CallRaw("lib_uninstall", new { repository = "System", library = "NoSuchLib_Mcp, 1.0.0.0 (Nobody)" });

        Assert.True(isError);
        Assert.Contains("not installed", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Save_solution_archive_export_plc_project_and_open_archive()
    {
        await fx.Call("solution_save");
        var tnzip = fx.NewPath("McpIt.tnzip");
        var tpzip = fx.NewPath("Plc1.tpzip");

        var archive = await fx.Call("archive_save_solution", new { filePath = tnzip });
        await fx.Call("archive_export_plc_project", new { plcName = XaeProjectFixture.PlcName, filePath = tpzip });

        Assert.True(archive.GetProperty("size").GetInt64() > 0);
        using (var zip = ZipFile.OpenRead(tnzip))
        {
            Assert.Contains(zip.Entries, e => e.FullName.EndsWith("McpIt.sln"));
            Assert.Contains(zip.Entries, e => e.FullName.EndsWith("Plc1.plcproj"));
        }

        using (var zip = ZipFile.OpenRead(tpzip))
        {
            Assert.Contains(zip.Entries, e => e.FullName.EndsWith("Plc1.plcproj"));
        }

        var status = await fx.Call("xae_status");
        var originalSolution = status.GetProperty("solution").GetString()!;
        var target = Path.Combine(fx.OutDir, "opened-" + Guid.NewGuid().ToString("N")[..6]);
        try
        {
            var opened = await fx.Call("archive_open", new { archivePath = tnzip, targetFolder = target });

            Assert.StartsWith(target, opened.GetProperty("solution").GetString());
            Assert.True(File.Exists(Path.Combine(target, "McpIt.sln")));
            Assert.Equal("McpIt", opened.GetProperty("selectedProject").GetString());
            var exists = await fx.Call("tree_exists", new { treePaths = new[] { fx.PlcProjectPath } });
            Assert.True(exists.GetProperty(fx.PlcProjectPath).GetBoolean());
        }
        finally
        {
            await fx.Call("solution_open", new { path = originalSolution });
        }
    }

    [Fact]
    public async Task Save_twincat_project_archive_as_tszip()
    {
        var tszip = fx.NewPath("McpIt.tszip");

        await fx.Call("archive_save_solution", new { filePath = tszip, scope = "twincatProject" });

        using var zip = ZipFile.OpenRead(tszip);
        Assert.Contains(zip.Entries, e => e.FullName == "McpIt.tsproj");
        Assert.Contains(zip.Entries, e => e.FullName.EndsWith("Plc1.plcproj"));
    }

    [Fact]
    public async Task Opening_a_tszip_adds_the_project_to_the_solution_and_selects_it()
    {
        var tszip = fx.NewPath("McpIt-open.tszip");
        await fx.Call("solution_save");
        await fx.Call("archive_save_solution", new { filePath = tszip, scope = "twincatProject" });
        var originalSolution = (await fx.Call("xae_status")).GetProperty("solution").GetString()!;
        try
        {
            var opened = await fx.Call("archive_open", new { archivePath = tszip, targetFolder = Path.Combine(fx.OutDir, "tszip-" + Guid.NewGuid().ToString("N")[..6]), projectName = "McpRestored" });
            var status = await fx.Call("xae_status");

            Assert.Equal("McpRestored", opened.GetProperty("selectedProject").GetString());
            Assert.EndsWith("McpRestored.tsproj", opened.GetProperty("project").GetString());
            Assert.Equal("McpRestored", status.GetProperty("project").GetProperty("name").GetString());
        }
        finally
        {
            await fx.Call("solution_close", new { save = false });
            await fx.Call("solution_open", new { path = originalSolution });
        }
    }

    [Fact]
    public async Task Import_exported_plc_project_as_second_plc()
    {
        var tpzip = fx.NewPath("Plc1.tpzip");
        await fx.Call("archive_export_plc_project", new { plcName = XaeProjectFixture.PlcName, filePath = tpzip });
        try
        {
            var created = await fx.Call("archive_import_plc_project", new { filePath = tpzip, name = "PlcImported" });

            Assert.Equal("TIPC^PlcImported", created.GetProperty("path").GetString());
            var refs = await fx.Call("lib_list_references", new { plcProject = "PlcImported" });
            Assert.Equal("TIPC^PlcImported^PlcImported Project", refs.GetProperty("plcProject").GetString());

            // with two PLC projects the PLC must be named explicitly
            var (json, isError) = await fx.CallRaw("lib_list_references");
            Assert.True(isError);
            Assert.Contains("Several PLC projects", json.GetProperty("error").GetString());
        }
        finally
        {
            await fx.CallRaw("tree_delete_item", new { treePath = "TIPC^PlcImported" });
        }

        var exists = await fx.Call("tree_exists", new { treePaths = new[] { "TIPC^PlcImported" } });
        Assert.False(exists.GetProperty("TIPC^PlcImported").GetBoolean());
    }

    [Fact]
    public async Task Save_solution_archive_requires_tnzip_extension()
    {
        var (json, isError) = await fx.CallRaw("archive_save_solution", new { filePath = fx.NewPath("x.zip") });

        Assert.True(isError);
        Assert.Contains(".tnzip", json.GetProperty("error").GetString());
    }
}
