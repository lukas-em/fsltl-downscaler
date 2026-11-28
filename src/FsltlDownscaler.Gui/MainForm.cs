using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FsltlDownscaler.Package;
using FsltlDownscaler.Pipeline;
using FsltlDownscaler.Safety;
using Microsoft.Win32;

namespace FsltlDownscaler.Gui;

/// <summary>
/// One window. The package is checked automatically, the user picks a size and presses one button.
/// Heavy work runs on background threads.
/// </summary>
internal sealed class MainForm : Form
{
    private const string AppName = "FSLTL Downscaler";
    private const string GlyphFont = "Segoe MDL2 Assets";
    private const string GlyphOk = "";
    private const string GlyphInfo = "";
    private const string GlyphWarning = "";
    private const string GlyphBusy = "";
    private const string GlyphAirplane = "\uE709";
    private const string GlyphFolder = "\uE8B7";

    private static readonly (int Px, string Label)[] Sizes = [(2048, "High quality"), (1024, "Recommended"), (512, "Max performance")];

    private enum PrimaryMode { ChooseFolder, Checking, Downscale, NothingToDo, Restore, Repair, Cancel, Busy }

    private Theme _t = Theme.Current;

    // Header
    private readonly PictureBox _logo = new() { SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(44, 44), Margin = new Padding(0, 0, 12, 0) };
    private readonly Label _title = new() { AutoSize = true, Text = AppName, Margin = new Padding(0, 2, 0, 0) };
    private readonly Label _subtitle = new() { AutoSize = true, Text = "Smaller textures, more FPS, same traffic", Margin = new Padding(0) };
    private readonly Label _version = new() { AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
    private readonly LinkLabel _profileLink = new() { AutoSize = true, Text = "by FranklinFjM", Margin = new Padding(2, 4, 0, 0) };

    // Banner
    private readonly CardPanel _banner = new() { Visible = false };
    private readonly Label _bannerGlyph = new() { AutoSize = true, Margin = new Padding(0, 1, 10, 0) };
    private readonly Label _bannerText = new() { AutoSize = true, MaximumSize = new Size(470, 0), Margin = new Padding(0, 2, 0, 0) };
    private readonly LinkLabel _bannerAction = new() { AutoSize = true, Visible = false, Anchor = AnchorStyles.Right, Margin = new Padding(8, 2, 0, 0) };
    private readonly LinkLabel _bannerClose = new() { AutoSize = true, Text = "Dismiss", Anchor = AnchorStyles.Right, Margin = new Padding(8, 2, 0, 0) };

    // Package
    private readonly CardPanel _pkgCard = new();
    private readonly Label _pkgGlyph = new() { AutoSize = true, Margin = new Padding(0, 2, 12, 0) };
    private readonly Label _pkgTitle = new() { AutoSize = true, Margin = new Padding(0) };
    private readonly Label _pkgPath = new() { AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, Height = 20, Margin = new Padding(0, 2, 0, 0) };
    private readonly RoundButton _changeButton = new() { Text = "Change", AutoSize = true, MinimumSize = new Size(90, 34), Anchor = AnchorStyles.Right };

    // Sizes
    private readonly Label _sizeCaption = new() { AutoSize = true, Text = "Texture size", Margin = new Padding(2, 14, 0, 6) };
    private readonly SizeTile[] _tiles = Sizes.Select(s => new SizeTile { Value = s.Px, Title = $"{s.Px} px", Subtitle = s.Label }).ToArray();

    // Size / progress card
    private readonly CardPanel _sizeCard = new();
    private readonly Label _sizeLeft = new() { AutoSize = true, Text = "Package size", Margin = new Padding(0, 0, 0, 8) };
    private readonly Label _sizeRight = new() { AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 0, 0, 8) };
    private readonly SizeBar _bar = new() { Dock = DockStyle.Fill, Margin = new Padding(0) };
    private readonly Label _sizeDetail = new() { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };

