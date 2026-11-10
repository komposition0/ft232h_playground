using FT232H.ControlStudio.UI;
using FT232H.Core.Spi;
using FT232H.Core.Validation;

namespace FT232H.ControlStudio;

/// <summary>
/// SPI transaction laboratory: configuration panel, hex transmit editor,
/// receive viewers (hex/ASCII/binary), transfer kinds, repeat and history.
/// </summary>
public sealed class SpiPlaygroundControl : UserControl
{
    private readonly StudioContext _context;

    private readonly ComboBox _modeCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly NumericUpDown _clockInput = new() { Minimum = 916, Maximum = 30_000_000, Increment = 1000, Width = 120, ThousandsSeparator = true };
    private readonly ComboBox _bitOrderCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly ComboBox _csPinCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly ComboBox _csModeCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly Button _applyConfigButton = new() { Text = "Apply configuration", Width = 150 };
    private readonly Label _actualClockLabel = new() { AutoSize = true, Text = "Actual clock: -" };

    private readonly ComboBox _kindCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly TextBox _txHex = new() { Width = 320, Font = new Font(FontFamily.GenericMonospace, 10f) };
    private readonly NumericUpDown _readLengthInput = new() { Minimum = 0, Maximum = 65536, Width = 80 };
    private readonly Button _executeButton = new() { Text = "Execute", Width = 100 };
    private readonly Button _repeatButton = new() { Text = "Repeat x10", Width = 100 };
    private readonly Button _csAssertButton = new() { Text = "CS assert", Width = 90 };
    private readonly Button _csReleaseButton = new() { Text = "CS release", Width = 90 };

    private readonly TextBox _rxHex = new() { ReadOnly = true, Width = 320, Font = new Font(FontFamily.GenericMonospace, 10f) };
    private readonly TextBox _rxAscii = new() { ReadOnly = true, Width = 320, Font = new Font(FontFamily.GenericMonospace, 10f) };
    private readonly TextBox _rxBinary = new() { ReadOnly = true, Width = 320, Font = new Font(FontFamily.GenericMonospace, 10f) };
    private readonly Label _transferStats = new() { AutoSize = true, Text = "Last transfer: -" };

