namespace Tempo.Core.Shell;

public static class AppLauncher
{
    public static string? ResolveExecutable(string? executable)
    {
        const string helper = @"\bin\cef\cef.win64\steamwebhelper.exe";
        if (executable?.EndsWith(helper, StringComparison.OrdinalIgnoreCase) == true)
            return executable[..^helper.Length] + @"\Steam.exe";
        return executable;
    }
}
