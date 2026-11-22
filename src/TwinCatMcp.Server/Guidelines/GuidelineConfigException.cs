using TwinCatMcp.Core;

namespace TwinCatMcp.Guidelines;

/// <summary>The coding guideline configuration (.twincat-mcp.json or a file it extends) cannot be used.</summary>
public sealed class GuidelineConfigException(string message) : TcException(message);
