namespace TwinCatMcp.Core;

/// <summary>COM ProgIDs of the TwinCAT XAE Shell (InfoSys "Which DTE ProgID should be used for the respective TwinCAT/VS version?").</summary>
public static class XaeProgIds
{
    /// <summary>TwinCAT XAE Shell 64-bit (VS 2022 based), TwinCAT 3.1.4026.</summary>
    public const string Shell4026 = "TcXaeShell.DTE.17.0";

    /// <summary>TwinCAT XAE Shell (VS 2017 based), TwinCAT 3.1.4024.</summary>
    public const string Shell4024 = "TcXaeShell.DTE.15.0";

    /// <summary>Default ProgID for xae_start: the newest registered TcXaeShell; the 4026 shell when none is registered.</summary>
    public static string SelectDefault(Func<string, bool> isRegistered)
        => isRegistered(Shell4026) ? Shell4026
            : isRegistered(Shell4024) ? Shell4024
            : Shell4026;

    /// <summary>Default ProgID based on the COM registrations of this PC.</summary>
    public static string SelectDefault() => SelectDefault(id => Type.GetTypeFromProgID(id) is not null);
}
