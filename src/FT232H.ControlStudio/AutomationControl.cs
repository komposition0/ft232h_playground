using FT232H.ControlStudio.UI;
using FT232H.Core.Automation;

namespace FT232H.ControlStudio;

/// <summary>
/// Visual test sequence editor: build sequences from actions, save/load as JSON,
/// execute with progress reporting and pass/fail results, cancel mid-run.
/// </summary>
public sealed class AutomationControl : UserControl
{
    private readonly StudioContext _context;
    private readonly Automation.TestSequenceRunner _runner = new();

    private readonly TextBox _sequenceName = new() { Width = 220, Text = "Untitled sequence" };
    private readonly DataGridView _stepsGrid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
    };
    private readonly ComboBox _actionCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly TextBox _actionArg = new() { Width = 220 };
    private readonly Button _addStepButton = new() { Text = "Add step", Width = 90 };
    private readonly Button _removeStepButton = new() { Text = "Remove step", Width = 100 };
    private readonly Button _runButton = new() { Text = "Run sequence", Width = 120 };
    private readonly Button _cancelButton = new() { Text = "Cancel", Width = 80, Enabled = false };
    private readonly Button _saveButton = new() { Text = "Save JSON", Width = 90 };
    private readonly Button _loadButton = new() { Text = "Load JSON", Width = 90 };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Bottom, Height = 18 };
    private readonly TextBox _results = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Bottom,
        Height = 140,
        Font = new Font(FontFamily.GenericMonospace, 9f),
    };

    private CancellationTokenSource? _cts;
    private TestSequence _sequence = new();

    public AutomationControl(StudioContext context)
    {
        _context = context;

        _actionCombo.Items.AddRange(Enum.GetValues<TestActionKind>().Cast<object>().ToArray());
        _actionCombo.SelectedIndex = 0;

        _stepsGrid.ColumnCount = 2;
        _stepsGrid.Columns[0].Name = "Action";
        _stepsGrid.Columns[1].Name = "Parameters";

        _addStepButton.Click += (_, _) => AddStep();
        _removeStepButton.Click += (_, _) =>
        {
            if (_stepsGrid.CurrentRow is { Index: >= 0 } row)
            {
                _sequence.Steps.RemoveAt(row.Index);
                RebuildGrid();
            }
        };
        _runButton.Click += async (_, _) => await RunAsync();
        _cancelButton.Click += (_, _) => _cts?.Cancel();
        _saveButton.Click += (_, _) => SaveSequence();
        _loadButton.Click += (_, _) => LoadSequence();

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        top.Controls.AddRange(new Control[]
        {
            new Label { Text = "Name:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) },
            _sequenceName,
            new Label { Text = "Action:", AutoSize = true, Padding = new Padding(8, 6, 0, 0) },
            _actionCombo,
            new Label { Text = "Args:", AutoSize = true, Padding = new Padding(8, 6, 0, 0) },
            _actionArg,
            _addStepButton, _removeStepButton,
            _runButton, _cancelButton,
            _saveButton, _loadButton,
        });

        Controls.Add(_stepsGrid);
        Controls.Add(_results);
        Controls.Add(_progress);
        Controls.Add(top);
    }

    private void RebuildGrid()
    {
        _stepsGrid.Rows.Clear();
        for (int i = 0; i < _sequence.Steps.Count; i++)
            _stepsGrid.Rows.Add(_sequence.Steps[i].Kind, _sequence.Steps[i].ToString());
    }

    /// <summary>
    /// Step argument syntax: hex bytes (e.g. "DE AD"), pin name (e.g. "AdBus4"),
    /// integer (delay ms / repeat count), "PIN=HIGH|LOW", or free text for Log.
    /// </summary>
    private void AddStep()
    {
        var kind = (TestActionKind)_actionCombo.SelectedItem!;
        string args = _actionArg.Text.Trim();
        var step = new TestSequenceStep { Kind = kind };

        try
        {
            switch (kind)
            {
                case TestActionKind.SpiWrite:
                case TestActionKind.SpiWriteRead:
                    step.WriteData = Core.Text.HexCodec.Parse(args);
                    break;
                case TestActionKind.SpiRead:
                    step.ReadLength = int.Parse(args);
                    break;
                case TestActionKind.GpioSet:
                case TestActionKind.AssertGpio:
                {
                    var parts = args.Split('=');
                    step.Pin = Enum.Parse<Core.Gpio.GpioPinId>(parts[0].Trim());
                    step.PinValue = parts.Length > 1 &&
                        parts[1].Trim().Equals("HIGH", StringComparison.OrdinalIgnoreCase);
                    break;
                }
                case TestActionKind.GpioRead:
                    step.Pin = Enum.Parse<Core.Gpio.GpioPinId>(args);
                    break;
                case TestActionKind.Delay:
                    step.DelayMilliseconds = int.Parse(args);
                    break;
                case TestActionKind.Repeat:
                    step.RepeatCount = int.Parse(args);
                    step.Steps = new List<TestSequenceStep>();
                    break;
                case TestActionKind.CompareBytes:
                    step.ExpectedData = Core.Text.HexCodec.Parse(args);
                    break;
                case TestActionKind.LogMessage:
                    step.Message = args;
                    break;
                case TestActionKind.Stop:
                    step.StopReason = args;
                    break;
                case TestActionKind.ConditionalBranch:
                {
                    var parts = args.Split('=');
                    step.Pin = Enum.Parse<Core.Gpio.GpioPinId>(parts[0].Trim());
                    step.PinValue = parts.Length > 1 &&
                        parts[1].Trim().Equals("HIGH", StringComparison.OrdinalIgnoreCase);
                    step.Steps = new List<TestSequenceStep>();
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Invalid argument: {ex.Message}", "Add step",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _sequence.Steps.Add(step);
        RebuildGrid();
    }

    private async Task RunAsync()
    {
        var device = _context.Device;
        if (device is null || !device.IsConnected)
        {
            MessageBox.Show(this, "Connect a device first.", "Automation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_sequence.Steps.Count == 0)
        {
            MessageBox.Show(this, "Add at least one step.", "Automation",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _sequence.Name = _sequenceName.Text;
        _cts = new CancellationTokenSource();
        _runButton.Enabled = false;
        _cancelButton.Enabled = true;
        _results.Clear();
        _progress.Style = ProgressBarStyle.Marquee;

        try
        {
            var progress = new Progress<TestSequenceProgress>(p =>
            {
                _results.AppendText($"[{p.CompletedSteps}/{p.TotalSteps}] {p.CurrentDescription}{Environment.NewLine}");
            });

            var result = await _runner.RunAsync(_sequence, device.Spi, device.Gpio,
                progress, _cts.Token);

            _results.AppendText($"--- {(result.Success ? "PASS" : "FAIL")} in " +
                $"{result.HostDuration.TotalMilliseconds:0.0} ms | passed {result.PassedCount}, failed {result.FailedCount} ---{Environment.NewLine}");
            if (result.Error is not null)
                _results.AppendText($"Error: {result.Error}{Environment.NewLine}");
            foreach (var step in result.Steps.Where(s => s.Outcome is StepOutcome.Failed or StepOutcome.Error))
                _results.AppendText($"  FAIL: {step.Description} - {step.Detail}{Environment.NewLine}");
        }
        finally
        {
            _progress.Style = ProgressBarStyle.Blocks;
            _runButton.Enabled = true;
            _cancelButton.Enabled = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private void SaveSequence()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "Test sequence (*.json)|*.json",
            FileName = "sequence.json",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _sequence.Name = _sequenceName.Text;
        Automation.TestSequenceSerializer.SaveToFile(_sequence, dialog.FileName);
    }

    private void LoadSequence()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Test sequence (*.json)|*.json",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            _sequence = Automation.TestSequenceSerializer.LoadFromFile(dialog.FileName);
            _sequenceName.Text = _sequence.Name;
            RebuildGrid();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Load failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}