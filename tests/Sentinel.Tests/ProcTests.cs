namespace Sentinel.Tests;

public sealed class ProcTests
{
    [Fact]
    public async Task RunAsync_IgnoresBrokenPipeWhenChildExitsWithoutReadingStdin()
    {
        var result = await Sentinel.Core.Proc.RunAsync(
            "/bin/sh",
            ["-c", "exit 0"],
            Path.GetTempPath(),
            stdin: new string('x', 1024 * 1024));

        Assert.Equal(0, result.Code);
    }
}