using System.Globalization;
using System.IO;
using System.Text;

namespace Derotate;

/// <summary>
/// Minimal FITS writer for monochrome images, BITPIX=32 (signed integer).
/// ASTAP reads this directly. Big-endian pixel data, padded to 2880-byte
/// blocks per the FITS standard. No third-party dependency.
/// </summary>
public static class FitsWriter
{
    private const int FitsBlockBytes = 2880;
    private const int RecordsPerBlock = 36;   // 2880 / 80
    private const int RecordChars = 80;

    /// <summary>
    /// Write <paramref name="image"/> to <paramref name="path"/>. Image is
    /// width-major (image[x, y]) matching ASCOM Camera.ImageArray.
    /// </summary>
    public static void WriteMono(string path, int[,] image, double exposureSec, DateTime utcStart)
    {
        int width = image.GetLength(0);
        int height = image.GetLength(1);

        using var fs = File.Create(path);
        WriteHeader(fs, width, height, exposureSec, utcStart);
        WriteData(fs, image, width, height);
    }

    private static void WriteHeader(Stream fs, int width, int height, double exposureSec, DateTime utcStart)
    {
        var records = new List<string>
        {
            FormatBoolRecord("SIMPLE", true, "Standard FITS file"),
            FormatIntRecord("BITPIX", 32, "32-bit signed integer pixels"),
            FormatIntRecord("NAXIS", 2, "Number of axes"),
            FormatIntRecord("NAXIS1", width, "Image width (pixels)"),
            FormatIntRecord("NAXIS2", height, "Image height (pixels)"),
            FormatFloatRecord("EXPTIME", exposureSec, "Exposure duration (seconds)"),
            FormatStringRecord("DATE-OBS",
                utcStart.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture),
                "UTC start of exposure"),
            EndRecord(),
        };

        // Pad to a multiple of 36 records (2880 bytes) per FITS spec.
        while (records.Count % RecordsPerBlock != 0)
            records.Add(BlankRecord());

        foreach (var r in records)
            fs.Write(Encoding.ASCII.GetBytes(r), 0, RecordChars);
    }

    private static string FormatBoolRecord(string keyword, bool value, string comment)
    {
        var val = (value ? "T" : "F").PadLeft(20);
        return TruncatePad80($"{keyword.PadRight(8)}= {val} / {comment}");
    }

    private static string FormatIntRecord(string keyword, long value, string comment)
    {
        var val = value.ToString(CultureInfo.InvariantCulture).PadLeft(20);
        return TruncatePad80($"{keyword.PadRight(8)}= {val} / {comment}");
    }

    private static string FormatFloatRecord(string keyword, double value, string comment)
    {
        var val = value.ToString("G15", CultureInfo.InvariantCulture).PadLeft(20);
        return TruncatePad80($"{keyword.PadRight(8)}= {val} / {comment}");
    }

    private static string FormatStringRecord(string keyword, string value, string comment)
    {
        // FITS strings: single-quoted, minimum 8 chars between the quotes.
        var paddedVal = value.Length < 8 ? value.PadRight(8) : value;
        var quoted = $"'{paddedVal}'";
        return TruncatePad80($"{keyword.PadRight(8)}= {quoted} / {comment}");
    }

    private static string EndRecord() => "END".PadRight(RecordChars);
    private static string BlankRecord() => new string(' ', RecordChars);

    private static string TruncatePad80(string s) =>
        s.Length > RecordChars ? s.Substring(0, RecordChars) : s.PadRight(RecordChars);

    private static void WriteData(Stream fs, int[,] image, int width, int height)
    {
        // FITS data is row-major, big-endian. The input image is width-major
        // (image[x, y]) per ASCOM, so we iterate y outer, x inner.
        var rowBytes = new byte[width * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int v = image[x, y];
                int o = x * 4;
                rowBytes[o    ] = (byte)((v >> 24) & 0xff);
                rowBytes[o + 1] = (byte)((v >> 16) & 0xff);
                rowBytes[o + 2] = (byte)((v >> 8)  & 0xff);
                rowBytes[o + 3] = (byte)( v        & 0xff);
            }
            fs.Write(rowBytes, 0, rowBytes.Length);
        }

        // Pad data to a multiple of 2880 bytes.
        long dataBytes = (long)width * height * 4;
        long padBytes = (FitsBlockBytes - (dataBytes % FitsBlockBytes)) % FitsBlockBytes;
        if (padBytes > 0)
            fs.Write(new byte[padBytes], 0, (int)padBytes);
    }
}
