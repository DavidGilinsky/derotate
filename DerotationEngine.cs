namespace Derotate;

/// <summary>
/// Runs the 1 Hz derotation control loop on a background <see cref="Task"/>.
/// Each iteration: read mount state, read rotator status, compute the target
/// rotator position from the current parallactic angle, and (if the delta
/// exceeds <see cref="MoveThresholdDeg"/> and rate-limit allows) issue a
/// MoveAbsolute.
/// </summary>
/// <remarks>
/// State model:
///   reference = (refPA, refQ, refRotatorPos, refUtc)
///     refPA is the sky-frame position angle we want the camera to maintain.
///     refQ is the parallactic angle at the moment the reference was set.
///     refRotatorPos is the rotator's sky-frame position at that moment.
///   target_now = refRotatorPos + (q_now − refQ)
///
/// References are set via <see cref="SetReferenceFromManualSolve"/> (uses the
/// most recent <see cref="NotifyManualSolveResult"/> snapshot) or directly
/// via <see cref="SetReferenceFromValues"/> (used by the auto-bootstrap path
/// in MainForm when starting with a guide-camera solve).
///
/// The loop body and Tick events fire on a thread-pool thread; UI consumers
/// must marshal back via Control.Invoke.
/// </remarks>
public sealed class DerotationEngine
{
    private readonly MountClient _mount;
    private readonly RotatorClient _rotator;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    private DerotationReference? _ref;
    private ManualSolveSnapshot? _lastManualSolve;
    private DateTime _lastMoveUtc = DateTime.MinValue;
    private bool _zenithSuspended;

    public DerotationEngine(MountClient mount, RotatorClient rotator)
    {
        _mount = mount;
        _rotator = rotator;
    }

    /// <summary>True while the control loop is running.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>True once a reference has been set this session.</summary>
    public bool HasReference => _ref.HasValue;

    /// <summary>
    /// Current reference PA (sky angle the engine is maintaining), or null if
    /// no reference is set.
    /// </summary>
    public double? ReferencePaDeg => _ref?.PaDeg;

    /// <summary>Move only when |target − current| exceeds this many degrees.</summary>
    public double MoveThresholdDeg { get; set; } = 0.5;

    /// <summary>Loop tick interval (milliseconds).</summary>
    public int IntervalMs { get; set; } = 1000;

    /// <summary>
    /// Minimum gap between successive MoveAbsolute commands, in addition to
    /// the delta-threshold gate. Prevents spamming the rotator when the
    /// computed target moves quickly but the rotator can't keep up.
    /// </summary>
    public int MoveIntervalMs { get; set; } = 5000;

    /// <summary>
    /// Suspend moves when altitude exceeds (90 − this) degrees. The
    /// parallactic angle goes singular near the zenith, so we just freeze the
    /// rotator and resume when the object descends again.
    /// </summary>
    public double ZenithAvoidanceDeg { get; set; } = 5.0;

    /// <summary>Fired once per loop iteration on the worker thread.</summary>
    public event EventHandler<DerotationTick>? Tick;

    /// <summary>Fired when the engine wants to log a message.</summary>
    public event EventHandler<string>? LogMessage;

    /// <summary>
    /// Record the result of a manual plate-solve along with the mount/rotator
    /// state that was current when the exposure was taken. The engine doesn't
    /// re-reference itself yet — call <see cref="SetReferenceFromManualSolve"/>
    /// to commit. Lets the user inspect the solved PA before deciding to use it.
    /// </summary>
    public void NotifyManualSolveResult(double paDeg, DateTime utc, MountState mountState, double rotatorPosDeg)
    {
        _lastManualSolve = new ManualSolveSnapshot(paDeg, utc, mountState, rotatorPosDeg);
    }

    /// <summary>
    /// Adopt the most recent manual-solve result as the engine's reference.
    /// Throws if no <see cref="NotifyManualSolveResult"/> has been received
    /// this session.
    /// </summary>
    public void SetReferenceFromManualSolve()
    {
        if (_lastManualSolve is not { } s)
            throw new InvalidOperationException(
                "No plate-solve result available. Click Solve before Set Reference PA.");
        SetReferenceFromValues(s.PaDeg, s.Utc, s.MountState, s.RotatorPosDeg);
    }

    /// <summary>
    /// Set the engine's reference directly from a (PA, time, mount, rotator)
    /// tuple. Used by the Start-Derotation auto-bootstrap path.
    /// </summary>
    public void SetReferenceFromValues(double paDeg, DateTime utc, MountState mountState, double rotatorPosDeg)
    {
        var qDeg = ParallacticAngleDeg(
            mountState.AltitudeDeg,
            mountState.AzimuthDeg,
            mountState.SiteLatitudeDeg);
        _ref = new DerotationReference(paDeg, qDeg, rotatorPosDeg, utc);
    }

