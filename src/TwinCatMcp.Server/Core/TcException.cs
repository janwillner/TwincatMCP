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
