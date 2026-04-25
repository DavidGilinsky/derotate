using System.Drawing;

namespace Derotate;

internal sealed class MainForm : Form
{
    private readonly MountClient _mount = new();
    private readonly RotatorClient _rotator = new();
    private readonly FocuserClient _focuser = new();
    private readonly CameraClient _mainCamera = new();
    private readonly CameraClient _guideCamera = new();
    private readonly DerotationEngine _engine;

    private AppSettings _settings;

    // Top-of-form global controls
    private readonly Button _connectAllBtn;
    private readonly Button _settingsBtn;

    // Per-device connection controls
    private readonly Button _connectMountBtn;
    private readonly Button _disconnectMountBtn;
    private readonly Label _mountStatusLbl;

    private readonly Button _connectRotatorBtn;
    private readonly Button _disconnectRotatorBtn;
    private readonly Label _rotatorStatusLbl;

    private readonly Button _connectFocuserBtn;
    private readonly Button _disconnectFocuserBtn;
    private readonly Label _focuserStatusLbl;

    private readonly Button _connectMainCamBtn;
    private readonly Button _disconnectMainCamBtn;
    private readonly Label _mainCameraStatusLbl;

    private readonly Button _connectGuideCamBtn;
    private readonly Button _disconnectGuideCamBtn;
    private readonly Label _guideCameraStatusLbl;

    /// <summary>
    /// Index of all device wrappers paired with their settings ProgID and
    /// their per-device buttons. Used for Connect/Disconnect All so the bulk
    /// handler doesn't grow a wall of repeated per-device branches.
    /// </summary>
    private readonly List<DeviceEntry> _devices;

    // Live readout (value labels; their captions are added inside MakeReadout)
    private readonly Label _altLbl;
    private readonly Label _azLbl;
    private readonly Label _utcLbl;
    private readonly Label _latLbl;
    private readonly Label _rotatorPosLbl;
    private readonly Label _targetPaLbl;
    private readonly Label _lastDeltaLbl;
    private readonly Label _iterationLbl;

    // Derotation controls
    private readonly Button _setReferenceBtn;
    private readonly Label _referencePaLbl;
    private readonly Button _startBtn;
    private readonly Button _stopBtn;
    private readonly Button _haltBtn;

    // Plate-solve controls
    private readonly ComboBox _solveCameraCombo;
    private readonly NumericUpDown _solveExposureNum;
    private readonly Button _solveBtn;
    private readonly Label _solveResultLbl;

    // Log
    private readonly TextBox _logBox;

