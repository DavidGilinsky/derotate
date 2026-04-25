namespace Derotate;

/// <summary>
/// Common surface for our ASCOM device wrappers (mount, rotator, focuser,
/// cameras). Used by <see cref="MainForm"/> to drive Connect/Disconnect All
/// without per-device branches. Each device exposes additional, type-specific
/// members (e.g. <c>ReadStatus</c>) outside of this interface.
/// </summary>
public interface IDeviceClient : IDisposable
{
    /// <summary>True when the wrapper holds a connected ASCOM driver.</summary>
    bool IsConnected { get; }

    /// <summary>Connect to the driver identified by <paramref name="progId"/>.</summary>
    Task ConnectAsync(string progId, CancellationToken ct = default);

    /// <summary>Disconnect and release the driver. Idempotent.</summary>
    Task DisconnectAsync();
}
