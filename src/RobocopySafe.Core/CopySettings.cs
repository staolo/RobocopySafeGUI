namespace RobocopySafe.Core;

public enum CopyOperation
{
    Copy,
    Move,
}

public enum LinkHandling
{
    Skip,
    CopyLink,
    FollowTarget,
}

public sealed record CopySettings
{
    public bool ExcludeHiddenSystemFiles { get; init; } = true;

    public bool ExcludeHiddenSystemDirectories { get; init; } = true;

    public LinkHandling LinkHandling { get; init; } = LinkHandling.Skip;

    public string ExcludedFilePatterns { get; init; } = string.Empty;

    public string ExcludedDirectoryPatterns { get; init; } = "$RECYCLE.BIN;System Volume Information";

    public int Threads { get; init; } = Math.Clamp(Environment.ProcessorCount, 4, 16);

    public int Retries { get; init; } = 2;

    public int RetryWaitSeconds { get; init; } = 2;

    public CopyOperation Operation { get; init; } = CopyOperation.Copy;
}
