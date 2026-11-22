namespace TwinCatMcp.Guidelines;

/// <summary>
/// Loads the guidelines of a solution on demand: .twincat-mcp.json next to the .sln, or - for a solution without
/// one - the server default (<see cref="DefaultVariable"/>). Keyed on the config and re-read when a file of its chain
/// changes. A broken config is cached as its error, so write tools can refuse to work.
/// </summary>
public sealed class GuidelineProvider
{
    public const string FileName = ".twincat-mcp.json";

    /// <summary>Environment variable with the server-wide default: 'builtin:&lt;name&gt;' or a path to a config file.</summary>
    public const string DefaultVariable = "TWINCAT_MCP_DEFAULT_GUIDELINES";

    private readonly string _builtinDirectory;
    private readonly string? _serverDefault;
    private string? _key;
    private DateTime[] _stamps = [];
    private string[] _files = [];
    private GuidelineSet? _set;
    private GuidelineConfigException? _error;

    /// <param name="builtinDirectory">Folder of the shipped examples; default: guidelines next to the server.</param>
    /// <param name="serverDefault">Guidelines for solutions without own config ('builtin:&lt;name&gt;' or a file path); null = none.</param>
    public GuidelineProvider(string? builtinDirectory = null, string? serverDefault = null)
    {
        _builtinDirectory = builtinDirectory ?? Path.Combine(AppContext.BaseDirectory, "guidelines");
        _serverDefault = string.IsNullOrWhiteSpace(serverDefault) ? null : serverDefault.Trim();
    }

    /// <summary>Folder with the shipped example configs (.twincat-mcp.&lt;Name&gt;.json).</summary>
    public string BuiltinDirectory => _builtinDirectory;

    public IEnumerable<string> BuiltinNames => GuidelineConfigFile.BuiltinNames(_builtinDirectory);

    /// <summary>The configured server default ('builtin:&lt;name&gt;' or a path), null without one.</summary>
    public string? ServerDefault => _serverDefault;

    /// <summary>True when the last <see cref="For"/> result came from the server default, not from the solution's own file.</summary>
    public bool IsServerDefault { get; private set; }

    /// <summary>The config file a solution would use (it may not exist).</summary>
    public static string? ConfigPathOf(string? solutionFile)
        => string.IsNullOrWhiteSpace(solutionFile) ? null : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(solutionFile))!, FileName);

    /// <summary>
    /// The guidelines of a solution: its own .twincat-mcp.json, else the server default; null without both or without
    /// open solution. Throws <see cref="GuidelineConfigException"/> for a broken config.
    /// </summary>
    public GuidelineSet? For(string? solutionFile)
    {
        var own = ConfigPathOf(solutionFile);
        var useOwn = own is not null && File.Exists(own);
        var key = useOwn ? own : own is not null && _serverDefault is not null ? DefaultVariable + "=" + _serverDefault : null;
        IsServerDefault = key is not null && !useOwn;
        if (key is null)
        {
            Reset(null);
            return null;
        }

        // A broken config is loaded again on every call: the fix may be in any file of the chain (extends, guide, template).
        if (_error is not null || !string.Equals(_key, key, StringComparison.OrdinalIgnoreCase) || Changed())
        {
            Reset(key);
            try
            {
                _set = GuidelineSet.Load(useOwn ? own! : ResolveServerDefault(), _builtinDirectory);
                _files = [.. _set.DependencyFiles];
                _stamps = _files.Select(Stamp).ToArray();
            }
            catch (GuidelineConfigException ex)
            {
                _error = IsServerDefault ? new GuidelineConfigException($"Server default {DefaultVariable}='{_serverDefault}': {ex.Message}") : ex;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Anything else (I/O, unexpected JSON shapes) must not switch the guidelines off silently.
                _error = new GuidelineConfigException($"{(useOwn ? own : $"{DefaultVariable}='{_serverDefault}'")}: {ex.Message}");
            }
        }

        return _error is not null ? throw _error : _set;
    }

    /// <summary>Like <see cref="For"/>, but a broken config yields null and its error.</summary>
    public GuidelineSet? TryFor(string? solutionFile, out GuidelineConfigException? error)
    {
        try
        {
            error = null;
            return For(solutionFile);
        }
        catch (GuidelineConfigException ex)
        {
            error = ex;
            return null;
        }
    }

    private string ResolveServerDefault()
    {
        var reference = _serverDefault!;
        if (reference.StartsWith(GuidelineConfigFile.BuiltinPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return GuidelineConfigFile.BuiltinPath(reference[GuidelineConfigFile.BuiltinPrefix.Length..], _builtinDirectory)
                ?? throw new GuidelineConfigException(
                    $"unknown built-in guideline. Shipped: {string.Join(", ", BuiltinNames.Select(n => GuidelineConfigFile.BuiltinPrefix + n))}.");
        }

        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(reference));
        return File.Exists(path) ? path : throw new GuidelineConfigException($"file not found: {path}");
    }

    private bool Changed() => _files.Length != _stamps.Length || _files.Where((f, i) => Stamp(f) != _stamps[i]).Any();

    private void Reset(string? key)
    {
        _key = key;
        _set = null;
        _error = null;
        _files = [];
        _stamps = [];
    }

    private static DateTime Stamp(string file) => File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
}
