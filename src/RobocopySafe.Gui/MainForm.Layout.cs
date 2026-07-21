using System.Drawing;
using RobocopySafe.Core;

namespace RobocopySafe.Gui;

internal sealed partial class MainForm
{
    private static readonly Color WindowBackground = Color.FromArgb(245, 247, 248);
    private static readonly Color PanelBackground = Color.White;
    private static readonly Color Primary = Color.FromArgb(0, 104, 116);
    private static readonly Color PrimaryHover = Color.FromArgb(0, 82, 92);
    private static readonly Color TextPrimary = Color.FromArgb(32, 36, 40);
    private static readonly Color TextMuted = Color.FromArgb(88, 96, 104);
    private static readonly Color Warning = Color.FromArgb(181, 101, 29);

    private readonly TextBox sourceTextBox = new();
    private readonly TextBox destinationTextBox = new();
    private readonly Button sourceBrowseButton = new();
    private readonly Button destinationBrowseButton = new();
    private readonly ComboBox operationComboBox = new();
    private readonly ComboBox languageComboBox = new();
    private readonly CheckBox excludeFilesCheckBox = new();
    private readonly CheckBox excludeDirectoriesCheckBox = new();
    private readonly ComboBox linkHandlingComboBox = new();
    private readonly TextBox excludedFilesTextBox = new();
    private readonly TextBox excludedDirectoriesTextBox = new();
    private readonly NumericUpDown threadsInput = new();
    private readonly NumericUpDown retriesInput = new();
    private readonly NumericUpDown waitInput = new();
    private readonly TextBox commandTextBox = new();
    private readonly Button previewButton = new();
    private readonly Button startButton = new();
    private readonly Button cancelButton = new();
    private readonly Button openLogsButton = new();
    private readonly Button contextMenuButton = new();
    private readonly ProgressBar progressBar = new();
    private readonly Label statusLabel = new();
    private readonly RichTextBox logTextBox = new();
    private readonly ToolTip toolTip = new();
    private Icon? applicationIcon;

