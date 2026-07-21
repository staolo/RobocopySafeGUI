using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using RobocopySafe.Core;

namespace RobocopySafe.Gui;

internal sealed partial class MainForm : Form
{
    private const int MaxPendingLogLines = 4_000;
    private const int MaxVisibleLogCharacters = 400_000;
    private const int TrimmedVisibleLogCharacters = 250_000;
    private const int MaxLogLinesPerTick = 400;
    private const int MaxLogCharactersPerTick = 64 * 1024;

    private readonly RobocopyProcessRunner runner = new();
    private readonly bool clipboardPaste;
    private readonly ConcurrentQueue<string> pendingLogLines = new();
    private readonly object logWriterLock = new();
    private readonly System.Windows.Forms.Timer uiFlushTimer = new() { Interval = 100 };
    private readonly Stopwatch operationStopwatch = new();
    private CancellationTokenSource? operationCancellation;
    private StreamWriter? logWriter;
    private RobocopyProgress? latestProgress;
    private int pendingLogLineCount;
    private long omittedVisibleLogLines;
    private bool isRunning;
    private bool isLoading = true;
    private bool isApplyingLanguage;
    private bool robocopyActive;
    private bool activePreviewOnly;

    public MainForm(StartupOptions? startupOptions = null, UiPreferences? preferences = null)
    {
        var loadedPreferences = preferences ?? SettingsStore.Load();
        AppText.SetLanguage(loadedPreferences.Language);
        clipboardPaste = startupOptions?.ClipboardPaste == true;
        InitializeInterface();
        ApplyPreferences(loadedPreferences);
        ApplyStartupOptions(startupOptions);
        WireEvents();
        runner.OutputReceived += AppendLog;
        runner.ProgressChanged += UpdateProgress;
        uiFlushTimer.Tick += (_, _) => FlushUiUpdates();
        uiFlushTimer.Start();
        isLoading = false;
        RefreshContextMenuButton();
        UpdateCommandPreview();
        if (clipboardPaste && startButton.Enabled)
        {
            SetStatus(
                AppText.Get(
                    operationComboBox.SelectedIndex == 1
                        ? TextId.ClipboardCutLoaded
                        : TextId.ClipboardCopyLoaded),
                Primary);
        }
    }

