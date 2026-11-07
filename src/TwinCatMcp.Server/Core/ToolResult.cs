using System.Runtime.InteropServices;
using ModelContextProtocol.Protocol;

namespace TwinCatMcp.Core;

/// <summary>
/// Builds MCP <see cref="CallToolResult"/> objects. Every tool returns JSON text.
/// Errors are reported with <c>IsError = true</c> and a JSON body that contains the
/// message, the HRESULT (for COM errors) and the TwinCAT System Manager error log.
/// </summary>
public static class ToolResult
{
    public static CallToolResult Ok(object? payload)
    {
        var text = payload switch
        {
            null => "{\"ok\":true}",
            string s => s,
            _ => Json.Serialize(payload),
        };

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = text }],
            IsError = false,
        };
    }

    public static CallToolResult Error(Exception ex, string? tcErrorLog = null)
    {
        var inner = ex is AggregateException ae && ae.InnerExceptions.Count == 1 ? ae.InnerException! : ex;
        if (inner is System.Reflection.TargetInvocationException tie && tie.InnerException is not null)
        {
            inner = tie.InnerException;
        }

        var body = new Dictionary<string, object?>
        {
            ["ok"] = false,
            ["error"] = inner.Message,
            ["type"] = inner.GetType().Name,
        };

        if (inner is COMException com)
        {
            body["hresult"] = $"0x{com.HResult:X8}";
            body["hresultName"] = HResults.Describe(com.HResult);
        }

        if (inner is TcException tce && tce.Hint is not null)
        {
            body["hint"] = tce.Hint;
        }
        else if (inner is TimeoutException)
        {
            body["hint"] = "The XAE did not answer in time. It may show a modal dialog or still be busy (build/activation). " +
                           "Call xae_status, close dialogs in the IDE if visible, or retry with a larger timeoutSeconds.";
        }

        if (!string.IsNullOrWhiteSpace(tcErrorLog))
        {
            body["twinCatErrors"] = tcErrorLog.Trim();
        }

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = Json.Serialize(body) }],
            IsError = true,
        };
    }

    public static CallToolResult Error(string message, string? hint = null)
        => Error(new TcException(message) { Hint = hint });
}

public static class HResults
{
    public static string? Describe(int hr) => unchecked((uint)hr) switch
    {
        0x80010001 => "RPC_E_CALL_REJECTED (IDE busy)",
        0x8001010A => "RPC_E_SERVERCALL_RETRYLATER (IDE busy)",
        0x800706BA => "RPC_S_SERVER_UNAVAILABLE (IDE process gone)",
        0x800706BE => "RPC_S_CALL_FAILED (IDE crashed or closed)",
        0x80004005 => "E_FAIL",
        0x80070057 => "E_INVALIDARG",
        0x80004002 => "E_NOINTERFACE",
        0x98510001 => "TSM_E_ITEMNOTFOUND",
        0x98510002 => "TSM_E_INVALIDITEMTYPE",
        0x98510003 => "TSM_E_INVALIDITEMSUBTYPE",
        0x98510004 => "TSM_E_CORRUPTEDLINK",
        0x98510005 => "TSM_E_MISMATCHINGITEMS",
        0x98510006 => "TSM_E_ITEMHASCHILDREN",
        _ => null,
    };
}
