using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;

namespace KeystoneDigital.YouTubeMusic.GUI;

/// <summary>
/// Plugin configuration: the connection token the browser extension needs, the
/// port, and the optional extension ID allow-list.
/// </summary>
internal sealed class ConfiguratorDialog : DialogForm
{
    private readonly YouTubeMusicPlugin _plugin;

    private readonly TextBox _tokenBox = new();
    private readonly NumericUpDown _portBox = new();
    private readonly TextBox _extensionIdBox = new();
    private readonly Label _statusLabel = new();

    public ConfiguratorDialog(YouTubeMusicPlugin plugin)
    {
        _plugin = plugin;

        Text = "YouTube Music";
        ClientSize = new Size(560, 430);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        BuildLayout();
        LoadValues();
    }

    private void BuildLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 56, 24, 24),
            ColumnCount = 2,
            RowCount = 9,
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddFullWidth(layout, Heading("Connection token"), 0);

        _tokenBox.ReadOnly = true;
        _tokenBox.Dock = DockStyle.Fill;
        _tokenBox.Font = new Font(FontFamily.GenericMonospace, 9.5f);
        layout.Controls.Add(_tokenBox, 0, 1);

        var copyButton = new ButtonPrimary { Text = "Copy", AutoSize = true };
        copyButton.Click += (_, _) => CopyToken();
        layout.Controls.Add(copyButton, 1, 1);

        AddFullWidth(layout, Hint("Paste this into the Chrome extension's options page. Keep it private."), 2);

        var regenerateButton = new ButtonPrimary { Text = "Generate a new token", AutoSize = true };
        regenerateButton.Click += (_, _) => RegenerateToken();
        layout.Controls.Add(regenerateButton, 0, 3);

        AddFullWidth(layout, Heading("Port"), 4);

        _portBox.Minimum = 1024;
        _portBox.Maximum = 65535;
        _portBox.Width = 120;
        layout.Controls.Add(_portBox, 0, 5);

        AddFullWidth(layout, Heading("Allowed Chrome extension IDs (optional)"), 6);

        _extensionIdBox.Dock = DockStyle.Fill;
        AddFullWidth(layout, _extensionIdBox, 7);

        AddFullWidth(
            layout,
            Hint("Comma separated. Leave empty to accept any Chrome extension that presents the "
                 + "correct token. Websites are refused either way."),
            8);

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(24, 8, 24, 16),
            Height = 60,
        };

        var saveButton = new ButtonPrimary { Text = "Save", AutoSize = true };
        saveButton.Click += (_, _) => Save();

        var closeButton = new ButtonPrimary { Text = "Close", AutoSize = true };
        closeButton.Click += (_, _) => Close();

        _statusLabel.AutoSize = true;
        _statusLabel.Padding = new Padding(0, 8, 12, 0);

        buttonRow.Controls.Add(closeButton);
        buttonRow.Controls.Add(saveButton);
        buttonRow.Controls.Add(_statusLabel);

        Controls.Add(layout);
        Controls.Add(buttonRow);
    }

    private static void AddFullWidth(TableLayoutPanel layout, Control control, int row)
    {
        layout.Controls.Add(control, 0, row);
        layout.SetColumnSpan(control, 2);
    }

    private static Label Heading(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
        Padding = new Padding(0, 12, 0, 4),
    };

    private static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Height = 34,
        Dock = DockStyle.Fill,
        ForeColor = Color.Gray,
    };

    private void LoadValues()
    {
        _tokenBox.Text = _plugin.Settings.Token;
        _portBox.Value = _plugin.Settings.Port;
        _extensionIdBox.Text = string.Join(", ", _plugin.Settings.AllowedExtensionIds);
        ShowStatus(_plugin.Server.IsClientConnected ? "Browser connected." : "Waiting for the browser.");
    }

    private void CopyToken()
    {
        Clipboard.SetText(_tokenBox.Text);
        ShowStatus("Token copied.");
    }

    private void RegenerateToken()
    {
        _tokenBox.Text = _plugin.Settings.RegenerateToken();
        _plugin.RestartServer();
        ShowStatus("New token. Paste it into the extension.");
    }

    private void Save()
    {
        var port = (int)_portBox.Value;
        var portChanged = port != _plugin.Settings.Port;

        _plugin.Settings.SetPort(port);
        _plugin.Settings.SetAllowedExtensionIds(_extensionIdBox.Text.Split(','));

        // The allow-list and the port are both read when the listener starts.
        _plugin.RestartServer();

        ShowStatus(portChanged ? $"Saved. Now listening on port {port}." : "Saved.");
    }

    private void ShowStatus(string message) => _statusLabel.Text = message;
}
