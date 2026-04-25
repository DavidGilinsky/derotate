using ASCOM.Com;
using ASCOM.Com.DriverAccess;
using ASCOM.Common;

namespace Derotate;

/// <summary>
/// Wraps the ASCOM Rotator driver — used here for the PrimaLuceLab ARCO
/// sitting on top of an Esatto focuser. The vendor's ASCOM driver is the
/// transport.
/// </summary>
public sealed class RotatorClient : IDeviceClient
{
    private Rotator? _rotator;

    /// <summary>True when the rotator driver is connected.</summary>
    public bool IsConnected { get; private set; }

    /// <summary>ProgID of the connected (or last-connected) rotator driver.</summary>
    public string? ProgId { get; private set; }

    /// <summary>True while the rotator reports an in-progress move.</summary>
    public bool IsMoving { get; private set; }

    /// <summary>
    /// Sky-frame position the driver reports. This is the value that
    /// <see cref="MoveAbsolute"/> targets.
    /// </summary>
    public double Position { get; private set; }

    /// <summary>Raw mechanical (encoder) position, for diagnostics.</summary>
    public double MechanicalPosition { get; private set; }

    /// <summary>
    /// Show the ASCOM Chooser dialog filtered to Rotator drivers and return
    /// the selected ProgID, or null if the user cancelled.
    /// </summary>
    public static string? ChooseProgId(string currentProgId = "")
    {
        // See MountClient.ChooseProgId for why ChooserSA is used over Chooser.
        var picked = ChooserSA.Choose(DeviceTypes.Rotator, currentProgId ?? "", null);
        return string.IsNullOrEmpty(picked) ? null : picked;
    }

    /// <summary>
    /// Open the rotator driver's own setup dialog. Requires the driver
    /// identified by <paramref name="progId"/> to be installed.
    /// </summary>
    public static void ShowSetupDialog(string progId)
    {
        if (string.IsNullOrWhiteSpace(progId))
            throw new ArgumentException("ProgID is empty.", nameof(progId));
        using var r = new Rotator(progId);
        r.SetupDialog();
    }

    /// <summary>
    /// Connect to the ASCOM rotator identified by <paramref name="progId"/>.
    /// The blocking COM call runs on a thread-pool thread so the UI stays
    /// responsive while the driver initialises.
    /// </summary>
    public async Task ConnectAsync(string progId, CancellationToken ct = default)
    {
        if (IsConnected) return;
        if (string.IsNullOrWhiteSpace(progId))
            throw new ArgumentException("ProgID is empty.", nameof(progId));

        var r = new Rotator(progId);
        try
        {
            await Task.Run(() => r.Connected = true, ct);
        }
        catch
        {
            r.Dispose();
            throw;
        }

        _rotator = r;
        ProgId = progId;
        IsConnected = true;
    }

    /// <summary>
    /// Disconnect and release the underlying driver. Driver errors during
    /// disconnect are swallowed.
    /// </summary>
    public async Task DisconnectAsync()
    {
        var r = _rotator;
        if (r is null) return;
        _rotator = null;
        IsConnected = false;
        await Task.Run(() =>
        {
            try { r.Connected = false; } catch { }
            r.Dispose();
        });
    }

    /// <summary>
    /// Issue an absolute move to the given sky-frame position (degrees).
    /// Caller is responsible for checking <see cref="IsMoving"/> first.
    /// ASCOM IRotator.MoveAbsolute takes a single-precision float; we cast
    /// from double — degrees fit easily in float precision.
    /// </summary>
    public void MoveAbsolute(double positionDeg)
    {
        if (_rotator is null)
            throw new InvalidOperationException("Rotator is not connected.");
        _rotator.MoveAbsolute((float)positionDeg);
    }

    /// <summary>Emergency stop — calls the driver's Halt method.</summary>
    public void Halt()
    {
        if (_rotator is null)
            throw new InvalidOperationException("Rotator is not connected.");
        _rotator.Halt();
    }

    /// <summary>Synchronous teardown for app shutdown.</summary>
    public void Dispose()
    {
        var r = _rotator;
        if (r is null) return;
        _rotator = null;
        IsConnected = false;
        try { r.Connected = false; } catch { }
        r.Dispose();
    }

    /// <summary>
    /// Quick poll for the UI status indicator. Returns NaN for the position
    /// if the driver throws on the read.
    /// </summary>
    public RotatorStatus ReadStatus()
    {
        if (_rotator is null)
            throw new InvalidOperationException("Rotator is not connected.");
        return new RotatorStatus(
            TryRead(() => _rotator.Position, double.NaN),
            TryRead(() => _rotator.IsMoving, false));
    }

    private static T TryRead<T>(Func<T> reader, T fallback)
    {
        try { return reader(); }
        catch { return fallback; }
    }
}

/// <summary>UI-status snapshot for the rotator.</summary>
/// <param name="PositionDeg">Sky-frame position in degrees, or NaN on read error.</param>
/// <param name="IsMoving">True while the rotator reports an in-progress move.</param>
public readonly record struct RotatorStatus(double PositionDeg, bool IsMoving);