    private readonly DataGridView _historyGrid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
    };
    private readonly Button _exportCsvButton = new() { Text = "Export CSV", Width = 110 };
    private readonly Button _exportJsonButton = new() { Text = "Export JSON", Width = 110 };
    private readonly Button _clearHistoryButton = new() { Text = "Clear", Width = 80 };

    private readonly List<HistoryRow> _history = new();

    private sealed record HistoryRow(long Seq, string Time, string Kind, string Tx, string Rx, string DurationMs);

    public SpiPlaygroundControl(StudioContext context)
    {
        _context = context;

        _modeCombo.Items.AddRange(new object[] { SpiMode.Mode0, SpiMode.Mode1, SpiMode.Mode2, SpiMode.Mode3 });
        _modeCombo.SelectedIndex = 0;
        _clockInput.Value = 1_000_000;
        _bitOrderCombo.Items.AddRange(new object[] { BitOrder.MsbFirst, BitOrder.LsbFirst });
        _bitOrderCombo.SelectedIndex = 0;
        foreach (var pin in Enum.GetValues<Core.Gpio.GpioPinId>())
            _csPinCombo.Items.Add(pin);
        _csPinCombo.SelectedItem = Core.Gpio.GpioPinId.AdBus3;
        _csModeCombo.Items.AddRange(new object[] { ChipSelectMode.Automatic, ChipSelectMode.Manual });
        _csModeCombo.SelectedIndex = 0;

        _kindCombo.Items.AddRange(new object[]
        {
            SpiTransferKind.WriteOnly, SpiTransferKind.ReadOnly,
            SpiTransferKind.WriteRead, SpiTransferKind.WriteThenRead,
        });
        _kindCombo.SelectedIndex = 2;

        _applyConfigButton.Click += async (_, _) => await ApplyConfigurationAsync();
        _executeButton.Click += async (_, _) => await ExecuteTransferAsync();
        _repeatButton.Click += async (_, _) => await RepeatTransfersAsync();
        _csAssertButton.Click += async (_, _) => await SetChipSelectAsync(true);
        _csReleaseButton.Click += async (_, _) => await SetChipSelectAsync(false);
        _exportCsvButton.Click += (_, _) => ExportHistory("csv");
        _exportJsonButton.Click += (_, _) => ExportHistory("json");
        _clearHistoryButton.Click += (_, _) =>
        {
            _history.Clear();
            _historyGrid.Rows.Clear();
        };

        BuildLayout();
    }

    public SpiConfiguration CurrentConfiguration => new()
    {
        Mode = (SpiMode)_modeCombo.SelectedItem!,
        ClockHz = (double)_clockInput.Value,
        BitOrder = (BitOrder)_bitOrderCombo.SelectedItem!,
        ChipSelectPin = (Core.Gpio.GpioPinId)_csPinCombo.SelectedItem!,
        ChipSelectMode = (ChipSelectMode)_csModeCombo.SelectedItem!,
    };

    public void ApplyConfiguration(SpiConfiguration config)
    {
        _modeCombo.SelectedItem = config.Mode;
        _clockInput.Value = (decimal)Math.Clamp(config.ClockHz, 916, 30_000_000);
        _bitOrderCombo.SelectedItem = config.BitOrder;
        _csPinCombo.SelectedItem = config.ChipSelectPin;
        _csModeCombo.SelectedItem = config.ChipSelectMode;
    }

    private void BuildLayout()
    {
        var configPanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        configPanel.Controls.Add(new Label { Text = "Mode:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        configPanel.Controls.Add(_modeCombo);
        configPanel.Controls.Add(new Label { Text = "Clock (Hz):", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
        configPanel.Controls.Add(_clockInput);
        configPanel.Controls.Add(new Label { Text = "Bit order:", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
        configPanel.Controls.Add(_bitOrderCombo);
        configPanel.Controls.Add(new Label { Text = "CS pin:", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
        configPanel.Controls.Add(_csPinCombo);
        configPanel.Controls.Add(new Label { Text = "CS mode:", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
        configPanel.Controls.Add(_csModeCombo);
        configPanel.Controls.Add(_applyConfigButton);
        configPanel.Controls.Add(_actualClockLabel);

        var transferPanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        transferPanel.Controls.Add(new Label { Text = "Transfer:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        transferPanel.Controls.Add(_kindCombo);
        transferPanel.Controls.Add(new Label { Text = "TX hex:", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
        transferPanel.Controls.Add(_txHex);
        transferPanel.Controls.Add(new Label { Text = "Read len:", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
        transferPanel.Controls.Add(_readLengthInput);
        transferPanel.Controls.Add(_executeButton);
        transferPanel.Controls.Add(_repeatButton);
        transferPanel.Controls.Add(_csAssertButton);
        transferPanel.Controls.Add(_csReleaseButton);

        var rxPanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        rxPanel.Controls.Add(new Label { Text = "RX hex:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        rxPanel.Controls.Add(_rxHex);
        rxPanel.Controls.Add(new Label { Text = "ASCII:", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
        rxPanel.Controls.Add(_rxAscii);
        rxPanel.Controls.Add(new Label { Text = "Binary:", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
        rxPanel.Controls.Add(_rxBinary);
        rxPanel.Controls.Add(_transferStats);

        _historyGrid.ColumnCount = 6;
        _historyGrid.Columns[0].Name = "#";
        _historyGrid.Columns[1].Name = "Time";
        _historyGrid.Columns[2].Name = "Kind";
        _historyGrid.Columns[3].Name = "TX";
        _historyGrid.Columns[4].Name = "RX";
        _historyGrid.Columns[5].Name = "Duration (ms)";

        var historyButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        historyButtons.Controls.Add(_exportCsvButton);
        historyButtons.Controls.Add(_exportJsonButton);
        historyButtons.Controls.Add(_clearHistoryButton);

        Controls.Add(_historyGrid);
        Controls.Add(historyButtons);
        Controls.Add(rxPanel);
        Controls.Add(transferPanel);
        Controls.Add(configPanel);
    }

    private Core.Hardware.IFtdiDevice? RequireDevice()
    {
        var device = _context.Device;
        if (device is null || !device.IsConnected)
        {
            MessageBox.Show(this, "Connect a device first.", "SPI",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        return device;
    }

    private async Task ApplyConfigurationAsync()
    {
        var device = RequireDevice();
        if (device is null) return;

        var config = CurrentConfiguration;
        var validation = ConfigurationValidator.Validate(config);
        if (!validation.IsValid)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, validation.Errors),
                "Invalid SPI configuration", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            await device.Spi.ApplyConfigurationAsync(config);
            _actualClockLabel.Text = $"Actual clock: {device.Spi.ActualClockHz / 1_000_000.0:0.###} MHz";
            StudioLogSink.Instance.Append(Core.Diagnostics.DiagnosticSeverity.Info, "SPI",
                $"Configuration applied: {config.Mode}, {config.ActualClockHz / 1_000_000.0:0.###} MHz");
        }
        catch (Core.Hardware.FtdiException ex)
        {
            StudioLogSink.Instance.Append(Core.Diagnostics.DiagnosticSeverity.Error, "SPI", ex.Message);
            MessageBox.Show(this, ex.Message, "Configuration failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ExecuteTransferAsync()
    {
        var device = RequireDevice();
        if (device is null) return;

        if (!Core.Text.HexCodec.TryParse(_txHex.Text, out var txData, out var parseError))
        {
            MessageBox.Show(this, parseError, "Invalid TX hex", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var kind = (SpiTransferKind)_kindCombo.SelectedItem!;
        var request = new SpiTransferRequest
        {
            Kind = kind,
            WriteData = txData,
            ReadLength = (int)_readLengthInput.Value,
        };

        try
        {
            var result = await device.Spi.TransferAsync(request);
            ShowResult(result, kind, txData);

            _context.Counters.AddWritten(txData.Length);
            _context.Counters.AddRead(result.ReceivedData.Length);
            if (!result.Success) _context.Counters.AddError();
        }
        catch (Core.Hardware.FtdiException ex)
        {
            StudioLogSink.Instance.Append(Core.Diagnostics.DiagnosticSeverity.Error, "SPI", ex.Message);
            MessageBox.Show(this, ex.Message, "Transfer failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowResult(SpiTransferResult result, SpiTransferKind kind, byte[] txData)
    {
        _rxHex.Text = Core.Text.HexCodec.ToHex(result.ReceivedData);
        _rxAscii.Text = Core.Text.HexCodec.ToAscii(result.ReceivedData);
        _rxBinary.Text = Core.Text.HexCodec.ToBinary(result.ReceivedData);
        _transferStats.Text = result.Success
            ? $"Last transfer: OK in {result.HostObservedDuration.TotalMilliseconds:0.00} ms (host-observed)"
            : $"Last transfer: FAILED - {result.Error}";

        var row = new HistoryRow(
            _history.Count + 1,
            DateTime.Now.ToString("HH:mm:ss.fff"),
            kind.ToString(),
            Core.Text.HexCodec.ToHex(txData),
            Core.Text.HexCodec.ToHex(result.ReceivedData),
            result.HostObservedDuration.TotalMilliseconds.ToString("0.000"));
        _history.Add(row);
        _historyGrid.Rows.Add(row.Seq, row.Time, row.Kind, row.Tx, row.Rx, row.DurationMs);
    }

    private async Task RepeatTransfersAsync()
    {
        for (int i = 0; i < 10; i++)
            await ExecuteTransferAsync();
    }

    private async Task SetChipSelectAsync(bool asserted)
    {
        var device = RequireDevice();
        if (device is null) return;
        try
        {
            await device.Spi.SetChipSelectAsync(asserted);
            StudioLogSink.Instance.Append(Core.Diagnostics.DiagnosticSeverity.Trace, "SPI",
                $"Chip select {(asserted ? "asserted" : "released")}");
        }
        catch (Core.Hardware.FtdiException ex)
        {
            StudioLogSink.Instance.Append(Core.Diagnostics.DiagnosticSeverity.Error, "SPI", ex.Message);
        }
    }

    private void ExportHistory(string format)
    {
        using var dialog = new SaveFileDialog
        {
            Filter = format == "csv" ? "CSV (*.csv)|*.csv" : "JSON (*.json)|*.json",
            FileName = $"spi-history.{format}",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            if (format == "csv")
            {
                var lines = new List<string> { "Sequence,Time,Kind,TX,RX,DurationMs" };
                lines.AddRange(_history.Select(h =>
                    $"{h.Seq},{h.Time},{h.Kind},\"{h.Tx}\",\"{h.Rx}\",{h.DurationMs}"));
                File.WriteAllLines(dialog.FileName, lines);
            }
            else
            {
                File.WriteAllText(dialog.FileName,
                    System.Text.Json.JsonSerializer.Serialize(_history, new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true,
                    }));
            }
            StudioLogSink.Instance.Append(Core.Diagnostics.DiagnosticSeverity.Info, "SPI",
                $"History exported to {dialog.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}