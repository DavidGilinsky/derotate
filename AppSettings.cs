namespace Derotate;

/// <summary>
/// User-editable application settings persisted next to the executable as
/// <c>settings.json</c>. Edited via <see cref="SettingsForm"/>, persisted by
/// <see cref="SettingsStore"/>.
/// </summary>
public sealed class AppSettings
{
    /// <summary>ProgID of the configured ASCOM Telescope driver, or empty if none.</summary>
    public string MountProgId { get; set; } = "";

    /// <summary>ProgID of the configured ASCOM Rotator driver, or empty if none.</summary>
    public string RotatorProgId { get; set; } = "";

    /// <summary>
    /// ProgID of the ASCOM Focuser driver. The Esatto focuser and ARCO
    /// rotator share a serial bus (focuser is motor 1, rotator is motor 2)
    /// but PrimaLuceLab ships them as two separate ASCOM drivers, so this is
    /// independent of <see cref="RotatorProgId"/>.
    /// </summary>
    public string FocuserProgId { get; set; } = "";

    /// <summary>ProgID of the main (imaging) camera ASCOM driver, or empty if none.</summary>
    public string MainCameraProgId { get; set; } = "";

    /// <summary>ProgID of the guide camera ASCOM driver, or empty if none.</summary>
    public string GuideCameraProgId { get; set; } = "";

    /// <summary>
    /// True when the guide camera is mounted on the same rotator as the main
    /// camera and therefore rotates with it. When true, the engine should
    /// account for guide-camera rotation in its calibration; when false, the
    /// guide camera is fixed to the OTA and does not rotate.
    /// </summary>
    public bool GuideCameraRotatesWithMain { get; set; }

    /// <summary>How often the derotation loop reads alt/az/UTC from the mount.</summary>
    public int MountPollingIntervalMs { get; set; } = 1000;

    /// <summary>
    /// Minimum time between successive MoveAbsolute commands sent to the rotator.
    /// A move only fires when this interval has elapsed AND |delta| exceeds
    /// the engine's move threshold AND the rotator isn't already moving.
    /// </summary>
    public int RotatorMoveIntervalMs { get; set; } = 5000;

    /// <summary>Directory the app watches for newly-saved FITS frames.</summary>
    public string ImageWatchDirectory { get; set; } = "";

    /// <summary>Plate-solve every Nth FITS frame that lands in the watch directory.</summary>
    public int PlateSolveEveryNFrames { get; set; } = 5;

    /// <summary>Full path to the ASTAP executable used for plate solving.</summary>
    public string AstapPath { get; set; } = "";

    /// <summary>Returns a deep copy — used by <see cref="SettingsForm"/> so the dialog
    /// can edit a working copy and discard it on Cancel.</summary>
    public AppSettings Clone() => new()
    {
        MountProgId = MountProgId,
        RotatorProgId = RotatorProgId,
        FocuserProgId = FocuserProgId,
        MainCameraProgId = MainCameraProgId,
        GuideCameraProgId = GuideCameraProgId,
        GuideCameraRotatesWithMain = GuideCameraRotatesWithMain,
        MountPollingIntervalMs = MountPollingIntervalMs,
        RotatorMoveIntervalMs = RotatorMoveIntervalMs,
        ImageWatchDirectory = ImageWatchDirectory,
        PlateSolveEveryNFrames = PlateSolveEveryNFrames,
        AstapPath = AstapPath,
    };
}
