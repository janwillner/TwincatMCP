namespace TwinCatMcp.Core;

/// <summary>
/// An expected, user-facing error (bad argument, missing session, item not found ...).
/// Its message is returned verbatim to the MCP client as a tool error.
/// </summary>
public class TcException : Exception
{
    public TcException(string message) : base(message)
    {
    }

    public TcException(string message, Exception inner) : base(message, inner)
    {
    }

    /// <summary>Optional hint telling the client how to recover.</summary>
    public string? Hint { get; init; }
}

/// <summary>No IDE is connected - a session-level failure that batch tools must not report as a per-item error.</summary>
public sealed class XaeNotConnectedException(string message) : TcException(message);

/// <summary>The IDE process started by xae_start ended before it could be connected.</summary>
public sealed class IdeStartupExitedException(string message) : TcException(message);
