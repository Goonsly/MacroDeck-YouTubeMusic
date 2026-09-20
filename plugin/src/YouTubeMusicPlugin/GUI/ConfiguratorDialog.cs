using System.Windows.Forms;
using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;

namespace KeystoneDigital.YouTubeMusic.GUI;

/// <summary>
/// Plugin configuration: the connection token the browser extension needs, the
/// port, and the optional extension ID allow-list.
///
/// Controls come from Macro Deck's own themed set where one exists, so the
/// dialog matches the rest of the application. A plain WinForms TextBox renders
/// white-on-dark here and looks broken.
/// </summary>
internal sealed class ConfiguratorDialog : DialogForm
{
    private readonly YouTubeMusicPlugin _plugin;

    private readonly RoundedTextBox _tokenBox = new();
    private readonly RoundedTextBox _portBox = new();
    private readonly RoundedTextBox _extensionIdBox = new();
    private readonly Label _statusLabel = new();

    public ConfiguratorDialog(YouTubeMusicPlugin plugin)
    {
        _plugin = plugin;

        Text = "YouTube Music";
        ClientSize = new Size(580, 470);
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
            AutoSize = false,
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (var row = 0; row < layout.RowCount; row++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        AddFullWidth(layout, Heading("Connection token"), 0);

        _tokenBox.ReadOnly = true;
        _tokenBox.Dock = DockStyle.Fill;
        _tokenBox.Height = 36;
        _tokenBox.Font = new Font(FontFamily.GenericMonospace, 9.5f);
        layout.Controls.Add(_tokenBox, 0, 1);

        var copyButton = new ButtonPrimary { Text = "Copy", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        copyButton.Click += (_, _) => CopyToken();
        layout.Controls.Add(copyButton, 1, 1);

        AddFullWidth(layout, Hint("Paste this into the Chrome extension's options page. Keep it private."), 2);

        var regenerateButton = new ButtonPrimary { Text = "Generate a new token", AutoSize = true };
        regenerateButton.Click += (_, _) => RegenerateToken();
        layout.Controls.Add(regenerateButton, 0, 3);

        AddFullWidth(layout, Heading("Port"), 4);

        // A NumericUpDown would be the obvious control, but WinForms paints its
        // spinner buttons in the system colour whatever BackColor says, leaving
        // a white block in a dark dialog. The value is validated in Save().
        _portBox.Width = 120;
        _portBox.Height = 36;
        _portBox.MaxCharacters = 5;
        _portBox.PlaceHolderText = PluginSettings.DefaultPort.ToString();
        _portBox.PlaceHolderColor = PlaceholderColor;
        _portBox.Margin = new Padding(0, 4, 0, 8);
        layout.Controls.Add(_portBox, 0, 5);

        AddFullWidth(layout, Heading("Allowed Chrome extension IDs (optional)"), 6);

        _extensionIdBox.Dock = DockStyle.Fill;
        _extensionIdBox.Height = 36;
        _extensionIdBox.PlaceHolderText = "Leave empty to accept any extension with the right token";
        _extensionIdBox.PlaceHolderColor = PlaceholderColor;
        AddFullWidth(layout, _extensionIdBox, 7);

        AddFullWidth(
            layout,
            Hint("Comma separated. Websites are refused either way."),
            8);

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(24, 8, 24, 16),
            Height = 60,
            BackColor = Color.Transparent,
        };

        var saveButton = new ButtonPrimary { Text = "Save", AutoSize = true };
        saveButton.Click += (_, _) => Save();

        var closeButton = new ButtonPrimary { Text = "Close", AutoSize = true };
        closeButton.Click += (_, _) => Close();

        _statusLabel.AutoSize = true;
        _statusLabel.ForeColor = Color.FromArgb(160, 160, 160);
        _statusLabel.Padding = new Padding(0, 10, 12, 0);

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
        ForeColor = Color.White,
        Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
        Padding = new Padding(0, 14, 0, 4),
    };

    private static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(500, 0),
        ForeColor = Color.FromArgb(150, 150, 150),
        Padding = new Padding(0, 4, 0, 0),
    };

    private static Color PlaceholderColor => Color.FromArgb(130, 130, 130);

    private void LoadValues()
    {
        _tokenBox.Text = _plugin.Settings.Token;
        _portBox.Text = _plugin.Settings.Port.ToString();
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
        if (!int.TryParse(_portBox.Text.Trim(), out var port) || port is < 1024 or > 65535)
        {
            ShowStatus("Port must be a number between 1024 and 65535.");
            return;
        }

        var portChanged = port != _plugin.Settings.Port;

        _plugin.Settings.SetPort(port);
        _plugin.Settings.SetAllowedExtensionIds(_extensionIdBox.Text.Split(','));

        // The allow-list and the port are both read when the listener starts.
        _plugin.RestartServer();

        ShowStatus(portChanged ? $"Saved. Now listening on port {port}." : "Saved.");
    }

    private void ShowStatus(string message) => _statusLabel.Text = message;
}
