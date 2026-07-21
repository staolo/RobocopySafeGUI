using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using RobocopySafe.Core;
using RobocopySafe.Gui;

var artifactRoot = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")),
        "artifacts",
        $"integration-{DateTime.Now:yyyyMMdd-HHmmss}");

Directory.CreateDirectory(artifactRoot);
var source = Path.Combine(artifactRoot, "source");
var external = Path.Combine(artifactRoot, "external-target");
Directory.CreateDirectory(source);
Directory.CreateDirectory(external);

File.WriteAllText(Path.Combine(source, "visible.txt"), "visible");
File.WriteAllText(Path.Combine(source, "temporary.tmp"), "excluded by file pattern");
File.WriteAllText(Path.Combine(external, "external-payload.txt"), "outside source tree");

var hiddenFile = Path.Combine(source, "hidden-system-file.txt");
File.WriteAllText(hiddenFile, "hidden system file");
File.SetAttributes(hiddenFile, File.GetAttributes(hiddenFile) | FileAttributes.Hidden | FileAttributes.System);

var hiddenDirectory = Path.Combine(source, "HiddenSystemDir");
Directory.CreateDirectory(hiddenDirectory);
File.WriteAllText(Path.Combine(hiddenDirectory, "inside-hidden-system-dir.txt"), "hidden system directory payload");
File.SetAttributes(hiddenDirectory, File.GetAttributes(hiddenDirectory) | FileAttributes.Hidden | FileAttributes.System);

var customDirectory = Path.Combine(source, "CacheData");
Directory.CreateDirectory(customDirectory);
File.WriteAllText(Path.Combine(customDirectory, "cache.bin"), "excluded by directory pattern");

var junction = Path.Combine(source, "JunctionToExternal");
await CreateJunctionAsync(junction, external);

var baseSettings = new CopySettings
{
    ExcludeHiddenSystemFiles = true,
    ExcludeHiddenSystemDirectories = true,
    LinkHandling = LinkHandling.Skip,
    ExcludedFilePatterns = "*.tmp",
    ExcludedDirectoryPatterns = "$RECYCLE.BIN;System Volume Information;CacheData",
    Threads = 4,
    Retries = 1,
    RetryWaitSeconds = 1,
};

var safeDestination = Path.Combine(artifactRoot, "destination-safe");
var safePlan = RobocopyPlanBuilder.Build(source, safeDestination, baseSettings);
AssertContains(safePlan.Arguments, "/XA:SH", "Safe plan must exclude hidden/system files.");
AssertContains(safePlan.Arguments, "/XJ", "Safe plan must exclude junctions.");
AssertContains(safePlan.Arguments, "/XJD", "Safe plan must exclude directory reparse points.");
AssertContains(safePlan.Arguments, "/XJF", "Safe plan must exclude file reparse points.");
AssertContains(safePlan.Arguments, "/MT:4", "Safe plan must preserve configured Robocopy multithreading.");
AssertContains(safePlan.Arguments, "/NDL", "Safe plan must suppress redundant directory-list output.");
AssertDoesNotContain(safePlan.Arguments, "/ETA", "Safe plan must not request high-frequency ETA output.");
AssertDoesNotContain(safePlan.Arguments, "/UNICODE", "Piped Robocopy output must not use the broken hybrid /UNICODE format.");
AssertDoesNotContain(safePlan.Arguments, "/IS", "Copy plans must not recopy identical files.");
AssertDoesNotContain(safePlan.Arguments, "/IT", "Copy plans must not recopy tweaked files.");
AssertDoesNotContain(safePlan.Arguments, "/IM", "Copy plans must not recopy modified files.");
Assert(safePlan.ExcludedDirectories.Contains(hiddenDirectory, StringComparer.OrdinalIgnoreCase), "Hidden/system directory was not discovered.");
Assert(safePlan.ExcludedDirectories.Contains(junction, StringComparer.OrdinalIgnoreCase), "Junction was not discovered.");
Assert(safePlan.ExcludedDirectories.Contains(customDirectory, StringComparer.OrdinalIgnoreCase), "Custom excluded directory was not discovered.");

