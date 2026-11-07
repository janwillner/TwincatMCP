using System.Runtime.CompilerServices;

namespace TwinCatMcp.Tests.Unit;

/// <summary>
/// Real ProduceXml / .xti / .tmc snippets captured live from TwinCAT 3.1.4026.22 (see MotionTestData folder).
/// Located via the source path so no csproj change is needed to copy them.
/// </summary>
internal static class MotionTestData
{
    public static string Read(string fileName, [CallerFilePath] string sourceFile = "")
        => File.ReadAllText(Path.Combine(Path.GetDirectoryName(sourceFile)!, "MotionTestData", fileName));
}
