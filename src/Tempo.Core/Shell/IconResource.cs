using System.Globalization;

namespace Tempo.Core.Shell;

public readonly record struct IconResource(string Path, int Index)
{
    public static bool TryParse(string? resource, out IconResource icon)
    {
        icon = default;
        if (string.IsNullOrWhiteSpace(resource)) return false;
        var text = resource.Trim();
        var separator = text.LastIndexOf(',');
        var index = 0;
        if (separator >= 0 && !int.TryParse(text[(separator + 1)..].Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out index))
            return false;
        var path = (separator < 0 ? text : text[..separator]).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path)) return false;
        icon = new IconResource(Environment.ExpandEnvironmentVariables(path), index);
        return true;
    }
}
