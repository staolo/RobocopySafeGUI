namespace RobocopySafe.Core;

public sealed record MoveSourceFinalizationResult(
    bool Complete,
    bool SourceDeleted,
    bool SourceExists,
    bool RootPreserved,
    IReadOnlyList<string> RemainingEntries,
    string? Error);

public static class MoveSourceFinalizer
{
    private const int RemainingEntrySampleLimit = 6;

    public static MoveSourceFinalizationResult Finalize(string source)
    {
        try
        {
            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
            if (!Directory.Exists(normalized))
            {
                return Completed(sourceDeleted: false, sourceExists: false, rootPreserved: false);
            }

            var remainingEntries = Directory
                .EnumerateFileSystemEntries(normalized)
                .Take(RemainingEntrySampleLimit)
                .ToArray();
            if (remainingEntries.Length > 0)
            {
                return new(false, false, true, false, remainingEntries, null);
            }

            if (IsRootPath(normalized))
            {
                return Completed(sourceDeleted: false, sourceExists: true, rootPreserved: true);
            }

            Directory.Delete(normalized);
            return Completed(sourceDeleted: true, sourceExists: false, rootPreserved: false);
        }
        catch (DirectoryNotFoundException)
        {
            return Completed(sourceDeleted: false, sourceExists: false, rootPreserved: false);
        }
        catch (Exception ex) when (
            ex is ArgumentException or
                NotSupportedException or
                IOException or
                UnauthorizedAccessException)
        {
            return new(false, false, Directory.Exists(source), false, Array.Empty<string>(), ex.Message);
        }
    }

    private static MoveSourceFinalizationResult Completed(
        bool sourceDeleted,
        bool sourceExists,
        bool rootPreserved) =>
        new(true, sourceDeleted, sourceExists, rootPreserved, Array.Empty<string>(), null);

    private static bool IsRootPath(string path) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(path),
            Path.TrimEndingDirectorySeparator(Path.GetPathRoot(path) ?? string.Empty),
            StringComparison.OrdinalIgnoreCase);
}
