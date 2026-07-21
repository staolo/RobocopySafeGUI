using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;

namespace RobocopySafe.Core;

public static partial class RobocopyPlanBuilder
{
    private const int SafeCommandLength = 30_000;

    public static RobocopyPlan Build(
        string source,
        string destination,
        CopySettings settings,
        bool listOnly = false,
        bool discoverDirectories = true)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalizedSource = NormalizeExistingDirectory(source, AppText.Get(TextId.SourceDirectoryName));
        var normalizedDestination = NormalizeDestination(destination);
        ValidatePathRelationship(normalizedSource, normalizedDestination);
        ValidateLinkSafety(normalizedSource, settings);

        var filePatterns = SplitPatterns(settings.ExcludedFilePatterns);
        var directoryPatterns = SplitPatterns(settings.ExcludedDirectoryPatterns);
        ValidateExclusionPatterns(filePatterns, AppText.Get(TextId.ExcludedFilesName));
        ValidateExclusionPatterns(directoryPatterns, AppText.Get(TextId.ExcludedDirectoriesName));
        var warnings = new List<string>();
        var discoveredDirectories = discoverDirectories
            ? DiscoverExcludedDirectories(normalizedSource, settings, directoryPatterns, warnings)
            : Array.Empty<string>();

        var excludedDirectories = directoryPatterns
            .Concat(discoveredDirectories)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var arguments = new List<string>
        {
            normalizedSource,
            normalizedDestination,
            "/E",
            "/COPY:DAT",
            "/DCOPY:DAT",
            $"/R:{Math.Clamp(settings.Retries, 0, 100)}",
            $"/W:{Math.Clamp(settings.RetryWaitSeconds, 0, 60)}",
            $"/MT:{Math.Clamp(settings.Threads, 1, 128)}",
            "/BYTES",
            "/FP",
            "/NDL",
        };

        if (settings.Operation == CopyOperation.Move)
        {
            arguments.Add("/MOVE");
            arguments.Add("/IS");
            arguments.Add("/IT");
            arguments.Add("/IM");
        }

        if (settings.ExcludeHiddenSystemFiles)
        {
            arguments.Add("/XA:SH");
        }

