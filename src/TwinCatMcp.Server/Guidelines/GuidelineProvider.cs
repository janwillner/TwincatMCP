namespace TwinCatMcp.Guidelines;

/// <summary>
/// Loads the guidelines of a solution on demand: .twincat-mcp.json next to the .sln, keyed on the solution file and
/// re-read when a file of its chain changes. A broken config is cached as its error, so write tools can refuse to work.
/// </summary>
public sealed class GuidelineProvider
{
    public const string FileName = ".twincat-mcp.json";

    private readonly string _builtinDirectory;
    private string? _key;
    private DateTime[] _stamps = [];
    private string[] _files = [];
    private GuidelineSet? _set;
    private GuidelineConfigException? _error;

    public GuidelineProvider(string? builtinDirectory = null)
        => _builtinDirectory = builtinDirectory ?? Path.Combine(AppContext.BaseDirectory, "guidelines");

    /// <summary>Folder with the shipped example configs (.twincat-mcp.&lt;Name&gt;.json).</summary>
    public string BuiltinDirectory => _builtinDirectory;

    public IEnumerable<string> BuiltinNames => GuidelineConfigFile.BuiltinNames(_builtinDirectory);

    /// <summary>The config file a solution would use (it may not exist).</summary>
    public static string? ConfigPathOf(string? solutionFile)
        => string.IsNullOrWhiteSpace(solutionFile) ? null : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(solutionFile))!, FileName);

    /// <summary>The guidelines of a solution; null without config. Throws <see cref="GuidelineConfigException"/> for a broken config.</summary>
    public GuidelineSet? For(string? solutionFile)
    {
        var config = ConfigPathOf(solutionFile);
        if (config is null || !File.Exists(config))
        {
            Reset(null);
            return null;
        }

        // A broken config is loaded again on every call: the fix may be in any file of the chain (extends, guide, template).
        if (_error is not null || !string.Equals(_key, config, StringComparison.OrdinalIgnoreCase) || Changed())
        {
            Reset(config);
            try
            {
                _set = GuidelineSet.Load(config, _builtinDirectory);
                _files = [.. _set.DependencyFiles];
                _stamps = _files.Select(Stamp).ToArray();
            }
            catch (GuidelineConfigException ex)
            {
                _error = ex;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Anything else (I/O, unexpected JSON shapes) must not switch the guidelines off silently.
                _error = new GuidelineConfigException($"{config}: {ex.Message}");
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
