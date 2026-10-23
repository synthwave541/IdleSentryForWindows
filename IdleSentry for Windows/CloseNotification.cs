using System.Globalization;

namespace IdleSentry;

internal static class CloseNotification
{
    // Shell balloon text has room for 255 UTF-16 characters plus its terminator.
    internal const int MaxBodyLength = 255;
    internal const int MaxVisibleNames = 5;
    private const int MaxNameLength = 48;

    internal static (string Title, string Body) Create(IReadOnlyList<string> closedApps)
    {
        if (closedApps.Count == 0) throw new ArgumentException("At least one closed app is required.", nameof(closedApps));
        var names = closedApps.Take(MaxVisibleNames).Select(name =>
        {
            string safe = new((name ?? string.Empty).Select(character => char.IsControl(character) ? ' ' : character).ToArray());
            string clean = string.Join(" ", safe.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return Truncate(string.IsNullOrEmpty(clean) ? "Unnamed app" : clean, MaxNameLength);
        }).ToList();
        if (closedApps.Count == 1)
            return ("App Closed Automatically", $"{names[0]} was closed after inactivity.");

        for (int visible = names.Count; visible > 0; visible--)
        {
            int remaining = closedApps.Count - visible;
            string list = remaining > 0
                ? string.Join(", ", names.Take(visible)) + $", and {remaining} more"
                : string.Join(", ", names.Take(visible - 1)) + " and " + names[visible - 1];
            string body = $"Closed after inactivity: {list}.";
            if (body.Length <= MaxBodyLength)
                return ($"{closedApps.Count} Apps Closed Automatically", body);
        }
        throw new InvalidOperationException("The bounded app list must fit the notification.");
    }

    private static string Truncate(string value, int limit)
    {
        if (value.Length <= limit) return value;
        int end = StringInfo.ParseCombiningCharacters(value).Last(index => index <= limit - 1);
        return value[..end] + "…";
    }
}
