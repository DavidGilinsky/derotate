using ASCOM.Com;
using ASCOM.Com.DriverAccess;
using ASCOM.Common;

namespace Derotate;

/// <summary>
/// Wraps the ASCOM Telescope driver. Owns the connection lifecycle and exposes
/// the small subset of state the derotation engine needs each loop iteration:
/// altitude, azimuth, UTC time, and observer latitude.
/// </summary>
/// <remarks>
/// All non-static members are intended to be called from the derotation worker
/// thread. The UI thread should not touch this object directly; updates flow
/// out through <see cref="DerotationEngine"/> events.
/// </remarks>
public sealed class MountClient : IDeviceClient
{
    private Telescope? _telescope;

    /// <summary>True when an ASCOM telescope is connected and ready.</summary>
    public bool IsConnected { get; private set; }

    /// <summary>ProgID of the connected (or last-connected) telescope driver.</summary>
    public string? ProgId { get; private set; }

    /// <summary>
    /// Show the ASCOM Chooser dialog filtered to Telescope drivers and return
    /// the selected ProgID, or null if the user cancelled.
    /// </summary>
    public static string? ChooseProgId(string currentProgId = "")
    {
        // ChooserSA is the stand-alone, .NET-native Chooser added by ASCOM for
        // CET-safe use on .NET 9+. The legacy ASCOM.Com.Chooser wraps a 3.5
        // COM component that the platform warns against on this runtime.
        var picked = ChooserSA.Choose(DeviceTypes.Telescope, currentProgId ?? "", null);
        return string.IsNullOrEmpty(picked) ? null : picked;
    }

    /// <summary>
    /// Open the driver's own setup dialog so the user can configure COM port,
    /// baud rate, etc. Requires the driver identified by <paramref name="progId"/>
    /// to be installed; throws on instantiation failure.
    /// </summary>
    public static void ShowSetupDialog(string progId)
    {
        if (string.IsNullOrWhiteSpace(progId))
            throw new ArgumentException("ProgID is empty.", nameof(progId));
        using var t = new Telescope(progId);
        t.SetupDialog();
    }

    /// <summary>
    /// Connect to the ASCOM telescope identified by <paramref name="progId"/>.
    /// The blocking COM call runs on a thread-pool thread so the caller's
    /// SynchronizationContext (typically the UI thread) stays responsive.
    /// </summary>
    public async Task ConnectAsync(string progId, CancellationToken ct = default)
    {
        if (IsConnected) return;
        if (string.IsNullOrWhiteSpace(progId))
            throw new ArgumentException("ProgID is empty.", nameof(progId));

        var t = new Telescope(progId);
        try
        {
            // ct can only abort scheduling, not the in-flight COM call.
            await Task.Run(() => t.Connected = true, ct);
        }
        catch
        {
            t.Dispose();
            throw;
        }

        _telescope = t;
        ProgId = progId;
        IsConnected = true;
    }

    /// <summary>
    /// Disconnect and release the underlying driver. Swallows driver errors
    /// during the disconnect — we want the wrapper's IsConnected to flip
    /// regardless of the driver's response.
    /// </summary>
    public async Task DisconnectAsync()
    {
        var t = _telescope;
        if (t is null) return;
        _telescope = null;
        IsConnected = false;
        await Task.Run(() =>
        {
            try { t.Connected = false; } catch { }
            t.Dispose();
        });
    }

    /// <summary>
    /// Read the alt/az/UTC/latitude snapshot used by the derotation engine.
    /// All angles in degrees. UTC is what the mount reports (LX200GPS gets it
    /// from its own GPS, so we trust it over the PC clock).
    /// </summary>
    public MountState ReadState()
    {
        if (_telescope is null)
            throw new InvalidOperationException("Mount is not connected.");
        var utc = _telescope.UTCDate;
        if (utc.Kind != DateTimeKind.Utc)
            utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return new MountState(
            _telescope.Altitude,
            _telescope.Azimuth,
            utc,
            _telescope.SiteLatitude);
    }

    /// <summary>
    /// Quick poll for the UI status indicator: tracking, stopped, or parked.
    /// Property reads are wrapped so a driver that doesn't implement (e.g.)
    /// AtPark falls back to "not parked" rather than tearing the call apart.
    /// </summary>
    public MountDriveState ReadStatus()
    {
        if (_telescope is null)
            throw new InvalidOperationException("Mount is not connected.");
        bool atPark = TryRead(() => _telescope.AtPark, false);
        bool tracking = TryRead(() => _telescope.Tracking, false);
        return atPark ? MountDriveState.Parked
             : tracking ? MountDriveState.Tracking
             : MountDriveState.Stopped;
    }

    private static T TryRead<T>(Func<T> reader, T fallback)
    {
        try { return reader(); }
        catch { return fallback; }
    }

    /// <summary>
    /// Synchronous teardown for app shutdown. Awaiting <see cref="DisconnectAsync"/>
    /// is preferable when on a UI thread; this exists so <see cref="Form.OnFormClosing"/>
    /// can clean up without async plumbing.
    /// </summary>
    public void Dispose()
    {
        var t = _telescope;
        if (t is null) return;
        _telescope = null;
        IsConnected = false;
        try { t.Connected = false; } catch { }
        t.Dispose();
    }
}

/// <summary>Snapshot of mount state used by the derotation control loop.</summary>
/// <param name="AltitudeDeg">Apparent altitude, degrees.</param>
/// <param name="AzimuthDeg">Apparent azimuth (north = 0, east = 90), degrees.</param>
/// <param name="UtcTime">UTC time as reported by the mount.</param>
/// <param name="SiteLatitudeDeg">Observer latitude, degrees (north positive).</param>
public readonly record struct MountState(
    double AltitudeDeg,
    double AzimuthDeg,
    DateTime UtcTime,
    double SiteLatitudeDeg);

/// <summary>
/// Coarse mount drive status for the UI indicator. Parked takes priority over
/// Tracking — a parked mount that's still reporting Tracking=true is still
/// considered Parked for display purposes.
/// </summary>
public enum MountDriveState
{
    Stopped,
    Tracking,
    Parked,
}