    private void InitializeInterface()
    {
        SuspendLayout();

        Text = AppText.Get(TextId.WindowTitle);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(860, 680);
        Size = new Size(1020, 800);
        BackColor = WindowBackground;
        ForeColor = TextPrimary;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;
        applicationIcon = LoadApplicationIcon();
        if (applicationIcon is not null)
        {
            Icon = applicationIcon;
        }

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(18, 14, 18, 16),
            BackColor = WindowBackground,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 202));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new Label
        {
            Text = AppText.Get(TextId.WindowTitle),
            Tag = TextId.WindowTitle,
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };
        ConfigureComboBox(languageComboBox);
        languageComboBox.Items.AddRange([
            AppText.Get(TextId.LanguageSystem),
            AppText.Get(TextId.LanguageChinese),
            AppText.Get(TextId.LanguageEnglish),
        ]);
        languageComboBox.Margin = new Padding(0, 7, 0, 7);

        var headerPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
        };
        headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        headerPanel.Controls.Add(header, 0, 0);
        headerPanel.Controls.Add(languageComboBox, 1, 0);
        root.Controls.Add(headerPanel, 0, 0);

        root.Controls.Add(CreatePathPanel(), 0, 1);
        root.Controls.Add(CreateOptionsPanel(), 0, 2);
        root.Controls.Add(CreateCommandPanel(), 0, 3);
        root.Controls.Add(CreateActionPanel(), 0, 4);
        root.Controls.Add(CreateStatusPanel(), 0, 5);
        root.Controls.Add(CreateLogPanel(), 0, 6);

        Controls.Add(root);
        ConfigureToolTips();
        ResumeLayout(performLayout: true);
    }

    private Control CreatePathPanel()
    {
        var panel = CreateSectionPanel();
        panel.ColumnCount = 3;
        panel.RowCount = 3;
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));

        ConfigurePathTextBox(sourceTextBox, TextId.SourcePlaceholder);
        ConfigurePathTextBox(destinationTextBox, TextId.DestinationPlaceholder);
        ConfigurePathButton(sourceBrowseButton);
        ConfigurePathButton(destinationBrowseButton);

        panel.Controls.Add(CreateLabel(TextId.SourceLabel), 0, 0);
        panel.Controls.Add(sourceTextBox, 1, 0);
        panel.Controls.Add(sourceBrowseButton, 2, 0);
        panel.Controls.Add(CreateLabel(TextId.DestinationLabel), 0, 1);
        panel.Controls.Add(destinationTextBox, 1, 1);
        panel.Controls.Add(destinationBrowseButton, 2, 1);

        return panel;
    }

    private Control CreateOptionsPanel()
    {
        var outer = CreateSectionPanel();
        outer.ColumnCount = 2;
        outer.RowCount = 1;
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));

        var safetyPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(4, 4, 12, 4),
        };
        safetyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        safetyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 4; i++)
        {
            safetyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        }

        ConfigureComboBox(operationComboBox);
        operationComboBox.Items.AddRange([AppText.Get(TextId.OperationCopy), AppText.Get(TextId.OperationMove)]);

        excludeFilesCheckBox.Text = AppText.Get(TextId.ExcludeHiddenSystemFiles);
        excludeFilesCheckBox.Tag = TextId.ExcludeHiddenSystemFiles;
        excludeFilesCheckBox.AutoSize = true;
        excludeFilesCheckBox.Anchor = AnchorStyles.Left;

        excludeDirectoriesCheckBox.Text = AppText.Get(TextId.ExcludeHiddenSystemDirectories);
        excludeDirectoriesCheckBox.Tag = TextId.ExcludeHiddenSystemDirectories;
        excludeDirectoriesCheckBox.AutoSize = true;
        excludeDirectoriesCheckBox.Anchor = AnchorStyles.Left;

        ConfigureComboBox(linkHandlingComboBox);
        linkHandlingComboBox.Items.AddRange([
            AppText.Get(TextId.LinkSkip),
            AppText.Get(TextId.LinkCopy),
            AppText.Get(TextId.LinkFollow),
        ]);

        safetyPanel.Controls.Add(CreateLabel(TextId.OperationLabel), 0, 0);
        safetyPanel.Controls.Add(operationComboBox, 1, 0);
        safetyPanel.Controls.Add(excludeFilesCheckBox, 1, 1);
        safetyPanel.SetColumnSpan(excludeFilesCheckBox, 1);
        safetyPanel.Controls.Add(excludeDirectoriesCheckBox, 1, 2);
        safetyPanel.Controls.Add(CreateLabel(TextId.LinkLabel), 0, 3);
        safetyPanel.Controls.Add(linkHandlingComboBox, 1, 3);

        var tuningPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 4,
            Padding = new Padding(12, 4, 4, 4),
        };
        tuningPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        tuningPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        tuningPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        tuningPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var i = 0; i < 4; i++)
        {
            tuningPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        }

        ConfigureNumericInput(threadsInput, 1, 128);
        ConfigureNumericInput(retriesInput, 0, 100);
        ConfigureNumericInput(waitInput, 0, 60);
        ConfigurePatternTextBox(excludedFilesTextBox, TextId.ExcludedFilesPlaceholder);
        ConfigurePatternTextBox(excludedDirectoriesTextBox, TextId.ExcludedDirectoriesPlaceholder);

        tuningPanel.Controls.Add(CreateLabel(TextId.ThreadsLabel), 0, 0);
        tuningPanel.Controls.Add(threadsInput, 1, 0);
        tuningPanel.Controls.Add(CreateLabel(TextId.RetriesLabel), 2, 0);
        tuningPanel.Controls.Add(retriesInput, 3, 0);
        tuningPanel.Controls.Add(CreateLabel(TextId.WaitLabel), 0, 1);
        tuningPanel.Controls.Add(waitInput, 1, 1);
        tuningPanel.Controls.Add(CreateLabel(TextId.FilePatternsLabel), 0, 2);
        tuningPanel.Controls.Add(excludedFilesTextBox, 1, 2);
        tuningPanel.SetColumnSpan(excludedFilesTextBox, 3);
        tuningPanel.Controls.Add(CreateLabel(TextId.DirectoryPatternsLabel), 0, 3);
        tuningPanel.Controls.Add(excludedDirectoriesTextBox, 1, 3);
        tuningPanel.SetColumnSpan(excludedDirectoriesTextBox, 3);

        outer.Controls.Add(safetyPanel, 0, 0);
        outer.Controls.Add(tuningPanel, 1, 0);
        return outer;
    }

    private Control CreateCommandPanel()
    {
        var panel = CreateSectionPanel();
        panel.ColumnCount = 1;
        panel.RowCount = 2;
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var label = CreateLabel(TextId.CommandPreviewLabel);
        label.ForeColor = TextMuted;
        commandTextBox.Dock = DockStyle.Fill;
        commandTextBox.ReadOnly = true;
        commandTextBox.BackColor = Color.FromArgb(249, 250, 251);
        commandTextBox.BorderStyle = BorderStyle.FixedSingle;
        commandTextBox.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);

        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(commandTextBox, 0, 1);
        return panel;
    }

    private Control CreateActionPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 3),
        };

        ConfigurePrimaryButton(startButton, TextId.StartCopy);
        ConfigureSecondaryButton(previewButton, TextId.Preview);
        ConfigureSecondaryButton(cancelButton, TextId.Cancel);
        ConfigureSecondaryButton(openLogsButton, TextId.Logs);
        ConfigureSecondaryButton(contextMenuButton, TextId.InstallContextMenu);
        cancelButton.Enabled = false;

        panel.Controls.Add(startButton);
        panel.Controls.Add(previewButton);
        panel.Controls.Add(cancelButton);
        panel.Controls.Add(openLogsButton);
        panel.Controls.Add(contextMenuButton);
        return panel;
    }

    private Control CreateStatusPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));

        statusLabel.Text = AppText.Get(TextId.Ready);
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusLabel.ForeColor = TextMuted;
        statusLabel.AutoEllipsis = true;

        progressBar.Dock = DockStyle.Fill;
        progressBar.Style = ProgressBarStyle.Continuous;
        progressBar.Minimum = 0;
        progressBar.Maximum = 100;
        progressBar.Margin = new Padding(8, 6, 0, 6);

        panel.Controls.Add(statusLabel, 0, 0);
        panel.Controls.Add(progressBar, 1, 0);
        return panel;
    }

    private Control CreateLogPanel()
    {
        var group = new GroupBox
        {
            Text = AppText.Get(TextId.ExecutionLogLabel),
            Tag = TextId.ExecutionLogLabel,
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 8, 10, 10),
            ForeColor = TextPrimary,
        };

        logTextBox.Dock = DockStyle.Fill;
        logTextBox.ReadOnly = true;
        logTextBox.DetectUrls = false;
        logTextBox.BorderStyle = BorderStyle.None;
        logTextBox.BackColor = Color.FromArgb(30, 34, 38);
        logTextBox.ForeColor = Color.FromArgb(224, 230, 234);
        logTextBox.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
        logTextBox.WordWrap = false;
        group.Controls.Add(logTextBox);
        return group;
    }

    private static TableLayoutPanel CreateSectionPanel() => new()
    {
        Dock = DockStyle.Fill,
        Margin = new Padding(0, 3, 0, 7),
        Padding = new Padding(12, 8, 12, 8),
        BackColor = PanelBackground,
    };

    private static Icon? LoadApplicationIcon()
    {
        var executablePath = Environment.ProcessPath;
        return executablePath is null ? null : Icon.ExtractAssociatedIcon(executablePath);
    }

    private static Label CreateLabel(TextId textId) => new()
    {
        Text = AppText.Get(textId),
        Tag = textId,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = TextPrimary,
        AutoEllipsis = true,
    };

    private static void ConfigurePathButton(Button button)
    {
        ConfigureSecondaryButton(button, TextId.Browse);
        button.AutoSize = false;
        button.MinimumSize = Size.Empty;
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(6, 5, 0, 5);
        button.Padding = Padding.Empty;
    }

    private static void ConfigurePathTextBox(TextBox textBox, TextId placeholderId)
    {
        textBox.Dock = DockStyle.Fill;
        textBox.Margin = new Padding(0, 7, 8, 7);
        textBox.PlaceholderText = AppText.Get(placeholderId);
        textBox.BorderStyle = BorderStyle.FixedSingle;
    }

    private static void ConfigurePatternTextBox(TextBox textBox, TextId placeholderId)
    {
        textBox.Dock = DockStyle.Fill;
        textBox.Margin = new Padding(0, 7, 4, 7);
        textBox.PlaceholderText = AppText.Get(placeholderId);
        textBox.BorderStyle = BorderStyle.FixedSingle;
    }

    private static void ConfigureComboBox(ComboBox comboBox)
    {
        comboBox.Dock = DockStyle.Fill;
        comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBox.Margin = new Padding(0, 6, 4, 6);
    }

    private static void ConfigureNumericInput(NumericUpDown input, int minimum, int maximum)
    {
        input.Dock = DockStyle.Fill;
        input.Minimum = minimum;
        input.Maximum = maximum;
        input.TextAlign = HorizontalAlignment.Right;
        input.Margin = new Padding(0, 6, 8, 6);
    }

    private static void ConfigurePrimaryButton(Button button, TextId textId)
    {
        ConfigureButtonBase(button, textId);
        button.BackColor = Primary;
        button.ForeColor = Color.White;
        button.FlatAppearance.MouseOverBackColor = PrimaryHover;
    }

    private static void ConfigureSecondaryButton(Button button, TextId textId)
    {
        ConfigureButtonBase(button, textId);
        button.BackColor = Color.White;
        button.ForeColor = TextPrimary;
        button.FlatAppearance.BorderColor = Color.FromArgb(188, 195, 201);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(235, 239, 241);
    }

    private static void ConfigureButtonBase(Button button, TextId textId)
    {
        button.Text = AppText.Get(textId);
        button.Tag = textId;
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.MinimumSize = new Size(82, 34);
        button.Padding = new Padding(12, 0, 12, 0);
        button.Margin = new Padding(8, 0, 0, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
    }

    private void ConfigureToolTips()
    {
        toolTip.SetToolTip(sourceBrowseButton, AppText.Get(TextId.ToolTipSourceBrowse));
        toolTip.SetToolTip(destinationBrowseButton, AppText.Get(TextId.ToolTipDestinationBrowse));
        toolTip.SetToolTip(excludeFilesCheckBox, AppText.Get(TextId.ToolTipExcludeFiles));
        toolTip.SetToolTip(excludeDirectoriesCheckBox, AppText.Get(TextId.ToolTipExcludeDirectories));
        toolTip.SetToolTip(linkHandlingComboBox, AppText.Get(TextId.ToolTipLinkHandling));
        toolTip.SetToolTip(excludedFilesTextBox, AppText.Get(TextId.ToolTipExcludedFiles));
        toolTip.SetToolTip(excludedDirectoriesTextBox, AppText.Get(TextId.ToolTipExcludedDirectories));
        toolTip.SetToolTip(previewButton, AppText.Get(TextId.ToolTipPreview));
        toolTip.SetToolTip(openLogsButton, AppText.Get(TextId.ToolTipOpenLogs));
        toolTip.SetToolTip(contextMenuButton, AppText.Get(TextId.ToolTipContextMenu));
        toolTip.SetToolTip(languageComboBox, AppText.Get(TextId.ToolTipLanguage));
    }
}
