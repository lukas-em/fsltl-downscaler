using System.Diagnostics;
using System.Globalization;
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

    private enum PrimaryMode { ChooseFolder, Checking, Downscale, NothingToDo, Restore, Cancel, Busy }

    private Theme _t = Theme.Current;

    // Header
    private readonly PictureBox _logo = new() { SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(44, 44), Margin = new Padding(0, 0, 12, 0) };
    private readonly Label _title = new() { AutoSize = true, Text = AppName, Margin = new Padding(0, 2, 0, 0) };
    private readonly Label _subtitle = new() { AutoSize = true, Text = "Smaller textures, more FPS, same traffic", Margin = new Padding(0) };
    private readonly Label _version = new() { AutoSize = true, Anchor = AnchorStyles.Right | AnchorStyles.Top, Margin = new Padding(0, 4, 0, 0) };
    private readonly LinkLabel _profileLink = new() { AutoSize = true, Text = "by FranklinFjM", Anchor = AnchorStyles.Right | AnchorStyles.Top, Margin = new Padding(0, 2, 0, 0) };

    // Banner
    private readonly CardPanel _banner = new() { Visible = false };
    private readonly Label _bannerGlyph = new() { AutoSize = true, Margin = new Padding(0, 1, 10, 0) };
    private readonly Label _bannerText = new() { AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(0, 2, 0, 0) };
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
    private readonly RoundButton _primary = new() { AutoSize = true, MinimumSize = new Size(230, 42), Margin = new Padding(0, 0, 10, 0), Primary = true };
    private readonly RoundButton _restore = new() { Text = "Restore original", AutoSize = true, MinimumSize = new Size(160, 42), Margin = new Padding(0) };
    private readonly LinkLabel _advancedLink = new() { AutoSize = true, Text = "Advanced", Margin = new Padding(14, 13, 0, 0) };
    private readonly LinkLabel _detailsLink = new() { AutoSize = true, Text = "Details", Margin = new Padding(14, 13, 0, 0) };
    private readonly LinkLabel _aboutLink = new() { AutoSize = true, Text = "About", Margin = new Padding(14, 13, 0, 0) };

    // Advanced
    private readonly CardPanel _advCard = new() { Visible = false };
    private readonly CheckOption _mipsBox = new() { Checked = true, Text = "Create mip maps for textures that have none (recommended)", Margin = new Padding(0, 0, 0, 10) };
    private readonly Label _backupLabel = new() { AutoSize = true, MaximumSize = new Size(600, 0), Margin = new Padding(0, 2, 8, 0) };
    private readonly LinkLabel _openBackupLink = new() { AutoSize = true, Text = "Open", Margin = new Padding(0, 2, 0, 0) };

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
        Text = "Originals are moved to a backup folder and can be restored at any time. Only your local installation is changed. " +
               "No FSLTL content is distributed with this tool. Use at your own risk. Written with the help of AI.",
    };

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

        _version.Text = $"v{AppVersion}";
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
        header.Controls.Add(_version, 2, 0);
        header.Controls.Add(_profileLink, 2, 1);
        root.Controls.Add(header);

        // Banner
        _banner.ColumnCount = 3;
        _banner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _banner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _banner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _banner.Controls.Add(_bannerGlyph, 0, 0);
        _banner.Controls.Add(_bannerText, 1, 0);
        _banner.Controls.Add(_bannerClose, 2, 0);
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

        // Actions
        var actions = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var left = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        left.Controls.Add(_primary);
        left.Controls.Add(_restore);
        var right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Anchor = AnchorStyles.Right };
        right.Controls.Add(_advancedLink);
        right.Controls.Add(_detailsLink);
        right.Controls.Add(_aboutLink);
        actions.Controls.Add(left, 0, 0);
        actions.Controls.Add(right, 1, 0);
        root.Controls.Add(actions);

        // Advanced
        _advCard.ColumnCount = 1;
        _advCard.Controls.Add(_mipsBox, 0, 0);
        var backupRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = Color.Transparent, Dock = DockStyle.Fill };
        backupRow.Controls.Add(_backupLabel);
        backupRow.Controls.Add(_openBackupLink);
        _advCard.Controls.Add(backupRow, 0, 1);
        _advCard.Margin = new Padding(0, 14, 0, 0);
        root.Controls.Add(_advCard);

        // Details
        _logCard.ColumnCount = 1;
        _logCard.Controls.Add(_log, 0, 0);
        _logCard.Margin = new Padding(0, 14, 0, 0);
        root.Controls.Add(_logCard);

        root.Controls.Add(_footer);
        Controls.Add(root);

        _title.Font = new Font("Segoe UI Semibold", 15f);
        _primary.Font = new Font("Segoe UI Semibold", 10f);
        _pkgTitle.Font = new Font("Segoe UI Semibold", 10f);
        _sizeRight.Font = new Font("Segoe UI Semibold", 10f);
        foreach (var l in new[] { _pkgGlyph, _bannerGlyph })
        {
            l.Font = new Font(GlyphFont, 15f);
        }

        foreach (var small in new Control[] { _subtitle, _version, _profileLink, _pkgPath, _sizeDetail, _footer, _backupLabel })
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
        _primary.Click += async (_, _) => await PrimaryClickAsync();
        _restore.Click += async (_, _) => await RestoreAsync();
        _advancedLink.LinkClicked += (_, _) => _advCard.Visible = !_advCard.Visible;
        _detailsLink.LinkClicked += (_, _) => _logCard.Visible = !_logCard.Visible;
        _openBackupLink.LinkClicked += (_, _) => OpenBackupFolder();
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
        _backupLabel.ForeColor = _t.TextSecondary;
        _bannerText.ForeColor = _t.Text;

        foreach (var l in new Control[] { _pkgGlyph, _pkgTitle, _pkgPath, _sizeLeft, _sizeRight, _sizeDetail, _backupLabel, _bannerGlyph, _bannerText })
        {
            l.BackColor = Color.Transparent;
        }

        foreach (var link in new[] { _advancedLink, _detailsLink, _aboutLink, _profileLink, _openBackupLink, _bannerClose })
        {
            link.LinkColor = _t.Accent;
            link.ActiveLinkColor = _t.AccentHover;
            link.VisitedLinkColor = _t.Accent;
            link.LinkBehavior = LinkBehavior.HoverUnderline;
            link.BackColor = Color.Transparent;
        }

        _mipsBox.Theme = _t;
        _mipsBox.CanvasColor = _t.Card;
        _mipsBox.Invalidate();

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

        foreach (var b in new[] { _changeButton, _primary, _restore })
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
            RecomputePlans();
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
            return PrimaryMode.Restore;
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
                        _pkgTitle.Text = $"{name} · {size} · downscaled to {_status.Target} px{when}";
                        break;
                    case PackageKind.ReducedOther:
                        SetGlyph(_pkgGlyph, GlyphInfo, _t.Warning);
                        _pkgTitle.Text = $"{name} · {size} · textures already reduced";
                        break;
                    case PackageKind.Interrupted:
                        SetGlyph(_pkgGlyph, GlyphWarning, _t.Warning);
                        _pkgTitle.Text = $"{name} · last run was interrupted, please restore";
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
            _sizeLeft.Text = "Downscaling";
            _sizeRight.Text = $"{f * 100:0} %";
            _bar.Value = f;
            _sizeDetail.Text = $"{rp.Done:N0} of {rp.Total:N0} textures";
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
        _backupLabel.Text = _pkg is null ? "Backup folder: –" : $"Backup folder: {BackupBase}";
        _openBackupLink.Visible = _pkg is not null && (_status?.Restorable is not null || Directory.Exists(BackupBase));
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
            case PrimaryMode.Cancel:
                _runCts?.Cancel();
                Log("Cancel requested. Finishing the files in progress.");
                UpdateUi();
                break;
        }
    }

    private async Task RunAsync()
    {
        if (_pkg is null || _plan is null || _status is null || !_plan.ToProcess.Any())
        {
            return;
        }

        var pkg = _pkg;
        var plan = _plan;
        string backupBase = BackupBase;
        long total = _status.PackageSize;

        try
        {
            SystemChecks.EnsureSimulatorNotRunning();
            var (required, available) = SystemChecks.CheckSpace(pkg.Root, backupBase, plan);
            if (available < required)
            {
                throw new UserException($"There isn't enough free disk space. Needed: {Gb1(required)}, available: {Gb1(available)}.");
            }
        }
        catch (UserException ex)
        {
            MessageBox.Show(this, ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string question =
            $"Downscale {plan.ToProcess.Count():N0} textures to {plan.Target} px?{Environment.NewLine}{Environment.NewLine}" +
            $"Package size: {Gb1(total)} → {Gb1(total - plan.Saving)}{Environment.NewLine}{Environment.NewLine}" +
            $"Originals are moved to:{Environment.NewLine}{backupBase}{Environment.NewLine}{Environment.NewLine}" +
            "Keep MSFS closed until the tool has finished.";
        if (MessageBox.Show(this, question, AppName, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
        {
            return;
        }

        _banner.Visible = false;
        var runCts = new CancellationTokenSource();
        _runCts = runCts;
        var ct = runCts.Token;
        _busy = true;
        _runProgress = (0, plan.ToProcess.Count());
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
                Runner.Execute(pkg, plan, backupBase, msg => ((IProgress<string>)logProgress).Report(msg), ct, barProgress));
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

            if (result.Status == "completed" && result.Failed == 0 && layoutOk)
            {
                ShowBanner(GlyphOk, _t.Success, _t.SuccessSoft,
                    $"Done. {result.Replaced:N0} textures were downscaled to {plan.Target} px in {took} min. You can start MSFS now.");
            }
            else if (result.Status == "cancelled")
            {
                ShowBanner(GlyphInfo, _t.Warning, _t.WarningSoft,
                    $"Cancelled. {result.Replaced:N0} textures were already downscaled. The package works as it is, or you can restore the original.");
            }
            else
            {
                ShowBanner(GlyphWarning, _t.Warning, _t.WarningSoft,
                    $"Finished with {result.Failed:N0} problems. The package works as it is. Open Details for more, or restore the original.");
                _logCard.Visible = true;
            }
        }
        catch (Exception ex)
        {
            ShowError("The run failed. Use \"Restore original\" to undo the changes.", ex);
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
            $"{session.Info.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm} are restored.";
        if (MessageBox.Show(this, question, AppName, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
        {
            return;
        }

        _banner.Visible = false;
        _busy = true;
        UpdateUi();
        bool force = false;
        try
        {
            while (true)
            {
                try
                {
                    var logProgress = new Progress<string>(Log);
                    bool f = force;
                    int n = await Task.Run(() => Restorer.Restore(pkg, session, f, msg => ((IProgress<string>)logProgress).Report(msg)));
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
            _busy = false;
            _ = ScanAsync();
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

    private void ShowBanner(string glyph, Color glyphColor, Color fill, string text)
    {
        SetGlyph(_bannerGlyph, glyph, glyphColor);
        _bannerText.Text = text;
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

    private static string Gb1(long bytes) =>
        (bytes / 1_000_000_000d).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
}