    // Actions
    // Same grid as the size tiles: three equal columns, so the buttons line up with the tiles above.
    private readonly RoundButton _primary = new() { AutoSize = false, Height = 42, MinimumSize = new Size(0, 42), Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0), Primary = true };
    private readonly RoundButton _restore = new() { Text = "Restore original", AutoSize = false, Height = 42, MinimumSize = new Size(0, 42), Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
    private readonly RoundButton _deleteBackup = new() { Text = "Delete backup", AutoSize = false, Height = 42, MinimumSize = new Size(0, 42), Dock = DockStyle.Fill, Margin = new Padding(0) };

    // Advanced and Details open right below the buttons. About sits in the header.
    private readonly LinkLabel _advancedLink = new() { AutoSize = true, Margin = new Padding(2, 12, 0, 0) };
    private readonly LinkLabel _detailsLink = new() { AutoSize = true, Margin = new Padding(20, 12, 0, 0) };
    private readonly LinkLabel _aboutLink = new() { AutoSize = true, Text = "About", Margin = new Padding(0, 2, 0, 0) };

    // Advanced
    private readonly CardPanel _advCard = new() { Visible = false };
    private readonly CheckOption _mipsBox = new() { Checked = true, Text = "Create mip maps for textures that have none (recommended)", Margin = new Padding(0, 0, 0, 10) };
    private readonly Label _backupModeCaption = new() { AutoSize = true, Text = "Backup of the original textures", Margin = new Padding(0, 0, 0, 4) };
    private readonly CheckOption _modeFolder = new() { Radio = true, Text = "Folder: fast, takes as much space as the original textures", Margin = new Padding(0, 0, 0, 2) };
    private readonly CheckOption _modeZip = new() { Radio = true, Text = "ZIP: about 80 % smaller, takes a few minutes longer", Margin = new Padding(0, 0, 0, 2) };
    private readonly CheckOption _modeNone = new() { Radio = true, Text = "No backup: not recommended, at your own risk. \"Restore original\" isn't possible", Margin = new Padding(0, 0, 0, 10) };
    private readonly LinkLabel _compressLink = new() { AutoSize = true, Text = "Compress to ZIP", Visible = false, Margin = new Padding(0, 8, 0, 0) };
    private readonly Label _backupLabel = new() { AutoSize = true, MaximumSize = new Size(600, 0), Margin = new Padding(0, 2, 8, 0) };
    private readonly LinkLabel _openBackupLink = new() { AutoSize = true, Text = "Open", Margin = new Padding(0, 2, 0, 0) };
    private readonly Label _backupSizeLabel = new() { AutoSize = true, Margin = new Padding(0, 8, 8, 0) };
    private readonly Label _diagLabel = new() { AutoSize = true, Text = "Problems? Copy the details for a bug report:", Margin = new Padding(0, 8, 8, 0) };
    private readonly LinkLabel _diagLink = new() { AutoSize = true, Text = "Copy diagnostics", Margin = new Padding(0, 8, 0, 0) };

    // Details
    private readonly CardPanel _logCard = new() { Visible = false };
    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.None,
        Dock = DockStyle.Fill,
        Height = 170,
        Font = new Font("Consolas", 9f),
        Margin = new Padding(0),
    };

    private readonly Label _footer = new()
    {
        AutoSize = true,
        MaximumSize = new Size(720, 0),
        Margin = new Padding(2, 16, 0, 0),
        Text = "Originals are kept in a backup (folder or ZIP) and can be restored until you delete it. Only your local installation is changed. " +
               "No FSLTL content is distributed with this tool. Use at your own risk. Written with the help of AI.",
    };

    // Easter egg: a small cat at the end of the footer.
    private readonly CatMark _cat = new() { Size = new Size(14, 14), Anchor = AnchorStyles.Right | AnchorStyles.Bottom, Margin = new Padding(8, 0, 0, 1) };
    private readonly ToolTip _catTip = new() { InitialDelay = 400 };

    private IReadOnlyList<PackageCandidate> _candidates = [];
    private PackagePicker? _picker;
    private long _pickerClosedByButtonTick;
    private PackageContext? _pkg;
    private string? _simulator;
    private ScanResult? _scan;
    private PackageStatus? _status;
    private Plan? _plan;
    private int _target = 1024;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _runCts;
    private bool _scanning;
    private bool _busy;
    private string? _scanError;
    private (int Done, int Total)? _runProgress;
    private string _progressTitle = "Downscaling";
    private string? _progressUnit = "textures";
    private BackupMode _backupMode = AppSettings.Load().BackupMode;
    private Action? _bannerActionHandler;
    private string? _outdatedNoticeFor;
    private readonly Stopwatch _runWatch = new();

    public MainForm()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = AppName;
        Font = new Font("Segoe UI", 9.75f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        StartPosition = FormStartPosition.CenterScreen;

        var icon = LoadIcon();
        if (icon is not null)
        {
            Icon = icon;
            _logo.Image = new Icon(icon, 48, 48).ToBitmap();
        }

        _version.Text = $"v{AppVersion} ·";
        BuildLayout();
        WireEvents();
        ApplyTheme();
        ResumeLayout(false);
        PerformLayout();
        UpdateUi();
    }

    private static string AppVersion =>
        typeof(MainForm).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?";

    private string BackupBase => _pkg is null ? string.Empty : BackupSession.DefaultBaseDir(_pkg.Root);

    private static Icon? LoadIcon()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("app.ico");
        return stream is null ? null : new Icon(stream);
    }

    // ================================================================ layout

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(22, 18, 22, 18),
            Location = new Point(0, 0),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 720));

        // Header
        var header = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Margin = new Padding(0, 0, 0, 14) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(_logo, 0, 0);
        header.SetRowSpan(_logo, 2);
        header.Controls.Add(_title, 1, 0);
        header.Controls.Add(_subtitle, 1, 1);
        var headerTop = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Anchor = AnchorStyles.Right | AnchorStyles.Top };
        headerTop.Controls.Add(_version);
        headerTop.Controls.Add(_profileLink);
        var headerLinks = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Anchor = AnchorStyles.Right | AnchorStyles.Top };
        headerLinks.Controls.Add(_aboutLink);
        header.Controls.Add(headerTop, 2, 0);
        header.Controls.Add(headerLinks, 2, 1);
        root.Controls.Add(header);

        // Banner
        _banner.ColumnCount = 4;
        _banner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _banner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _banner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _banner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _banner.Controls.Add(_bannerGlyph, 0, 0);
        _banner.Controls.Add(_bannerText, 1, 0);
        _banner.Controls.Add(_bannerAction, 2, 0);
        _banner.Controls.Add(_bannerClose, 3, 0);
        _banner.Margin = new Padding(0, 0, 0, 12);
        root.Controls.Add(_banner);

        // Package card
        _pkgCard.ColumnCount = 3;
        _pkgCard.RowCount = 2;
        _pkgCard.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _pkgCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _pkgCard.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _pkgCard.Controls.Add(_pkgGlyph, 0, 0);
        _pkgCard.SetRowSpan(_pkgGlyph, 2);
        _pkgCard.Controls.Add(_pkgTitle, 1, 0);
        _pkgCard.Controls.Add(_pkgPath, 1, 1);
        _pkgCard.Controls.Add(_changeButton, 2, 0);
        _pkgCard.SetRowSpan(_changeButton, 2);
        _pkgCard.Margin = new Padding(0);
        root.Controls.Add(_pkgCard);

        // Size tiles
        root.Controls.Add(_sizeCaption);
        var tiles = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0, 0, 0, 12) };
        for (int i = 0; i < _tiles.Length; i++)
        {
            tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            _tiles[i].Dock = DockStyle.Fill;
            _tiles[i].Margin = new Padding(0, 0, i < _tiles.Length - 1 ? 10 : 0, 0);
            tiles.Controls.Add(_tiles[i], i, 0);
        }

        root.Controls.Add(tiles);

        // Size / progress card
        _sizeCard.ColumnCount = 2;
        _sizeCard.RowCount = 3;
        _sizeCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _sizeCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _sizeCard.Controls.Add(_sizeLeft, 0, 0);
        _sizeCard.Controls.Add(_sizeRight, 1, 0);
        _sizeCard.Controls.Add(_bar, 0, 1);
        _sizeCard.SetColumnSpan(_bar, 2);
        _sizeCard.Controls.Add(_sizeDetail, 0, 2);
        _sizeCard.SetColumnSpan(_sizeDetail, 2);
        _sizeCard.Margin = new Padding(0, 0, 0, 14);
        root.Controls.Add(_sizeCard);

        // Actions: three equal columns like the size tiles. A hidden button keeps its column free,
        // so the others don't jump around.
        var actions = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0) };
        RoundButton[] buttons = [_primary, _restore, _deleteBackup];
        for (int i = 0; i < buttons.Length; i++)
        {
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            actions.Controls.Add(buttons[i], i, 0);
        }

        root.Controls.Add(actions);

        // Expanders for Advanced and Details, directly under the action they belong to.
        var expanders = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        expanders.Controls.Add(_advancedLink);
        expanders.Controls.Add(_detailsLink);
        root.Controls.Add(expanders);

        // Advanced
        _advCard.ColumnCount = 1;
        _advCard.Controls.Add(_mipsBox, 0, 0);
        var modeGroup = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Margin = new Padding(0), BackColor = Color.Transparent, Dock = DockStyle.Fill,
        };
        modeGroup.Controls.Add(_backupModeCaption);
        modeGroup.Controls.Add(_modeFolder);
        modeGroup.Controls.Add(_modeZip);
        modeGroup.Controls.Add(_modeNone);
        _advCard.Controls.Add(modeGroup, 0, 1);
        var backupRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = Color.Transparent, Dock = DockStyle.Fill };
        backupRow.Controls.Add(_backupLabel);
        backupRow.Controls.Add(_openBackupLink);
        _advCard.Controls.Add(backupRow, 0, 2);
        var backupSizeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = Color.Transparent, Dock = DockStyle.Fill };
        backupSizeRow.Controls.Add(_backupSizeLabel);
        backupSizeRow.Controls.Add(_compressLink);
        _advCard.Controls.Add(backupSizeRow, 0, 3);
        var diagRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = Color.Transparent, Dock = DockStyle.Fill };
        diagRow.Controls.Add(_diagLabel);
        diagRow.Controls.Add(_diagLink);
        _advCard.Controls.Add(diagRow, 0, 4);
        _advCard.Margin = new Padding(0, 14, 0, 0);
        root.Controls.Add(_advCard);

        // Details
        _logCard.ColumnCount = 1;
        _logCard.Controls.Add(_log, 0, 0);
        _logCard.Margin = new Padding(0, 14, 0, 0);
        root.Controls.Add(_logCard);

        // Footer text with the cat in the bottom right corner.
        var footerRow = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        footerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _footer.MaximumSize = new Size(690, 0);
        footerRow.Controls.Add(_footer, 0, 0);
        footerRow.Controls.Add(_cat, 1, 0);
        root.Controls.Add(footerRow);
        _catTip.SetToolTip(_cat, "Meow. Smaller textures, happier cats.");
        Controls.Add(root);

        _title.Font = new Font("Segoe UI Semibold", 15f);
        _primary.Font = new Font("Segoe UI Semibold", 10f);
        _pkgTitle.Font = new Font("Segoe UI Semibold", 10f);
        _sizeRight.Font = new Font("Segoe UI Semibold", 10f);
        foreach (var l in new[] { _pkgGlyph, _bannerGlyph })
        {
            l.Font = new Font(GlyphFont, 15f);
        }

        foreach (var small in new Control[] { _subtitle, _version, _profileLink, _aboutLink, _pkgPath, _sizeDetail, _footer, _backupLabel, _backupSizeLabel, _diagLabel, _compressLink })
        {
            small.Font = new Font("Segoe UI", 9f);
        }
    }

    private void WireEvents()
    {
        Shown += (_, _) =>
        {
            if (!ShowDisclaimerIfNeeded())
            {
                Close();
                return;
            }

            AutoDetect();
        };
        _profileLink.LinkClicked += (_, _) => DisclaimerForm.OpenUrl(DisclaimerForm.ProfileUrl);
        _aboutLink.LinkClicked += (_, _) =>
        {
            using var about = new DisclaimerForm(_t, firstStart: false, Icon);
            about.ShowDialog(this);
        };
        HandleCreated += (_, _) => ApplyNativeTheme();
        _log.HandleCreated += (_, _) => NativeMethods.SetControlTheme(_log, _t.Dark);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        FormClosed += (_, _) => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

        _changeButton.Click += (_, _) => ChangePackage();
        foreach (var tile in _tiles)
        {
            tile.Click += (_, _) => SelectTarget(tile.Value);
        }

        _mipsBox.CheckedChanged += (_, _) => { RecomputePlans(); UpdateUi(); };
        _modeFolder.Click += (_, _) => SetBackupMode(BackupMode.Folder);
        _modeZip.Click += (_, _) => SetBackupMode(BackupMode.Zip);
        _modeNone.Click += (_, _) => SetBackupMode(BackupMode.None);
        _compressLink.LinkClicked += async (_, _) => await CompressBackupsAsync();
        _primary.Click += async (_, _) => await PrimaryClickAsync();
        _restore.Click += async (_, _) => await RestoreAsync();
        _advancedLink.LinkClicked += (_, _) => _advCard.Visible = !_advCard.Visible;
        _detailsLink.LinkClicked += (_, _) => _logCard.Visible = !_logCard.Visible;
        _advCard.VisibleChanged += (_, _) => UpdateExpanders();
        _logCard.VisibleChanged += (_, _) => UpdateExpanders();
        UpdateExpanders();
        _openBackupLink.LinkClicked += (_, _) => OpenBackupFolder();
        _deleteBackup.Click += async (_, _) => await DeleteBackupsAsync(outdatedOnly: false);
        _diagLink.LinkClicked += (_, _) => CopyDiagnostics();
        _bannerAction.LinkClicked += (_, _) => _bannerActionHandler?.Invoke();
        _bannerClose.LinkClicked += (_, _) => _banner.Visible = false;

        FormClosing += (_, e) =>
        {
            if (_busy)
            {
                e.Cancel = true;
                MessageBox.Show(this, "Please wait until the current step has finished, or press Cancel first.",
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
    }

    // ================================================================ theme

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle))
        {
            return;
        }

        var next = Theme.Current;
        if (next.Dark == _t.Dark || !IsHandleCreated)
        {
            return;
        }

        BeginInvoke(new Action(() =>
        {
            _t = next;
            ApplyTheme();
            ApplyNativeTheme();
            UpdateUi();
            Invalidate(true);
        }));
    }

    /// <summary>Shows the risk notice unless the user chose not to see it again. Returns false if the user chose Exit.</summary>
    private bool ShowDisclaimerIfNeeded()
    {
        var settings = AppSettings.Load();
        if (settings.DisclaimerAccepted)
        {
            return true;
        }

        using var dlg = new DisclaimerForm(_t, firstStart: true, Icon);
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return false;
        }

        if (dlg.DontShowAgain)
        {
            settings.DisclaimerAcceptedVersion = AppSettings.CurrentDisclaimerVersion;
            settings.Save();
        }

        return true;
    }

    private void ApplyNativeTheme()
    {
        NativeMethods.SetDarkTitleBar(Handle, _t.Dark);
        NativeMethods.SetControlTheme(_log, _t.Dark);
    }

    private void ApplyTheme()
    {
        BackColor = _t.Window;
        ForeColor = _t.Text;

        foreach (var card in new[] { _pkgCard, _sizeCard, _advCard, _logCard })
        {
            card.CanvasColor = _t.Window;
            card.FillColor = card == _sizeCard ? _t.CardTint : _t.Card;
            card.BorderColor = card == _sizeCard ? _t.CardTint : _t.Border;
            card.Invalidate();
        }

        _banner.CanvasColor = _t.Window;

        _title.ForeColor = _t.Text;
        _subtitle.ForeColor = _t.TextSecondary;
        _version.ForeColor = _t.TextMuted;
        _sizeCaption.ForeColor = _t.TextSecondary;
        _pkgTitle.ForeColor = _t.Text;
        _pkgPath.ForeColor = _t.TextSecondary;
        _sizeLeft.ForeColor = _t.TextSecondary;
        _sizeRight.ForeColor = _t.Text;
        _sizeDetail.ForeColor = _t.TextMuted;
        _footer.ForeColor = _t.TextMuted;
        _cat.CanvasColor = _t.Window;
        _cat.CatColor = _t.TextMuted;
        _cat.HoverColor = _t.Accent;
        _cat.Invalidate();
        _backupLabel.ForeColor = _t.TextSecondary;
        _backupSizeLabel.ForeColor = _t.TextSecondary;
        _backupModeCaption.ForeColor = _t.TextSecondary;
        _diagLabel.ForeColor = _t.TextSecondary;
        _bannerText.ForeColor = _t.Text;

        foreach (var l in new Control[] { _pkgGlyph, _pkgTitle, _pkgPath, _sizeLeft, _sizeRight, _sizeDetail, _backupLabel, _backupSizeLabel, _backupModeCaption, _diagLabel, _bannerGlyph, _bannerText })
        {
            l.BackColor = Color.Transparent;
        }

        foreach (var link in new[] { _advancedLink, _detailsLink, _aboutLink, _profileLink, _openBackupLink, _diagLink, _compressLink, _bannerAction, _bannerClose })
        {
            link.LinkColor = _t.Accent;
            link.ActiveLinkColor = _t.AccentHover;
            link.VisitedLinkColor = _t.Accent;
            link.LinkBehavior = LinkBehavior.HoverUnderline;
            link.BackColor = Color.Transparent;
        }

        foreach (var option in new[] { _mipsBox, _modeFolder, _modeZip, _modeNone })
        {
            option.Theme = _t;
            option.CanvasColor = _t.Card;
            option.Invalidate();
        }

        foreach (var tile in _tiles)
        {
            tile.Theme = _t;
            tile.Invalidate();
        }

        _bar.CanvasColor = _t.CardTint;
        _bar.TrackColor = _t.Track;
        _bar.FillColor = _t.Accent;
        _bar.Invalidate();

        _log.BackColor = _t.Card;
        _log.ForeColor = _t.TextSecondary;

        foreach (var b in new[] { _changeButton, _primary, _restore, _deleteBackup })
        {
            b.Theme = _t;
            b.CanvasColor = b == _changeButton ? _t.Card : _t.Window;
            b.Invalidate();
        }
    }

    // ================================================================ package and scan

    private void AutoDetect()
    {
        _candidates = PackageLocator.FindCandidates();
        foreach (var c in _candidates)
        {
            string link = PathResolver.SameFolder(c.Path, c.RealPath) ? string.Empty : $" (linked to {c.RealPath})";
            Log($"Found FSLTL for {c.Simulator}: {c.Path}{link}");
        }

        if (_candidates.Count > 0)
        {
            OpenPackage(_candidates[0].Path);
        }
        else
        {
            Log("FSLTL was not found automatically.");
            UpdateUi();
        }
    }

    /// <summary>With detected packages (e.g. MSFS 2020 and 2024) a small picker, otherwise the folder dialog.</summary>
    private void ChangePackage()
    {
        // A click on Change while the picker is open closes it. The picker closes itself on mouse down,
        // so the click that follows must not open it again.
        if (_picker is not null)
        {
            _picker.Close();
            return;
        }

        if (Environment.TickCount64 - _pickerClosedByButtonTick < 300)
        {
            return;
        }

        if (_candidates.Count == 0)
        {
            Browse();
            return;
        }

        var entries = new List<PickerEntry>();
        foreach (var c in _candidates)
        {
            bool current = _pkg is not null && string.Equals(_pkg.Root, c.RealPath, StringComparison.OrdinalIgnoreCase);
            string path = c.Path;
            entries.Add(new PickerEntry(c.Simulator, c.RealPath, GlyphAirplane, current, () => BeginInvoke(new Action(() => OpenPackage(path)))));
        }

        entries.Add(new PickerEntry("Browse for another folder…", null, GlyphFolder, false, () => BeginInvoke(new Action(Browse)), SeparatorBefore: true));

        int width = Math.Max(_changeButton.LogicalToDeviceUnits(420), _pkgCard.Width - _changeButton.LogicalToDeviceUnits(120));
        var picker = new PackagePicker(_t, entries, Font, _changeButton.DeviceDpi, width);
        picker.Closed += (_, e) =>
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.AppClicked
                && _changeButton.RectangleToScreen(_changeButton.ClientRectangle).Contains(Cursor.Position))
            {
                _pickerClosedByButtonTick = Environment.TickCount64;
            }

            _picker = null;
            BeginInvoke(new Action(picker.Dispose));
        };
        _picker = picker;
        picker.Show(_changeButton, new Point(_changeButton.Width - width, _changeButton.Height + _changeButton.LogicalToDeviceUnits(6)));
    }

    private void Browse()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select the fsltl-traffic-base folder inside your Community folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        if (_pkg is not null)
        {
            dlg.InitialDirectory = _pkg.Root;
        }

        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            OpenPackage(dlg.SelectedPath);
        }
    }

    private void OpenPackage(string path)
    {
        _scanCts?.Cancel();
        _pkg = null;
        _scan = null;
        _status = null;
        _plan = null;
        _scanError = null;
        _simulator = null;

        try
        {
            _pkg = PackageContext.Open(path);
            _simulator = PackageLocator.SimulatorFor(_pkg.Root, _candidates);
            Log($"Package: {_pkg.Title} {_pkg.Version} in {_pkg.Root}");
            if (_pkg.IsLinked)
            {
                Log($"Selected folder is a link: {_pkg.SelectedPath} → {_pkg.Root}. The tool works on the real folder.");
            }

            _ = ScanAsync();
        }
        catch (Exception ex) when (ex is UserException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _scanError = ex.Message;
            Log($"Not usable: {path} ({ex.Message})");
        }

        UpdateUi();
    }

    private async Task ScanAsync()
    {
        if (_pkg is null)
        {
            return;
        }

        var pkg = _pkg;
        var cts = new CancellationTokenSource();
        _scanCts = cts;
        _scanning = true;
        UpdateUi();

        try
        {
            var (scan, status) = await Task.Run(() =>
            {
                var layout = LayoutFile.Load(pkg.LayoutPath);
                var s = Planner.Scan(pkg, layout, null, cts.Token);
                return (s, PackageInspector.Inspect(pkg, s));
            }, cts.Token);

            if (_scanCts != cts)
            {
                return;
            }

            _scan = scan;
            _status = status;
            Log($"Checked {scan.Items.Count:N0} textures. State: {Describe(status)}.");
            if (status.MovedFrom is { } movedFrom)
            {
                Log($"The backup was made when the package was in {movedFrom}. It still belongs to this package.");
            }

            if (status.Backups.Count > 0)
            {
                Log($"Backups of this package: {status.Backups.Count}, {status.BackupFiles:N0} original textures, {SizeText(status.BackupBytes)}.");
            }

            RecomputePlans();
            NotifyOutdatedBackups(pkg, status);
            _ = RemoveLeftoversAsync(status);
        }
        catch (OperationCanceledException)
        {
            // A newer scan replaced this one.
        }
        catch (Exception ex)
        {
            if (_scanCts == cts)
            {
                _scanError = $"Could not read the package: {ex.Message}";
                Log($"ERROR: {ex.Message}");
            }
        }
        finally
        {
            if (_scanCts == cts)
            {
                _scanning = false;
                UpdateUi();
            }
        }
    }

    private void SelectTarget(int px)
    {
        if (_busy || px == _target)
        {
            return;
        }

        _target = px;
        RecomputePlans();
        UpdateUi();
    }

    private void RecomputePlans()
    {
        if (_scan is null || _status is null)
        {
            _plan = null;
            foreach (var (tile, s) in _tiles.Zip(Sizes))
            {
                tile.Subtitle = s.Label;
            }

            return;
        }

        foreach (var (tile, s) in _tiles.Zip(Sizes))
        {
            var p = Planner.PlanFor(_scan, s.Px, _mipsBox.Checked);
            tile.Subtitle = p.ToProcess.Any()
                ? $"{s.Label} · ~{Gb1(_status.PackageSize - p.Saving)}"
                : $"{s.Label} · no change";
            if (s.Px == _target)
            {
                _plan = p;
            }
        }
    }

    private static string Describe(PackageStatus s) => s.Kind switch
    {
        PackageKind.Original => "original textures",
        PackageKind.DownscaledByTool => $"downscaled to {s.Target} px",
        PackageKind.ReducedOther => "textures already reduced",
        PackageKind.Interrupted => "last run was interrupted",
        _ => "?",
    };

    // ================================================================ UI state

    private PrimaryMode CurrentMode()
    {
        if (_runCts is not null)
        {
            return PrimaryMode.Cancel;
        }

        if (_busy)
        {
            return PrimaryMode.Busy;
        }

        if (_pkg is null)
        {
            return PrimaryMode.ChooseFolder;
        }

        if (_scanning || _status is null || _plan is null)
        {
            return _scanError is null ? PrimaryMode.Checking : PrimaryMode.ChooseFolder;
        }

        if (_status.Kind == PackageKind.Interrupted)
        {
            return _status.Repairable is not null ? PrimaryMode.Repair : PrimaryMode.Restore;
        }

        return _plan.ToProcess.Any() ? PrimaryMode.Downscale : PrimaryMode.NothingToDo;
    }

    private void UpdateUi()
    {
        var mode = CurrentMode();

        // Package card
        if (_pkg is null)
        {
            SetGlyph(_pkgGlyph, GlyphInfo, _scanError is null ? _t.Accent : _t.Warning);
            _pkgTitle.Text = _scanError is null ? "Select your FSLTL folder" : "This folder can't be used";
            _pkgPath.Text = _scanError ?? "Choose the fsltl-traffic-base folder inside your Community folder.";
            _changeButton.Text = "Browse";
        }
        else
        {
            _changeButton.Text = "Change";
            _pkgPath.Text = _scanError ?? _pkg.Root;
            string name = _simulator is null ? $"{_pkg.Title} {_pkg.Version}" : $"{_pkg.Title} {_pkg.Version} · {_simulator}";
            if (_scanning || _status is null)
            {
                SetGlyph(_pkgGlyph, GlyphBusy, _t.TextMuted);
                _pkgTitle.Text = _scanError is null ? $"{name} · checking textures…" : name;
            }
            else
            {
                string size = Gb1(_status.PackageSize);
                switch (_status.Kind)
                {
                    case PackageKind.Original:
                        SetGlyph(_pkgGlyph, GlyphOk, _t.Success);
                        _pkgTitle.Text = $"{name} · {size} · original textures";
                        break;
                    case PackageKind.DownscaledByTool:
                        SetGlyph(_pkgGlyph, GlyphOk, _t.Accent);
                        string when = _status.WhenUtc is { } w ? $" on {w.ToLocalTime():yyyy-MM-dd}" : string.Empty;
                        _pkgTitle.Text = $"{name} · {size} · downscaled to {_status.Target} px{when}" +
                                         (_status.LastRunWithoutBackup ? " · no backup" : string.Empty);
                        break;
                    case PackageKind.ReducedOther:
                        SetGlyph(_pkgGlyph, GlyphInfo, _t.Warning);
                        _pkgTitle.Text = $"{name} · {size} · textures already reduced";
                        break;
                    case PackageKind.Interrupted:
                        SetGlyph(_pkgGlyph, GlyphWarning, _t.Warning);
                        _pkgTitle.Text = _status.Repairable is not null
                            ? $"{name} · last run was interrupted, please repair"
                            : $"{name} · last run was interrupted, please restore";
                        break;
                }
            }
        }

        // Tiles
        foreach (var tile in _tiles)
        {
            tile.Selected = tile.Value == _target;
            tile.Enabled = !_busy && _pkg is not null;
        }

        // Size / progress card
        if (_runProgress is { } rp)
        {
            double f = rp.Total == 0 ? 0 : (double)rp.Done / rp.Total;
            _sizeLeft.Text = _progressTitle;
            _sizeRight.Text = $"{f * 100:0} %";
            _bar.Value = f;
            _sizeDetail.Text = _progressUnit is null
                ? "Packing the originals, then checking every file against its original"
                : $"{rp.Done:N0} of {rp.Total:N0} {_progressUnit}";
        }
        else if (_plan is not null && _status is not null)
        {
            long total = _status.PackageSize;
            long after = total - _plan.Saving;
            _sizeLeft.Text = "Package size";
            if (_plan.ToProcess.Any())
            {
                double pct = total > 0 ? 100.0 * _plan.Saving / total : 0;
                _sizeRight.Text = $"{Gb1(total)}  →  {Gb1(after)}   (−{pct:0} %)";
                int skipped = _plan.Count(PlanAction.Skip);
                _sizeDetail.Text = $"{_plan.ToProcess.Count():N0} textures will be reduced" +
                                   (skipped > 0 ? $" · {skipped:N0} unsupported textures stay as they are" : string.Empty);
            }
            else
            {
                _sizeRight.Text = Gb1(total);
                _sizeDetail.Text = $"All textures are already {_target} px or smaller.";
            }

            _bar.Value = total > 0 ? (double)after / total : 0;
        }
        else
        {
            _sizeLeft.Text = "Package size";
            _sizeRight.Text = "–";
            _bar.Value = 0;
            _sizeDetail.Text = _scanning ? "Checking textures…" : "Select your FSLTL folder to start.";
        }

        // Primary button
        bool cancelRequested = _runCts?.IsCancellationRequested == true;
        (string text, bool enabled, bool primaryStyle) = mode switch
        {
            PrimaryMode.ChooseFolder => ("Choose FSLTL folder", true, true),
            PrimaryMode.Checking => ("Checking textures…", false, true),
            PrimaryMode.Downscale => ($"Downscale to {_target} px", true, true),
            PrimaryMode.NothingToDo => ($"Already {_target} px or smaller", false, true),
            PrimaryMode.Restore => ("Restore original", true, true),
            PrimaryMode.Repair => ("Repair package", true, true),
            PrimaryMode.Cancel => (cancelRequested ? "Cancelling…" : "Cancel", !cancelRequested, false),
            _ => ("Please wait…", false, true),
        };
        _primary.Text = text;
        _primary.Enabled = enabled;
        _primary.Primary = primaryStyle;
        _primary.Invalidate();

        // Restore button: only when there is something of ours to restore.
        bool canRestore = _status?.Restorable is not null
                          && _status.Kind is PackageKind.DownscaledByTool
                          && !_busy && !_scanning;
        _restore.Visible = canRestore;

        _changeButton.Enabled = !_busy;
        _mipsBox.Enabled = !_busy;
        _modeFolder.Checked = _backupMode == BackupMode.Folder;
        _modeZip.Checked = _backupMode == BackupMode.Zip;
        _modeNone.Checked = _backupMode == BackupMode.None;
        foreach (var option in new[] { _modeFolder, _modeZip, _modeNone })
        {
            option.Enabled = !_busy;
        }

        _backupLabel.Text = _pkg is null ? "Backup folder: –" : $"Backup folder: {BackupBase}";
        _openBackupLink.Visible = _pkg is not null && (_status?.Restorable is not null || Directory.Exists(BackupBase));
        int backups = _status?.Backups.Count ?? 0;
        string zipNote = _status is not null && backups > 0 && _status.Backups.All(b => b.IsZip) ? " (ZIP)" : string.Empty;
        _backupSizeLabel.Text = _status is null || _scanning
            ? "Backup: –"
            : backups == 0
                ? _status.LastRunWithoutBackup
                    ? "Backup: none. The last run was made without a backup."
                    : _backupMode == BackupMode.None
                        ? "Backup: none. No backup is made with this setting."
                        : "Backup: none. It is created when you downscale."
                : backups == 1
                    ? $"Backup: {_status.BackupFiles:N0} original textures · {SizeText(_status.BackupBytes)}{zipNote}"
                    : $"Backups: {backups} runs, {_status.BackupFiles:N0} original textures · {SizeText(_status.BackupBytes)}{zipNote}";
        _compressLink.Visible = _status is not null && !_busy && !_scanning
                                && _status.Kind != PackageKind.Interrupted && _status.Compressible.Any();
        // Key feature for disk space, so it sits next to Restore. Not offered after an interrupted run:
        // that backup is needed to repair the package.
        _deleteBackup.Text = backups > 1 ? "Delete backups" : "Delete backup";
        _deleteBackup.Visible = backups > 0 && !_busy && !_scanning && _status?.Kind != PackageKind.Interrupted;
        _diagLink.Enabled = !_busy;
        _bannerAction.Enabled = !_busy && !_scanning;
    }

    /// <summary>The arrow shows whether the section is open.</summary>
    private void UpdateExpanders()
    {
        _advancedLink.Text = _advCard.Visible ? "Advanced options ▴" : "Advanced options ▾";
        _detailsLink.Text = _logCard.Visible ? "Details ▴" : "Details ▾";
    }

    private static void SetGlyph(Label l, string glyph, Color color)
    {
        l.Text = glyph;
        l.ForeColor = color;
    }

    // ================================================================ actions

    private async Task PrimaryClickAsync()
    {
        switch (CurrentMode())
        {
            case PrimaryMode.ChooseFolder:
                Browse();
                break;
            case PrimaryMode.Downscale:
                await RunAsync();
                break;
            case PrimaryMode.Restore:
                await RestoreAsync();
                break;
            case PrimaryMode.Repair:
                await RepairAsync();
                break;
            case PrimaryMode.Cancel:
                _runCts?.Cancel();
                Log("Cancel requested. Finishing the files in progress.");
                UpdateUi();
                break;
        }
    }

    private void SetBackupMode(BackupMode mode)
    {
        if (_busy || mode == _backupMode)
        {
            UpdateUi();
            return;
        }

        if (mode == BackupMode.None)
        {
            string nl = Environment.NewLine;
            string warn =
                $"Turn the backup off?{nl}{nl}" +
                "This is not recommended and at your own risk. The original textures are deleted when you downscale, " +
                $"and \"Restore original\" isn't possible. To get them back, you would have to reinstall FSLTL.";
            if (MessageBox.Show(this, warn, AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                UpdateUi();
                return;
            }
        }

        SaveBackupMode(mode);
        Log(mode switch
        {
            BackupMode.Zip => "Backup: originals are compressed into a ZIP file after downscaling.",
            BackupMode.None => "Backup: off. Originals are deleted when you downscale.",
            _ => "Backup: originals are moved to a backup folder.",
        });
        UpdateUi();
    }

    private void SaveBackupMode(BackupMode mode)
    {
        _backupMode = mode;
        var settings = AppSettings.Load();
        settings.BackupMode = mode;
        settings.BackupModeChosen = true;
        settings.Save();
    }

    /// <summary>Before the very first downscale the user picks how the originals are kept. Returns false on Cancel.</summary>
    private bool AskBackupModeOnce()
    {
        if (AppSettings.Load().BackupModeChosen)
        {
            return true;
        }

        using var dlg = new BackupChoiceForm(_t, Icon);
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return false;
        }

        SaveBackupMode(dlg.Mode);
        Log($"Backup mode chosen: {dlg.Mode}. It can be changed under Advanced options.");
        UpdateUi();
        return true;
    }

    private async Task RunAsync()
    {
        if (_pkg is null || _plan is null || _status is null || !_plan.ToProcess.Any())
        {
            return;
        }

        if (!AskBackupModeOnce())
        {
            return;
        }

        var pkg = _pkg;
        var plan = _plan;
        var mode = _backupMode;
        string backupBase = BackupBase;
        long total = _status.PackageSize;
        bool hasBackups = _status.Backups.Count > 0;

        try
        {
            SystemChecks.EnsureSimulatorNotRunning();
            var (required, available) = SystemChecks.CheckSpace(pkg.Root, backupBase, plan, mode);
            if (available < required)
            {
                string hint = mode == BackupMode.None
                    ? string.Empty
                    : $"{Environment.NewLine}{Environment.NewLine}Under \"Advanced options\" you can choose a ZIP backup or no backup, which need less space.";
                throw new UserException($"There isn't enough free disk space. Needed: {Gb1(required)}, available: {Gb1(available)}.{hint}");
            }
        }
        catch (UserException ex)
        {
            MessageBox.Show(this, ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string nl = Environment.NewLine;
        string backupText = mode switch
        {
            BackupMode.Zip =>
                $"Originals are moved to:{nl}{backupBase}{nl}and then compressed into a ZIP file. This takes a few extra minutes.",
            BackupMode.None =>
                "No backup: the original textures are deleted. \"Restore original\" won't be possible. " +
                $"To get the full-size textures back, you would have to reinstall FSLTL." +
                (hasBackups ? $"{nl}{nl}Your existing backup can't restore the package after this run either. You can delete it afterwards." : string.Empty),
            _ => $"Originals are moved to:{nl}{backupBase}",
        };
        string question =
            $"Downscale {plan.ToProcess.Count():N0} textures to {plan.Target} px?{nl}{nl}" +
            $"Package size: {Gb1(total)} → {Gb1(total - plan.Saving)}{nl}{nl}" +
            $"{backupText}{nl}{nl}" +
            "Keep MSFS closed until the tool has finished.";
        var icon = mode == BackupMode.None ? MessageBoxIcon.Warning : MessageBoxIcon.Question;
        var defaultButton = mode == BackupMode.None ? MessageBoxDefaultButton.Button2 : MessageBoxDefaultButton.Button1;
        if (MessageBox.Show(this, question, AppName, MessageBoxButtons.OKCancel, icon, defaultButton) != DialogResult.OK)
        {
            return;
        }

        _banner.Visible = false;
        var runCts = new CancellationTokenSource();
        _runCts = runCts;
        var ct = runCts.Token;
        _busy = true;
        SetProgress("Downscaling", "textures", (0, plan.ToProcess.Count()));
        _runWatch.Restart();
        UpdateUi();

        var logProgress = new Progress<string>(Log);
        var barProgress = new Progress<(int Done, int Total)>(p =>
        {
            if (_runProgress is not null)
            {
                _runProgress = p;
                UpdateUi();
            }
        });

        try
        {
            var result = await Task.Run(() =>
                Runner.Execute(pkg, plan, backupBase, msg => ((IProgress<string>)logProgress).Report(msg), ct, barProgress, mode));
            var check = await Task.Run(() => Verifier.Run(pkg));
            _runWatch.Stop();
            string took = _runWatch.Elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture);

            bool layoutOk = check.DdsMismatches == 0 && (check.TotalMatchesEntries || check.TotalMatchesEntriesPlusMeta);
            Log($"Run {result.Status} in {took}: {result.Replaced:N0} textures, {Gb1(result.BytesBefore)} → {Gb1(result.BytesAfter)}, " +
                $"{result.Failed} errors. layout.json check: {(layoutOk ? "OK" : "PROBLEM")}.");
            foreach (var e in result.Errors.Take(50))
            {
                Log($"  Error: {e}");
            }

            // ZIP: pack the originals now. Not after a cancel, the user wants to stop.
            string backupNote = mode == BackupMode.None ? " No backup was kept." : string.Empty;
            bool zipFailed = false;
            if (mode == BackupMode.Zip && result.Replaced > 0 && !ct.IsCancellationRequested
                && BackupSession.TryLoad(result.SessionDir) is { } session)
            {
                var (before, after, error) = await CompressSessionsAsync([session], ct);
                if (error is null)
                {
                    backupNote = $" The backup was compressed from {SizeText(before)} to {SizeText(after)}.";
                }
                else
                {
                    zipFailed = true;
                    backupNote = $" The backup couldn't be compressed ({error}). It stays as a folder.";
                }
            }

            if (result.Status == "completed" && result.Failed == 0 && layoutOk)
            {
                ShowBanner(zipFailed ? GlyphInfo : GlyphOk, zipFailed ? _t.Warning : _t.Success, zipFailed ? _t.WarningSoft : _t.SuccessSoft,
                    $"Done. {result.Replaced:N0} textures were downscaled to {plan.Target} px in {took} min.{backupNote} You can start MSFS now.");
            }
            else if (result.Status == "cancelled")
            {
                ShowBanner(GlyphInfo, _t.Warning, _t.WarningSoft,
                    $"Cancelled. {result.Replaced:N0} textures were already downscaled. The package works as it is" +
                    (mode == BackupMode.None ? "." : ", or you can restore the original."));
            }
            else
            {
                ShowBanner(GlyphWarning, _t.Warning, _t.WarningSoft,
                    $"Finished with {result.Failed:N0} problems. The package works as it is. Open Details for more" +
                    (mode == BackupMode.None ? "." : ", or restore the original."));
                _logCard.Visible = true;
            }
        }
        catch (Exception ex)
        {
            ShowError(mode == BackupMode.None
                ? "The run failed. Use \"Repair package\" to bring layout.json in line with the textures."
                : "The run failed. Use \"Restore original\" to undo the changes.", ex);
        }
        finally
        {
            _runCts = null;
            runCts.Dispose();
            _runProgress = null;
            _busy = false;
            _ = ScanAsync();
        }
    }

    private void SetProgress(string title, string? unit, (int Done, int Total) start)
    {
        _progressTitle = title;
        _progressUnit = unit;
        _runProgress = start;
    }

    /// <summary>
    /// Packs the given backups into ZIP files one after the other, with progress in the size card.
    /// The caller has set _busy. Returns the sizes before and after, or the first error.
    /// </summary>
    private async Task<(long Before, long After, string? Error)> CompressSessionsAsync(IReadOnlyList<BackupSession> sessions, CancellationToken ct)
    {
        long before = 0;
        long after = 0;
        var logProgress = new Progress<string>(Log);
        foreach (var session in sessions)
        {
            SetProgress("Compressing backup", null, (0, 1));
            UpdateUi();
            var barProgress = new Progress<(int Done, int Total)>(p =>
            {
                if (_runProgress is not null)
                {
                    _runProgress = p;
                    UpdateUi();
                }
            });

            try
            {
                long size = await Task.Run(() => session.SizeBytes());
                var watch = Stopwatch.StartNew();
                long zipBytes = await Task.Run(() =>
                    session.CompressToZip(msg => ((IProgress<string>)logProgress).Report(msg), barProgress, ct), ct);
                before += size;
                after += zipBytes;
                Log($"Backup compressed in {watch.Elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture)} min: {SizeText(size)} → {SizeText(zipBytes)}.");
            }
            catch (OperationCanceledException)
            {
                Log("Compressing cancelled. The backup stays as a folder.");
                return (before, after, "cancelled");
            }
            catch (Exception ex)
            {
                // Whatever went wrong, files/ is only removed after a checked ZIP file is in place.
                Log($"Compressing failed: {ex.Message} The backup stays as a folder.");
                _logCard.Visible = true;
                return (before, after, ex.Message);
            }
            finally
            {
                _runProgress = null;
            }
        }

        return (before, after, null);
    }

    /// <summary>"Compress to ZIP" under Advanced: packs existing folder backups, e.g. from version 1.2.</summary>
    private async Task CompressBackupsAsync()
    {
        var status = _status;
        if (_pkg is null || status is null || _busy || _scanning)
        {
            return;
        }

        var sessions = status.Compressible.ToList();
        if (sessions.Count == 0)
        {
            return;
        }

        string nl = Environment.NewLine;
        string question =
            $"Compress the backup into a ZIP file?{nl}{nl}" +
            $"FSLTL textures usually shrink to about a fifth of their size. This takes a few minutes. " +
            $"Every file is checked against its original before the folder is removed.{nl}{nl}" +
            "\"Restore original\" keeps working. It then unpacks the textures from the ZIP file.";
        if (MessageBox.Show(this, question, AppName, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
        {
            return;
        }

        _banner.Visible = false;
        var runCts = new CancellationTokenSource();
        _runCts = runCts;
        _busy = true;
        UpdateUi();
        try
        {
            var (before, after, error) = await CompressSessionsAsync(sessions, runCts.Token);
            if (error is null)
            {
                ShowBanner(GlyphOk, _t.Success, _t.SuccessSoft,
                    $"Backup compressed from {SizeText(before)} to {SizeText(after)}. {SizeText(before - after)} of disk space were freed.");
            }
            else
            {
                ShowBanner(GlyphInfo, _t.Warning, _t.WarningSoft,
                    error == "cancelled"
                        ? "Compressing was cancelled. The backup stays as a folder."
                        : $"The backup couldn't be compressed ({error}). It stays as a folder and nothing was lost.");
            }
        }
        finally
        {
            _runCts = null;
            runCts.Dispose();
            _busy = false;
            _ = ScanAsync();
        }
    }

    /// <summary>An interrupted run without backup: bring layout.json and manifest.json in line with the textures on disk.</summary>
    private async Task RepairAsync()
    {
        var session = _status?.Repairable;
        if (_pkg is null || session is null)
        {
            return;
        }

        var pkg = _pkg;
        try
        {
            SystemChecks.EnsureSimulatorNotRunning();
        }
        catch (UserException ex)
        {
            MessageBox.Show(this, ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string nl = Environment.NewLine;
        string question =
            $"The last run without backup was interrupted.{nl}{nl}" +
            "Repair layout.json and manifest.json so they match the textures on disk? " +
            "Textures that were already downscaled stay downscaled. You can downscale the rest afterwards.";
        if (MessageBox.Show(this, question, AppName, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
        {
            return;
        }

        _banner.Visible = false;
        _busy = true;
        UpdateUi();
        try
        {
            var logProgress = new Progress<string>(Log);
            await Task.Run(() => Repairer.Repair(pkg, session, msg => ((IProgress<string>)logProgress).Report(msg)));
            var check = await Task.Run(() => Verifier.Run(pkg));
            bool layoutOk = check.DdsMismatches == 0 && (check.TotalMatchesEntries || check.TotalMatchesEntriesPlusMeta);
            Log($"layout.json check: {(layoutOk ? "OK" : "PROBLEM")}.");
            if (layoutOk)
            {
                ShowBanner(GlyphOk, _t.Success, _t.SuccessSoft, "The package was repaired. You can start MSFS or downscale the remaining textures.");
            }
            else
            {
                ShowBanner(GlyphWarning, _t.Warning, _t.WarningSoft,
                    "layout.json still doesn't match the textures. Please reinstall FSLTL with its installer.");
                _logCard.Visible = true;
            }
        }
        catch (Exception ex)
        {
            ShowError("Repairing failed.", ex);
        }
        finally
        {
            _busy = false;
            _ = ScanAsync();
        }
    }

    private async Task RestoreAsync()
    {
        var session = _status?.Restorable;
        if (_pkg is null || session is null)
        {
            return;
        }

        var pkg = _pkg;
        try
        {
            SystemChecks.EnsureSimulatorNotRunning();
        }
        catch (UserException ex)
        {
            MessageBox.Show(this, ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string question =
            $"Put the original textures back?{Environment.NewLine}{Environment.NewLine}" +
            $"{session.Info.FilesReplaced:N0} textures, layout.json and manifest.json from the run of " +
            $"{session.Info.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm} are restored." +
            (session.IsZip
                ? $"{Environment.NewLine}{Environment.NewLine}The textures are unpacked from the ZIP file. This needs about " +
                  $"{SizeText(session.Info.BytesBefore)} of free space and takes a few minutes. The ZIP file is removed afterwards."
                : string.Empty);
        if (MessageBox.Show(this, question, AppName, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
        {
            return;
        }

        _banner.Visible = false;
        _busy = true;
        SetProgress(session.IsZip ? "Restoring from ZIP" : "Restoring", "original textures", (0, Math.Max(1, session.Info.FilesReplaced)));
        UpdateUi();
        var barProgress = new Progress<(int Done, int Total)>(p =>
        {
            if (_runProgress is not null)
            {
                _runProgress = p;
                UpdateUi();
            }
        });
        bool force = false;
        try
        {
            while (true)
            {
                try
                {
                    var logProgress = new Progress<string>(Log);
                    bool f = force;
                    int n = await Task.Run(() => Restorer.Restore(pkg, session, f,
                        msg => ((IProgress<string>)logProgress).Report(msg), barProgress));
                    Log($"Restored {n:N0} files plus layout.json and manifest.json.");
                    ShowBanner(GlyphOk, _t.Success, _t.SuccessSoft, "The original textures are back.");
                    break;
                }
                catch (UserException ex) when (!force && ex.Message.Contains("changed after", StringComparison.Ordinal))
                {
                    string warn =
                        "layout.json changed after the downscale run. This usually means FSLTL was updated in the meantime." +
                        $"{Environment.NewLine}{Environment.NewLine}Restoring now would mix old and new files. " +
                        "Reinstalling FSLTL with its installer is the safer way back." +
                        $"{Environment.NewLine}{Environment.NewLine}Restore anyway?";
                    if (MessageBox.Show(this, warn, AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    {
                        break;
                    }

                    force = true;
                }
            }
        }
        catch (Exception ex)
        {
            ShowError("Restoring failed.", ex);
        }
        finally
        {
            _runProgress = null;
            _busy = false;
            _ = ScanAsync();
        }
    }

    /// <summary>
    /// After an FSLTL update the old backup no longer fits the package. Tell the user once per package,
    /// so the space (often 50 GB) isn't lost without them knowing.
    /// </summary>
    private void NotifyOutdatedBackups(PackageContext pkg, PackageStatus status)
    {
        if (status.Outdated.Count == 0 || _busy || string.Equals(_outdatedNoticeFor, pkg.Root, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _outdatedNoticeFor = pkg.Root;
        string? oldVersion = status.OutdatedVersion;
        bool versionChanged = oldVersion is not null && !string.Equals(oldVersion, pkg.Version, StringComparison.Ordinal);
        string size = SizeText(status.OutdatedBytes);
        string text;
        if (status.Kind == PackageKind.DownscaledByTool)
        {
            // Already downscaled again after the update. Only the old backup is left over.
            text = versionChanged
                ? $"There is still a backup of FSLTL {oldVersion} ({size}). It can't be used with {pkg.Version}. Delete it to free the space."
                : $"There is still a backup from before FSLTL was reinstalled ({size}). It can't be used any more. Delete it to free the space.";
        }
        else
        {
            text = (versionChanged
                       ? $"FSLTL was updated from {oldVersion} to {pkg.Version} after the last run."
                       : "FSLTL was reinstalled or updated after the last run.") +
                   $" The old backup ({size}) can't be used with this version. Delete it to free the space, then downscale again.";
        }

        Log($"{status.Outdated.Count} old backup(s) from before an FSLTL update, {size}, no longer match the package.");
        ShowBanner(GlyphInfo, _t.Warning, _t.WarningSoft, text,
            "Delete old backup", () => _ = DeleteBackupsAsync(outdatedOnly: true));
    }

    private async Task DeleteBackupsAsync(bool outdatedOnly)
    {
        var status = _status;
        if (_pkg is null || status is null || _busy || _scanning)
        {
            return;
        }

        var sessions = (outdatedOnly ? status.Outdated : status.Backups).ToList();
        if (sessions.Count == 0)
        {
            return;
        }

        // Folders of restored runs hold no textures. They go along without being mentioned.
        var leftovers = outdatedOnly ? new List<BackupSession>() : status.Leftovers.ToList();

        if (sessions.Any(s => s.Info.Status is "running" or "incomplete"))
        {
            MessageBox.Show(this, "The last run was interrupted. Please restore the original first. The backup is needed for that.",
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        long bytes = outdatedOnly ? status.OutdatedBytes : status.BackupBytes;
        bool losesRestore = !outdatedOnly && status.Restorable is not null && status.Kind == PackageKind.DownscaledByTool;
        string nl = Environment.NewLine;
        string question = outdatedOnly
            ? $"Delete the old backup from before the FSLTL update?{nl}{nl}" +
              $"{SizeText(bytes)} of disk space are freed. The files in it belong to the previous FSLTL installation " +
              "and can't be restored into the current one."
            : losesRestore
                ? $"Delete the backup?{nl}{nl}{SizeText(bytes)} of disk space are freed.{nl}{nl}" +
                  "After that, \"Restore original\" is no longer possible. To get the full-size textures back, " +
                  "you would have to reinstall FSLTL. Only do this once you're sure everything works in MSFS."
                : $"Delete {(sessions.Count == 1 ? "the backup" : $"all {sessions.Count} backups")} of this package?{nl}{nl}" +
                  $"{SizeText(bytes)} of disk space are freed. The current package doesn't need {(sessions.Count == 1 ? "it" : "them")}.";
        if (MessageBox.Show(this, question, AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        _banner.Visible = false;
        _busy = true;
        UpdateUi();
        try
        {
            var (deleted, errors) = await Task.Run(() =>
            {
                var ok = new List<string>();
                var failed = new List<string>();
                foreach (var s in sessions.Concat(leftovers))
                {
                    try
                    {
                        s.Delete();
                        ok.Add(s.Dir);
                    }
                    catch (Exception ex) when (ex is UserException or IOException or UnauthorizedAccessException)
                    {
                        failed.Add($"{s.Dir}: {ex.Message}");
                    }
                }

                BackupLocations.Prune();
                return (ok, failed);
            });

            foreach (var d in deleted)
            {
                Log($"Deleted backup {d}");
            }

            if (errors.Count == 0)
            {
                ShowBanner(GlyphOk, _t.Success, _t.SuccessSoft, $"Backup deleted. {SizeText(bytes)} of disk space were freed.");
            }
            else
            {
                foreach (var e in errors)
                {
                    Log($"  Could not delete: {e}");
                }

                ShowBanner(GlyphWarning, _t.Warning, _t.WarningSoft,
                    $"{errors.Count} of {sessions.Count} backups could not be deleted completely. Open Details for more.");
                _logCard.Visible = true;
            }
        }
        catch (Exception ex)
        {
            ShowError("Deleting the backup failed.", ex);
        }
        finally
        {
            _busy = false;
            _ = ScanAsync();
        }
    }

    /// <summary>
    /// Puts everything needed to understand a problem report on the clipboard: versions, paths, links,
    /// volumes, backups and the log. Nothing is sent anywhere and nothing is written to the package.
    /// </summary>
    private void CopyDiagnostics()
    {
        var sb = new StringBuilder();
        void Line(string text) => sb.AppendLine(text);
        string Safe(Func<string> f)
        {
            try
            {
                return f();
            }
            catch (Exception ex)
            {
                return $"? ({ex.GetType().Name}: {ex.Message})";
            }
        }

        Line($"FSLTL Downscaler {AppVersion} diagnostics, {DateTime.Now:yyyy-MM-dd HH:mm}");
        Line($"Windows: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), .NET {Environment.Version}, " +
             $"{(_t.Dark ? "dark" : "light")} mode, {DeviceDpi} dpi");
        Line(string.Empty);

        Line("Detected packages:");
        if (_candidates.Count == 0)
        {
            Line("  none");
        }

        foreach (var c in _candidates)
        {
            Line($"  {c.Simulator}: {c.Path}{(PathResolver.SameFolder(c.Path, c.RealPath) ? string.Empty : $" -> {c.RealPath}")}");
        }

        Line(string.Empty);
        if (_pkg is null)
        {
            Line($"Selected package: none{(_scanError is null ? string.Empty : $" ({_scanError})")}");
        }
        else
        {
            var pkg = _pkg;
            Line($"Selected package: {pkg.Title} {pkg.Version}{(_simulator is null ? string.Empty : $" ({_simulator})")}");
            Line($"  Selected path: {pkg.SelectedPath}");
            Line($"  Real folder:   {pkg.Root}{(pkg.IsLinked ? " (linked)" : string.Empty)}");
            Line($"  Volume:        {Safe(() => PathResolver.VolumeRootOf(pkg.Root))}, free {Safe(() => Gb1(PathResolver.FreeBytes(pkg.Root)))}");
            Line($"  Backup folder: {BackupBase}, mode: {_backupMode}");
            Line($"  Backup volume: {Safe(() => PathResolver.VolumeRootOf(BackupBase))}, " +
                 $"same as package: {Safe(() => PathResolver.SameVolume(pkg.Root, PathResolver.NearestExisting(BackupBase)) ? "yes" : "no")}");
            if (_status is { } st)
            {
                Line($"  State: {Describe(st)}, package size {Gb1(st.PackageSize)}" +
                     (_scan is null ? string.Empty : $", {_scan.Items.Count:N0} textures, {_scan.LargerThan(_target):N0} larger than {_target} px"));
                if (st.MovedFrom is not null)
                {
                    Line($"  Backup made in: {st.MovedFrom}");
                }

                Line($"  Backups: {st.Backups.Count}, {st.BackupFiles:N0} files, {SizeText(st.BackupBytes)}" +
                     $"{(st.Outdated.Count > 0 ? $", {st.Outdated.Count} from before an FSLTL update" : string.Empty)}" +
                     $"{(st.Leftovers.Count > 0 ? $", {st.Leftovers.Count} restored runs left over" : string.Empty)}");
                if (st.LastRunWithoutBackup)
                {
                    Line("  Last run: without backup");
                }

                foreach (var s in st.Backups.Concat(st.Leftovers))
                {
                    Line($"    {s.Dir}: {s.Info.Status}, {s.Info.BackupFormat}, FSLTL {s.Info.PackageVersion}, {s.Info.Target} px, " +
                         $"{s.Info.FilesReplaced:N0} files, {s.Info.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm}, tool {s.Info.ToolVersion}");
                }
            }
            else
            {
                Line($"  State: {(_scanning ? "checking" : _scanError ?? "unknown")}");
            }
        }

        Line(string.Empty);
        Line("Log:");
        Line(_log.Text.TrimEnd());

        try
        {
            Clipboard.SetText(sb.ToString());
            Log("Diagnostics copied to the clipboard.");
            ShowBanner(GlyphOk, _t.Success, _t.SuccessSoft,
                "Diagnostics copied to the clipboard. Paste them into your comment or message. " +
                "They contain folder paths, which may include your Windows user name.");
        }
        catch (Exception ex) when (ex is ExternalException or ThreadStateException)
        {
            ShowError("The diagnostics could not be copied to the clipboard.", ex);
        }
    }

    private void OpenBackupFolder()
    {
        string dir = _status?.Restorable?.Dir ?? BackupBase;
        if (Directory.Exists(dir))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
    }

    // ================================================================ helpers

    private void ShowBanner(string glyph, Color glyphColor, Color fill, string text, string? actionText = null, Action? action = null)
    {
        SetGlyph(_bannerGlyph, glyph, glyphColor);
        _bannerText.Text = text;
        _bannerActionHandler = action;
        _bannerAction.Text = actionText ?? string.Empty;
        _bannerAction.Visible = actionText is not null && action is not null;
        _banner.FillColor = fill;
        _banner.BorderColor = fill;
        _banner.CanvasColor = _t.Window;
        _banner.Visible = true;
        _banner.Invalidate();
    }

    private void Log(string line) =>
        _log.AppendText($"{DateTime.Now:HH:mm:ss}  {line}{Environment.NewLine}");

    private void ShowError(string message, Exception ex)
    {
        Log($"ERROR: {ex.Message}");
        _logCard.Visible = true;
        string detail = ex is UserException ? ex.Message : ex.ToString();
        MessageBox.Show(this, $"{message}{Environment.NewLine}{Environment.NewLine}{detail}", AppName,
            MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    /// <summary>
    /// Folders of runs that were already restored, and of old runs without backup, only hold a copy of the old
    /// layout.json and the journal. They are of no use any more, so they are removed quietly.
    /// Only when no original texture is left in them.
    /// </summary>
    private async Task RemoveLeftoversAsync(PackageStatus status)
    {
        if ((status.Leftovers.Count == 0 && status.Backups.Count == 0) || _busy)
        {
            return;
        }

        int removed = await Task.Run(() =>
        {
            // Finishes an interrupted compression, e.g. removes files/ once a checked ZIP file is in place.
            foreach (var s in status.Backups.Where(b => b.Info.Status is not ("running" or "incomplete")))
            {
                s.TidyUp();
            }

            int n = 0;
            foreach (var s in status.Leftovers)
            {
                try
                {
                    s.TidyUp();
                    if (!s.HasOriginals)
                    {
                        s.Delete();
                        n++;
                    }
                }
                catch (Exception ex) when (ex is UserException or IOException or UnauthorizedAccessException)
                {
                    // Not important. Tried again next time.
                }
            }

            BackupLocations.Prune();
            return n;
        });

        if (removed > 0)
        {
            Log($"Removed {removed} empty folder(s) of runs that were already restored or made without a backup.");
        }
    }

    /// <summary>Sizes below 1 GB in MB, so a small backup doesn't show up as "0.0 GB".</summary>
    private static string SizeText(long bytes) =>
        bytes >= 1_000_000_000 ? Gb1(bytes)
        : bytes >= 1_000_000 ? (bytes / 1_000_000d).ToString("0", CultureInfo.InvariantCulture) + " MB"
        : "less than 1 MB";

    private static string Gb1(long bytes) =>
        (bytes / 1_000_000_000d).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
}
