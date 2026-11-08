using System.Diagnostics;
using TwinCatMcp.Core;

namespace TwinCatMcp.Tests.Integration;

/// <summary>
/// xae_attach on an IDE the server did not start (the user's own TcXaeShell) must leave it as it was found.
/// The "foreign" IDE here is a TcXaeShell process started by the test itself.
/// </summary>
[Collection(PlainServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AttachTests(McpServerFixture fx)
{
    [Fact]
    public async Task Detaching_from_an_attached_IDE_restores_its_silent_mode()
    {
        using var ide = StartTcXaeShell();
        try
        {
            await WaitUntilRegisteredAsync(ide.Id);

            var first = await fx.Call("xae_attach", new { solutionPathOrProcessId = ide.Id.ToString() });
            var status = await fx.Call("xae_status");
            var detach = await fx.Call("xae_close", new { quitIde = false });
            var second = await fx.Call("xae_attach", new { solutionPathOrProcessId = ide.Id.ToString() });
            await fx.Call("xae_close", new { quitIde = true });

            Assert.False(first.GetProperty("ownsInstance").GetBoolean());
            Assert.False(first.GetProperty("silentModeBeforeAttach").GetBoolean());
            Assert.True(status.GetProperty("silentMode").GetBoolean());
            Assert.False(detach.GetProperty("quit").GetBoolean());
            Assert.False(second.GetProperty("silentModeBeforeAttach").GetBoolean()); // restored on detach
        }
        finally
        {
            if (!ide.HasExited)
            {
                ide.Kill();
            }
        }
    }

    [Fact]
    public async Task Process_launch_mode_starts_and_owns_its_own_hidden_IDE()
    {
        var started = await fx.Call("xae_start", new { launchMode = "process" }, TimeSpan.FromMinutes(5));
        var pid = started.GetProperty("processId").GetInt32();
        var closed = await fx.Call("xae_close");

        Assert.Equal("process", started.GetProperty("launchedAs").GetString());
        Assert.True(started.GetProperty("ownsInstance").GetBoolean());
        Assert.False(started.GetProperty("visible").GetBoolean());
        Assert.True(closed.GetProperty("quit").GetBoolean());
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.DoesNotContain(Process.GetProcessesByName("TcXaeShell"), p => p.Id == pid);
    }

    [Fact]
    public async Task Unknown_launch_mode_is_rejected()
    {
        var (json, isError) = await fx.CallRaw("xae_start", new { launchMode = "fast" });

        Assert.True(isError);
        Assert.Contains("'com'", json.GetProperty("hint").GetString());
    }

    private static Process StartTcXaeShell()
    {
        var exe = ComRegistration.LocalServerExe(new Guid("47f09213-63bf-4966-9a9f-cf03a1e94334"))
                  ?? throw new InvalidOperationException("TcXaeShell 4026 is not registered.");
        return Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Minimized })!;
    }

    private async Task WaitUntilRegisteredAsync(int pid)
    {
        var until = DateTime.UtcNow.AddMinutes(4);
        while (DateTime.UtcNow < until)
        {
            var list = await fx.Call("xae_list_instances");
            if (list.EnumerateArray().Any(i => i.GetProperty("processId").GetInt32() == pid))
            {
                await Task.Delay(TimeSpan.FromSeconds(10)); // let the shell finish loading its packages
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        throw new TimeoutException($"TcXaeShell {pid} did not register in the Running Object Table.");
    }
}