        switch (settings.LinkHandling)
        {
            case LinkHandling.Skip:
                arguments.Add("/XJ");
                arguments.Add("/XJD");
                arguments.Add("/XJF");
                break;
            case LinkHandling.CopyLink:
                arguments.Add("/SJ");
                arguments.Add("/SL");
                break;
            case LinkHandling.FollowTarget:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(settings), settings.LinkHandling, AppText.Get(TextId.UnknownLinkHandling));
        }

        if (filePatterns.Count > 0)
        {
            arguments.Add("/XF");
            arguments.AddRange(filePatterns);
        }

        if (excludedDirectories.Length > 0)
        {
            arguments.Add("/XD");
            arguments.AddRange(excludedDirectories);
        }

        if (listOnly)
        {
            arguments.Add("/L");
        }

        var plan = new RobocopyPlan(
            normalizedSource,
            normalizedDestination,
            new ReadOnlyCollection<string>(arguments),
            new ReadOnlyCollection<string>(discoveredDirectories),
            new ReadOnlyCollection<string>(warnings));

        if (plan.DisplayCommand.Length > SafeCommandLength)
        {
            throw new InvalidOperationException(
                AppText.Get(TextId.TooManyExclusions));
        }

        return plan;
    }

    public static IReadOnlyList<string> SplitPatterns(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value
            .Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(TrimBalancedQuotes)
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] DiscoverExcludedDirectories(
        string source,
        CopySettings settings,
        IReadOnlyList<string> directoryPatterns,
        List<string> warnings)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(source));

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            DirectoryInfo[] children;
            try
            {
                children = current.GetDirectories();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                if (!PathEquals(current.FullName, source))
                {
                    excluded.Add(current.FullName);
                }

                warnings.Add(AppText.Format(TextId.CannotReadDirectory, current.FullName, ex.Message));
                continue;
            }

            foreach (var child in children)
            {
                FileAttributes attributes;
                try
                {
                    attributes = child.Attributes;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    excluded.Add(child.FullName);
                    warnings.Add(AppText.Format(TextId.CannotReadDirectoryAttributes, child.FullName, ex.Message));
                    continue;
                }

                var relativePath = Path.GetRelativePath(source, child.FullName);
                if (MatchesAnyDirectoryPattern(child.Name, relativePath, child.FullName, directoryPatterns))
                {
                    excluded.Add(child.FullName);
                    continue;
                }

                var isReparsePoint = attributes.HasFlag(FileAttributes.ReparsePoint);
                if (isReparsePoint)
                {
                    if (settings.LinkHandling == LinkHandling.Skip)
                    {
                        excluded.Add(child.FullName);
                    }

                    // CopyLink is copied as a node. FollowTarget is delegated to Robocopy.
                    // Neither mode should make this safety scan traverse a possibly cyclic target.
                    continue;
                }

                if (settings.ExcludeHiddenSystemDirectories &&
                    (attributes.HasFlag(FileAttributes.Hidden) || attributes.HasFlag(FileAttributes.System)))
                {
                    excluded.Add(child.FullName);
                    continue;
                }

                pending.Push(child);
            }
        }

        return excluded.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool MatchesAnyDirectoryPattern(
        string name,
        string relativePath,
        string fullPath,
        IReadOnlyList<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            var normalizedPattern = pattern.Replace('/', '\\').TrimEnd('\\');
            var candidate = normalizedPattern.Contains('\\', StringComparison.Ordinal) || normalizedPattern.Contains(':', StringComparison.Ordinal)
                ? normalizedPattern.Contains(':', StringComparison.Ordinal) ? fullPath : relativePath
                : name;

            if (WildcardMatch(candidate, normalizedPattern))
            {
                return true;
            }
        }

        return false;
    }

    private static bool WildcardMatch(string candidate, string pattern)
    {
        var expression = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(candidate, expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static void ValidateLinkSafety(string source, CopySettings settings)
    {
        if (settings.Operation == CopyOperation.Move && settings.LinkHandling == LinkHandling.FollowTarget)
        {
            throw new InvalidOperationException(
                AppText.Get(TextId.MoveFollowForbidden));
        }

        if (File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint) &&
            settings.LinkHandling != LinkHandling.FollowTarget)
        {
            throw new InvalidOperationException(
                AppText.Get(TextId.RootLinkForbidden));
        }
    }

    private static void ValidateExclusionPatterns(IEnumerable<string> patterns, string displayName)
    {
        var optionLikePattern = patterns.FirstOrDefault(pattern => pattern.StartsWith('/'));
        if (optionLikePattern is not null)
        {
            throw new ArgumentException(
                AppText.Format(TextId.OptionLikeExclusion, displayName, optionLikePattern));
        }
    }

    private static string NormalizeExistingDirectory(string value, string displayName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(AppText.Format(TextId.SelectNamedDirectory, displayName));
        }

        var fullPath = Path.GetFullPath(value.Trim());
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(AppText.Format(TextId.NamedDirectoryMissing, displayName, fullPath));
        }

        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    private static string NormalizeDestination(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(AppText.Get(TextId.SelectDestinationDirectory));
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value.Trim()));
    }

    private static void ValidatePathRelationship(string source, string destination)
    {
        var resolvedSource = ResolveDirectoryLinks(source);
        var resolvedDestination = ResolveDirectoryLinks(destination);
        if (PathEquals(source, destination) || PathEquals(resolvedSource, resolvedDestination))
        {
            throw new InvalidOperationException(AppText.Get(TextId.SameSourceDestination));
        }

        var sourcePrefix = EnsureTrailingSeparator(source);
        var destinationPrefix = EnsureTrailingSeparator(destination);
        var resolvedSourcePrefix = EnsureTrailingSeparator(resolvedSource);
        var resolvedDestinationPrefix = EnsureTrailingSeparator(resolvedDestination);
        if (destinationPrefix.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase) ||
            resolvedDestinationPrefix.StartsWith(resolvedSourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(AppText.Get(TextId.DestinationInsideSource));
        }
    }

    private static string ResolveDirectoryLinks(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root))
        {
            return fullPath;
        }

        var segments = fullPath[root.Length..]
            .Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        for (var index = 0; index < segments.Length; index++)
        {
            var candidate = Path.Combine(current, segments[index]);
            if (!Directory.Exists(candidate))
            {
                for (; index < segments.Length; index++)
                {
                    current = Path.Combine(current, segments[index]);
                }

                break;
            }

            var directory = new DirectoryInfo(candidate);
            var target = directory.Attributes.HasFlag(FileAttributes.ReparsePoint)
                ? directory.ResolveLinkTarget(returnFinalTarget: true)
                : null;
            current = target is null ? candidate : Path.GetFullPath(target.FullName);
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private static string EnsureTrailingSeparator(string path) =>
        Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;

    private static string TrimBalancedQuotes(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value[1..^1].Trim();
        }

        return value.Trim();
    }
}