    // 1 Hz UI status poll; ticks on the UI thread.
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 1000 };

    // Last logged exception message per device, so 1 Hz error spam to the
    // log box only fires when the underlying error message *changes*.
    private string _focuserStatusError = "";

    public MainForm()
    {
        Text = "Alt-Az Field Derotator";
        ClientSize = new Size(720, 808);
        StartPosition = FormStartPosition.CenterScreen;

        _engine = new DerotationEngine(_mount, _rotator);
        _settings = SettingsStore.Load();

        const int x = 12;
        int y = 12;

        // -- Row 0: global session controls --
        _connectAllBtn = new Button
        {
            Text = "Connect All Equipment",
            Left = x,
            Top = y,
            Width = 240,
            Enabled = false,
        };
        _settingsBtn = new Button
        {
            Text = "Settings…",
            Left = ClientSize.Width - 12 - 100,
            Top = y,
            Width = 100,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        y += 32;

        // -- Per-device rows --
        _connectMountBtn = new Button { Text = "Connect Mount", Left = x, Top = y, Width = 130 };
        _disconnectMountBtn = new Button { Text = "Disconnect Mount", Left = x + 140, Top = y, Width = 130, Enabled = false };
        _mountStatusLbl = new Label { Text = "Mount: Not Connected", Left = x + 280, Top = y + 4, Width = 400, AutoSize = false };
        y += 32;

        _connectRotatorBtn = new Button { Text = "Connect Rotator", Left = x, Top = y, Width = 130 };
        _disconnectRotatorBtn = new Button { Text = "Disconnect Rotator", Left = x + 140, Top = y, Width = 130, Enabled = false };
        _rotatorStatusLbl = new Label { Text = "Rotator: Not Connected", Left = x + 280, Top = y + 4, Width = 400, AutoSize = false };
        y += 32;

        _connectFocuserBtn = new Button { Text = "Connect Focuser", Left = x, Top = y, Width = 130 };
        _disconnectFocuserBtn = new Button { Text = "Disconnect Focuser", Left = x + 140, Top = y, Width = 130, Enabled = false };
        _focuserStatusLbl = new Label { Text = "Focuser: Not Connected", Left = x + 280, Top = y + 4, Width = 400, AutoSize = false };
        y += 32;

        _connectMainCamBtn = new Button { Text = "Connect Main", Left = x, Top = y, Width = 130 };
        _disconnectMainCamBtn = new Button { Text = "Disconnect Main", Left = x + 140, Top = y, Width = 130, Enabled = false };
        _mainCameraStatusLbl = new Label { Text = "Main camera: Not Connected", Left = x + 280, Top = y + 4, Width = 400, AutoSize = false };
        y += 32;

        _connectGuideCamBtn = new Button { Text = "Connect Guide", Left = x, Top = y, Width = 130 };
        _disconnectGuideCamBtn = new Button { Text = "Disconnect Guide", Left = x + 140, Top = y, Width = 130, Enabled = false };
        _guideCameraStatusLbl = new Label { Text = "Guide camera: Not Connected", Left = x + 280, Top = y + 4, Width = 400, AutoSize = false };
        y += 40;

        _altLbl         = MakeReadout("Alt:",          x, ref y);
        _azLbl          = MakeReadout("Az:",           x, ref y);
        _utcLbl         = MakeReadout("UTC:",          x, ref y);
        _latLbl         = MakeReadout("Lat:",          x, ref y);
        _rotatorPosLbl  = MakeReadout("Rotator pos:",  x, ref y);
        _targetPaLbl    = MakeReadout("Target PA:",    x, ref y);
        _lastDeltaLbl   = MakeReadout("Last delta:",   x, ref y);
        _iterationLbl   = MakeReadout("Iteration:",    x, ref y);
        y += 8;

        _setReferenceBtn = new Button { Text = "Set Reference PA", Left = x, Top = y, Width = 160 };
        _referencePaLbl = new Label { Text = "Reference PA: (unset)", Left = x + 170, Top = y + 4, Width = 400, AutoSize = false };
        y += 40;

        // -- Plate-solve row: Solve button | Camera selector | Exposure | Result --
        _solveBtn = new Button { Text = "Solve", Left = x, Top = y, Width = 90 };

        Controls.Add(new Label { Text = "Camera:", Left = x + 100, Top = y + 4, Width = 60, AutoSize = false });
        _solveCameraCombo = new ComboBox
        {
            Left = x + 160, Top = y, Width = 80,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _solveCameraCombo.Items.AddRange(new object[] { "Main", "Guide" });
        _solveCameraCombo.SelectedIndex = 0;

        Controls.Add(new Label { Text = "Exposure (s):", Left = x + 250, Top = y + 4, Width = 90, AutoSize = false });
        _solveExposureNum = new NumericUpDown
        {
            Left = x + 340, Top = y, Width = 70,
            Minimum = 0.1m, Maximum = 600m,
            DecimalPlaces = 1, Increment = 1m,
            Value = 5m,
        };

        _solveResultLbl = new Label
        {
            Text = "Solved PA: —",
            Left = x + 420,
            Top = y + 4,
            Width = 276,
            AutoSize = false,
        };
        y += 40;

        _startBtn = new Button { Text = "Start Derotation", Left = x, Top = y, Width = 140 };
        _stopBtn = new Button { Text = "Stop Derotation", Left = x + 150, Top = y, Width = 140, Enabled = false };
        _haltBtn = new Button { Text = "HALT ROTATOR", Left = x + 300, Top = y, Width = 160, BackColor = Color.MistyRose };
        y += 40;

        _logBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Left = x,
            Top = y,
            Width = ClientSize.Width - 2 * x,
            Height = ClientSize.Height - y - 12,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom,
            Font = new Font("Consolas", 9f),
        };

        Controls.AddRange(new Control[]
        {
            _connectAllBtn, _settingsBtn,
            _connectMountBtn, _disconnectMountBtn, _mountStatusLbl,
            _connectRotatorBtn, _disconnectRotatorBtn, _rotatorStatusLbl,
            _connectFocuserBtn, _disconnectFocuserBtn, _focuserStatusLbl,
            _connectMainCamBtn, _disconnectMainCamBtn, _mainCameraStatusLbl,
            _connectGuideCamBtn, _disconnectGuideCamBtn, _guideCameraStatusLbl,
            _altLbl, _azLbl, _utcLbl, _latLbl,
            _rotatorPosLbl, _targetPaLbl, _lastDeltaLbl, _iterationLbl,
            _setReferenceBtn, _referencePaLbl,
            _solveCameraCombo, _solveExposureNum, _solveBtn, _solveResultLbl,
            _startBtn, _stopBtn, _haltBtn,
            _logBox,
        });

        // Build the device index after all controls and clients are constructed.
        _devices = new List<DeviceEntry>
        {
            new("mount",        () => _settings.MountProgId,        _mount,       _connectMountBtn,     _disconnectMountBtn),
            new("rotator",      () => _settings.RotatorProgId,      _rotator,     _connectRotatorBtn,   _disconnectRotatorBtn),
            new("focuser",      () => _settings.FocuserProgId,      _focuser,     _connectFocuserBtn,   _disconnectFocuserBtn),
            new("main camera",  () => _settings.MainCameraProgId,   _mainCamera,  _connectMainCamBtn,   _disconnectMainCamBtn),
            new("guide camera", () => _settings.GuideCameraProgId,  _guideCamera, _connectGuideCamBtn,  _disconnectGuideCamBtn),
        };

        WireHandlers();
        RefreshConnectionUI();

        _statusTimer.Tick += (_, _) => UpdateStatusLabels();
        _statusTimer.Start();
    }

    private void OnSettingsClicked(object? sender, EventArgs e)
    {
        using var dlg = new SettingsForm(_settings);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result is null)
            return;

        _settings = dlg.Result;
        try
        {
            SettingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Settings were updated in memory but could not be written to {SettingsStore.FilePath}:\n\n{ex.Message}",
                "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        RefreshConnectionUI();
    }

    // -- Per-device connect/disconnect handlers --

    private async void OnConnectMountClicked(object? sender, EventArgs e) =>
        await TryConnectDeviceAsync("mount", _settings.MountProgId, _connectMountBtn, _mount.ConnectAsync);

    private async void OnDisconnectMountClicked(object? sender, EventArgs e) =>
        await TryDisconnectDeviceAsync("mount", _disconnectMountBtn, _mount.DisconnectAsync);

    private async void OnConnectRotatorClicked(object? sender, EventArgs e) =>
        await TryConnectDeviceAsync("rotator", _settings.RotatorProgId, _connectRotatorBtn, _rotator.ConnectAsync);

    private async void OnDisconnectRotatorClicked(object? sender, EventArgs e) =>
        await TryDisconnectDeviceAsync("rotator", _disconnectRotatorBtn, _rotator.DisconnectAsync);

    private async void OnConnectFocuserClicked(object? sender, EventArgs e) =>
        await TryConnectDeviceAsync("focuser", _settings.FocuserProgId, _connectFocuserBtn, _focuser.ConnectAsync);

    private async void OnDisconnectFocuserClicked(object? sender, EventArgs e) =>
        await TryDisconnectDeviceAsync("focuser", _disconnectFocuserBtn, _focuser.DisconnectAsync);

    private async void OnConnectMainCamClicked(object? sender, EventArgs e) =>
        await TryConnectDeviceAsync("main camera", _settings.MainCameraProgId, _connectMainCamBtn, _mainCamera.ConnectAsync);

    private async void OnDisconnectMainCamClicked(object? sender, EventArgs e) =>
        await TryDisconnectDeviceAsync("main camera", _disconnectMainCamBtn, _mainCamera.DisconnectAsync);

    private async void OnConnectGuideCamClicked(object? sender, EventArgs e) =>
        await TryConnectDeviceAsync("guide camera", _settings.GuideCameraProgId, _connectGuideCamBtn, _guideCamera.ConnectAsync);

    private async void OnDisconnectGuideCamClicked(object? sender, EventArgs e) =>
        await TryDisconnectDeviceAsync("guide camera", _disconnectGuideCamBtn, _guideCamera.DisconnectAsync);

    // -- Plate solve --

    /// <summary>
    /// Capture a single frame from the selected camera, write it as FITS into
    /// a "plate-solves" subfolder of the configured image directory, run
    /// ASTAP, and report the rotation angle from the resulting WCS. Disables
    /// the Solve button while in flight to prevent re-entry.
    /// </summary>
    private async void OnSolveClicked(object? sender, EventArgs e)
    {
        var cameraName = (_solveCameraCombo.SelectedItem as string) ?? "Main";
        var camera = cameraName == "Guide" ? _guideCamera : _mainCamera;

        _solveBtn.Enabled = false;
        var outcome = await RunSolvePipelineAsync(camera, cameraName.ToLowerInvariant(),
            (double)_solveExposureNum.Value);

        // Feed the engine: it'll hold this snapshot until Set Reference PA is
        // pressed. Skip if mount or rotator wasn't connected at capture time
        // — without those we can't form a usable reference.
        if (outcome is { } o && o.MountState is { } ms && o.RotatorPosDeg is { } rp)
            _engine.NotifyManualSolveResult(o.Result.PositionAngleDeg, o.CaptureStart, ms, rp);

        UpdateSolveButton();
    }

    /// <summary>
    /// Shared capture → save → plate-solve pipeline. Used by both the manual
    /// Solve button and the Start-Derotation auto-bootstrap. Returns the
    /// solve result plus the mount/rotator snapshot taken at capture start
    /// (the latter two are null if the device wasn't connected). All status
    /// updates flow through <see cref="SetSolveStatus"/> + <see cref="AppendLog"/>;
    /// no MessageBox.
    /// </summary>
    private async Task<SolveOutcome?> RunSolvePipelineAsync(
        CameraClient camera, string cameraNameLower, double exposureSec)
    {
        if (!ValidateSolvePrereqs(camera, cameraNameLower)) return null;

        var captureDir = Path.Combine(_settings.ImageWatchDirectory, "plate-solves");
        try { Directory.CreateDirectory(captureDir); }
        catch (Exception ex)
        {
            SetSolveStatus("capture directory error");
            AppendLog($"Could not create {captureDir}: {ex.Message}");
            return null;
        }

        SetSolveStatus("solving…");

        // Snapshot mount + rotator at capture start so the engine can build a
        // proper reference frame later. The capture itself runs at this UTC.
        var captureStart = DateTime.UtcNow;
        MountState? mountAtCapture = _mount.IsConnected ? _mount.ReadState() : null;
        double? rotPosAtCapture = _rotator.IsConnected ? _rotator.ReadStatus().PositionDeg : null;

        var fitsPath = Path.Combine(captureDir,
            $"{cameraNameLower}-{captureStart:yyyyMMddTHHmmss}.fits");
        AppendLog($"Capturing {exposureSec:F1}s exposure on {cameraNameLower} camera…");

        try
        {
            var image = await camera.CaptureAsync(exposureSec);
            AppendLog($"Capture complete: {image.GetLength(0)}×{image.GetLength(1)} px.");

            // FITS write on the thread pool — large sensors produce ~70 MB
            // files, sync write on the UI thread would freeze briefly.
            await Task.Run(() => FitsWriter.WriteMono(fitsPath, image, exposureSec, captureStart));
            AppendLog($"Saved {fitsPath}");

            AppendLog("Plate-solving with ASTAP…");
            var result = await PlateSolver.SolveAsync(
                _settings.AstapPath, fitsPath,
                onLine: line => AppendLog($"ASTAP: {line}"));

            SetSolveStatus($"{result.PositionAngleDeg:F2}°");
            AppendLog(
                $"Solved: PA={result.PositionAngleDeg:F3}° " +
                $"RA={result.CenterRaDeg:F4}° Dec={result.CenterDecDeg:F4}° " +
                $"scale={result.ScaleArcsecPerPixel:F2}\"/px");
            return new SolveOutcome(result, captureStart, mountAtCapture, rotPosAtCapture);
        }
        catch (Exception ex)
        {
            SetSolveStatus("failed");
            AppendLog($"Solve failed: {ex.Message}");
            return null;
        }
    }

    private bool ValidateSolvePrereqs(CameraClient camera, string cameraNameLower)
    {
        if (!camera.IsConnected)
        {
            SetSolveStatus($"{cameraNameLower} camera not connected");
            return false;
        }
        if (string.IsNullOrEmpty(_settings.AstapPath) || !File.Exists(_settings.AstapPath))
        {
            SetSolveStatus("ASTAP not configured");
            return false;
        }
        if (string.IsNullOrEmpty(_settings.ImageWatchDirectory) ||
            !Directory.Exists(_settings.ImageWatchDirectory))
        {
            SetSolveStatus("image directory not configured");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Update the Solve result label with an in-place status string. The
    /// label's "Solved PA:" prefix is kept so the user knows which control
    /// they're reading; the value half is replaced with <paramref name="status"/>.
    /// </summary>
    private void SetSolveStatus(string status) =>
        _solveResultLbl.Text = $"Solved PA: {status}";

    private readonly record struct SolveOutcome(
        PlateSolveResult Result,
        DateTime CaptureStart,
        MountState? MountState,
        double? RotatorPosDeg);

    // -- Derotation engine controls --

    private void OnSetReferenceClicked(object? sender, EventArgs e)
    {
        try
        {
            _engine.SetReferenceFromManualSolve();
            var pa = _engine.ReferencePaDeg ?? 0;
            _referencePaLbl.Text = $"Reference PA: {pa:F2}°";
            AppendLog($"Reference PA set to {pa:F2}° from latest solve.");
        }
        catch (InvalidOperationException ex)
        {
            _referencePaLbl.Text = "Reference PA: solve first";
            AppendLog($"Set Reference PA failed: {ex.Message}");
        }
    }

    private async void OnStartDerotationClicked(object? sender, EventArgs e)
    {
        if (_engine.IsRunning) return;

        if (!_mount.IsConnected)   { AppendLog("Cannot start: mount not connected.");   return; }
        if (!_rotator.IsConnected) { AppendLog("Cannot start: rotator not connected."); return; }

        if (!_engine.HasReference)
        {
            // Auto-bootstrap rules (per design): only if guide cam is connected
            // AND it rotates with the main camera. Otherwise refuse — the user
            // needs to do a manual Solve and Set Reference PA first.
            if (_guideCamera.IsConnected && _settings.GuideCameraRotatesWithMain)
            {
                AppendLog("No reference set; bootstrapping from guide camera…");
                _startBtn.Enabled = false;
                var outcome = await RunSolvePipelineAsync(_guideCamera, "guide",
                    (double)_solveExposureNum.Value);
                if (outcome is not { } o ||
                    o.MountState is not { } ms ||
                    o.RotatorPosDeg is not { } rp)
                {
                    AppendLog("Bootstrap failed; engine not started.");
                    _startBtn.Enabled = true;
                    return;
                }
                _engine.SetReferenceFromValues(o.Result.PositionAngleDeg, o.CaptureStart, ms, rp);
                _referencePaLbl.Text = $"Reference PA: {o.Result.PositionAngleDeg:F2}°";
                AppendLog($"Reference PA set to {o.Result.PositionAngleDeg:F2}° from guide bootstrap.");
            }
            else
            {
                string why = _guideCamera.IsConnected
                    ? "guide camera not configured to rotate with main camera"
                    : "guide camera not connected";
                AppendLog($"Cannot start: no reference set, and {why}. " +
                    "Solve manually and Set Reference PA, or connect the guide camera.");
                return;
            }
        }

        try
        {
            _engine.IntervalMs = _settings.MountPollingIntervalMs;
            _engine.MoveIntervalMs = _settings.RotatorMoveIntervalMs;
            await _engine.StartAsync();
            _startBtn.Enabled = false;
            _stopBtn.Enabled = true;
            _setReferenceBtn.Enabled = false;
            AppendLog($"Derotation engine started (poll {_engine.IntervalMs} ms, " +
                $"move gate {_engine.MoveIntervalMs} ms).");
        }
        catch (Exception ex)
        {
            AppendLog($"Start failed: {ex.Message}");
            _startBtn.Enabled = true;
            _stopBtn.Enabled = false;
            _setReferenceBtn.Enabled = true;
        }
    }

    private async void OnStopDerotationClicked(object? sender, EventArgs e)
    {
        _stopBtn.Enabled = false;
        try
        {
            await _engine.StopAsync();
            AppendLog("Derotation engine stopped.");
        }
        catch (Exception ex)
        {
            AppendLog($"Stop error: {ex.Message}");
        }
        _startBtn.Enabled = true;
        _setReferenceBtn.Enabled = true;
    }

    private void OnHaltClicked(object? sender, EventArgs e)
    {
        if (!_rotator.IsConnected) { AppendLog("HALT: rotator not connected."); return; }
        try
        {
            _rotator.Halt();
            AppendLog("HALT issued to rotator.");
        }
        catch (Exception ex)
        {
            AppendLog($"HALT error: {ex.Message}");
        }
    }

    private void OnEngineTick(object? sender, DerotationTick t)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnEngineTick(sender, t));
            return;
        }
        _altLbl.Text        = $"{t.AltitudeDeg:F2}°";
        _azLbl.Text         = $"{t.AzimuthDeg:F2}°";
        _utcLbl.Text        = t.Utc.ToString("yyyy-MM-dd HH:mm:ss");
        _latLbl.Text        = $"{t.SiteLatitudeDeg:F2}°";
        _rotatorPosLbl.Text = $"{t.RotatorPositionDeg:F2}°";
        _targetPaLbl.Text   = t.ZenithSuspended
            ? $"{t.TargetPositionDeg:F2}° (suspended)"
            : $"{t.TargetPositionDeg:F2}°";
        _lastDeltaLbl.Text  = $"{t.DeltaAppliedDeg:+0.00°;-0.00°;0.00°}";
        _iterationLbl.Text  = t.Iteration.ToString();
    }

    private void OnEngineLogMessage(object? sender, string message) => AppendLog(message);

    private void UpdateSolveButton()
    {
        bool anyCameraConnected = _mainCamera.IsConnected || _guideCamera.IsConnected;
        bool astapConfigured = !string.IsNullOrEmpty(_settings.AstapPath);
        bool imageDirConfigured = !string.IsNullOrEmpty(_settings.ImageWatchDirectory);
        _solveBtn.Enabled = anyCameraConnected && astapConfigured && imageDirConfigured;
    }

    // -- Connect/Disconnect All --

    private async void OnConnectAllClicked(object? sender, EventArgs e)
    {
        // Snapshot the action up-front so a state flip mid-loop doesn't switch
        // us from connecting to disconnecting halfway through.
        bool connecting = AnyConfiguredDisconnected();
        _connectAllBtn.Enabled = false;
        AppendLog(connecting ? "Connecting all configured equipment…" : "Disconnecting all equipment…");

        foreach (var d in _devices)
        {
            if (connecting)
            {
                var progId = d.GetProgId();
                if (string.IsNullOrEmpty(progId) || d.Client.IsConnected) continue;
                await TryConnectDeviceAsync(d.Name, progId, d.ConnectBtn, d.Client.ConnectAsync);
            }
            else
            {
                if (!d.Client.IsConnected) continue;
                await TryDisconnectDeviceAsync(d.Name, d.DisconnectBtn, d.Client.DisconnectAsync);
            }
        }

        AppendLog(connecting ? "Connect All complete." : "Disconnect All complete.");
        RefreshConnectionUI();
    }

    private bool AnyConfigured() =>
        _devices.Any(d => !string.IsNullOrEmpty(d.GetProgId()));

    private bool AnyConfiguredDisconnected() =>
        _devices.Any(d => !string.IsNullOrEmpty(d.GetProgId()) && !d.Client.IsConnected);

    /// <summary>
    /// Shared "Connect" flow: validate ProgID, log, await the device-specific
    /// connector, surface errors, refresh UI. <paramref name="deviceName"/> is
    /// the lowercase noun used in messages ("mount", "main camera", ...).
    /// </summary>
    private async Task TryConnectDeviceAsync(
        string deviceName,
        string progId,
        Button connectBtn,
        Func<string, CancellationToken, Task> connector)
    {
        if (string.IsNullOrEmpty(progId))
        {
            MessageBox.Show(this, $"Configure a {deviceName} driver in Settings first.",
                $"Connect {Capitalize(deviceName)}", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        connectBtn.Enabled = false;
        AppendLog($"Connecting to {deviceName}: {progId}…");
        try
        {
            await connector(progId, CancellationToken.None);
            AppendLog($"{Capitalize(deviceName)} connected.");
        }
        catch (Exception ex)
        {
            AppendLog($"{Capitalize(deviceName)} connect failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, $"Connect {Capitalize(deviceName)}",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        RefreshConnectionUI();
    }

    /// <summary>Shared "Disconnect" flow.</summary>
    private async Task TryDisconnectDeviceAsync(
        string deviceName,
        Button disconnectBtn,
        Func<Task> disconnector)
    {
        disconnectBtn.Enabled = false;
        AppendLog($"Disconnecting {deviceName}…");
        try
        {
            await disconnector();
            AppendLog($"{Capitalize(deviceName)} disconnected.");
        }
        catch (Exception ex)
        {
            AppendLog($"{Capitalize(deviceName)} disconnect error: {ex.Message}");
        }
        RefreshConnectionUI();
    }

    /// <summary>
    /// Update Connect/Disconnect button enable state and refresh status
    /// labels. Safe to call after every state change; the status labels are
    /// also refreshed on a 1 Hz timer so live values keep flowing.
    /// </summary>
    private void RefreshConnectionUI()
    {
        SetButtonState(_settings.MountProgId,       _mount.IsConnected,       _connectMountBtn,    _disconnectMountBtn);
        SetButtonState(_settings.RotatorProgId,     _rotator.IsConnected,     _connectRotatorBtn,  _disconnectRotatorBtn);
        SetButtonState(_settings.FocuserProgId,     _focuser.IsConnected,     _connectFocuserBtn,  _disconnectFocuserBtn);
        SetButtonState(_settings.MainCameraProgId,  _mainCamera.IsConnected,  _connectMainCamBtn,  _disconnectMainCamBtn);
        SetButtonState(_settings.GuideCameraProgId, _guideCamera.IsConnected, _connectGuideCamBtn, _disconnectGuideCamBtn);

        UpdateConnectAllButton();
        UpdateSolveButton();
        UpdateStatusLabels();
    }

    private static void SetButtonState(string progId, bool isConnected, Button connectBtn, Button disconnectBtn)
    {
        bool configured = !string.IsNullOrEmpty(progId);
        connectBtn.Enabled = configured && !isConnected;
        disconnectBtn.Enabled = isConnected;
    }

    private void UpdateConnectAllButton()
    {
        if (!AnyConfigured())
        {
            _connectAllBtn.Enabled = false;
            _connectAllBtn.Text = "Connect All Equipment";
            return;
        }
        _connectAllBtn.Enabled = true;
        _connectAllBtn.Text = AnyConfiguredDisconnected()
            ? "Connect All Equipment"
            : "Disconnect All Equipment";
    }

    /// <summary>
    /// Refresh the five status indicators. Called on every status-timer tick
    /// and immediately after any connect/disconnect. ASCOM property reads
    /// happen on the UI thread (the COM objects are STA-bound to it anyway,
    /// so background polling would just marshal back).
    /// </summary>
    private void UpdateStatusLabels()
    {
        _mountStatusLbl.Text       = $"Mount: {ReadMountStatusText()}";
        _rotatorStatusLbl.Text     = $"Rotator: {ReadRotatorStatusText()}";
        _focuserStatusLbl.Text     = $"Focuser: {ReadFocuserStatusText()}";
        _mainCameraStatusLbl.Text  = $"Main camera: {ReadCameraStatusText(_mainCamera)}";
        _guideCameraStatusLbl.Text = $"Guide camera: {ReadCameraStatusText(_guideCamera)}";
    }

    private string ReadMountStatusText()
    {
        if (!_mount.IsConnected) return "Not Connected";
        try { return _mount.ReadStatus().ToString(); }   // "Tracking" / "Stopped" / "Parked"
        catch { return "Error"; }
    }

    private string ReadRotatorStatusText()
    {
        if (!_rotator.IsConnected) return "Not Connected";
        try
        {
            var s = _rotator.ReadStatus();
            if (double.IsNaN(s.PositionDeg)) return "Error";
            var moving = s.IsMoving ? " (moving)" : "";
            return $"{s.PositionDeg:F2}°{moving}";
        }
        catch { return "Error"; }
    }

    private string ReadFocuserStatusText()
    {
        if (!_focuser.IsConnected)
        {
            _focuserStatusError = "";
            return "Not Connected";
        }
        try
        {
            var s = _focuser.ReadStatus();
            _focuserStatusError = "";
            return double.IsNaN(s.TemperatureC)
                ? $"{s.Position} steps"
                : $"{s.Position} steps, {s.TemperatureC:F1}°C";
        }
        catch (Exception ex)
        {
            // Log the full exception once per distinct message so we can
            // diagnose without spamming the log at 1 Hz. Show a truncated
            // version on the status label itself.
            if (_focuserStatusError != ex.Message)
            {
                AppendLog($"Focuser status read failed: {ex.GetType().Name}: {ex.Message}");
                _focuserStatusError = ex.Message;
            }
            var msg = ex.Message;
            if (msg.Length > 40) msg = msg.Substring(0, 39) + "…";
            return $"Error: {msg}";
        }
    }

    private static string ReadCameraStatusText(CameraClient camera)
    {
        if (!camera.IsConnected) return "Not Connected";
        try
        {
            var s = camera.ReadStatus();
            var stateStr = s.IsExposing ? "Exposing" : "Paused";
            return double.IsNaN(s.TemperatureC)
                ? stateStr
                : $"{stateStr}, {s.TemperatureC:F1}°C";
        }
        catch { return "Error"; }
    }

    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        if (_logBox.InvokeRequired)
            _logBox.BeginInvoke(() => _logBox.AppendText(line));
        else
            _logBox.AppendText(line);
    }

    /// <summary>
    /// Adds a caption Label at (x, y) and returns a value Label next to it.
    /// Advances <paramref name="y"/> to the next row. The value label is
    /// returned (not yet added) so the caller can stash it as a field and
    /// add it via Controls.AddRange alongside the other top-level controls.
    /// </summary>
    private Label MakeReadout(string caption, int x, ref int y)
    {
        var captionLbl = new Label { Text = caption, Left = x, Top = y, Width = 110, AutoSize = false };
        Controls.Add(captionLbl);
        var valLbl = new Label { Text = "—", Left = x + 120, Top = y, Width = 280, AutoSize = false };
        y += 22;
        return valLbl;
    }

    private void WireHandlers()
    {
        _settingsBtn.Click += OnSettingsClicked;
        _connectAllBtn.Click += OnConnectAllClicked;

        _connectMountBtn.Click += OnConnectMountClicked;
        _disconnectMountBtn.Click += OnDisconnectMountClicked;
        _connectRotatorBtn.Click += OnConnectRotatorClicked;
        _disconnectRotatorBtn.Click += OnDisconnectRotatorClicked;
        _connectFocuserBtn.Click += OnConnectFocuserClicked;
        _disconnectFocuserBtn.Click += OnDisconnectFocuserClicked;
        _connectMainCamBtn.Click += OnConnectMainCamClicked;
        _disconnectMainCamBtn.Click += OnDisconnectMainCamClicked;
        _connectGuideCamBtn.Click += OnConnectGuideCamClicked;
        _disconnectGuideCamBtn.Click += OnDisconnectGuideCamClicked;

        _solveBtn.Click += OnSolveClicked;

        _setReferenceBtn.Click += OnSetReferenceClicked;
        _startBtn.Click += OnStartDerotationClicked;
        _stopBtn.Click += OnStopDerotationClicked;
        _haltBtn.Click += OnHaltClicked;

        _engine.Tick += OnEngineTick;
        _engine.LogMessage += OnEngineLogMessage;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Cancel the engine without awaiting — awaiting on the UI thread can
        // deadlock against COM calls the loop has marshaled back to us. The
        // cancelled loop will exit on its next WaitForNextTickAsync; the
        // process is shutting down so any leaked tick is harmless.
        _engine.RequestStop();

        _statusTimer.Stop();
        _statusTimer.Dispose();
        _mount.Dispose();
        _rotator.Dispose();
        _focuser.Dispose();
        _mainCamera.Dispose();
        _guideCamera.Dispose();
        base.OnFormClosing(e);
    }

    private sealed record DeviceEntry(
        string Name,
        Func<string> GetProgId,
        IDeviceClient Client,
        Button ConnectBtn,
        Button DisconnectBtn);
}
