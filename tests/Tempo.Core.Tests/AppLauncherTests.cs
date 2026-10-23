using Tempo.Core.Shell;

namespace Tempo.Core.Tests;

public class AppLauncherTests
{
    [Theory]
    [InlineData(@"C:\Program Files (x86)\Steam\bin\cef\cef.win64\steamwebhelper.exe", @"C:\Program Files (x86)\Steam\Steam.exe")]
    [InlineData(@"D:\Steam\BIN\CEF\CEF.WIN64\STEAMWEBHELPER.EXE", @"D:\Steam\Steam.exe")]
    [InlineData(@"D:\Other\steamwebhelper.exe", @"D:\Other\steamwebhelper.exe")]
    [InlineData(@"D:\Steam\Steam.exe", @"D:\Steam\Steam.exe")]
    [InlineData(null, null)]
    public void ResolvesLaunchableExecutable(string? executable, string? expected) =>
        Assert.Equal(expected, AppLauncher.ResolveExecutable(executable));
}
