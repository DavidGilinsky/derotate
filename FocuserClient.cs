using ASCOM.Com;
using ASCOM.Com.DriverAccess;
using ASCOM.Common;

namespace Derotate;

/// <summary>
/// Wraps the ASCOM Focuser driver — used here for the PrimaLuceLab Esatto
/// (motor 1 on the same serial bus as the ARCO rotator's motor 2). Despite
/// living on one serial connection, PrimaLuceLab ships them as two separate
/// ASCOM drivers, so this client uses its own ProgID, distinct from the
/// rotator's.
/// </summary>
public sealed class FocuserClient : IDeviceClient
{
    private Focuser? _focuser;

    public bool IsConnected { get; private set; }
    public string? ProgId { get; private set; }

    /// <summary>Show the Chooser filtered to Focuser drivers; returns null on cancel.</summary>
    public static string? ChooseProgId(string currentProgId = "")
    {
        // ChooserSA — see MountClient for the .NET 9 / Intel CET rationale.
        var picked = ChooserSA.Choose(DeviceTypes.Focuser, currentProgId ?? "", null);
        return string.IsNullOrEmpty(picked) ? null : picked;
    }

    /// <summary>Open the focuser driver's setup dialog.</summary>
    public static void ShowSetupDialog(string progId)
    {
        if (string.IsNullOrWhiteSpace(progId))
            throw new ArgumentException("ProgID is empty.", nameof(progId));
        using var f = new Focuser(progId);
        f.SetupDialog();
    }

    public async Task ConnectAsync(string progId, CancellationToken ct = default)
    {
        if (IsConnected) return;
        if (string.IsNullOrWhiteSpace(progId))
            throw new ArgumentException("ProgID is empty.", nameof(progId));

        var f = new Focuser(progId);
        try
        {
            await Task.Run(() => f.Connected = true, ct);
        }
        catch
        {
            f.Dispose();
            throw;
        }

        _focuser = f;
        ProgId = progId;
        IsConnected = true;
    }

    public async Task DisconnectAsync()
    {
        var f = _focuser;
        if (f is null) return;
        _focuser = null;
        IsConnected = false;
        await Task.Run(() =>
        {
            try { f.Connected = false; } catch { }
            f.Dispose();
        });
    }

    public void Dispose()
    {
        var f = _focuser;
        if (f is null) return;
        _focuser = null;
        IsConnected = false;
        try { f.Connected = false; } catch { }
        f.Dispose();
    }

    /// <summary>
    /// Quick poll for the UI status indicator. Position is required (any
    /// driver exception propagates); Temperature is optional and falls back
    /// to NaN on failure.
    /// </summary>
    public FocuserStatus ReadStatus()
    {
        if (_focuser is null)
            throw new InvalidOperationException("Focuser is not connected.");
        return new FocuserStatus(
            _focuser.Position,
            TryRead(() => _focuser.Temperature, double.NaN));
    }

    private static T TryRead<T>(Func<T> reader, T fallback)
    {
        try { return reader(); }
        catch { return fallback; }
    }
}

/// <summary>UI-status snapshot for the focuser.</summary>
/// <param name="Position">Focuser encoder position in steps.</param>
/// <param name="TemperatureC">Probe temperature in degrees Celsius; NaN if unsupported.</param>
public readonly record struct FocuserStatus(int Position, double TemperatureC);
