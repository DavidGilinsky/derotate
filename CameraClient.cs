using ASCOM.Com;
using ASCOM.Com.DriverAccess;
using ASCOM.Common;
using ASCOM.Common.DeviceInterfaces;

namespace Derotate;

/// <summary>
/// Wraps an ASCOM Camera driver. Two instances are typically used — one for
/// the main imaging camera and one for the guide camera. Currently only the
/// connection lifecycle is implemented; frame retrieval, exposure control,
/// binning, and so on will be added when the imaging / plate-solving
/// pipeline lands.
/// </summary>
public sealed class CameraClient : IDeviceClient
{
    private Camera? _camera;

    /// <summary>True when the camera driver is connected.</summary>
    public bool IsConnected { get; private set; }

    /// <summary>ProgID of the connected (or last-connected) camera driver.</summary>
    public string? ProgId { get; private set; }

    /// <summary>
    /// Show the ASCOM Chooser filtered to Camera drivers and return the
    /// selected ProgID, or null if the user cancelled. Used by the Settings
    /// dialog for both the main and guide camera fields.
    /// </summary>
    public static string? ChooseProgId(string currentProgId = "")
    {
        // ChooserSA — see MountClient for the .NET 9 / Intel CET rationale.
        var picked = ChooserSA.Choose(DeviceTypes.Camera, currentProgId ?? "", null);
        return string.IsNullOrEmpty(picked) ? null : picked;
    }

    /// <summary>Open the camera driver's own setup dialog.</summary>
    public static void ShowSetupDialog(string progId)
    {
        if (string.IsNullOrWhiteSpace(progId))
            throw new ArgumentException("ProgID is empty.", nameof(progId));
        using var c = new Camera(progId);
        c.SetupDialog();
    }

    /// <summary>
    /// Connect to the ASCOM camera identified by <paramref name="progId"/>.
    /// The blocking COM call runs on a thread-pool thread so the UI stays
    /// responsive while the driver initialises.
    /// </summary>
    public async Task ConnectAsync(string progId, CancellationToken ct = default)
    {
        if (IsConnected) return;
        if (string.IsNullOrWhiteSpace(progId))
            throw new ArgumentException("ProgID is empty.", nameof(progId));

        var c = new Camera(progId);
        try
        {
            await Task.Run(() => c.Connected = true, ct);
        }
        catch
        {
            c.Dispose();
            throw;
        }

        _camera = c;
        ProgId = progId;
        IsConnected = true;
    }

    /// <summary>
    /// Disconnect and release the underlying driver. Driver errors during
    /// disconnect are swallowed.
    /// </summary>
    public async Task DisconnectAsync()
    {
        var c = _camera;
        if (c is null) return;
        _camera = null;
        IsConnected = false;
        await Task.Run(() =>
        {
            try { c.Connected = false; } catch { }
            c.Dispose();
        });
    }

    /// <summary>Synchronous teardown for app shutdown.</summary>
    public void Dispose()
    {
        var c = _camera;
        if (c is null) return;
        _camera = null;
        IsConnected = false;
        try { c.Connected = false; } catch { }
        c.Dispose();
    }

    /// <summary>
    /// Take a single full-frame, 1×1 binned exposure and return the image as
    /// a 2D int array indexed image[x, y] (matching the ASCOM convention).
    /// Setup and the polling loop run on a thread-pool thread so the UI stays
    /// responsive; individual COM calls still marshal back to the camera's
    /// home apartment, but each call is short enough that the message pump
    /// keeps moving.
    /// </summary>
    public async Task<int[,]> CaptureAsync(double exposureSec, CancellationToken ct = default)
    {
        if (_camera is null)
            throw new InvalidOperationException("Camera is not connected.");
        if (exposureSec <= 0)
            throw new ArgumentOutOfRangeException(nameof(exposureSec), "Exposure must be positive.");

        var camera = _camera;

        return await Task.Run(() =>
        {
            // Full frame, 1×1 binning — most general for plate solving.
            camera.BinX = 1;
            camera.BinY = 1;
            camera.StartX = 0;
            camera.StartY = 0;
            camera.NumX = camera.CameraXSize;
            camera.NumY = camera.CameraYSize;

            camera.StartExposure(exposureSec, true);

            // Poll for completion. Allow exposure + 60s for readout/download.
            var deadline = DateTime.UtcNow.AddSeconds(exposureSec + 60);
            while (!camera.ImageReady)
            {
                ct.ThrowIfCancellationRequested();
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException(
                        "Camera did not report ImageReady within the expected window.");
                Thread.Sleep(200);
            }

            // ASCOM specifies int[,] for monochrome ImageArray, but some drivers
            // return short[,] or other ranks. v1 only handles int[,]; if you
            // hit this exception, tell me what your camera returns.
            var arr = camera.ImageArray;
            if (arr is int[,] image) return image;
            throw new InvalidOperationException(
                $"Unsupported ImageArray element type: {arr.GetType()}.");
        }, ct);
    }

    /// <summary>
    /// Quick poll for the UI status indicator. <see cref="CameraStatus.IsExposing"/>
    /// is true while the camera reports any active state (Exposing/Reading/
    /// Download/Waiting); Idle and Error map to false. <see cref="CameraStatus.TemperatureC"/>
    /// is NaN if the driver doesn't expose CCD temperature.
    /// </summary>
    public CameraStatus ReadStatus()
    {
        if (_camera is null)
            throw new InvalidOperationException("Camera is not connected.");
        var state = TryRead(() => _camera.CameraState, CameraState.Idle);
        bool exposing = state == CameraState.Exposing
                     || state == CameraState.Reading
                     || state == CameraState.Download
                     || state == CameraState.Waiting;
        return new CameraStatus(exposing, TryRead(() => _camera.CCDTemperature, double.NaN));
    }

    private static T TryRead<T>(Func<T> reader, T fallback)
    {
        try { return reader(); }
        catch { return fallback; }
    }
}

/// <summary>UI-status snapshot for a camera.</summary>
/// <param name="IsExposing">True while the camera reports an active exposure-related state.</param>
/// <param name="TemperatureC">CCD temperature in degrees Celsius, or NaN if unsupported / read error.</param>
public readonly record struct CameraStatus(bool IsExposing, double TemperatureC);