var safeExitCode = await RunRobocopyAsync(safePlan);
Assert(File.Exists(Path.Combine(safeDestination, "visible.txt")), "Visible file was not copied.");
Assert(!File.Exists(Path.Combine(safeDestination, "hidden-system-file.txt")), "Hidden/system file was copied.");
Assert(!File.Exists(Path.Combine(safeDestination, "HiddenSystemDir", "inside-hidden-system-dir.txt")), "Hidden/system directory payload was copied.");
Assert(!File.Exists(Path.Combine(safeDestination, "JunctionToExternal", "external-payload.txt")), "Junction target payload was copied.");
Assert(!File.Exists(Path.Combine(safeDestination, "CacheData", "cache.bin")), "Custom excluded directory was copied.");
Assert(!File.Exists(Path.Combine(safeDestination, "temporary.tmp")), "Custom excluded file was copied.");

var linkDestination = Path.Combine(artifactRoot, "destination-link-node");
var linkPlan = RobocopyPlanBuilder.Build(
    source,
    linkDestination,
    baseSettings with { LinkHandling = LinkHandling.CopyLink });
AssertContains(linkPlan.Arguments, "/SJ", "Copy-link plan must preserve junction nodes.");
AssertContains(linkPlan.Arguments, "/SL", "Copy-link plan must preserve symbolic link nodes.");
AssertDoesNotContain(linkPlan.Arguments, "/XJ", "Copy-link plan must not exclude junctions.");
var linkExitCode = await RunRobocopyAsync(linkPlan);
var copiedJunction = Path.Combine(linkDestination, "JunctionToExternal");
Assert(Directory.Exists(copiedJunction), "Junction node was not copied.");
Assert(File.GetAttributes(copiedJunction).HasFlag(FileAttributes.ReparsePoint), "Copied junction became a normal directory.");

var followDestination = Path.Combine(artifactRoot, "destination-follow");
var followPlan = RobocopyPlanBuilder.Build(
    source,
    followDestination,
    baseSettings with { LinkHandling = LinkHandling.FollowTarget });
AssertDoesNotContain(followPlan.Arguments, "/XJ", "Follow plan must not exclude junctions.");
AssertDoesNotContain(followPlan.Arguments, "/SJ", "Follow plan must not preserve junctions.");
AssertDoesNotContain(followPlan.Arguments, "/SL", "Follow plan must not preserve symbolic links.");
var followExitCode = await RunRobocopyAsync(followPlan);
Assert(File.Exists(Path.Combine(followDestination, "JunctionToExternal", "external-payload.txt")), "Follow mode did not copy the junction target payload.");
Assert(!File.GetAttributes(Path.Combine(followDestination, "JunctionToExternal")).HasFlag(FileAttributes.ReparsePoint), "Follow mode unexpectedly preserved the junction node.");

var rootJunctionRejected = false;
try
{
    _ = RobocopyPlanBuilder.Build(
        junction,
        Path.Combine(artifactRoot, "destination-root-junction"),
        baseSettings);
}
catch (InvalidOperationException)
{
    rootJunctionRejected = true;
}
Assert(rootJunctionRejected, "Root junction was not rejected in skip-link mode.");

var rootJunctionFollowPlan = RobocopyPlanBuilder.Build(
    junction,
    Path.Combine(artifactRoot, "destination-root-junction-follow"),
    baseSettings with { LinkHandling = LinkHandling.FollowTarget });
AssertDoesNotContain(rootJunctionFollowPlan.Arguments, "/XJ", "Explicit root-junction follow plan must not exclude junctions.");

var moveFollowRejected = false;
try
{
    _ = RobocopyPlanBuilder.Build(
        source,
        Path.Combine(artifactRoot, "destination-move-follow"),
        baseSettings with { Operation = CopyOperation.Move, LinkHandling = LinkHandling.FollowTarget });
}
catch (InvalidOperationException)
{
    moveFollowRejected = true;
}
Assert(moveFollowRejected, "Move-follow mode was not rejected.");

