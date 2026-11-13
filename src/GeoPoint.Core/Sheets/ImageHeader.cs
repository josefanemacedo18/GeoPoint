namespace GeoPoint.Core.Sheets;

/// <summary>Formato e tamanho (px) de imagens PNG, JPEG, GIF e BMP, lendo só o cabeçalho.</summary>
public static class ImageHeader
{
    public static string? Extension(byte[]? d)
    {
        if (d is null || d.Length < 4) return null;
        if (d[0] == 0x89 && d[1] == (byte)'P' && d[2] == (byte)'N' && d[3] == (byte)'G') return "png";
        if (d[0] == 0xFF && d[1] == 0xD8) return "jpg";
        if (d[0] == (byte)'G' && d[1] == (byte)'I' && d[2] == (byte)'F') return "gif";
        if (d[0] == (byte)'B' && d[1] == (byte)'M') return "bmp";
        return null;
    }

    public static (int W, int H)? Size(byte[]? d)
    {
        switch (Extension(d))
        {
            case "png" when d!.Length >= 24:
                return Ok(Be32(d, 16), Be32(d, 20));
            case "gif" when d!.Length >= 10:
                return Ok(d[6] | d[7] << 8, d[8] | d[9] << 8);
            case "bmp" when d!.Length >= 26:
                return Ok(Le32(d, 18), Math.Abs(Le32(d, 22)));
            case "jpg":
            {
                // Percorre os marcadores até o SOFn (início do quadro), que tem altura e largura.
                int i = 2;
                while (i + 9 < d!.Length)
                {
                    if (d[i] != 0xFF) { i++; continue; }
                    byte m = d[i + 1];
                    if (m == 0xFF || m == 0x01 || m == 0xD8 || (m >= 0xD0 && m <= 0xD7)) { i += m == 0xFF ? 1 : 2; continue; }
                    if (m == 0xD9 || m == 0xDA) return null;
                    int len = d[i + 2] << 8 | d[i + 3];
                    if (m >= 0xC0 && m <= 0xCF && m != 0xC4 && m != 0xC8 && m != 0xCC)
                        return Ok(d[i + 7] << 8 | d[i + 8], d[i + 5] << 8 | d[i + 6]);
                    if (len < 2) return null;
                    i += 2 + len;
                }
                return null;
            }
        }
        return null;
    }

    private static (int W, int H)? Ok(int w, int h) => w > 0 && h > 0 ? (w, h) : null;
    private static int Be32(byte[] d, int i) => d[i] << 24 | d[i + 1] << 16 | d[i + 2] << 8 | d[i + 3];
    private static int Le32(byte[] d, int i) => d[i] | d[i + 1] << 8 | d[i + 2] << 16 | d[i + 3] << 24;
}
