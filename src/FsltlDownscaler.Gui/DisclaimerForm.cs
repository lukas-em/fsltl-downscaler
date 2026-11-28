using System.Diagnostics;

namespace FsltlDownscaler.Gui;

/// <summary>
/// "Use at your own risk" notice. Shown at start (with "Don't show this again") and from the About link.
/// </summary>
internal sealed class DisclaimerForm : Form
{
    public const string ProfileUrl = "https://flightsim.to/profile/FranklinFjM";

    private readonly Theme _t;
    private readonly CheckOption _dontShow = new() { Text = "Don't show this again", Margin = new Padding(0, 14, 0, 0) };

    public DisclaimerForm(Theme theme, bool firstStart, Icon? icon)
    {
        _t = theme;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.75f);
        Text = firstStart ? "FSLTL Downscaler · Before you start" : "About FSLTL Downscaler";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = firstStart;
        StartPosition = firstStart ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = _t.Window;
        ForeColor = _t.Text;
        if (icon is not null)
        {
            Icon = icon;
        }

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(24, 20, 24, 20),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 480));

        root.Controls.Add(new Label
        {
            AutoSize = true,
            Text = firstStart ? "Before you start" : $"FSLTL Downscaler {Version}",
            Font = new Font("Segoe UI Semibold", 13f),
            ForeColor = _t.Text,
            Margin = new Padding(0, 0, 0, 10),
        });

        AddParagraph(root, "Use at your own risk", bold: true);
        AddParagraph(root,
            "This tool changes files of your FSLTL installation. Unless you turn the backup off, it keeps every original file " +
            "and can put everything back, but it comes without any warranty. The author isn't liable for damage or data loss.");
        AddParagraph(root,
            "Close MSFS before you use the tool, and keep the backup until you're sure everything works.");
        AddParagraph(root, "Made with AI", bold: true);
        AddParagraph(root,
            "This tool was written with the help of AI (Claude by Anthropic). It was tested by the author.");
        AddParagraph(root,
            "FSLTL Downscaler isn't affiliated with FSLTL. It doesn't contain or distribute any FSLTL files.");

        var profile = new LinkLabel
        {
            AutoSize = true,
            Text = "FranklinFjM on flightsim.to",
            LinkColor = _t.Accent,
            ActiveLinkColor = _t.AccentHover,
            VisitedLinkColor = _t.Accent,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Margin = new Padding(0, 4, 0, 0),
        };
        profile.LinkClicked += (_, _) => OpenUrl(ProfileUrl);
        root.Controls.Add(profile);

        if (firstStart)
        {
            _dontShow.Theme = _t;
            _dontShow.CanvasColor = _t.Window;
            root.Controls.Add(_dontShow);
        }

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            WrapContents = false,
            Margin = new Padding(0, 18, 0, 0),
        };
        var ok = MakeButton(firstStart ? "I understand" : "Close", primary: true);
        ok.DialogResult = DialogResult.OK;
        buttons.Controls.Add(ok);
        AcceptButton = ok;
        if (firstStart)
        {
            var exit = MakeButton("Exit", primary: false);
            exit.DialogResult = DialogResult.Cancel;
            exit.Margin = new Padding(0, 0, 10, 0);
            buttons.Controls.Add(exit);
            CancelButton = exit;
        }
        else
        {
            CancelButton = ok;
        }

        root.Controls.Add(buttons);
        Controls.Add(root);
        HandleCreated += (_, _) => NativeMethods.SetDarkTitleBar(Handle, _t.Dark);
        ResumeLayout(false);
        PerformLayout();
    }

    public bool DontShowAgain => _dontShow.Checked;

    private static string Version =>
        typeof(DisclaimerForm).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : string.Empty;

    public static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private void AddParagraph(TableLayoutPanel root, string text, bool bold = false)
    {
        root.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(480, 0),
            Text = text,
            Font = bold ? new Font("Segoe UI Semibold", 10f) : Font,
            ForeColor = bold ? _t.Text : _t.TextSecondary,
            Margin = new Padding(0, bold ? 8 : 0, 0, 6),
        });
    }

    private RoundButton MakeButton(string text, bool primary) => new()
    {
        Text = text,
        Primary = primary,
        Theme = _t,
        CanvasColor = _t.Window,
        AutoSize = true,
        MinimumSize = new Size(120, 36),
        Margin = new Padding(0),
        Font = new Font("Segoe UI Semibold", 9.75f),
    };
}
