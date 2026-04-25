using System.Drawing;

namespace Derotate;

/// <summary>
/// Modal preferences dialog. Construct with the current <see cref="AppSettings"/>;
/// the form edits a clone, and exposes the new instance via <see cref="Result"/>
/// when the user clicks OK. Cancel discards the working copy.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly AppSettings _working;

    private readonly TextBox _mountProgIdTb;
    private readonly TextBox _rotatorProgIdTb;
    private readonly TextBox _focuserProgIdTb;
    private readonly TextBox _mainCameraProgIdTb;
    private readonly TextBox _guideCameraProgIdTb;
    private readonly CheckBox _guideRotatesWithMainCb;
    private readonly NumericUpDown _mountPollMsNum;
    private readonly NumericUpDown _rotatorMoveMsNum;
    private readonly TextBox _imageDirTb;
    private readonly NumericUpDown _solveEveryNum;
    private readonly TextBox _astapTb;

    /// <summary>
    /// New settings the user committed. Only populated when this form's
    /// <see cref="Form.DialogResult"/> is <see cref="DialogResult.OK"/>.
    /// </summary>
    public AppSettings? Result { get; private set; }

    public SettingsForm(AppSettings current)
    {
        _working = current.Clone();

        Text = "Settings";
        ClientSize = new Size(640, 452);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        const int xLabel = 16;
        const int wLabel = 220;
        const int xField = xLabel + wLabel;
        int y = 16;

        // -- Mount ProgID row --
        _mountProgIdTb = AddProgIdRow("Mount ProgID:", _working.MountProgId,
            xLabel, xField, ref y, MountClient.ChooseProgId, MountClient.ShowSetupDialog);

        // -- Rotator ProgID row --
        _rotatorProgIdTb = AddProgIdRow("Rotator ProgID:", _working.RotatorProgId,
            xLabel, xField, ref y, RotatorClient.ChooseProgId, RotatorClient.ShowSetupDialog);

        // -- Focuser ProgID row (separate from rotator: Esatto and ARCO are
        //    two motors on the same serial bus, but PrimaLuceLab ships them
        //    as two distinct ASCOM drivers — the focuser addresses MOT1, the
        //    rotator addresses MOT2). --
        _focuserProgIdTb = AddProgIdRow("Focuser ProgID:", _working.FocuserProgId,
            xLabel, xField, ref y, FocuserClient.ChooseProgId, FocuserClient.ShowSetupDialog);

        // -- Main camera ProgID row --
        _mainCameraProgIdTb = AddProgIdRow("Main camera ProgID:", _working.MainCameraProgId,
            xLabel, xField, ref y, CameraClient.ChooseProgId, CameraClient.ShowSetupDialog);

        // -- Guide camera ProgID row (tighter so the checkbox sub-row hugs it) --
        _guideCameraProgIdTb = AddProgIdRow("Guide camera ProgID:", _working.GuideCameraProgId,
            xLabel, xField, ref y, CameraClient.ChooseProgId, CameraClient.ShowSetupDialog,
            rowAdvance: 28);

        // -- Guide-rotates-with-main checkbox (indented under the guide camera field) --
        _guideRotatesWithMainCb = new CheckBox
        {
            Text = "Guide camera rotates with main camera",
            Left = xField,
            Top = y,
            Width = 360,
            Checked = _working.GuideCameraRotatesWithMain,
        };
        Controls.Add(_guideRotatesWithMainCb);
        y += 36;

        // -- Mount poll interval --
        AddLabel("Mount poll interval (ms):", xLabel, y, wLabel);
        _mountPollMsNum = new NumericUpDown
        {
            Left = xField, Top = y, Width = 100,
            Minimum = 100, Maximum = 60000, Increment = 100,
            Value = Clamp(_working.MountPollingIntervalMs, 100, 60000),
        };
        Controls.Add(_mountPollMsNum);
        y += 28;

        // -- Rotator move interval --
        AddLabel("Rotator move interval (ms):", xLabel, y, wLabel);
        _rotatorMoveMsNum = new NumericUpDown
        {
            Left = xField, Top = y, Width = 100,
            Minimum = 500, Maximum = 600000, Increment = 500,
            Value = Clamp(_working.RotatorMoveIntervalMs, 500, 600000),
        };
        Controls.Add(_rotatorMoveMsNum);
        y += 36;

        // -- Image watch directory --
        AddLabel("Image watch directory:", xLabel, y, wLabel);
        _imageDirTb = new TextBox { Left = xField, Top = y, Width = 290, Text = _working.ImageWatchDirectory };
        var imageBrowseBtn = new Button { Text = "Browse…", Left = xField + 298, Top = y - 1, Width = 80 };
        imageBrowseBtn.Click += (_, _) => OnBrowseFolder(_imageDirTb);
        Controls.AddRange([_imageDirTb, imageBrowseBtn]);
        y += 28;

        // -- Plate-solve every N frames --
        AddLabel("Plate-solve every N frames:", xLabel, y, wLabel);
        _solveEveryNum = new NumericUpDown
        {
            Left = xField, Top = y, Width = 100,
            Minimum = 1, Maximum = 999, Increment = 1,
            Value = Clamp(_working.PlateSolveEveryNFrames, 1, 999),
        };
        Controls.Add(_solveEveryNum);
        y += 36;

        // -- ASTAP path --
        AddLabel("ASTAP executable:", xLabel, y, wLabel);
        _astapTb = new TextBox { Left = xField, Top = y, Width = 290, Text = _working.AstapPath };
        var astapBrowseBtn = new Button { Text = "Browse…", Left = xField + 298, Top = y - 1, Width = 80 };
        astapBrowseBtn.Click += (_, _) => OnBrowseAstap(_astapTb);
        Controls.AddRange([_astapTb, astapBrowseBtn]);
        y += 48;

        // -- OK / Cancel --
        var okBtn = new Button
        {
            Text = "OK", Width = 90, Top = y,
            Left = ClientSize.Width - 16 - 90 - 8 - 90,
            DialogResult = DialogResult.OK,
        };
        var cancelBtn = new Button
        {
            Text = "Cancel", Width = 90, Top = y,
            Left = ClientSize.Width - 16 - 90,
            DialogResult = DialogResult.Cancel,
        };
        okBtn.Click += (_, _) => OnOk();
        Controls.AddRange([okBtn, cancelBtn]);

        AcceptButton = okBtn;
        CancelButton = cancelBtn;
    }

    /// <summary>
    /// Builds one ProgID row (label + textbox + Choose… + Configure…) and
    /// returns the TextBox so the caller can stash it as a field. <paramref name="y"/>
    /// is advanced by <paramref name="rowAdvance"/>.
    /// </summary>
    private TextBox AddProgIdRow(
        string caption,
        string initialValue,
        int xLabel,
        int xField,
        ref int y,
        Func<string, string?> chooser,
        Action<string> setupDialog,
        int rowAdvance = 32)
    {
        AddLabel(caption, xLabel, y, xField - xLabel);
        var tb = new TextBox { Left = xField, Top = y, Width = 180, Text = initialValue };
        var chooseBtn = new Button { Text = "Choose…", Left = xField + 188, Top = y - 1, Width = 80 };
        var setupBtn = new Button { Text = "Configure…", Left = xField + 272, Top = y - 1, Width = 100 };
        chooseBtn.Click += (_, _) => OnChoose(tb, chooser);
        setupBtn.Click += (_, _) => OnConfigure(tb, setupDialog);
        Controls.AddRange([tb, chooseBtn, setupBtn]);
        y += rowAdvance;
        return tb;
    }

    private void AddLabel(string text, int x, int y, int width)
    {
        Controls.Add(new Label
        {
            Text = text,
            Left = x,
            Top = y + 3,
            Width = width,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Height = 20,
        });
    }

    private static decimal Clamp(int value, int min, int max) =>
        value < min ? min : value > max ? max : value;

    private void OnChoose(TextBox target, Func<string, string?> chooser)
    {
        try
        {
            var picked = chooser(target.Text);
            if (!string.IsNullOrEmpty(picked))
                target.Text = picked;
        }
        catch (Exception ex)
        {
            ShowError("ASCOM Chooser failed", ex);
        }
    }

    private void OnConfigure(TextBox source, Action<string> setupDialog)
    {
        var progId = source.Text.Trim();
        if (string.IsNullOrEmpty(progId))
        {
            MessageBox.Show(this, "Pick a driver first (Choose…), then Configure.",
                "Settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            setupDialog(progId);
        }
        catch (Exception ex)
        {
            ShowError("Driver setup failed", ex);
        }
    }

    private void OnBrowseFolder(TextBox target)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select the directory the app should watch for new FITS frames",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(target.Text) ? target.Text : "",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            target.Text = dlg.SelectedPath;
    }

    private void OnBrowseAstap(TextBox target)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Locate astap.exe",
            Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*",
            FileName = target.Text,
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            target.Text = dlg.FileName;
    }

    private void OnOk()
    {
        Result = new AppSettings
        {
            MountProgId = _mountProgIdTb.Text.Trim(),
            RotatorProgId = _rotatorProgIdTb.Text.Trim(),
            FocuserProgId = _focuserProgIdTb.Text.Trim(),
            MainCameraProgId = _mainCameraProgIdTb.Text.Trim(),
            GuideCameraProgId = _guideCameraProgIdTb.Text.Trim(),
            GuideCameraRotatesWithMain = _guideRotatesWithMainCb.Checked,
            MountPollingIntervalMs = (int)_mountPollMsNum.Value,
            RotatorMoveIntervalMs = (int)_rotatorMoveMsNum.Value,
            ImageWatchDirectory = _imageDirTb.Text.Trim(),
            PlateSolveEveryNFrames = (int)_solveEveryNum.Value,
            AstapPath = _astapTb.Text.Trim(),
        };
        // DialogResult was set on the OK button, which closes the form.
    }

    private void ShowError(string title, Exception ex)
    {
        MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
