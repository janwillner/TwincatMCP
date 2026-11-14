namespace TwinCatMcp.Core;

/// <summary>
/// Access to newer Automation Interface versions (ITcSysManager18, ITcPlcIECProject7, ...). Older XAEs such as
/// TwinCAT 3.1.4024 do not implement every interface of the TCatSysManagerLib 3.4 interop the server is compiled against,
/// so a hard cast would surface as a bare InvalidCastException.
/// </summary>
public static class ComCapability
{
    public const string DefaultRequiredVersion = "TwinCAT 3.1.4026";

    /// <summary>Returns <paramref name="obj"/> as <typeparamref name="T"/> or throws a TcException naming the missing feature.</summary>
    public static T Require<T>(object obj, string feature, string requiredVersion = DefaultRequiredVersion)
        where T : class
        => obj as T ?? throw new TcException($"{feature} is not supported by this TwinCAT XAE (interface {typeof(T).Name} is not available).")
        {
            Hint = $"{feature} needs {requiredVersion} or newer. Check the XAE version with system_get_versions.",
        };
}
