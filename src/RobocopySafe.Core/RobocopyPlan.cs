namespace RobocopySafe.Core;

public sealed record RobocopyPlan(
    string Source,
    string Destination,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<string> ExcludedDirectories,
    IReadOnlyList<string> Warnings)
{
    public string DisplayCommand => "robocopy.exe " + string.Join(' ', Arguments.Select(QuoteForDisplay));

    private static string QuoteForDisplay(string value)
    {
        if (value.Length > 0 && value.All(c => !char.IsWhiteSpace(c) && c != '"'))
        {
            return value;
        }

        return '"' + value.Replace("\"", "\\\"", StringComparison.Ordinal) + '"';
    }
}