    private string LogsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RobocopySafeGUI",
        "logs");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            operationCancellation?.Cancel();
            operationCancellation?.Dispose();
            uiFlushTimer.Stop();
            uiFlushTimer.Dispose();
            StopLog();
            runner.Dispose();
            toolTip.Dispose();
        }

        base.Dispose(disposing);
        if (disposing)
        {
            applicationIcon?.Dispose();
            applicationIcon = null;
        }
    }

    private void WireEvents()
    {
        sourceBrowseButton.Click += (_, _) => BrowseForFolder(sourceTextBox, createNewFolder: false);
        destinationBrowseButton.Click += (_, _) => BrowseForFolder(destinationTextBox, createNewFolder: true);
        previewButton.Click += async (_, _) => await RunOperationAsync(previewOnly: true);
        startButton.Click += async (_, _) => await RunOperationAsync(previewOnly: false);
        cancelButton.Click += (_, _) => CancelOperation();
        openLogsButton.Click += (_, _) => OpenLogsDirectory();
        contextMenuButton.Click += (_, _) => ToggleContextMenu();
        Shown += (_, _) => ResetInitialFocus();
        FormClosing += HandleFormClosing;

        sourceTextBox.TextChanged += (_, _) => HandleSettingsChanged();
        destinationTextBox.TextChanged += (_, _) => HandleSettingsChanged();
        excludedFilesTextBox.TextChanged += (_, _) => HandleSettingsChanged();
        excludedDirectoriesTextBox.TextChanged += (_, _) => HandleSettingsChanged();
        excludeFilesCheckBox.CheckedChanged += (_, _) => HandleSettingsChanged();
        excludeDirectoriesCheckBox.CheckedChanged += (_, _) => HandleSettingsChanged();
        threadsInput.ValueChanged += (_, _) => HandleSettingsChanged();
        retriesInput.ValueChanged += (_, _) => HandleSettingsChanged();
        waitInput.ValueChanged += (_, _) => HandleSettingsChanged();
        operationComboBox.SelectedIndexChanged += (_, _) =>
        {
            if (isApplyingLanguage)
            {
                return;
            }

            UpdateStartButtonText();
            HandleSettingsChanged();
        };
        linkHandlingComboBox.SelectedIndexChanged += (_, _) =>
        {
            if (isApplyingLanguage)
            {
                return;
            }

            if (!isLoading && linkHandlingComboBox.SelectedIndex == (int)LinkHandling.FollowTarget)
            {
                SetStatus(AppText.Get(TextId.FollowWarning), Warning);
            }

            HandleSettingsChanged();
        };
        languageComboBox.SelectedIndexChanged += (_, _) => HandleLanguageChanged();
    }

    private void ResetInitialFocus()
    {
        sourceTextBox.Select(0, 0);
        sourceTextBox.ScrollToCaret();
        destinationTextBox.Select(0, 0);
        destinationTextBox.ScrollToCaret();
        startButton.Select();
    }

    private void ApplyPreferences(UiPreferences preferences)
    {
        languageComboBox.SelectedIndex = Enum.IsDefined(preferences.Language)
            ? (int)preferences.Language
            : (int)UiLanguage.System;
        sourceTextBox.Text = preferences.Source;
        destinationTextBox.Text = preferences.Destination;
        excludeFilesCheckBox.Checked = preferences.ExcludeHiddenSystemFiles;
        excludeDirectoriesCheckBox.Checked = preferences.ExcludeHiddenSystemDirectories;
        excludedFilesTextBox.Text = preferences.ExcludedFilePatterns;
        excludedDirectoriesTextBox.Text = preferences.ExcludedDirectoryPatterns;
        operationComboBox.SelectedIndex = Enum.IsDefined(preferences.Operation)
            ? (int)preferences.Operation
            : (int)CopyOperation.Copy;
        linkHandlingComboBox.SelectedIndex = Enum.IsDefined(preferences.LinkHandling)
            ? (int)preferences.LinkHandling
            : (int)LinkHandling.Skip;
        threadsInput.Value = Math.Clamp(preferences.Threads, (int)threadsInput.Minimum, (int)threadsInput.Maximum);
        retriesInput.Value = Math.Clamp(preferences.Retries, (int)retriesInput.Minimum, (int)retriesInput.Maximum);
        waitInput.Value = Math.Clamp(preferences.RetryWaitSeconds, (int)waitInput.Minimum, (int)waitInput.Maximum);
    }

    private void ApplyStartupOptions(StartupOptions? options)
    {
        if (options is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(options.Source))
        {
            sourceTextBox.Text = options.Source;
        }

        if (!string.IsNullOrWhiteSpace(options.Destination))
        {
            destinationTextBox.Text = options.Destination;
        }

        if (options.Operation is not null)
        {
            operationComboBox.SelectedIndex = (int)options.Operation.Value;
        }
    }

    private UiPreferences CollectPreferences() => new()
    {
        Language = Enum.IsDefined((UiLanguage)Math.Max(languageComboBox.SelectedIndex, 0))
            ? (UiLanguage)Math.Max(languageComboBox.SelectedIndex, 0)
            : UiLanguage.System,
        Source = sourceTextBox.Text.Trim(),
        Destination = destinationTextBox.Text.Trim(),
        ExcludeHiddenSystemFiles = excludeFilesCheckBox.Checked,
        ExcludeHiddenSystemDirectories = excludeDirectoriesCheckBox.Checked,
        LinkHandling = (LinkHandling)Math.Max(linkHandlingComboBox.SelectedIndex, 0),
        ExcludedFilePatterns = excludedFilesTextBox.Text.Trim(),
        ExcludedDirectoryPatterns = excludedDirectoriesTextBox.Text.Trim(),
        Threads = (int)threadsInput.Value,
        Retries = (int)retriesInput.Value,
        RetryWaitSeconds = (int)waitInput.Value,
        Operation = operationComboBox.SelectedIndex == 1 ? CopyOperation.Move : CopyOperation.Copy,
    };

    private CopySettings CollectCopySettings()
    {
        var preferences = CollectPreferences();
        return new CopySettings
        {
            ExcludeHiddenSystemFiles = preferences.ExcludeHiddenSystemFiles,
            ExcludeHiddenSystemDirectories = preferences.ExcludeHiddenSystemDirectories,
            LinkHandling = preferences.LinkHandling,
            ExcludedFilePatterns = preferences.ExcludedFilePatterns,
            ExcludedDirectoryPatterns = preferences.ExcludedDirectoryPatterns,
            Threads = preferences.Threads,
            Retries = preferences.Retries,
            RetryWaitSeconds = preferences.RetryWaitSeconds,
            Operation = preferences.Operation,
        };
    }

    private void BrowseForFolder(TextBox target, bool createNewFolder)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = AppText.Get(
                createNewFolder ? TextId.SelectDestinationDialog : TextId.SelectSourceDialog),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = createNewFolder,
            SelectedPath = Directory.Exists(target.Text) ? target.Text : string.Empty,
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            target.Text = dialog.SelectedPath;
        }
    }

    private void HandleSettingsChanged()
    {
        if (!isLoading && !isRunning)
        {
            UpdateCommandPreview();
        }
    }

    private void HandleLanguageChanged()
    {
        if (isLoading || isApplyingLanguage || languageComboBox.SelectedIndex < 0)
        {
            return;
        }

        var selectedLanguage = (UiLanguage)languageComboBox.SelectedIndex;
        AppText.SetLanguage(Enum.IsDefined(selectedLanguage) ? selectedLanguage : UiLanguage.System);
        ApplyLocalizedText();

        Exception? persistenceError = null;
        try
        {
            SettingsStore.Save(CollectPreferences());
            var executablePath = Environment.ProcessPath;
            if (executablePath is not null && ExplorerContextMenu.IsInstalledFor(executablePath))
            {
                ExplorerContextMenu.Install(executablePath);
            }
        }
        catch (Exception ex) when (
            ex is IOException or
                UnauthorizedAccessException or
                System.Security.SecurityException)
        {
            persistenceError = ex;
        }

        UpdateCommandPreview();
        if (persistenceError is not null)
        {
            SetStatus(AppText.Format(TextId.PrefixError, persistenceError.Message), Color.Firebrick);
        }
    }

    private void ApplyLocalizedText()
    {
        isApplyingLanguage = true;
        SuspendLayout();
        try
        {
            Text = AppText.Get(TextId.WindowTitle);
            ApplyTaggedText(Controls);

            ReplaceComboItems(
                languageComboBox,
                (int)AppText.LanguagePreference,
                AppText.Get(TextId.LanguageSystem),
                AppText.Get(TextId.LanguageChinese),
                AppText.Get(TextId.LanguageEnglish));
            ReplaceComboItems(
                operationComboBox,
                Math.Max(operationComboBox.SelectedIndex, 0),
                AppText.Get(TextId.OperationCopy),
                AppText.Get(TextId.OperationMove));
            ReplaceComboItems(
                linkHandlingComboBox,
                Math.Max(linkHandlingComboBox.SelectedIndex, 0),
                AppText.Get(TextId.LinkSkip),
                AppText.Get(TextId.LinkCopy),
                AppText.Get(TextId.LinkFollow));

            sourceTextBox.PlaceholderText = AppText.Get(TextId.SourcePlaceholder);
            destinationTextBox.PlaceholderText = AppText.Get(TextId.DestinationPlaceholder);
            excludedFilesTextBox.PlaceholderText = AppText.Get(TextId.ExcludedFilesPlaceholder);
            excludedDirectoriesTextBox.PlaceholderText = AppText.Get(TextId.ExcludedDirectoriesPlaceholder);
            ConfigureToolTips();
            UpdateStartButtonText();
            RefreshContextMenuButton();
            SetStatus(AppText.Get(TextId.Ready), TextMuted);
        }
        finally
        {
            ResumeLayout(performLayout: true);
            isApplyingLanguage = false;
        }
    }

    private static void ApplyTaggedText(Control.ControlCollection controls)
    {
        foreach (Control control in controls)
        {
            if (control.Tag is TextId textId)
            {
                control.Text = AppText.Get(textId);
            }

            if (control.HasChildren)
            {
                ApplyTaggedText(control.Controls);
            }
        }
    }

    private static void ReplaceComboItems(ComboBox comboBox, int selectedIndex, params string[] items)
    {
        comboBox.BeginUpdate();
        try
        {
            comboBox.Items.Clear();
            comboBox.Items.AddRange(items);
            comboBox.SelectedIndex = Math.Clamp(selectedIndex, 0, items.Length - 1);
        }
        finally
        {
            comboBox.EndUpdate();
        }
    }

    private void UpdateStartButtonText()
    {
        var textId = operationComboBox.SelectedIndex == 1 ? TextId.StartMove : TextId.StartCopy;
        startButton.Tag = textId;
        startButton.Text = AppText.Get(textId);
    }

    private void UpdateCommandPreview(bool updateReadyStatus = true)
    {
        try
        {
            var plan = RobocopyPlanBuilder.Build(
                sourceTextBox.Text,
                destinationTextBox.Text,
                CollectCopySettings(),
                discoverDirectories: false);
            commandTextBox.Text = plan.DisplayCommand;
            startButton.Enabled = true;
            previewButton.Enabled = true;
            if (updateReadyStatus && linkHandlingComboBox.SelectedIndex != (int)LinkHandling.FollowTarget)
            {
                SetStatus(AppText.Get(TextId.Ready), TextMuted);
            }
        }
        catch (Exception ex) when (
            ex is ArgumentException or
                DirectoryNotFoundException or
                InvalidOperationException or
                IOException or
                UnauthorizedAccessException or
                System.Security.SecurityException)
        {
            commandTextBox.Text = string.Empty;
            startButton.Enabled = false;
            previewButton.Enabled = false;
            SetStatus(ex.Message, TextMuted);
        }
    }

    private async Task RunOperationAsync(bool previewOnly)
    {
        if (isRunning)
        {
            return;
        }

        var settings = CollectCopySettings();
        var sourceAtStart = sourceTextBox.Text.Trim();
        if (!previewOnly && settings.Operation == CopyOperation.Move && !ConfirmMove())
        {
            return;
        }

        try
        {
            SettingsStore.Save(CollectPreferences());
            SetRunningState(true);
            SetStatus(AppText.Get(TextId.ScanningExclusions), TextMuted);
            operationStopwatch.Restart();
            activePreviewOnly = previewOnly;
            robocopyActive = false;
            Volatile.Write(ref latestProgress, null);
            ClearPendingLogLines();
            progressBar.Style = ProgressBarStyle.Marquee;
            progressBar.MarqueeAnimationSpeed = 24;
            progressBar.Value = 0;
            logTextBox.Clear();

            var plan = await Task.Run(() => RobocopyPlanBuilder.Build(
                sourceTextBox.Text,
                destinationTextBox.Text,
                settings,
                listOnly: previewOnly,
                discoverDirectories: true));

            commandTextBox.Text = plan.DisplayCommand;
            StartLog(previewOnly);
            AppendLog(AppText.Get(previewOnly ? TextId.LogPreview : TextId.LogStart));
            AppendLog(plan.DisplayCommand);
            AppendLog(AppText.Format(TextId.LogExcludedCount, plan.ExcludedDirectories.Count));
            foreach (var warning in plan.Warnings)
            {
                AppendLog(AppText.Format(TextId.PrefixWarning, warning));
            }

            operationCancellation = new CancellationTokenSource();
            SetStatus(
                AppText.Get(previewOnly ? TextId.StatusPreviewing : TextId.StatusExecuting),
                Primary);
            robocopyActive = true;
            var exitCode = await runner.RunAsync(plan, operationCancellation.Token);
            robocopyActive = false;
            operationStopwatch.Stop();
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.MarqueeAnimationSpeed = 0;
            FlushUiUpdates();

            if (operationCancellation.IsCancellationRequested)
            {
                AppendLog(AppText.Get(TextId.LogCanceled));
                SetStatus(AppText.Get(TextId.StatusCanceled), Warning);
            }
            else if (exitCode < 8)
            {
                AppendLog(AppText.Format(TextId.LogCompleteExit, exitCode));
                SetStatus(
                    AppText.Get(
                        previewOnly
                            ? TextId.StatusPreviewComplete
                            : TextId.StatusOperationComplete),
                    Primary);
                progressBar.Value = 100;
                if (!previewOnly && settings.Operation == CopyOperation.Move)
                {
                    FinalizeSuccessfulMove(sourceAtStart);
                }
            }
            else
            {
                AppendLog(AppText.Format(TextId.LogFailedExit, exitCode));
                SetStatus(AppText.Format(TextId.StatusFailedExit, exitCode), Color.Firebrick);
            }
        }
        catch (Exception ex) when (
            ex is ArgumentException or
                DirectoryNotFoundException or
                InvalidOperationException or
                IOException or
                UnauthorizedAccessException or
                System.Security.SecurityException)
        {
            AppendLog(AppText.Format(TextId.PrefixError, ex.Message));
            SetStatus(ex.Message, Color.Firebrick);
            MessageBox.Show(
                this,
                ex.Message,
                AppText.Get(TextId.CannotExecuteTitle),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            robocopyActive = false;
            operationStopwatch.Stop();
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.MarqueeAnimationSpeed = 0;
            operationCancellation?.Dispose();
            operationCancellation = null;
            StopLog();
            FlushUiUpdates();
            SetRunningState(false);
        }
    }

    private void FinalizeSuccessfulMove(string source)
    {
        var result = MoveSourceFinalizer.Finalize(source);
        if (result.Complete)
        {
            if (result.SourceDeleted)
            {
                AppendLog(AppText.Get(TextId.LogEmptySourceDeleted));
            }
            else if (!result.SourceExists)
            {
                AppendLog(AppText.Get(TextId.LogSourceRemoved));
            }
            else if (result.RootPreserved)
            {
                AppendLog(AppText.Get(TextId.LogRootPreserved));
            }

            if (clipboardPaste)
            {
                AppendLog(
                    SafeCopyClipboard.TryClearIfSourceMatches(source)
                        ? AppText.Get(TextId.LogClipboardCleared)
                        : AppText.Get(TextId.LogClipboardClearFailed));
            }

            return;
        }

        AppendLog(AppText.Format(TextId.LogResidualNotice, source));
        if (result.RemainingEntries.Count > 0)
        {
            var samples = result.RemainingEntries
                .Take(5)
                .Select(path => Path.GetFileName(Path.TrimEndingDirectorySeparator(path)))
                .Where(name => !string.IsNullOrWhiteSpace(name));
            var suffix = result.RemainingEntries.Count > 5 ? "; ..." : string.Empty;
            AppendLog(AppText.Format(TextId.LogResidualItems, string.Join("; ", samples), suffix));
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            AppendLog(AppText.Format(TextId.LogResidualCheckError, result.Error));
        }

        AppendLog(
            clipboardPaste
                ? AppText.Get(TextId.LogResidualClipboard)
                : AppText.Get(TextId.LogResidualManual));
        SetStatus(AppText.Get(TextId.StatusMovePartial), Warning);
    }

    private bool ConfirmMove()
    {
        var message = AppText.Format(
            TextId.MoveConfirmMessage,
            sourceTextBox.Text,
            destinationTextBox.Text);
        return MessageBox.Show(
            this,
            message,
            AppText.Get(TextId.MoveConfirmTitle),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    private void CancelOperation()
    {
        if (!isRunning)
        {
            return;
        }

        SetStatus(AppText.Get(TextId.StatusCanceling), Warning);
        operationCancellation?.Cancel();
        runner.Cancel();
    }

    private void SetRunningState(bool running)
    {
        isRunning = running;
        sourceTextBox.Enabled = !running;
        destinationTextBox.Enabled = !running;
        sourceBrowseButton.Enabled = !running;
        destinationBrowseButton.Enabled = !running;
        operationComboBox.Enabled = !running;
        languageComboBox.Enabled = !running;
        excludeFilesCheckBox.Enabled = !running;
        excludeDirectoriesCheckBox.Enabled = !running;
        linkHandlingComboBox.Enabled = !running;
        excludedFilesTextBox.Enabled = !running;
        excludedDirectoriesTextBox.Enabled = !running;
        threadsInput.Enabled = !running;
        retriesInput.Enabled = !running;
        waitInput.Enabled = !running;
        contextMenuButton.Enabled = !running;
        previewButton.Enabled = !running;
        startButton.Enabled = !running;
        cancelButton.Enabled = running;

        if (!running)
        {
            UpdateCommandPreview(updateReadyStatus: false);
        }
    }

    private void RefreshContextMenuButton()
    {
        var executablePath = Environment.ProcessPath;
        var installed = executablePath is not null && ExplorerContextMenu.IsInstalledFor(executablePath);
        var textId = installed ? TextId.RemoveContextMenu : TextId.InstallContextMenu;
        contextMenuButton.Tag = textId;
        contextMenuButton.Text = AppText.Get(textId);
    }

    private void ToggleContextMenu()
    {
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException(AppText.Get(TextId.ProcessPathUnavailable));
        var installed = ExplorerContextMenu.IsInstalledFor(executablePath);

        try
        {
            if (installed)
            {
                var answer = MessageBox.Show(
                    this,
                    AppText.Get(TextId.ContextRemoveConfirmMessage),
                    AppText.Get(TextId.ContextRemoveTitle),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes)
                {
                    return;
                }

                ExplorerContextMenu.Uninstall();
                SetStatus(AppText.Get(TextId.StatusContextRemoved), TextMuted);
            }
            else
            {
                ExplorerContextMenu.Install(executablePath);
                SetStatus(AppText.Get(TextId.StatusContextInstalled), Primary);
            }

            RefreshContextMenuButton();
        }
        catch (Exception ex) when (
            ex is IOException or
                UnauthorizedAccessException or
                System.Security.SecurityException or
                InvalidOperationException)
        {
            MessageBox.Show(
                this,
                ex.Message,
                AppText.Get(TextId.ContextConfigFailedTitle),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            SetStatus(AppText.Get(TextId.StatusContextFailed), Color.Firebrick);
        }
    }

    private void StartLog(bool previewOnly)
    {
        Directory.CreateDirectory(LogsDirectory);
        var kind = previewOnly ? "preview" : "copy";
        var path = Path.Combine(LogsDirectory, $"robocopy-{kind}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        lock (logWriterLock)
        {
            logWriter?.Dispose();
            logWriter = new StreamWriter(
                path,
                append: false,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 64 * 1024)
            {
                AutoFlush = false,
            };
        }
    }

    private void StopLog()
    {
        lock (logWriterLock)
        {
            logWriter?.Flush();
            logWriter?.Dispose();
            logWriter = null;
        }
    }

    private void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var pendingCount = Interlocked.Increment(ref pendingLogLineCount);
        pendingLogLines.Enqueue(line);
        while (pendingCount > MaxPendingLogLines && pendingLogLines.TryDequeue(out _))
        {
            pendingCount = Interlocked.Decrement(ref pendingLogLineCount);
            Interlocked.Increment(ref omittedVisibleLogLines);
        }

        lock (logWriterLock)
        {
            logWriter?.WriteLine(line);
        }
    }

    private void UpdateProgress(RobocopyProgress progress) =>
        Volatile.Write(ref latestProgress, progress);

    private void FlushUiUpdates()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        var builder = new StringBuilder();
        var omitted = Interlocked.Exchange(ref omittedVisibleLogLines, 0);
        if (omitted > 0)
        {
            builder.AppendLine(AppText.Format(TextId.LogUiOmitted, omitted));
        }

        var lineCount = 0;
        while (lineCount < MaxLogLinesPerTick &&
               builder.Length < MaxLogCharactersPerTick &&
               pendingLogLines.TryDequeue(out var line))
        {
            Interlocked.Decrement(ref pendingLogLineCount);
            builder.AppendLine(line);
            lineCount++;
        }

        if (builder.Length > 0)
        {
            logTextBox.AppendText(builder.ToString());
            TrimVisibleLogIfNeeded();
            logTextBox.SelectionStart = logTextBox.TextLength;
            logTextBox.ScrollToCaret();
        }

        if (!robocopyActive)
        {
            return;
        }

        var elapsed = operationStopwatch.Elapsed.ToString(@"hh\:mm\:ss");
        var progress = Volatile.Read(ref latestProgress);
        if (progress?.CurrentFilePercent is int percent)
        {
            if (progressBar.Style != ProgressBarStyle.Continuous)
            {
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.MarqueeAnimationSpeed = 0;
            }

            progressBar.Value = Math.Clamp(percent, progressBar.Minimum, progressBar.Maximum);
            var verb = AppText.Get(activePreviewOnly ? TextId.VerbPreviewing : TextId.VerbCopying);
            SetStatus(
                AppText.Format(
                    TextId.StatusCurrentProgress,
                    verb,
                    percent,
                    progress.CompletedItems,
                    elapsed),
                Primary);
        }
        else
        {
            if (progressBar.Style != ProgressBarStyle.Marquee)
            {
                progressBar.Style = ProgressBarStyle.Marquee;
                progressBar.MarqueeAnimationSpeed = 24;
            }

            var outputLines = progress?.OutputLines ?? 0;
            var verb = AppText.Get(activePreviewOnly ? TextId.VerbPreviewing : TextId.VerbCopying);
            SetStatus(
                AppText.Format(TextId.StatusOutputProgress, verb, outputLines, elapsed),
                Primary);
        }
    }

    private void TrimVisibleLogIfNeeded()
    {
        var currentText = logTextBox.Text;
        var trimmedText = LogTextLimiter.TrimToRecentLines(
            currentText,
            MaxVisibleLogCharacters,
            TrimmedVisibleLogCharacters,
            AppText.Get(TextId.LogTrimmedNotice));
        if (!ReferenceEquals(currentText, trimmedText))
        {
            // Replace the complete text in one operation. Selection replacement on a
            // read-only RichEdit control can emit the Windows default notification sound.
            logTextBox.Text = trimmedText;
        }
    }

    private void ClearPendingLogLines()
    {
        while (pendingLogLines.TryDequeue(out _))
        {
        }

        Interlocked.Exchange(ref pendingLogLineCount, 0);
        Interlocked.Exchange(ref omittedVisibleLogLines, 0);
    }

    private void SetStatus(string message, Color color)
    {
        statusLabel.Text = message;
        statusLabel.ForeColor = color;
    }

    private void OpenLogsDirectory()
    {
        try
        {
            Directory.CreateDirectory(LogsDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                ArgumentList = { LogsDirectory },
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (
            ex is IOException or
                UnauthorizedAccessException or
                System.Security.SecurityException or
                System.ComponentModel.Win32Exception)
        {
            SetStatus(ex.Message, Color.Firebrick);
            MessageBox.Show(
                this,
                ex.Message,
                AppText.Get(TextId.CannotExecuteTitle),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void HandleFormClosing(object? sender, FormClosingEventArgs args)
    {
        if (isRunning)
        {
            var result = MessageBox.Show(
                this,
                AppText.Get(TextId.CloseRunningMessage),
                AppText.Get(TextId.CloseConfirmTitle),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                args.Cancel = true;
                return;
            }

            CancelOperation();
        }

        try
        {
            SettingsStore.Save(CollectPreferences());
        }
        catch (IOException)
        {
            // Closing should not be blocked by a settings write failure.
        }
        catch (UnauthorizedAccessException)
        {
            // Closing should not be blocked by a settings write failure.
        }
        catch (System.Security.SecurityException)
        {
            // Closing should not be blocked by a settings write failure.
        }
    }
}
