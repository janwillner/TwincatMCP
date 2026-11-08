using TwinCatMcp.Core;

namespace TwinCatMcp.Tests.Unit;

/// <summary>LocalServer32 command lines as found in the registry -> executable path.</summary>
public sealed class ComRegistrationTests
{
    private const string Exe = @"C:\Program Files\Beckhoff\TcXaeShell\Common7\IDE\TcXaeShell.exe";

    private static bool Exists(string path) => string.Equals(path, Exe, StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData(@"C:\Program Files\Beckhoff\TcXaeShell\Common7\IDE\TcXaeShell.exe")]              // TwinCAT 4026: unquoted, with spaces
    [InlineData(@"C:\Program Files\Beckhoff\TcXaeShell\Common7\IDE\TcXaeShell.exe -Embedding")]   // unquoted with argument
    [InlineData(@"""C:\Program Files\Beckhoff\TcXaeShell\Common7\IDE\TcXaeShell.exe"" /automation")]
    [InlineData(@"  ""C:\Program Files\Beckhoff\TcXaeShell\Common7\IDE\TcXaeShell.exe""  ")]
    public void Executable_is_resolved_from_the_command_line(string command)
    {
        Assert.Equal(Exe, ComRegistration.ExecutableFromCommand(command, Exists));
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"C:\Missing\Tool.exe -Embedding")]
    [InlineData(@"""C:\Missing\Tool.exe""")]
    public void Unresolvable_command_returns_null(string command)
    {
        Assert.Null(ComRegistration.ExecutableFromCommand(command, Exists));
    }
}
