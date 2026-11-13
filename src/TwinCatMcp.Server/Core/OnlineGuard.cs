using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TCatSysManagerLib;
using TwinCatMcp.Runtime;

namespace TwinCatMcp.Core;

/// <summary>
/// While the XAE is logged in to a PLC runtime nothing may be changed, built or activated in the engineering project.
/// Every tool call that is not read-only (and not an online / ADS tool) therefore first logs the XAE out of every PLC
/// project of the selected TwinCAT project. The tool result then reports which PLC projects were logged out.
/// </summary>
public static class OnlineGuard
{
    /// <summary>
    /// Tools that run while logged in by design: the online tools themselves and the ADS channel, which talks to the
    /// runtime directly and does not change the engineering project.
    /// </summary>
    private static readonly string[] ExemptPrefixes = ["plc_online_", "ads_", "route_"];

    /// <summary>IDE session tools that neither change the project nor build it.</summary>
    private static readonly HashSet<string> ExemptTools = new(StringComparer.Ordinal)
    {
        "xae_start", "xae_attach", "xae_set_visible", "xae_set_silent_mode",
    };

    private static readonly TimeSpan LogoutTimeout = TimeSpan.FromSeconds(60);

    /// <summary>True when the XAE must be logged out of the PLC before <paramref name="toolName"/> may run.</summary>
    public static bool RequiresLogout(string toolName, bool readOnly)
        => !readOnly
           && !ExemptPrefixes.Any(p => toolName.StartsWith(p, StringComparison.Ordinal))
           && !ExemptTools.Contains(toolName);

    /// <summary>Logs the XAE out of every logged-in PLC project (STA only). Returns the nested project paths that were logged out.</summary>
    public static IReadOnlyList<string> LogoutAll(XaeSession s)
    {
        if (!s.IsConnected || !s.HasProject)
        {
            return [];
        }

        // Without a PLC node (or with an unreadable tree) there is nothing to be logged in to.
        var projects = XaeRuntime.Try(() => XaeRuntime.NestedProjects(s).ToList()) ?? [];
        var loggedOut = new List<string>();
        foreach (var project in projects)
        {
            if (project is not ITcPlcOnline online || !online.IsLoggedIn)
            {
                continue;
            }

            var path = XaeRuntime.Try(() => project.PathName) ?? project.Name;
            online.Logoff();
            if (online.IsLoggedIn)
            {
                throw new TcException($"Automatic logout from '{path}' failed: the XAE is still logged in.");
            }

            loggedOut.Add(path);
        }

        return loggedOut;
    }

    /// <summary>Call-tool filter that enforces <see cref="RequiresLogout"/> before the tool runs.</summary>
    public static IMcpRequestFilterBuilder AddAutoLogout(this IMcpRequestFilterBuilder filters)
        => filters.AddCallToolFilter(next => async (context, ct) =>
        {
            var toolName = context.Params?.Name ?? "";
            var tool = context.MatchedPrimitive as McpServerTool
                       ?? context.Server.ServerOptions.ToolCollection?.FirstOrDefault(t => t.ProtocolTool.Name == toolName);
            var session = context.Services?.GetService<XaeSession>();
            if (tool is null || session is null || !RequiresLogout(toolName, tool.ProtocolTool.Annotations?.ReadOnlyHint == true))
            {
                return await next(context, ct).ConfigureAwait(false);
            }

            IReadOnlyList<string> loggedOut;
            try
            {
                loggedOut = await session.Sta.InvokeAsync(() => LogoutAll(session), LogoutTimeout, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return ToolResult.Error(new TcException(
                    $"'{toolName}' was not executed: the XAE is logged in to a PLC and nothing may be changed or built while logged in. " +
                    $"The automatic logout failed: {ex.Message}", ex)
                {
                    Hint = "Call plc_online_logout (and plc_online_state to verify), close any dialog in the IDE, then retry.",
                });
            }

            var result = await next(context, ct).ConfigureAwait(false);
            if (loggedOut.Count > 0)
            {
                result.Content = [.. result.Content, new TextContentBlock
                {
                    Text = Json.Serialize(new
                    {
                        autoLogout = loggedOut,
                        note = "The XAE was logged out of these PLC projects before the tool ran (no changes or builds while logged in). " +
                               "The PLC keeps running; log in again with plc_online_login if needed.",
                    }),
                }];
            }

            return result;
        });
}
