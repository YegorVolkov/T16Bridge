using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using HidSharp;
using HIDMaestro;

ApplicationConfiguration.Initialize();

if (!IsAdministrator())
{
    try
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = Environment.ProcessPath!,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory
        });
    }
    catch
    {
        MessageBox.Show(
            "T16Bridge needs Administrator rights.",
            "T16Bridge",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    return;
}

Application.Run(new MainForm());

static bool IsAdministrator()
{
    using var identity = WindowsIdentity.GetCurrent();
    var principal = new WindowsPrincipal(identity);
    return principal.IsInRole(WindowsBuiltInRole.Administrator);
}

public sealed class MainForm : Form
{
    private const int PhysicalVid = 0x044F;
    private const int PhysicalPid = 0xB10A;

    private readonly string _settingsDir =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "T16Bridge");

    private string ConfigPath =>
        Path.Combine(_settingsDir, "curves.json");

    private string DeviceMappingPath =>
        Path.Combine(_settingsDir, "device-mapping.json");

    private readonly object _configLock = new();
    private AppCurveConfig _config = AppCurveConfig.CreateDefault();

    private HMContext? _hmContext;
    private HMController? _virtualLeft;
    private HMController? _virtualRight;

    private CancellationTokenSource? _cts;
    private Task? _leftTask;
    private Task? _rightTask;

    private readonly ComboBox _deviceBox = new();
    private readonly ComboBox _axisBox = new();
    private readonly ComboBox _copySourceBox = new();
    private readonly Button _copyCurveButton = new();
    private readonly CurveEditorPanel _curvePanel = new();
    private readonly Label _status = new();
    private readonly Label _inputLabel = new();
    private readonly Label _outputLabel = new();
    private readonly Button _saveButton = new();
    private readonly Button _linearButton = new();
    private readonly Button _addPointButton = new();
    private readonly Button _devicesButton = new();

    private volatile float _leftX, _leftY, _leftRz, _leftSlider;
    private volatile float _rightX, _rightY, _rightRz, _rightSlider;
    private volatile float _leftOutX, _leftOutY, _leftOutRz, _leftOutSlider;
    private volatile float _rightOutX, _rightOutY, _rightOutRz, _rightOutSlider;

    private readonly System.Windows.Forms.Timer _uiTimer = new()
    {
        Interval = 33
    };

    public MainForm()
    {
        Text = "T16Bridge Curves";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(860, 620);
        Size = new Size(980, 720);

        Directory.CreateDirectory(_settingsDir);

        BuildUi();
        LoadCurveConfig();
        RefreshEditor();

        Shown += async (_, _) =>
        {
            try
            {
                await StartBridgeAsync();
                _uiTimer.Start();
            }
            catch (Exception ex)
            {
                _status.Text = "ERROR: " + ex.Message;
                MessageBox.Show(
                    ex.ToString(),
                    "T16Bridge startup error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        };

        FormClosing += (_, _) => StopBridge();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12)
        };

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true
        };

        top.Controls.Add(new Label
        {
            Text = "Device:",
            AutoSize = true,
            Margin = new Padding(0, 9, 6, 0)
        });

        _deviceBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _deviceBox.Items.AddRange(["LEFT", "RIGHT"]);
        _deviceBox.SelectedIndex = 0;
        _deviceBox.Width = 120;
        _deviceBox.SelectedIndexChanged += (_, _) => RefreshEditor();
        top.Controls.Add(_deviceBox);

        top.Controls.Add(new Label
        {
            Text = "Axis:",
            AutoSize = true,
            Margin = new Padding(18, 9, 6, 0)
        });

        _axisBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _axisBox.Items.AddRange(["X", "Y", "Rz", "Slider"]);
        _axisBox.SelectedIndex = 0;
        _axisBox.Width = 120;
        _axisBox.SelectedIndexChanged += (_, _) => RefreshEditor();
        top.Controls.Add(_axisBox);

        top.Controls.Add(new Label
        {
            Text = "Copy from:",
            AutoSize = true,
            Margin = new Padding(18, 9, 6, 0)
        });

        _copySourceBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _copySourceBox.Items.AddRange(
        [
            "LEFT/X",
            "LEFT/Y",
            "LEFT/Rz",
            "LEFT/Slider",
            "RIGHT/X",
            "RIGHT/Y",
            "RIGHT/Rz",
            "RIGHT/Slider"
        ]);
        _copySourceBox.SelectedIndex = 0;
        _copySourceBox.Width = 125;
        top.Controls.Add(_copySourceBox);

        _copyCurveButton.Text = "Copy";
        _copyCurveButton.AutoSize = true;
        _copyCurveButton.Margin = new Padding(6, 3, 0, 0);
        _copyCurveButton.Click += (_, _) => CopySelectedCurve();
        top.Controls.Add(_copyCurveButton);

        _linearButton.Text = "Reset Linear";
        _linearButton.AutoSize = true;
        _linearButton.Margin = new Padding(18, 3, 0, 0);
        _linearButton.Click += (_, _) =>
        {
            lock (_configLock)
            {
                var curve = GetSelectedCurve();
                curve.Points = CurveDefinition.CreateLinear(curve.Centered).Points;
            }

            RefreshEditor();
        };
        top.Controls.Add(_linearButton);

        _addPointButton.Text = "Add Point";
        _addPointButton.AutoSize = true;
        _addPointButton.Margin = new Padding(8, 3, 0, 0);
        _addPointButton.Click += (_, _) =>
        {
            lock (_configLock)
            {
                var curve = GetSelectedCurve();
                curve.AddPoint(0.5f, 0.5f);
            }
            RefreshEditor();
        };
        top.Controls.Add(_addPointButton);

        _saveButton.Text = "Save";
        _saveButton.AutoSize = true;
        _saveButton.Margin = new Padding(8, 3, 0, 0);
        _saveButton.Click += (_, _) =>
        {
            SaveCurveConfig();
            _status.Text = $"Saved: {ConfigPath}";
        };
        top.Controls.Add(_saveButton);

        _devicesButton.Text = "Devices...";
        _devicesButton.AutoSize = true;
        _devicesButton.Margin = new Padding(8, 3, 0, 0);
        _devicesButton.Click += async (_, _) =>
        {
            try
            {
                _devicesButton.Enabled = false;
                StopBridge();
                await Task.Delay(150);

                // Force the assignment dialog so USB-port changes or a
                // mistaken LEFT/RIGHT choice can be corrected without editing code.
                ResolvePhysicalDevices(forcePrompt: true);

                await StartBridgeAsync();
                _uiTimer.Start();
            }
            catch (OperationCanceledException)
            {
                _status.Text = "Device assignment cancelled.";
            }
            catch (Exception ex)
            {
                _status.Text = "ERROR: " + ex.Message;
                MessageBox.Show(
                    ex.ToString(),
                    "T16Bridge device setup error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                _devicesButton.Enabled = true;
            }
        };
        top.Controls.Add(_devicesButton);

        _curvePanel.Dock = DockStyle.Fill;
        _curvePanel.Margin = new Padding(0, 10, 0, 10);
        _curvePanel.CurveChanged += (_, _) =>
        {
            // Curve object is edited in-place, so bridge sees changes immediately.
            _curvePanel.Invalidate();
        };

        var live = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true
        };

        _inputLabel.AutoSize = true;
        _inputLabel.Margin = new Padding(0, 4, 24, 0);
        _outputLabel.AutoSize = true;
        _outputLabel.Margin = new Padding(0, 4, 0, 0);

        live.Controls.Add(_inputLabel);
        live.Controls.Add(_outputLabel);

        _status.AutoSize = true;
        _status.Text = "Starting...";
        _status.Padding = new Padding(0, 4, 0, 0);

        root.Controls.Add(top, 0, 0);
        root.Controls.Add(_curvePanel, 0, 1);
        root.Controls.Add(live, 0, 2);
        root.Controls.Add(_status, 0, 3);

        Controls.Add(root);

        _uiTimer.Tick += (_, _) =>
        {
            var device = _deviceBox.SelectedItem?.ToString() ?? "LEFT";
            var axis = _axisBox.SelectedItem?.ToString() ?? "X";

            float input = GetLiveValue(device, axis, output: false);
            float output = GetLiveValue(device, axis, output: true);

            _curvePanel.LiveInput = ToGraphValue(input, axis);
            _curvePanel.LiveOutput = ToGraphValue(output, axis);
            _curvePanel.Invalidate();

            _inputLabel.Text = $"Input: {FormatPercent(input, axis)}";
            _outputLabel.Text = $"Output: {FormatPercent(output, axis)}";
        };
    }

    private async Task StartBridgeAsync()
    {
        _status.Text = "Creating virtual devices...";

        HMOemNameOverride.RecoverOrphans();

        _hmContext = new HMContext();
        _hmContext.InstallDriver();

        static HidDescriptorBuilder CreateDescriptor()
        {
            return new HidDescriptorBuilder()
                .Joystick()
                .AddAxis(HMAxis.X, 16, -32768, 32767)
                .AddAxis(HMAxis.Y, 16, -32768, 32767)
                .AddAxis(HMAxis.Rz, 16, -32768, 32767)
                .AddAxis(HMAxis.Slider, 8, 0, 255)
                .AddButtons(16)
                .AddHat(8);
        }

        var leftProfile = new HMProfileBuilder()
            .Id("t16left")
            .Name("T16LEFT")
            .Vendor("T16Bridge")
            .Vid(0x1209)
            .Pid(0x1601)
            .ProductString("T16LEFT")
            .ManufacturerString("T16Bridge")
            .Type("flightstick")
            .Connection("usb")
            .FromDescriptorBuilder(CreateDescriptor())
            .Build();

        var rightProfile = new HMProfileBuilder()
            .Id("t16right")
            .Name("T16RIGHT")
            .Vendor("T16Bridge")
            .Vid(0x1209)
            .Pid(0x1602)
            .ProductString("T16RIGHT")
            .ManufacturerString("T16Bridge")
            .Type("flightstick")
            .Connection("usb")
            .FromDescriptorBuilder(CreateDescriptor())
            .Build();

        _virtualLeft =
            _hmContext.CreateController(leftProfile, "t16bridge-left");

        _virtualRight =
            _hmContext.CreateController(rightProfile, "t16bridge-right");

        HMOemNameOverride.Set(
            leftProfile.VendorId,
            leftProfile.ProductId,
            "T16LEFT");

        HMOemNameOverride.Set(
            rightProfile.VendorId,
            rightProfile.ProductId,
            "T16RIGHT");

        _hmContext.FinalizeNames();

        var (physicalLeft, physicalRight) =
            ResolvePhysicalDevices(forcePrompt: false);

        var hidHideResult = ConfigureHidHide(physicalLeft, physicalRight);

        _cts = new CancellationTokenSource();

        _leftTask = Task.Run(() =>
            ForwardLoop(
                Side.Left,
                physicalLeft,
                _virtualLeft,
                _cts.Token));

        _rightTask = Task.Run(() =>
            ForwardLoop(
                Side.Right,
                physicalRight,
                _virtualRight,
                _cts.Token));

        _status.Text = hidHideResult.Success
            ? "ACTIVE — Physical LEFT → T16LEFT | Physical RIGHT → T16RIGHT | HidHide ON"
            : "ACTIVE — WARNING: HidHide auto-configuration failed";

        if (!hidHideResult.Success)
        {
            MessageBox.Show(
                this,
                hidHideResult.Message,
                "T16Bridge — HidHide",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        await Task.CompletedTask;
    }

    private (HidDevice Left, HidDevice Right) ResolvePhysicalDevices(
        bool forcePrompt)
    {
        var physical = DeviceList.Local
            .GetHidDevices(PhysicalVid, PhysicalPid)
            .OrderBy(d => d.DevicePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (physical.Count < 2)
        {
            throw new InvalidOperationException(
                $"Expected two T.16000M joysticks, found {physical.Count}.");
        }

        var mapping = LoadDeviceMapping();

        if (!forcePrompt && mapping is not null)
        {
            var left = physical.FirstOrDefault(
                d => string.Equals(
                    d.DevicePath,
                    mapping.LeftDevicePath,
                    StringComparison.OrdinalIgnoreCase));

            var right = physical.FirstOrDefault(
                d => string.Equals(
                    d.DevicePath,
                    mapping.RightDevicePath,
                    StringComparison.OrdinalIgnoreCase));

            if (left is not null &&
                right is not null &&
                !string.Equals(
                    left.DevicePath,
                    right.DevicePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return (left, right);
            }
        }

        using var dialog =
            new DeviceAssignmentForm(physical, mapping);

        var result = dialog.ShowDialog(this);

        if (result != DialogResult.OK ||
            dialog.SelectedLeft is null ||
            dialog.SelectedRight is null)
        {
            throw new OperationCanceledException(
                "Physical device assignment was cancelled.");
        }

        var selected = new DeviceMappingConfig
        {
            LeftDevicePath = dialog.SelectedLeft.DevicePath,
            RightDevicePath = dialog.SelectedRight.DevicePath
        };

        SaveDeviceMapping(selected);

        return (dialog.SelectedLeft, dialog.SelectedRight);
    }

    private HidHideConfigurationResult ConfigureHidHide(
        HidDevice physicalLeft,
        HidDevice physicalRight)
    {
        string? cli = FindHidHideCli();

        if (cli is null)
        {
            return HidHideConfigurationResult.Fail(
                "HidHideCLI.exe was not found.\n\n" +
                "If HidHide was just installed, restart Windows and launch T16Bridge again.");
        }

        string? appPath = Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(appPath))
        {
            return HidHideConfigurationResult.Fail(
                "T16Bridge could not determine its own executable path for the HidHide allow-list.");
        }

        try
        {
            string leftInstance = HidPathToDeviceInstanceId(physicalLeft.DevicePath);
            string rightInstance = HidPathToDeviceInstanceId(physicalRight.DevicePath);

            // One HidHideCLI transaction:
            // 1. allow T16Bridge to see hidden devices,
            // 2. hide both physical T.16000M devices,
            // 3. force normal allow-list semantics,
            // 4. enable cloaking.
            string arguments =
                $"--app-reg {QuoteArgument(appPath)} " +
                $"--dev-hide {QuoteArgument(leftInstance)} " +
                $"--dev-hide {QuoteArgument(rightInstance)} " +
                "--inv-off --cloak-on";

            var psi = new ProcessStartInfo
            {
                FileName = cli,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(cli) ?? AppContext.BaseDirectory
            };

            using var process = Process.Start(psi);

            if (process is null)
            {
                return HidHideConfigurationResult.Fail(
                    "Failed to start HidHideCLI.exe.");
            }

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(10000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }

                return HidHideConfigurationResult.Fail(
                    "HidHide configuration timed out.\n\n" +
                    "Restart Windows and launch T16Bridge again.");
            }

            if (process.ExitCode != 0)
            {
                string detail = string.IsNullOrWhiteSpace(stderr)
                    ? stdout
                    : stderr;

                if (detail.Length > 1200)
                {
                    detail = detail[..1200];
                }

                return HidHideConfigurationResult.Fail(
                    $"HidHideCLI failed with exit code {process.ExitCode}.\n\n" +
                    (string.IsNullOrWhiteSpace(detail)
                        ? "Restart Windows and launch T16Bridge again."
                        : detail.Trim()));
            }

            return HidHideConfigurationResult.Ok();
        }
        catch (Exception ex)
        {
            return HidHideConfigurationResult.Fail(
                "Automatic HidHide configuration failed.\n\n" +
                ex.Message +
                "\n\nIf HidHide was just installed, restart Windows and launch T16Bridge again.");
        }
    }

    private static string? FindHidHideCli()
    {
        string programFiles =
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        string[] candidates =
        [
            Path.Combine(
                programFiles,
                "Nefarius Software Solutions",
                "HidHide",
                "HidHideCLI.exe"),
            Path.Combine(
                programFiles,
                "Nefarius Software Solutions",
                "HidHide",
                "x64",
                "HidHideCLI.exe"),
            Path.Combine(
                programFiles,
                "Nefarius Software Solutions e.U",
                "HidHide",
                "HidHideCLI.exe"),
            Path.Combine(
                programFiles,
                "Nefarius Software Solutions e.U",
                "HidHide",
                "x64",
                "HidHideCLI.exe")
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string HidPathToDeviceInstanceId(string hidPath)
    {
        // HidSharp path example:
        // \\?\hid#vid_044f&pid_b10a#6&356926d2&0&0000#{GUID}
        // HidHide expects:
        // HID\VID_044F&PID_B10A\6&356926d2&0&0000
        string normalized = hidPath.Trim();

        if (normalized.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[4..];
        }

        string[] parts = normalized.Split('#');

        if (parts.Length < 3)
        {
            throw new InvalidOperationException(
                $"Unexpected HID device path: {hidPath}");
        }

        return $"{parts[0]}\\{parts[1]}\\{parts[2]}".ToUpperInvariant();
    }

    private static string QuoteArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private DeviceMappingConfig? LoadDeviceMapping()
    {
        if (!File.Exists(DeviceMappingPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(DeviceMappingPath);
            return JsonSerializer.Deserialize<DeviceMappingConfig>(
                json,
                JsonOptions());
        }
        catch
        {
            return null;
        }
    }

    private void SaveDeviceMapping(DeviceMappingConfig mapping)
    {
        var json = JsonSerializer.Serialize(mapping, JsonOptions());
        File.WriteAllText(DeviceMappingPath, json);
    }

    private void StopBridge()
    {
        _uiTimer.Stop();

        try
        {
            _cts?.Cancel();
        }
        catch { }

        try
        {
            Task.WaitAll(
                new[]
                {
                    _leftTask ?? Task.CompletedTask,
                    _rightTask ?? Task.CompletedTask
                },
                500);
        }
        catch { }

        try { _virtualLeft?.Dispose(); } catch { }
        try { _virtualRight?.Dispose(); } catch { }
        try { _hmContext?.Dispose(); } catch { }
        try { _cts?.Dispose(); } catch { }
    }

    private void ForwardLoop(
        Side side,
        HidDevice physical,
        HMController target,
        CancellationToken token)
    {
        if (!physical.TryOpen(out HidStream? stream))
        {
            BeginInvoke(() =>
                _status.Text = $"{side}: failed to open physical device");
            return;
        }

        using (stream)
        {
            stream.ReadTimeout = 250;

            var buffer =
                new byte[physical.GetMaxInputReportLength()];

            while (!token.IsCancellationRequested)
            {
                try
                {
                    int count =
                        stream.Read(buffer, 0, buffer.Length);

                    if (count < 10)
                    {
                        continue;
                    }

                    var decoded = DecodePhysical(buffer);
                    var output = ApplyCurves(side, decoded);

                    if (side == Side.Left)
                    {
                        _leftX = decoded.X;
                        _leftY = decoded.Y;
                        _leftRz = decoded.Rz;
                        _leftSlider = decoded.Slider;

                        _leftOutX = output.X;
                        _leftOutY = output.Y;
                        _leftOutRz = output.Rz;
                        _leftOutSlider = output.Slider;
                    }
                    else
                    {
                        _rightX = decoded.X;
                        _rightY = decoded.Y;
                        _rightRz = decoded.Rz;
                        _rightSlider = decoded.Slider;

                        _rightOutX = output.X;
                        _rightOutY = output.Y;
                        _rightOutRz = output.Rz;
                        _rightOutSlider = output.Slider;
                    }

                    var state = new HMGamepadState
                    {
                        Axes = new Dictionary<HMAxis, float>
                        {
                            [HMAxis.X] = output.X,
                            [HMAxis.Y] = output.Y,
                            [HMAxis.Rz] = output.Rz,
                            [HMAxis.Slider] = output.Slider
                        },
                        Buttons = decoded.Buttons,
                        Hat = decoded.Hat
                    };

                    target.SubmitState(in state);
                }
                catch (TimeoutException)
                {
                    // Normal: device sends on change.
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        BeginInvoke(() =>
                            _status.Text =
                                $"{side}: {ex.GetType().Name}: {ex.Message}");
                    }

                    Thread.Sleep(100);
                }
            }
        }
    }

    private PhysicalState ApplyCurves(
        Side side,
        PhysicalState input)
    {
        lock (_configLock)
        {
            var device =
                side == Side.Left
                    ? _config.Left
                    : _config.Right;

            return input with
            {
                X = device.X.Apply01(input.X),
                Y = device.Y.Apply01(input.Y),
                Rz = device.Rz.Apply01(input.Rz),
                Slider = device.Slider.Apply01(input.Slider)
            };
        }
    }

    private static PhysicalState DecodePhysical(byte[] r)
    {
        ushort rawButtons =
            (ushort)(r[1] | (r[2] << 8));

        HMButton buttons = HMButton.None;

        for (int i = 0; i < 16; i++)
        {
            if ((rawButtons & (1 << i)) != 0)
            {
                buttons |= (HMButton)(1u << i);
            }
        }

        int rawHat = r[3] & 0x0F;

        HMHat hat = rawHat switch
        {
            0 => HMHat.North,
            1 => HMHat.NorthEast,
            2 => HMHat.East,
            3 => HMHat.SouthEast,
            4 => HMHat.South,
            5 => HMHat.SouthWest,
            6 => HMHat.West,
            7 => HMHat.NorthWest,
            _ => HMHat.None
        };

        int rawX =
            r[4] | ((r[5] & 0x3F) << 8);

        int rawY =
            r[6] | ((r[7] & 0x3F) << 8);

        return new PhysicalState(
            X: rawX / 16383.0f,
            Y: rawY / 16383.0f,
            Rz: r[8] / 255.0f,
            Slider: r[9] / 255.0f,
            Buttons: buttons,
            Hat: hat);
    }

    private void LoadCurveConfig()
    {
        lock (_configLock)
        {
            if (!File.Exists(ConfigPath))
            {
                _config = AppCurveConfig.CreateDefault();
                SaveCurveConfig();
                return;
            }

            try
            {
                var json = File.ReadAllText(ConfigPath);

                _config =
                    JsonSerializer.Deserialize<AppCurveConfig>(
                        json,
                        JsonOptions())
                    ?? AppCurveConfig.CreateDefault();

                _config.Normalize();
            }
            catch
            {
                _config = AppCurveConfig.CreateDefault();
            }
        }
    }

    private void SaveCurveConfig()
    {
        lock (_configLock)
        {
            _config.Normalize();

            var json =
                JsonSerializer.Serialize(
                    _config,
                    JsonOptions());

            File.WriteAllText(ConfigPath, json);
        }
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
    }

    private void RefreshEditor()
    {
        if (_deviceBox.SelectedIndex < 0 ||
            _axisBox.SelectedIndex < 0)
        {
            return;
        }

        lock (_configLock)
        {
            _curvePanel.Curve = GetSelectedCurve();
        }

        _curvePanel.Invalidate();
    }

    private void CopySelectedCurve()
    {
        var sourceName =
            _copySourceBox.SelectedItem?.ToString();

        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return;
        }

        var parts = sourceName.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);

        if (parts.Length != 2)
        {
            return;
        }

        lock (_configLock)
        {
            var source = GetCurve(parts[0], parts[1]);
            var target = GetSelectedCurve();

            target.CopyShapeFrom(source);
        }

        RefreshEditor();

        _status.Text =
            $"Copied {sourceName} -> " +
            $"{_deviceBox.SelectedItem}/{_axisBox.SelectedItem}";
    }

    private CurveDefinition GetCurve(
        string deviceName,
        string axisName)
    {
        var device =
            deviceName.Equals(
                "LEFT",
                StringComparison.OrdinalIgnoreCase)
                ? _config.Left
                : _config.Right;

        return axisName switch
        {
            "X" => device.X,
            "Y" => device.Y,
            "Rz" => device.Rz,
            "Slider" => device.Slider,
            _ => device.X
        };
    }

    private CurveDefinition GetSelectedCurve()
    {
        return GetCurve(
            _deviceBox.SelectedItem?.ToString() ?? "LEFT",
            _axisBox.SelectedItem?.ToString() ?? "X");
    }

    private float GetLiveValue(
        string device,
        string axis,
        bool output)
    {
        bool left = device == "LEFT";

        return (left, axis, output) switch
        {
            (true, "X", false) => _leftX,
            (true, "Y", false) => _leftY,
            (true, "Rz", false) => _leftRz,
            (true, "Slider", false) => _leftSlider,

            (true, "X", true) => _leftOutX,
            (true, "Y", true) => _leftOutY,
            (true, "Rz", true) => _leftOutRz,
            (true, "Slider", true) => _leftOutSlider,

            (false, "X", false) => _rightX,
            (false, "Y", false) => _rightY,
            (false, "Rz", false) => _rightRz,
            (false, "Slider", false) => _rightSlider,

            (false, "X", true) => _rightOutX,
            (false, "Y", true) => _rightOutY,
            (false, "Rz", true) => _rightOutRz,
            (false, "Slider", true) => _rightOutSlider,

            _ => 0.5f
        };
    }

    private static float ToGraphValue(float value01, string axis)
    {
        if (axis == "Slider")
        {
            return Math.Clamp(value01, 0f, 1f);
        }

        // X/Y/Rz graph is center -> edge:
        // 0% = stick center, 100% = either physical edge.
        return MathF.Abs((Math.Clamp(value01, 0f, 1f) * 2f) - 1f);
    }

    private static string FormatPercent(float value01, string axis)
    {
        if (axis == "Slider")
        {
            return $"{value01 * 100f:0.0}%";
        }

        return $"{((value01 * 2f) - 1f) * 100f:+0.0;-0.0;0.0}%";
    }

    private enum Side
    {
        Left,
        Right
    }

    private readonly record struct PhysicalState(
        float X,
        float Y,
        float Rz,
        float Slider,
        HMButton Buttons,
        HMHat Hat);
}

public readonly record struct HidHideConfigurationResult(
    bool Success,
    string Message)
{
    public static HidHideConfigurationResult Ok() =>
        new(true, string.Empty);

    public static HidHideConfigurationResult Fail(string message) =>
        new(false, message);
}

public sealed class DeviceMappingConfig
{
    public string LeftDevicePath { get; set; } = string.Empty;
    public string RightDevicePath { get; set; } = string.Empty;
}

public sealed class DeviceAssignmentForm : Form
{
    private readonly IReadOnlyList<HidDevice> _devices;
    private readonly ComboBox _leftBox = new();
    private readonly ComboBox _rightBox = new();
    private readonly Button _saveButton = new();
    private readonly Button _swapButton = new();
    private readonly Button _testLeftButton = new();
    private readonly Button _testRightButton = new();
    private readonly Label _testStatus = new();

    private bool _testRunning;

    public HidDevice? SelectedLeft =>
        _leftBox.SelectedIndex >= 0
            ? _devices[_leftBox.SelectedIndex]
            : null;

    public HidDevice? SelectedRight =>
        _rightBox.SelectedIndex >= 0
            ? _devices[_rightBox.SelectedIndex]
            : null;

    public DeviceAssignmentForm(
        IReadOnlyList<HidDevice> devices,
        DeviceMappingConfig? existing)
    {
        _devices = devices;

        Text = "Assign physical T.16000M devices";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(860, 305);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 3,
            RowCount = 6
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.Controls.Add(
            new Label
            {
                Text = "Choose which physical stick is LEFT and RIGHT.",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 12)
            },
            0,
            0);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 0)!, 3);

        layout.Controls.Add(
            new Label
            {
                Text = "LEFT:",
                AutoSize = true,
                Margin = new Padding(0, 9, 10, 0)
            },
            0,
            1);

        layout.Controls.Add(
            new Label
            {
                Text = "RIGHT:",
                AutoSize = true,
                Margin = new Padding(0, 9, 10, 0)
            },
            0,
            2);

        ConfigureCombo(_leftBox);
        ConfigureCombo(_rightBox);

        layout.Controls.Add(_leftBox, 1, 1);
        layout.Controls.Add(_rightBox, 1, 2);

        _testLeftButton.Text = "Test LEFT";
        _testLeftButton.AutoSize = true;
        _testLeftButton.Margin = new Padding(8, 2, 0, 0);
        _testLeftButton.Click += async (_, _) =>
            await TestSelectedDeviceAsync(_leftBox, "LEFT");

        _testRightButton.Text = "Test RIGHT";
        _testRightButton.AutoSize = true;
        _testRightButton.Margin = new Padding(8, 2, 0, 0);
        _testRightButton.Click += async (_, _) =>
            await TestSelectedDeviceAsync(_rightBox, "RIGHT");

        layout.Controls.Add(_testLeftButton, 2, 1);
        layout.Controls.Add(_testRightButton, 2, 2);

        _testStatus.AutoSize = true;
        _testStatus.Text =
            "Select a candidate, click Test, then move that physical stick.";
        _testStatus.Margin = new Padding(0, 14, 0, 0);

        layout.Controls.Add(_testStatus, 0, 3);
        layout.SetColumnSpan(_testStatus, 3);

        _swapButton.Text = "Swap LEFT / RIGHT";
        _swapButton.AutoSize = true;
        _swapButton.Click += (_, _) =>
        {
            int left = _leftBox.SelectedIndex;
            _leftBox.SelectedIndex = _rightBox.SelectedIndex;
            _rightBox.SelectedIndex = left;
            _testStatus.Text = "LEFT / RIGHT selections swapped.";
            _testStatus.ForeColor = SystemColors.ControlText;
        };

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 14, 0, 0)
        };

        _saveButton.Text = "Save";
        _saveButton.AutoSize = true;
        _saveButton.Click += (_, _) => SaveSelection();

        var cancelButton = new Button
        {
            Text = "Cancel",
            AutoSize = true,
            DialogResult = DialogResult.Cancel
        };

        actions.Controls.Add(_saveButton);
        actions.Controls.Add(cancelButton);
        actions.Controls.Add(_swapButton);

        layout.Controls.Add(actions, 0, 5);
        layout.SetColumnSpan(actions, 3);

        Controls.Add(layout);

        AcceptButton = _saveButton;
        CancelButton = cancelButton;

        SelectExistingOrDefaults(existing);
    }

    private void ConfigureCombo(ComboBox box)
    {
        box.DropDownStyle = ComboBoxStyle.DropDownList;
        box.Dock = DockStyle.Fill;

        foreach (var device in _devices)
        {
            box.Items.Add(FormatDevice(device));
        }
    }

    private void SelectExistingOrDefaults(DeviceMappingConfig? existing)
    {
        int leftIndex = -1;
        int rightIndex = -1;

        if (existing is not null)
        {
            leftIndex = FindIndex(existing.LeftDevicePath);
            rightIndex = FindIndex(existing.RightDevicePath);
        }

        if (leftIndex < 0)
        {
            leftIndex = 0;
        }

        if (rightIndex < 0 || rightIndex == leftIndex)
        {
            rightIndex = _devices.Count > 1
                ? (leftIndex == 0 ? 1 : 0)
                : -1;
        }

        _leftBox.SelectedIndex = leftIndex;
        _rightBox.SelectedIndex = rightIndex;
    }

    private int FindIndex(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return -1;
        }

        for (int i = 0; i < _devices.Count; i++)
        {
            if (string.Equals(
                _devices[i].DevicePath,
                path,
                StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static string FormatDevice(HidDevice device)
    {
        string path = device.DevicePath;
        string product;

        try
        {
            product = device.GetProductName();
        }
        catch
        {
            product = "T.16000M Joystick";
        }

        // Show the unique HID instance section instead of the common GUID tail.
        // Example: 6&abcdef12&0&0000
        string uniquePart = ExtractUniquePathPart(path);

        return $"{product}  |  {uniquePart}";
    }

    private static string ExtractUniquePathPart(string path)
    {
        // Typical path:
        // \\?\hid#vid_044f&pid_b10a#6&abcdef12&0&0000#{GUID}
        var parts = path.Split('#');

        if (parts.Length >= 3 &&
            !string.IsNullOrWhiteSpace(parts[2]))
        {
            return parts[2];
        }

        return path.Length > 64
            ? "..." + path[^64..]
            : path;
    }

    private async Task TestSelectedDeviceAsync(
        ComboBox sourceBox,
        string sideName)
    {
        if (_testRunning)
        {
            return;
        }

        if (sourceBox.SelectedIndex < 0)
        {
            MessageBox.Show(
                this,
                $"Select a candidate device for {sideName} first.",
                "T16Bridge",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var device = _devices[sourceBox.SelectedIndex];

        _testRunning = true;
        SetTestControlsEnabled(false);

        try
        {
            _testStatus.ForeColor = SystemColors.ControlText;
            _testStatus.Text =
                $"{sideName}: move the selected physical stick now...";

            bool detected =
                await Task.Run(() => WaitForInput(device, 5000));

            if (detected)
            {
                _testStatus.ForeColor = Color.DarkGreen;
                _testStatus.Text =
                    $"{sideName}: INPUT DETECTED ✓ — this is the device you moved.";
            }
            else
            {
                _testStatus.ForeColor = Color.DarkRed;
                _testStatus.Text =
                    $"{sideName}: no input detected within 5 seconds.";
            }
        }
        catch (Exception ex)
        {
            _testStatus.ForeColor = Color.DarkRed;
            _testStatus.Text =
                $"{sideName}: test failed — {ex.Message}";
        }
        finally
        {
            _testRunning = false;
            SetTestControlsEnabled(true);
        }
    }

    private void SetTestControlsEnabled(bool enabled)
    {
        _leftBox.Enabled = enabled;
        _rightBox.Enabled = enabled;
        _testLeftButton.Enabled = enabled;
        _testRightButton.Enabled = enabled;
        _saveButton.Enabled = enabled;
        _swapButton.Enabled = enabled;
    }

    private static bool WaitForInput(
        HidDevice device,
        int timeoutMs)
    {
        if (!device.TryOpen(out HidStream? stream) ||
            stream is null)
        {
            throw new InvalidOperationException(
                "Could not open the selected HID device.");
        }

        using (stream)
        {
            stream.ReadTimeout = 250;

            var buffer =
                new byte[device.GetMaxInputReportLength()];

            byte[]? baseline = null;

            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    int count =
                        stream.Read(buffer, 0, buffer.Length);

                    if (count < 10)
                    {
                        continue;
                    }

                    if (baseline is null)
                    {
                        baseline = buffer.Take(count).ToArray();
                        continue;
                    }

                    if (HasMeaningfulInputChange(
                        baseline,
                        buffer,
                        count))
                    {
                        return true;
                    }
                }
                catch (TimeoutException)
                {
                    // Keep waiting until the overall 5-second timeout expires.
                }
            }

            return false;
        }
    }

    private static bool HasMeaningfulInputChange(
        byte[] baseline,
        byte[] current,
        int count)
    {
        int usable =
            Math.Min(
                Math.Min(baseline.Length, current.Length),
                count);

        // Ignore report-id byte 0. Any change in buttons, hat or axes
        // identifies activity from this physical joystick.
        for (int i = 1; i < usable; i++)
        {
            if (baseline[i] != current[i])
            {
                return true;
            }
        }

        return false;
    }

    private void SaveSelection()
    {
        if (_leftBox.SelectedIndex < 0 ||
            _rightBox.SelectedIndex < 0)
        {
            MessageBox.Show(
                this,
                "Select both LEFT and RIGHT devices.",
                "T16Bridge",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (_leftBox.SelectedIndex == _rightBox.SelectedIndex)
        {
            MessageBox.Show(
                this,
                "LEFT and RIGHT must be different physical devices.",
                "T16Bridge",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}

public sealed class CurveEditorPanel : Panel
{
    private CurveDefinition? _curve;
    private int _dragIndex = -1;

    public event EventHandler? CurveChanged;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float LiveInput { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float LiveOutput { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public CurveDefinition? Curve
    {
        get => _curve;
        set
        {
            _curve = value;
            _dragIndex = -1;
            Invalidate();
        }
    }

    public CurveEditorPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(28, 28, 30);
        ForeColor = Color.Gainsboro;
        Cursor = Cursors.Cross;

        MouseDown += OnEditorMouseDown;
        MouseMove += OnEditorMouseMove;
        MouseUp += (_, _) => _dragIndex = -1;
        MouseDoubleClick += OnEditorDoubleClick;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (_curve is null)
        {
            return;
        }

        var g = e.Graphics;
        g.SmoothingMode =
            System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var plot = GetPlotRect();

        using var borderPen =
            new Pen(Color.FromArgb(95, 95, 100), 1f);

        using var gridPen =
            new Pen(Color.FromArgb(55, 55, 60), 1f);

        using var zeroPen =
            new Pen(Color.FromArgb(105, 105, 110), 1.2f);

        using var linearPen =
            new Pen(Color.FromArgb(90, 90, 95), 1f)
            {
                DashStyle =
                    System.Drawing.Drawing2D.DashStyle.Dash
            };

        using var curvePen =
            new Pen(Color.DeepSkyBlue, 2.5f);

        using var livePen =
            new Pen(Color.Orange, 1.5f);

        using var liveBrush =
            new SolidBrush(Color.Orange);

        using var pointBrush =
            new SolidBrush(Color.WhiteSmoke);

        using var pointBorder =
            new Pen(Color.DeepSkyBlue, 2f);

        g.DrawRectangle(
            borderPen,
            plot.X,
            plot.Y,
            plot.Width,
            plot.Height);

        for (int i = 1; i < 4; i++)
        {
            float x =
                plot.Left + (plot.Width * i / 4f);

            float y =
                plot.Top + (plot.Height * i / 4f);

            g.DrawLine(
                gridPen,
                x,
                plot.Top,
                x,
                plot.Bottom);

            g.DrawLine(
                gridPen,
                plot.Left,
                y,
                plot.Right,
                y);
        }

        var linearStart =
            WorldToScreen(0f, 0f, plot);

        var linearEnd =
            WorldToScreen(1f, 1f, plot);

        g.DrawLine(
            linearPen,
            linearStart,
            linearEnd);

        var points =
            _curve.Points
                .OrderBy(p => p.X)
                .Select(p =>
                    WorldToScreen(
                        p.X,
                        p.Y,
                        plot))
                .ToArray();

        if (points.Length >= 2)
        {
            g.DrawLines(curvePen, points);
        }

        for (int i = 0; i < points.Length; i++)
        {
            var p = points[i];
            const float radius = 6f;

            g.FillEllipse(
                pointBrush,
                p.X - radius,
                p.Y - radius,
                radius * 2,
                radius * 2);

            g.DrawEllipse(
                pointBorder,
                p.X - radius,
                p.Y - radius,
                radius * 2,
                radius * 2);
        }

        var live =
            WorldToScreen(
                LiveInput,
                LiveOutput,
                plot);

        g.DrawLine(
            livePen,
            live.X,
            plot.Top,
            live.X,
            plot.Bottom);

        g.FillEllipse(
            liveBrush,
            live.X - 5,
            live.Y - 5,
            10,
            10);

        DrawLabels(g, plot);
    }

    private void DrawLabels(Graphics g, RectangleF plot)
    {
        if (_curve is null)
        {
            return;
        }

        using var brush =
            new SolidBrush(Color.Silver);

        using var font =
            new Font(Font.FontFamily, 9f);

        g.DrawString(
            "0%",
            font,
            brush,
            plot.Left - 4,
            plot.Bottom + 8);

        g.DrawString(
            "100%",
            font,
            brush,
            plot.Right - 38,
            plot.Bottom + 8);

        g.DrawString(
            "Input",
            font,
            brush,
            plot.Left + plot.Width / 2f - 18,
            plot.Bottom + 28);

        g.DrawString(
            "Output",
            font,
            brush,
            plot.Left - 48,
            plot.Top - 2);
    }

    private RectangleF GetPlotRect()
    {
        return new RectangleF(
            65,
            25,
            Math.Max(100, ClientSize.Width - 95),
            Math.Max(100, ClientSize.Height - 85));
    }

    private void OnEditorMouseDown(
        object? sender,
        MouseEventArgs e)
    {
        if (_curve is null ||
            e.Button != MouseButtons.Left)
        {
            return;
        }

        var plot = GetPlotRect();

        for (int i = 0; i < _curve.Points.Count; i++)
        {
            // Endpoints are fixed.
            if (i == 0 ||
                i == _curve.Points.Count - 1)
            {
                continue;
            }

            var p =
                WorldToScreen(
                    _curve.Points[i].X,
                    _curve.Points[i].Y,
                    plot);

            float dx = e.X - p.X;
            float dy = e.Y - p.Y;

            if ((dx * dx) + (dy * dy) <= 100)
            {
                _dragIndex = i;
                return;
            }
        }
    }

    private void OnEditorMouseMove(
        object? sender,
        MouseEventArgs e)
    {
        if (_curve is null ||
            _dragIndex < 0 ||
            e.Button != MouseButtons.Left)
        {
            return;
        }

        var plot = GetPlotRect();

        var world =
            ScreenToWorld(
                e.Location,
                plot);

        var points =
            _curve.Points
                .OrderBy(p => p.X)
                .ToList();

        float leftX =
            points[_dragIndex - 1].X + 0.01f;

        float rightX =
            points[_dragIndex + 1].X - 0.01f;

        points[_dragIndex].X =
            Math.Clamp(world.X, leftX, rightX);

        points[_dragIndex].Y =
            Math.Clamp(world.Y, 0f, 1f);

        _curve.Points = points;

        CurveChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void OnEditorDoubleClick(
        object? sender,
        MouseEventArgs e)
    {
        if (_curve is null ||
            e.Button != MouseButtons.Left)
        {
            return;
        }

        var plot = GetPlotRect();

        if (!plot.Contains(e.Location))
        {
            return;
        }

        var world =
            ScreenToWorld(
                e.Location,
                plot);

        _curve.AddPoint(world.X, world.Y);

        CurveChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private static PointF WorldToScreen(
        float x,
        float y,
        RectangleF plot)
    {
        x = Math.Clamp(x, 0f, 1f);
        y = Math.Clamp(y, 0f, 1f);

        return new PointF(
            plot.Left + x * plot.Width,
            plot.Bottom - y * plot.Height);
    }

    private static PointF ScreenToWorld(
        Point p,
        RectangleF plot)
    {
        float x =
            Math.Clamp(
                (p.X - plot.Left) / plot.Width,
                0f,
                1f);

        float y =
            Math.Clamp(
                (plot.Bottom - p.Y) / plot.Height,
                0f,
                1f);

        return new PointF(x, y);
    }
}

public sealed class AppCurveConfig
{
    public DeviceCurveConfig Left { get; set; } =
        DeviceCurveConfig.CreateDefault();

    public DeviceCurveConfig Right { get; set; } =
        DeviceCurveConfig.CreateDefault();

    public static AppCurveConfig CreateDefault()
    {
        return new AppCurveConfig
        {
            Left = DeviceCurveConfig.CreateDefault(),
            Right = DeviceCurveConfig.CreateDefault()
        };
    }

    public void Normalize()
    {
        Left ??= DeviceCurveConfig.CreateDefault();
        Right ??= DeviceCurveConfig.CreateDefault();

        Left.Normalize();
        Right.Normalize();
    }
}

public sealed class DeviceCurveConfig
{
    public CurveDefinition X { get; set; } =
        CurveDefinition.CreateLinear(true);

    public CurveDefinition Y { get; set; } =
        CurveDefinition.CreateLinear(true);

    public CurveDefinition Rz { get; set; } =
        CurveDefinition.CreateLinear(true);

    public CurveDefinition Slider { get; set; } =
        CurveDefinition.CreateLinear(false);

    public static DeviceCurveConfig CreateDefault()
    {
        return new DeviceCurveConfig
        {
            X = CurveDefinition.CreateLinear(true),
            Y = CurveDefinition.CreateLinear(true),
            Rz = CurveDefinition.CreateLinear(true),
            Slider = CurveDefinition.CreateLinear(false)
        };
    }

    public void Normalize()
    {
        X ??= CurveDefinition.CreateLinear(true);
        Y ??= CurveDefinition.CreateLinear(true);
        Rz ??= CurveDefinition.CreateLinear(true);
        Slider ??= CurveDefinition.CreateLinear(false);

        X.Centered = true;
        Y.Centered = true;
        Rz.Centered = true;
        Slider.Centered = false;

        X.Normalize();
        Y.Normalize();
        Rz.Normalize();
        Slider.Normalize();
    }
}

public sealed class CurveDefinition
{
    // Centered=true for X/Y/Rz. The editable curve itself is still always
    // 0..1: 0 = physical center, 1 = either edge.
    public bool Centered { get; set; }

    public List<CurvePoint> Points { get; set; } = [];

    public static CurveDefinition CreateLinear(bool centered)
    {
        return new CurveDefinition
        {
            Centered = centered,
            Points =
            [
                new(0f, 0f),
                new(0.25f, 0.25f),
                new(0.5f, 0.5f),
                new(0.75f, 0.75f),
                new(1f, 1f)
            ]
        };
    }

    public void CopyShapeFrom(CurveDefinition source)
    {
        source.Normalize();

        // All curves now use the same 0..1 graph domain, so a shape can be
        // copied directly between X/Y/Rz and Slider.
        Points =
            source.Points
                .Select(p => new CurvePoint(p.X, p.Y))
                .ToList();

        Normalize();
    }

    public float Apply01(float input01)
    {
        input01 = Math.Clamp(input01, 0f, 1f);

        if (!Centered)
        {
            return Math.Clamp(Evaluate(input01), 0f, 1f);
        }

        // HIDMaestro centered axes arrive as 0..1 where 0.5 is center.
        // Apply the same magnitude curve to both sides and restore the sign.
        float signedInput = (input01 * 2f) - 1f;
        float magnitude = MathF.Abs(signedInput);
        float curvedMagnitude = Math.Clamp(Evaluate(magnitude), 0f, 1f);

        float signedOutput =
            signedInput < 0f
                ? -curvedMagnitude
                : signedInput > 0f
                    ? curvedMagnitude
                    : 0f;

        return Math.Clamp((signedOutput + 1f) / 2f, 0f, 1f);
    }

    public float Evaluate(float x)
    {
        Normalize();
        x = Math.Clamp(x, 0f, 1f);

        if (Points.Count == 0)
        {
            return x;
        }

        if (x <= Points[0].X)
        {
            return Points[0].Y;
        }

        if (x >= Points[^1].X)
        {
            return Points[^1].Y;
        }

        for (int i = 0; i < Points.Count - 1; i++)
        {
            var a = Points[i];
            var b = Points[i + 1];

            if (x < a.X || x > b.X)
            {
                continue;
            }

            float width = b.X - a.X;

            if (Math.Abs(width) < 0.000001f)
            {
                return b.Y;
            }

            float t = (x - a.X) / width;
            return a.Y + ((b.Y - a.Y) * t);
        }

        return x;
    }

    public void AddPoint(float x, float y)
    {
        Normalize();

        x = Math.Clamp(x, 0f, 1f);
        y = Math.Clamp(y, 0f, 1f);

        if (Points.Any(p => Math.Abs(p.X - x) < 0.01f))
        {
            return;
        }

        Points.Add(new CurvePoint(x, y));
        Normalize();
    }

    public void Normalize()
    {
        Points ??= [];

        // Migration from the previous centered format (-1..1):
        // keep the positive half because the new curve represents
        // center -> either edge and is mirrored automatically.
        if (Centered &&
            Points.Any(p => p.X < 0f || p.Y < 0f))
        {
            Points =
                Points
                    .Where(p => p.X >= -0.0001f)
                    .Select(p => new CurvePoint(
                        Math.Clamp(p.X, 0f, 1f),
                        Math.Clamp(p.Y, 0f, 1f)))
                    .ToList();
        }

        foreach (var p in Points)
        {
            p.X = Math.Clamp(p.X, 0f, 1f);
            p.Y = Math.Clamp(p.Y, 0f, 1f);
        }

        Points =
            Points
                .OrderBy(p => p.X)
                .ToList();

        if (Points.Count < 2)
        {
            Points = CreateLinear(Centered).Points;
            return;
        }

        // Fixed endpoints: center is always 0%, edge is always 100%.
        if (Points[0].X > 0.0001f)
        {
            Points.Insert(0, new CurvePoint(0f, 0f));
        }
        else
        {
            Points[0].X = 0f;
            Points[0].Y = 0f;
        }

        if (Points[^1].X < 0.9999f)
        {
            Points.Add(new CurvePoint(1f, 1f));
        }
        else
        {
            Points[^1].X = 1f;
            Points[^1].Y = 1f;
        }
    }
}

public sealed class CurvePoint
{
    public float X { get; set; }
    public float Y { get; set; }

    public CurvePoint()
    {
    }

    public CurvePoint(float x, float y)
    {
        X = x;
        Y = y;
    }
}
