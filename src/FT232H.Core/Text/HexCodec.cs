using System.Text;

namespace FT232H.Core.Text;

/// <summary>
/// Hexadecimal/ASCII/binary converters for the transmit editors and receive viewers.
/// </summary>
public static class HexCodec
{
    /// <summary>
    /// Parses hex text into bytes. Accepts spaces, commas, dashes, colons,
    /// "0x" prefixes and arbitrary whitespace. Throws <see cref="FormatException"/>
    /// on invalid input (odd nibble count or non-hex characters).
    /// </summary>
    public static byte[] Parse(string hexText)
    {
        if (!TryParse(hexText, out var bytes, out var error))
            throw new FormatException(error ?? "Invalid hexadecimal input.");
        return bytes;
    }

    public static bool TryParse(string? hexText, out byte[] bytes, out string? error)
    {
        bytes = Array.Empty<byte>();
        error = null;

        if (hexText is null)
        {
            error = "Input is null.";
            return false;
        }

        var nibbles = new List<byte>(hexText.Length / 2);
        int i = 0;
        while (i < hexText.Length)
        {
            char c = hexText[i];
            if (char.IsWhiteSpace(c) || c is ',' or '-' or ':' or '_')
            {
                i++;
                continue;
            }

            if (c == '0' && i + 1 < hexText.Length && (hexText[i + 1] == 'x' || hexText[i + 1] == 'X'))
            {
                i += 2;
                continue;
            }

            int value = HexValue(c);
            if (value < 0)
            {
                error = $"Invalid hex character '{c}' at position {i + 1}.";
                return false;
            }

            nibbles.Add((byte)value);
            i++;
        }

        if (nibbles.Count % 2 != 0)
        {
            error = $"Odd number of hex digits ({nibbles.Count}); bytes require two digits each.";
            return false;
        }

        bytes = new byte[nibbles.Count / 2];
        for (int b = 0; b < bytes.Length; b++)
            bytes[b] = (byte)((nibbles[b * 2] << 4) | nibbles[b * 2 + 1]);
        return true;
    }

    /// <summary>Formats bytes as uppercase hex separated by spaces (e.g. "DE AD BE EF").</summary>
    public static string ToHex(ReadOnlySpan<byte> data, string separator = " ")
    {
        if (data.IsEmpty) return string.Empty;
        var sb = new StringBuilder(data.Length * 3);
        for (int i = 0; i < data.Length; i++)
        {
            if (i > 0) sb.Append(separator);
            sb.Append(data[i].ToString("X2"));
        }
        return sb.ToString();
    }

    /// <summary>Formats bytes as ASCII with non-printable characters replaced.</summary>
    public static string ToAscii(ReadOnlySpan<byte> data, char nonPrintable = '.')
    {
        var sb = new StringBuilder(data.Length);
        foreach (byte b in data)
            sb.Append(b is >= 0x20 and < 0x7F ? (char)b : nonPrintable);
        return sb.ToString();
    }

    /// <summary>Formats bytes as space-separated 8-bit binary groups (e.g. "11011110 10101101").</summary>
    public static string ToBinary(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return string.Empty;
        var sb = new StringBuilder(data.Length * 9);
        for (int i = 0; i < data.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(Convert.ToString(data[i], 2).PadLeft(8, '0'));
        }
        return sb.ToString();
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };
}