var optionLikeExclusionRejected = false;
try
{
    _ = RobocopyPlanBuilder.Build(
        source,
        Path.Combine(artifactRoot, "destination-option-injection"),
        baseSettings with { ExcludedFilePatterns = "/MIR" });
}
catch (ArgumentException)
{
    optionLikeExclusionRejected = true;
}
Assert(optionLikeExclusionRejected, "Option-like exclusion pattern was not rejected.");

var destinationAliasToSource = Path.Combine(artifactRoot, "destination-alias-to-source");
await CreateJunctionAsync(destinationAliasToSource, source);
var destinationJunctionInsideSourceRejected = false;
try
{
    _ = RobocopyPlanBuilder.Build(
        source,
        Path.Combine(destinationAliasToSource, "recursive-target"),
        baseSettings);
}
catch (InvalidOperationException)
{
    destinationJunctionInsideSourceRejected = true;
}
Assert(
    destinationJunctionInsideSourceRejected,
    "Destination path through a junction back into the source was not rejected.");

var destinationTextInsideSourceThroughJunctionRejected = false;
try
{
    _ = RobocopyPlanBuilder.Build(
        source,
        Path.Combine(junction, "nested-destination"),
        baseSettings);
}
catch (InvalidOperationException)
{
    destinationTextInsideSourceThroughJunctionRejected = true;
}
Assert(
    destinationTextInsideSourceThroughJunctionRejected,
    "Destination text inside the source was accepted after resolving its junction outside the source.");

var previewDestination = Path.Combine(artifactRoot, "destination-preview");
var previewPlan = RobocopyPlanBuilder.Build(source, previewDestination, baseSettings, listOnly: true);
AssertContains(previewPlan.Arguments, "/L", "Preview plan must use /L.");
var previewExitCode = await RunRobocopyAsync(previewPlan);
Assert(!File.Exists(Path.Combine(previewDestination, "visible.txt")), "Preview mode wrote a file.");

var moveSource = Path.Combine(artifactRoot, "move-overlap-source");
var moveDestination = Path.Combine(artifactRoot, "move-overlap-destination");
var moveNestedSource = Path.Combine(moveSource, "nested");
var moveNestedDestination = Path.Combine(moveDestination, "nested");
Directory.CreateDirectory(moveNestedSource);
Directory.CreateDirectory(moveNestedDestination);

var sameSource = Path.Combine(moveSource, "same.txt");
var sameDestination = Path.Combine(moveDestination, "same.txt");
File.WriteAllText(sameSource, "identical payload");
File.Copy(sameSource, sameDestination);
File.SetLastWriteTimeUtc(sameDestination, File.GetLastWriteTimeUtc(sameSource));

var tweakedSource = Path.Combine(moveNestedSource, "tweaked.txt");
var tweakedDestination = Path.Combine(moveNestedDestination, "tweaked.txt");
File.WriteAllText(tweakedSource, "tweaked payload");
File.Copy(tweakedSource, tweakedDestination);
File.SetLastWriteTimeUtc(tweakedDestination, File.GetLastWriteTimeUtc(tweakedSource));
File.SetAttributes(tweakedDestination, File.GetAttributes(tweakedDestination) | FileAttributes.Hidden);

var uniqueMoveSource = Path.Combine(moveSource, "unique.txt");
File.WriteAllText(uniqueMoveSource, "source-only payload");

var movePlan = RobocopyPlanBuilder.Build(
    moveSource,
    moveDestination,
    baseSettings with
    {
        Operation = CopyOperation.Move,
        ExcludedFilePatterns = string.Empty,
        ExcludedDirectoryPatterns = string.Empty,
    });
