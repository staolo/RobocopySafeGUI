namespace RobocopySafe.Gui;

internal static class LogTextLimiter
{
    public static string TrimToRecentLines(
        string text,
        int maximumCharacters,
        int retainedCharacters,
        string notice)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(notice);

        if (maximumCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        }

        if (retainedCharacters < 0 || retainedCharacters >= maximumCharacters)
        {
            throw new ArgumentOutOfRangeException(nameof(retainedCharacters));
        }

        if (text.Length <= maximumCharacters)
        {
            return text;
        }

        var charactersToRemove = text.Length - retainedCharacters;
        var newlineIndex = text.IndexOf('\n', charactersToRemove);
        var retainedStart = newlineIndex >= 0
            ? newlineIndex + 1
            : Math.Min(charactersToRemove, text.Length);

        return notice + Environment.NewLine + text[retainedStart..];
    }
}
