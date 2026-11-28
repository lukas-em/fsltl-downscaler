using FsltlDownscaler.Safety;

namespace FsltlDownscaler.Gui;

/// <summary>
/// Asked once, before the first downscale: how the original textures are kept.
/// Folder is preselected. Afterwards the choice can be changed under Advanced options.
/// </summary>
internal sealed class BackupChoiceForm : Form
{
    private const int TextWidth = 480;

    private readonly Theme _t;
    private readonly CheckOption _folder = new() { Radio = true, Checked = true, Text = "Folder (recommended)" };
    private readonly CheckOption _zip = new() { Radio = true, Text = "ZIP" };
    private readonly CheckOption _none = new() { Radio = true, Text = "No backup" };
    private readonly Label _noneWarning = new();

    public BackupChoiceForm(Theme theme, Icon? icon)
    {
        _t = theme;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.75f);
        Text = "FSLTL Downscaler · Backup";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
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
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, TextWidth));

        root.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "How should the original textures be kept?",
            Font = new Font("Segoe UI Semibold", 13f),
            ForeColor = _t.Text,
            Margin = new Padding(0, 0, 0, 6),
        });
        root.Controls.Add(Paragraph(
            "Before the first downscale, choose what happens to the original textures. " +
            "You can change this later under \"Advanced options\".", _t.TextSecondary, new Padding(0, 0, 0, 12)));

        AddOption(root, _folder,
            "Originals are moved to a backup folder next to your Community folder. Fast. " +
            "The backup takes as much space as the original textures until you delete it.");
        AddOption(root, _zip,
            "Originals are moved and then compressed into a ZIP file. FSLTL textures shrink to about a fifth " +
            "of their size. Takes a few extra minutes. \"Restore original\" unpacks them again.");
        AddOption(root, _none,
            "Originals are deleted. Frees the most space, but \"Restore original\" isn't possible. " +
            "To get the full-size textures back, you would have to reinstall FSLTL.");

        _noneWarning.AutoSize = true;
        _noneWarning.MaximumSize = new Size(TextWidth - Indent, 0);
        _noneWarning.Text = "Not recommended. Use this at your own risk.";
        _noneWarning.Font = new Font("Segoe UI Semibold", 9.75f);
        _noneWarning.ForeColor = _t.Warning;
        _noneWarning.Margin = new Padding(Indent, 0, 0, 6);
        root.Controls.Add(_noneWarning);

        foreach (var option in new[] { _folder, _zip, _none })
        {
            option.Theme = _t;
            option.CanvasColor = _t.Window;
            option.Click += (_, _) => SelectOption(option);
        }

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            WrapContents = false,
            Margin = new Padding(0, 16, 0, 0),
        };
        var ok = MakeButton("Continue", primary: true);
        ok.DialogResult = DialogResult.OK;
        var cancel = MakeButton("Cancel", primary: false);
        cancel.DialogResult = DialogResult.Cancel;
        cancel.Margin = new Padding(0, 0, 10, 0);
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
        root.Controls.Add(buttons);

        Controls.Add(root);
        HandleCreated += (_, _) => NativeMethods.SetDarkTitleBar(Handle, _t.Dark);
        ResumeLayout(false);
        PerformLayout();
    }

    /// <summary>The chosen mode. Only valid after OK.</summary>
    public BackupMode Mode => _none.Checked ? BackupMode.None : _zip.Checked ? BackupMode.Zip : BackupMode.Folder;

    /// <summary>Space in front of the descriptions, so they line up with the option text.</summary>
    private static int Indent => 27;

    private void SelectOption(CheckOption selected)
    {
        foreach (var option in new[] { _folder, _zip, _none })
        {
            option.Checked = option == selected;
        }
    }

    private void AddOption(TableLayoutPanel root, CheckOption option, string description)
    {
        option.Font = new Font("Segoe UI Semibold", 9.75f);
        option.Margin = new Padding(0, 4, 0, 0);
        root.Controls.Add(option);
        root.Controls.Add(Paragraph(description, _t.TextSecondary, new Padding(Indent, 0, 0, 6), TextWidth - Indent));
    }

    private Label Paragraph(string text, Color color, Padding margin, int width = TextWidth) => new()
    {
        AutoSize = true,
        MaximumSize = new Size(width, 0),
        Text = text,
        ForeColor = color,
        Margin = margin,
    };

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