AssertContains(movePlan.Arguments, "/MOVE", "Move plan must use /MOVE.");
AssertContains(movePlan.Arguments, "/IS", "Move plan must process identical destination files.");
AssertContains(movePlan.Arguments, "/IT", "Move plan must process tweaked destination files.");
AssertContains(movePlan.Arguments, "/IM", "Move plan must process modified destination files.");
var moveExitCode = await RunRobocopyAsync(movePlan);
var moveSourceExistedAfterRobocopy = Directory.Exists(moveSource);
var moveFinalization = MoveSourceFinalizer.Finalize(moveSource);
Assert(moveFinalization.Complete, "Move source finalization was not complete.");
Assert(!Directory.Exists(moveSource), "Move source directory still exists after overlapping move.");
Assert(File.ReadAllText(sameDestination) == "identical payload", "Identical destination payload changed.");
Assert(File.ReadAllText(tweakedDestination) == "tweaked payload", "Tweaked destination payload changed.");
Assert(!File.GetAttributes(tweakedDestination).HasFlag(FileAttributes.Hidden), "Tweaked destination attributes were not updated.");
Assert(File.ReadAllText(Path.Combine(moveDestination, "unique.txt")) == "source-only payload", "Unique move file is missing.");

var residualSource = Path.Combine(artifactRoot, "move-residual-source");
Directory.CreateDirectory(residualSource);
File.WriteAllText(Path.Combine(residualSource, "excluded-or-locked.txt"), "must remain");
var residualFinalization = MoveSourceFinalizer.Finalize(residualSource);
Assert(!residualFinalization.Complete, "Non-empty move source was incorrectly marked complete.");
Assert(Directory.Exists(residualSource), "Non-empty move source was deleted.");
Assert(residualFinalization.RemainingEntries.Count == 1, "Move residual sample was not reported.");

var emptyMoveSource = Path.Combine(artifactRoot, "move-empty-source");
Directory.CreateDirectory(emptyMoveSource);
var emptyFinalization = MoveSourceFinalizer.Finalize(emptyMoveSource);
Assert(emptyFinalization.Complete, "Empty move source finalization did not complete.");
Assert(emptyFinalization.SourceDeleted, "Empty move source was not deleted.");
Assert(!Directory.Exists(emptyMoveSource), "Empty move source directory still exists.");

const string logTrimNotice = "Older output was trimmed.";
var shortVisibleLog = "short log";
var unchangedVisibleLog = LogTextLimiter.TrimToRecentLines(shortVisibleLog, 100, 50, logTrimNotice);
Assert(ReferenceEquals(shortVisibleLog, unchangedVisibleLog), "Short visible log should not be replaced.");

var latestLogLine = "line-199-" + new string('x', 20);
var longVisibleLog = string.Join(
    '\n',
    Enumerable.Range(0, 200).Select(index => $"line-{index:D3}-{new string('x', 20)}"));
var trimmedVisibleLog = LogTextLimiter.TrimToRecentLines(longVisibleLog, 1_000, 600, logTrimNotice);
Assert(
    trimmedVisibleLog.StartsWith(logTrimNotice + Environment.NewLine, StringComparison.Ordinal),
    "Trimmed visible log is missing its notice.");
Assert(trimmedVisibleLog.EndsWith(latestLogLine, StringComparison.Ordinal), "Trimmed visible log lost its latest line.");
Assert(!trimmedVisibleLog.Contains("line-000-", StringComparison.Ordinal), "Trimmed visible log retained stale lines.");
Assert(
    trimmedVisibleLog.Length <= logTrimNotice.Length + Environment.NewLine.Length + 640,
    "Trimmed visible log retained too much text.");

var originalLanguage = AppText.LanguagePreference;
var textIds = Enum.GetValues<TextId>();
string englishWindowTitle;
string chineseWindowTitle;
try
{
    foreach (var language in new[] { UiLanguage.English, UiLanguage.SimplifiedChinese })
    {
        AppText.SetLanguage(language);
        foreach (var textId in textIds)
        {
            Assert(
                !string.IsNullOrWhiteSpace(AppText.Get(textId)),
                $"Localization value is empty: {language}/{textId}");
        }
    }

    AppText.SetLanguage(UiLanguage.English);
    englishWindowTitle = AppText.Get(TextId.WindowTitle);
    AppText.SetLanguage(UiLanguage.SimplifiedChinese);
    chineseWindowTitle = AppText.Get(TextId.WindowTitle);
    Assert(
        !string.Equals(englishWindowTitle, chineseWindowTitle, StringComparison.Ordinal),
        "English and Chinese window titles must differ.");
}
finally
{
    AppText.SetLanguage(originalLanguage);
}