    /// <summary>Start the 1 Hz control loop. Throws if no reference has been set.</summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning) return Task.CompletedTask;
        if (!HasReference)
            throw new InvalidOperationException("No reference set; call SetReference… first.");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IsRunning = true;
        _zenithSuspended = false;
        _lastMoveUtc = DateTime.MinValue;
        _loopTask = Task.Run(() => RunLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    /// <summary>Signal the loop to stop and wait for it to drain.</summary>
    public async Task StopAsync()
    {
        if (!IsRunning) return;
        _cts?.Cancel();
        var task = _loopTask;
        _loopTask = null;
        IsRunning = false;
        if (task != null)
        {
            try { await task.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>
    /// Cancel the loop without awaiting it. Intended for shutdown paths
    /// (form closing) where we don't want to risk a deadlock against the
    /// UI thread that the loop's COM calls marshal back to.
    /// </summary>
    public void RequestStop()
    {
        _cts?.Cancel();
        IsRunning = false;
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(IntervalMs));
        long iter = 0;
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            iter++;
            try
            {
                DoTick(iter);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Engine tick {iter} error: {ex.Message}");
            }
        }
    }

    private void DoTick(long iter)
    {
        var mountState = _mount.ReadState();
        var rotatorStatus = _rotator.ReadStatus();

        var qNow = ParallacticAngleDeg(
            mountState.AltitudeDeg, mountState.AzimuthDeg, mountState.SiteLatitudeDeg);

        bool zenithNow = mountState.AltitudeDeg > (90.0 - ZenithAvoidanceDeg);

        if (zenithNow != _zenithSuspended)
        {
            _zenithSuspended = zenithNow;
            LogMessage?.Invoke(this, zenithNow
                ? $"Zenith avoidance engaged (alt {mountState.AltitudeDeg:F1}°). Rotator holding."
                : "Zenith avoidance cleared. Resuming tracking.");
        }

        double targetPos = rotatorStatus.PositionDeg;
        double deltaApplied = 0;

        if (_ref is { } r && !zenithNow)
        {
            targetPos = NormalizeDeg(r.RotatorPosDeg + (qNow - r.QDeg));
            double delta = ShortestDelta(targetPos, rotatorStatus.PositionDeg);

            bool overThreshold = Math.Abs(delta) > MoveThresholdDeg;
            bool intervalElapsed = (DateTime.UtcNow - _lastMoveUtc).TotalMilliseconds >= MoveIntervalMs;

            if (overThreshold && intervalElapsed && !rotatorStatus.IsMoving)
            {
                _rotator.MoveAbsolute(targetPos);
                _lastMoveUtc = DateTime.UtcNow;
                deltaApplied = delta;
            }
        }

        Tick?.Invoke(this, new DerotationTick(
            Iteration:           iter,
            Utc:                 mountState.UtcTime,
            AltitudeDeg:         mountState.AltitudeDeg,
            AzimuthDeg:          mountState.AzimuthDeg,
            SiteLatitudeDeg:     mountState.SiteLatitudeDeg,
            ParallacticAngleDeg: qNow,
            RotatorPositionDeg:  rotatorStatus.PositionDeg,
            TargetPositionDeg:   targetPos,
            DeltaAppliedDeg:     deltaApplied,
            ZenithSuspended:     zenithNow));
    }

    /// <summary>
    /// Parallactic angle in degrees for the given alt/az/lat. Uses the
    /// standard formula by way of converting alt/az → HA/dec first:
    ///   q = atan2(sin H, tan φ · cos δ − sin δ · cos H)
    /// Northern hemisphere only for v1; sign convention may need review for
    /// southern observers.
    /// </summary>
    /// TODO: southern-hemisphere sign handling.
    internal static double ParallacticAngleDeg(double altDeg, double azDeg, double latDeg)
    {
        double alt = altDeg * Math.PI / 180.0;
        double az  = azDeg  * Math.PI / 180.0;
        double lat = latDeg * Math.PI / 180.0;

        // alt/az -> dec
        double sinDec = Math.Sin(lat) * Math.Sin(alt) + Math.Cos(lat) * Math.Cos(alt) * Math.Cos(az);
        sinDec = Math.Clamp(sinDec, -1.0, 1.0);
        double dec = Math.Asin(sinDec);
        double cosDec = Math.Cos(dec);

        if (Math.Abs(cosDec) < 1e-9)
            return 0.0;   // Object at celestial pole — q is undefined; caller's zenith-avoidance handles the practical case.

        // alt/az -> hour angle. Sign: H is positive west of meridian.
        double sinH = -Math.Cos(alt) * Math.Sin(az) / cosDec;
        double cosH = (Math.Sin(alt) - Math.Sin(lat) * sinDec) / (Math.Cos(lat) * cosDec);
        double ha = Math.Atan2(sinH, cosH);

        double q = Math.Atan2(Math.Sin(ha), Math.Tan(lat) * cosDec - sinDec * Math.Cos(ha));
        return q * 180.0 / Math.PI;
    }

    /// <summary>Normalise an angle into [0, 360).</summary>
    private static double NormalizeDeg(double d)
    {
        d %= 360.0;
        if (d < 0) d += 360.0;
        return d;
    }

    /// <summary>Shortest-arc difference target − current, in (−180, +180].</summary>
    private static double ShortestDelta(double targetDeg, double currentDeg)
    {
        double d = (targetDeg - currentDeg) % 360.0;
        if (d > 180) d -= 360;
        if (d <= -180) d += 360;
        return d;
    }

    private readonly record struct DerotationReference(double PaDeg, double QDeg, double RotatorPosDeg, DateTime Utc);

    private readonly record struct ManualSolveSnapshot(double PaDeg, DateTime Utc, MountState MountState, double RotatorPosDeg);
}

/// <summary>Per-iteration snapshot emitted by <see cref="DerotationEngine.Tick"/>.</summary>
public readonly record struct DerotationTick(
    long Iteration,
    DateTime Utc,
    double AltitudeDeg,
    double AzimuthDeg,
    double SiteLatitudeDeg,
    double ParallacticAngleDeg,
    double RotatorPositionDeg,
    double TargetPositionDeg,
    double DeltaAppliedDeg,
    bool ZenithSuspended);
