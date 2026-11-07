using TwinCatMcp.Core;

namespace TwinCatMcp.Runtime;

public sealed record PlcOnlineSettings(bool LoggedIn, string? AppState, string? OpState);

/// <summary>
/// Pure mapping for PLC online operations (ITcPlcOnline on the nested PLC project item "TIPC^P^P Project").
/// Numeric values are those of TCatSysManagerLib 3.4 (PLC_LOGIN_FLAGS, PLC_RESET_TYPE, PLC_OPSTATE).
/// </summary>
public static class PlcOnline
{
    private const int Regular = 1;
    private const int ForceOnlineChange = 2;
    private const int ForceDownload = 4;
    private const int ForceCompileCode = 16;
    private const int Silent = 256;

    /// <summary>
    /// mode: "auto" (online change if possible, else download), "download" (full download, PLC stops) or "onlineChange".
    /// rebuild forces the code to be compiled before login. SILENT is always set so the XAE never shows a dialog.
    /// </summary>
    public static int LoginFlags(string mode, bool rebuild)
    {
        var flags = (mode ?? "").Trim().ToLowerInvariant() switch
        {
            "auto" or "" => Regular,
            "download" => ForceDownload,
            "onlinechange" or "online_change" or "online-change" => ForceOnlineChange,
            _ => throw new TcException($"Unknown login mode '{mode}'.") { Hint = "Use 'auto', 'download' or 'onlineChange'." },
        };
        if (rebuild)
        {
            flags |= ForceCompileCode;
        }

        return flags | Silent;
    }

    public static int ResetType(string type) => (type ?? "").Trim().ToLowerInvariant() switch
    {
        "cold" => 1,
        "origin" => 2,
        _ => throw new TcException($"Unknown reset type '{type}'.") { Hint = "Use 'cold' (reset non-retain variables) or 'origin' (reset all variables incl. retain/persistent, removes the application)." },
    };

    private static readonly (int Bit, string Name)[] OpStates =
    [
        (1, "ProgramLoaded"), (2, "Download"), (4, "OnlineChange"), (8, "StoreBootProject"), (16, "ForceActive"), (32, "Exception"),
        (64, "RunAfterDownload"), (128, "StoreBootProjectOnly"), (256, "Exit"), (512, "Delete"), (1024, "Reset"), (2048, "RetainMismatch"),
        (4096, "BootProjectValid"), (8192, "LoadBootProject"), (16384, "FlowActive"), (32768, "RunInFlash"), (131072, "CoreDumpLoaded"),
        (262144, "ExecutionPointsActive"), (524288, "DumpCreating"),
    ];

    public static string[] DescribeOpState(int value) => OpStates.Where(s => (value & s.Bit) != 0).Select(s => s.Name).ToArray();

    public static PlcOnlineSettings ParseOnlineSettings(string nestedProjectXml)
    {
        var s = XmlText.Root(nestedProjectXml).Element("IECProjectDef")?.Element("OnlineSettings");
        return new PlcOnlineSettings(s.Bool("LoggedIn") ?? false, s.Str("PlcAppState"), s.Str("PlcOpState"));
    }

    /// <summary>XML command for ConsumeXml on the nested project: LoginCmd, LogoutCmd, StartCmd, StopCmd, ResetColdCmd, ResetOriginCmd.</summary>
    public static string CommandXml(string command)
        => $"<TreeItem><IECProjectDef><OnlineSettings><Commands><{command}>true</{command}></Commands></OnlineSettings></IECProjectDef></TreeItem>";
}
