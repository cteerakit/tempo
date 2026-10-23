using Tempo.Core.Shell;

namespace Tempo.Core.Tests;

public class IconResourceTests
{
    [Theory]
    [InlineData(@"C:\Profiles\Profile 2\Google Profile.ico,0", @"C:\Profiles\Profile 2\Google Profile.ico", 0)]
    [InlineData(" \"C:\\Profiles\\Family, work\\Google Profile.ico\", 0 ", @"C:\Profiles\Family, work\Google Profile.ico", 0)]
    [InlineData(@"C:\Windows\System32\shell32.dll,-128", @"C:\Windows\System32\shell32.dll", -128)]
    [InlineData(@"C:\Icons\profile.ico", @"C:\Icons\profile.ico", 0)]
    public void ParsesNativeIconResourcePathsAndIndices(string text, string path, int index)
    {
        Assert.True(IconResource.TryParse(text, out var icon));
        Assert.Equal(path, icon.Path);
        Assert.Equal(index, icon.Index);
    }

    [Fact]
    public void ExpandsEnvironmentVariables()
    {
        Assert.True(IconResource.TryParse(@"%TEMP%\profile.ico,0", out var icon));
        Assert.Equal(Path.Combine(Path.GetTempPath().TrimEnd('\\'), "profile.ico"), icon.Path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(",0")]
    [InlineData("\"\",0")]
    [InlineData(@"C:\Icons\profile.ico,no-index")]
    public void RejectsMissingPathsAndInvalidIndices(string? text) =>
        Assert.False(IconResource.TryParse(text, out _));
}
