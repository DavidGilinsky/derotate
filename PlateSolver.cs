using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Derotate;

/// <summary>
/// Drives ASTAP as a child process to plate-solve a FITS image. ASTAP writes
/// a sibling <c>.ini</c> file with key/value pairs (PLTSOLVD, CRVAL1/2,
/// CROTA2, CDELT1/2, …) that we parse for the rotation angle and centre
/// coordinates.
/// </summary>
public static class PlateSolver
{
    /// <summary>
    /// Run a blind-solve of <paramref name="fitsPath"/> via ASTAP and return
    /// the parsed result. <paramref name="onLine"/> is invoked for each line
    /// emitted on ASTAP's stdout or stderr, in real time, so the caller can
    /// stream progress to a log. The callback runs on a thread-pool thread.
    /// </summary>
    /// <remarks>
    /// Success/failure is determined from the <c>PLTSOLVD</c> key in the
    /// <c>.ini</c> file ASTAP writes alongside the input — that's the
    /// authoritative answer. The streamed output is for visibility only.
    /// </remarks>
    public static async Task<PlateSolveResult> SolveAsync(
        string astapExePath,
        string fitsPath,
        Action<string>? onLine = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(astapExePath) || !File.Exists(astapExePath))
            throw new FileNotFoundException("ASTAP executable not found.", astapExePath);
        if (!File.Exists(fitsPath))
            throw new FileNotFoundException("FITS file not found.", fitsPath);

        var psi = new ProcessStartInfo
        {
            FileName = astapExePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(fitsPath) ?? "",
        };
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add(fitsPath);
        psi.ArgumentList.Add("-r"); psi.ArgumentList.Add("999");   // blind solve, full sky
        psi.ArgumentList.Add("-z"); psi.ArgumentList.Add("2");     // 2x downsample for speed

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        // Always attach handlers so the redirected pipes get drained — a full
        // pipe buffer would deadlock the child process. Forwarding to onLine
        // is opportunistic.
        process.OutputDataReceived += (_, e) => { if (e.Data is { } line) onLine?.Invoke(line); };
        process.ErrorDataReceived  += (_, e) => { if (e.Data is { } line) onLine?.Invoke(line); };

        if (!process.Start())
            throw new InvalidOperationException("Failed to start ASTAP process.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);

        // ASTAP writes its result file regardless of exit code, so we parse it
        // first; non-zero exits with no .ini are reported as a launch failure.
        var iniPath = Path.ChangeExtension(fitsPath, ".ini");
        if (!File.Exists(iniPath))
        {
            throw new InvalidOperationException(
                $"ASTAP exited with code {process.ExitCode} and produced no .ini output.");
        }

        var dict = ParseIni(iniPath);

        if (!dict.TryGetValue("PLTSOLVD", out var solved) ||
            !solved.Equals("T", StringComparison.OrdinalIgnoreCase))
        {
            var warn = dict.GetValueOrDefault("WARNING", "no warning text").Trim();
            throw new InvalidOperationException($"ASTAP failed to plate-solve: {warn}");
        }

        var pa = ExtractRotation(dict);
        var raDeg = ParseDouble(dict, "CRVAL1");
        var decDeg = ParseDouble(dict, "CRVAL2");
        // Plate scale: CDELT2 is degrees per pixel along Y; report as arcsec/px.
        var scaleArcsecPx = Math.Abs(ParseDouble(dict, "CDELT2")) * 3600.0;
        if (scaleArcsecPx == 0)
            scaleArcsecPx = ParseDouble(dict, "SCALE");

        return new PlateSolveResult(pa, raDeg, decDeg, scaleArcsecPx);
    }

    private static Dictionary<string, string> ParseIni(string path)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('[')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            dict[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }
        return dict;
    }

    /// <summary>
    /// ASTAP writes the rotation under different keys depending on the build:
    /// CROTA2 is the FITS-standard rotation of axis 2 east of north, but some
    /// versions emit a plain "PA" key.
    /// </summary>
    private static double ExtractRotation(Dictionary<string, string> dict)
    {
        if (TryParseDouble(dict, "CROTA2", out var v)) return v;
        if (TryParseDouble(dict, "PA",     out v)) return v;
        if (TryParseDouble(dict, "CROTA1", out v)) return v;
        throw new InvalidOperationException(
            "ASTAP output has no CROTA2 / PA / CROTA1 rotation key.");
    }

    private static double ParseDouble(Dictionary<string, string> dict, string key) =>
        TryParseDouble(dict, key, out var v) ? v : 0.0;

    private static bool TryParseDouble(Dictionary<string, string> dict, string key, out double value)
    {
        value = 0;
        return dict.TryGetValue(key, out var s)
            && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}

/// <summary>Plate-solve result. All angles in degrees; scale in arcsec/pixel.</summary>
public readonly record struct PlateSolveResult(
    double PositionAngleDeg,
    double CenterRaDeg,
    double CenterDecDeg,
    double ScaleArcsecPerPixel);