var relationshipGuardTriggered = false;
try
{
    _ = RobocopyPlanBuilder.Build(source, Path.Combine(source, "nested-destination"), baseSettings);
}
catch (InvalidOperationException)
{
    relationshipGuardTriggered = true;
}
Assert(relationshipGuardTriggered, "Destination-inside-source guard did not trigger.");

var summary = new
{
    ArtifactRoot = artifactRoot,
    Safe = new { ExitCode = safeExitCode, Passed = true },
    CopyLink = new { ExitCode = linkExitCode, Passed = true, IsReparsePoint = true },
    FollowTarget = new { ExitCode = followExitCode, Passed = true, MaterializedPayload = true },
    Preview = new { ExitCode = previewExitCode, Passed = true, WroteData = false },
    MoveOverlap = new
    {
        ExitCode = moveExitCode,
        Passed = true,
        SourceExistedAfterRobocopy = moveSourceExistedAfterRobocopy,
        FinalizationComplete = moveFinalization.Complete,
        SourceRemoved = !Directory.Exists(moveSource),
    },
    MoveSourceFinalizer = new
    {
        PreservedNonEmptySource = !residualFinalization.Complete && Directory.Exists(residualSource),
        DeletedEmptySource = emptyFinalization.SourceDeleted,
    },
    SafetyGuards = new
    {
        RootJunctionRejected = rootJunctionRejected,
        MoveFollowRejected = moveFollowRejected,
        OptionLikeExclusionRejected = optionLikeExclusionRejected,
        DestinationJunctionInsideSourceRejected = destinationJunctionInsideSourceRejected,
        DestinationTextInsideSourceThroughJunctionRejected = destinationTextInsideSourceThroughJunctionRejected,
    },
    VisibleLogTrimming = new
    {
        Passed = true,
        RetainedLatestLine = trimmedVisibleLog.EndsWith(latestLogLine, StringComparison.Ordinal),
    },
    Localization = new
    {
        Passed = true,
        Languages = 2,
        TextIds = textIds.Length,
    },
    DestinationInsideSourceGuard = relationshipGuardTriggered,
};

var summaryPath = Path.Combine(artifactRoot, "summary.json");
await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));

static async Task CreateJunctionAsync(string linkPath, string targetPath)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
    };
    startInfo.ArgumentList.Add("/c");
    startInfo.ArgumentList.Add("mklink");
    startInfo.ArgumentList.Add("/J");
    startInfo.ArgumentList.Add(linkPath);
    startInfo.ArgumentList.Add(targetPath);

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start mklink.");
    await process.WaitForExitAsync();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException("Unable to create test junction: " + await process.StandardError.ReadToEndAsync());
    }
}

static async Task<int> RunRobocopyAsync(RobocopyPlan plan)
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    var robocopyOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
    var startInfo = new ProcessStartInfo
    {
        FileName = Path.Combine(Environment.SystemDirectory, "robocopy.exe"),
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
        StandardOutputEncoding = robocopyOutputEncoding,
        StandardErrorEncoding = robocopyOutputEncoding,
    };
    foreach (var argument in plan.Arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start robocopy.");
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    var output = await stdout;
    var error = await stderr;
    if (process.ExitCode >= 8)
    {
        throw new InvalidOperationException($"Robocopy failed with {process.ExitCode}.\n{output}\n{error}");
    }

    return process.ExitCode;
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertContains(IReadOnlyList<string> values, string expected, string message) =>
    Assert(values.Contains(expected, StringComparer.OrdinalIgnoreCase), message);

static void AssertDoesNotContain(IReadOnlyList<string> values, string expected, string message) =>
    Assert(!values.Contains(expected, StringComparer.OrdinalIgnoreCase), message);
