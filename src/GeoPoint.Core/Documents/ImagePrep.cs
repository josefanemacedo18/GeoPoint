namespace GeoPoint.Core.Documents;

/// <summary>Imagem em tons de cinza (1 byte por pixel, linha a linha).</summary>
public sealed class GrayImage
{
    public GrayImage(int width, int height, byte[]? px = null)
    {
        Width = width;
        Height = height;
        Px = px ?? new byte[width * height];
        if (Px.Length != width * height) throw new ArgumentException("Tamanho dos pixels não confere.");
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Px { get; }
    public byte this[int x, int y] => Px[y * Width + x];

    /// <summary>De BGRA (32 bits, como o Windows entrega) para cinza: Y = 0,299 R + 0,587 G + 0,114 B.</summary>
    public static GrayImage FromBgra(byte[] bgra, int width, int height, int stride)
    {
        var g = new GrayImage(width, height);
        for (int y = 0; y < height; y++)
        {
            int row = y * stride;
            for (int x = 0; x < width; x++)
            {
                int i = row + x * 4;
                g.Px[y * width + x] = (byte)((bgra[i + 2] * 299 + bgra[i + 1] * 587 + bgra[i] * 114 + 500) / 1000);
            }
        }
        return g;
    }

    /// <summary>Arquivo PGM (P5) – formato simples, usado nos testes com o Tesseract de linha de comando.</summary>
    public byte[] ToPgm()
    {
        var head = System.Text.Encoding.ASCII.GetBytes($"P5\n{Width} {Height}\n255\n");
        var res = new byte[head.Length + Px.Length];
        head.CopyTo(res, 0);
        Px.CopyTo(res, head.Length);
        return res;
    }

    public static GrayImage FromPgm(byte[] data)
    {
        int pos = 0;
        string Token()
        {
            while (pos < data.Length && (char.IsWhiteSpace((char)data[pos]) || data[pos] == '#'))
            {
                if (data[pos] == '#') while (pos < data.Length && data[pos] != '\n') pos++;
                else pos++;
            }
            int s = pos;
            while (pos < data.Length && !char.IsWhiteSpace((char)data[pos])) pos++;
            return System.Text.Encoding.ASCII.GetString(data, s, pos - s);
        }
        if (Token() != "P5") throw new InvalidDataException("Não é PGM binário (P5).");
        int w = int.Parse(Token()), h = int.Parse(Token());
        _ = Token();
        pos++;
        var px = new byte[w * h];
        Array.Copy(data, pos, px, 0, px.Length);
        return new GrayImage(w, h, px);
    }
}

/// <summary>Qualidade da foto/digitalização, avaliada antes do OCR.</summary>
public sealed class ImageQualityReport
{
    public int ShortSide { get; init; }
    /// <summary>Brilho médio (0–255).</summary>
    public double Mean { get; init; }
    /// <summary>Contraste: desvio padrão dos tons (0–128).</summary>
    public double Contrast { get; init; }
    /// <summary>Nitidez: variância do laplaciano (foto tremida ou fora de foco = baixa).</summary>
    public double Sharpness { get; init; }
    /// <summary>Iluminação desigual: diferença de brilho entre as regiões claras da página (sombra, reflexo), 0–255.</summary>
    public double Unevenness { get; init; }
    public List<string> Problems { get; } = new();
    public bool Good => Problems.Count == 0;
    public string Summary => Good ? "Foto boa para leitura." : string.Join(" ", Problems);
}

/// <summary>
/// Preparação da imagem para o OCR (o que mais melhorou a leitura de fotos de celular nos testes): tons de cinza, ampliação até as
/// letras ficarem com tamanho bom, esticamento do contraste e nitidez; a correção da iluminação (sombra) é uma segunda tentativa –
/// numa foto com luz boa ela piora a leitura.
/// </summary>
public static class ImagePrep
{
    /// <summary>Lado menor ideal da página para o OCR (uma foto de 900 px de largura é ampliada ~2,7×).</summary>
    public const int TargetShortSide = 2400;

    /// <summary>Fator de escala para o lado menor chegar perto de <see cref="TargetShortSide"/> (entre 0,5 e 3).</summary>
    public static double ScaleFor(int width, int height)
    {
        int s = Math.Min(width, height);
        if (s <= 0) return 1;
        double k = (double)TargetShortSide / s;
        if (k > 0.85 && k < 1.2) return 1;          // já está bom
        return Math.Clamp(k, 0.5, 3.0);
    }

    /// <summary>Pipeline completo. <paramref name="scale"/> devolve o fator usado (para voltar as posições das palavras).</summary>
    public static GrayImage Prepare(GrayImage src, out double scale, bool flatten = false, bool sharpen = true)
    {
        scale = ScaleFor(src.Width, src.Height);
        var g = Math.Abs(scale - 1) > 1e-9 ? Resize(src, scale) : src;
        if (flatten) g = FlattenIllumination(g);
        g = Stretch(g, 0.01, 0.99);
        if (sharpen) g = Sharpen(g, 1.0);
        return g;
    }

    /// <summary>Redimensiona (bilinear; para reduzir muito, média de blocos).</summary>
    public static GrayImage Resize(GrayImage s, double k)
    {
        int w = Math.Max(1, (int)Math.Round(s.Width * k)), h = Math.Max(1, (int)Math.Round(s.Height * k));
        var d = new GrayImage(w, h);
        if (k < 0.5)
        {
            // Média dos pixels de cada bloco (evita serrilhado ao reduzir).
            for (int y = 0; y < h; y++)
            {
                int y0 = (int)(y / k), y1 = Math.Min(s.Height, (int)((y + 1) / k));
                for (int x = 0; x < w; x++)
                {
                    int x0 = (int)(x / k), x1 = Math.Min(s.Width, (int)((x + 1) / k));
                    long sum = 0; int n = 0;
                    for (int yy = y0; yy < Math.Max(y1, y0 + 1); yy++)
                        for (int xx = x0; xx < Math.Max(x1, x0 + 1); xx++) { sum += s[Math.Min(xx, s.Width - 1), Math.Min(yy, s.Height - 1)]; n++; }
                    d.Px[y * w + x] = (byte)(sum / Math.Max(1, n));
                }
            }
            return d;
        }
        for (int y = 0; y < h; y++)
        {
            double sy = Math.Clamp((y + 0.5) / k - 0.5, 0, s.Height - 1);
            int y0 = (int)sy, y1 = Math.Min(y0 + 1, s.Height - 1);
            double fy = sy - y0;
            for (int x = 0; x < w; x++)
            {
                double sx = Math.Clamp((x + 0.5) / k - 0.5, 0, s.Width - 1);
                int x0 = (int)sx, x1 = Math.Min(x0 + 1, s.Width - 1);
                double fx = sx - x0;
                double v = (s[x0, y0] * (1 - fx) + s[x1, y0] * fx) * (1 - fy) + (s[x0, y1] * (1 - fx) + s[x1, y1] * fx) * fy;
                d.Px[y * w + x] = (byte)Math.Clamp((int)Math.Round(v), 0, 255);
            }
        }
        return d;
    }

    /// <summary>Estica os tons: o percentil <paramref name="lo"/> vira preto e o <paramref name="hi"/> vira branco.</summary>
    public static GrayImage Stretch(GrayImage s, double lo, double hi)
    {
        var hist = new long[256];
        foreach (var p in s.Px) hist[p]++;
        long n = s.Px.Length, acc = 0;
        int a = 0, b = 255;
        for (int i = 0; i < 256; i++) { acc += hist[i]; if (acc >= n * lo) { a = i; break; } }
        acc = 0;
        for (int i = 255; i >= 0; i--) { acc += hist[i]; if (acc >= n * (1 - hi)) { b = i; break; } }
        if (b - a < 10) return s;
        var lut = new byte[256];
        for (int i = 0; i < 256; i++) lut[i] = (byte)Math.Clamp((i - a) * 255 / (b - a), 0, 255);
        var d = new GrayImage(s.Width, s.Height);
        for (int i = 0; i < s.Px.Length; i++) d.Px[i] = lut[s.Px[i]];
        return d;
    }

    /// <summary>Nitidez (máscara de desfoque): realça a borda das letras.</summary>
    public static GrayImage Sharpen(GrayImage s, double amount)
    {
        var blur = Blur3(s);
        var d = new GrayImage(s.Width, s.Height);
        for (int i = 0; i < s.Px.Length; i++)
            d.Px[i] = (byte)Math.Clamp((int)Math.Round(s.Px[i] + amount * (s.Px[i] - blur.Px[i])), 0, 255);
        return d;
    }

    /// <summary>Desfoque 3×3 (1-2-1).</summary>
    public static GrayImage Blur3(GrayImage s)
    {
        int w = s.Width, h = s.Height;
        var tmp = new int[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int l = s[Math.Max(0, x - 1), y], c = s[x, y], r = s[Math.Min(w - 1, x + 1), y];
                tmp[y * w + x] = l + 2 * c + r;
            }
        var d = new GrayImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int u = tmp[Math.Max(0, y - 1) * w + x], c = tmp[y * w + x], b = tmp[Math.Min(h - 1, y + 1) * w + x];
                d.Px[y * w + x] = (byte)((u + 2 * c + b + 8) / 16);
            }
        return d;
    }

    /// <summary>
    /// Corrige sombra e iluminação desigual: estima o fundo (papel) pelo tom mais claro de cada bloco e divide a imagem por ele –
    /// o papel fica branco por igual e as letras continuam escuras.
    /// </summary>
    public static GrayImage FlattenIllumination(GrayImage s, int block = 0)
    {
        int w = s.Width, h = s.Height;
        if (block <= 0) block = Math.Max(16, Math.Min(w, h) / 24);
        int bw = (w + block - 1) / block, bh = (h + block - 1) / block;
        var bg = new double[bw * bh];
        var hist = new int[256];
        for (int by = 0; by < bh; by++)
            for (int bx = 0; bx < bw; bx++)
            {
                Array.Clear(hist);
                int n = 0;
                for (int y = by * block; y < Math.Min(h, (by + 1) * block); y++)
                    for (int x = bx * block; x < Math.Min(w, (bx + 1) * block); x++) { hist[s[x, y]]++; n++; }
                // Fundo = percentil 90 do bloco (o papel; as letras são poucas e escuras).
                int acc = 0, v = 255;
                for (int i = 255; i >= 0; i--) { acc += hist[i]; if (acc >= n * 0.10) { v = i; break; } }
                bg[by * bw + bx] = Math.Max(v, 1);
            }
        // Suaviza o fundo entre blocos vizinhos.
        var sm = new double[bg.Length];
        for (int by = 0; by < bh; by++)
            for (int bx = 0; bx < bw; bx++)
            {
                double sum = 0; int n = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = bx + dx, yy = by + dy;
                        if (xx < 0 || yy < 0 || xx >= bw || yy >= bh) continue;
                        sum += bg[yy * bw + xx]; n++;
                    }
                sm[by * bw + bx] = sum / n;
            }
        var d = new GrayImage(w, h);
        for (int y = 0; y < h; y++)
        {
            double gy = Math.Clamp((y + 0.5) / block - 0.5, 0, bh - 1);
            int y0 = (int)gy, y1 = Math.Min(y0 + 1, bh - 1);
            double fy = gy - y0;
            for (int x = 0; x < w; x++)
            {
                double gx = Math.Clamp((x + 0.5) / block - 0.5, 0, bw - 1);
                int x0 = (int)gx, x1 = Math.Min(x0 + 1, bw - 1);
                double fx = gx - x0;
                double b = (sm[y0 * bw + x0] * (1 - fx) + sm[y0 * bw + x1] * fx) * (1 - fy) + (sm[y1 * bw + x0] * (1 - fx) + sm[y1 * bw + x1] * fx) * fy;
                d.Px[y * w + x] = (byte)Math.Clamp((int)Math.Round(s[x, y] * 255.0 / b), 0, 255);
            }
        }
        return d;
    }

    /// <summary>Qualidade da imagem para o OCR: tamanho, brilho, contraste, nitidez e iluminação.</summary>
    public static ImageQualityReport Assess(GrayImage s)
    {
        // Avalia numa cópia de até ~1200 px (rápido e com a nitidez medida na mesma escala para qualquer câmera).
        var g = Math.Min(s.Width, s.Height) > 1200 ? Resize(s, 1200.0 / Math.Min(s.Width, s.Height)) : s;
        double mean = g.Px.Average(p => (double)p);
        double var = g.Px.Average(p => (p - mean) * (p - mean));
        // Laplaciano 4-vizinhos.
        double lsum = 0, lsq = 0; long n = 0;
        for (int y = 1; y < g.Height - 1; y++)
            for (int x = 1; x < g.Width - 1; x++)
            {
                int l = 4 * g[x, y] - g[x - 1, y] - g[x + 1, y] - g[x, y - 1] - g[x, y + 1];
                lsum += l; lsq += (double)l * l; n++;
            }
        double lapVar = n == 0 ? 0 : lsq / n - (lsum / n) * (lsum / n);
        // Iluminação: tom do papel (percentil 90) em uma grade 4×4.
        var paper = new List<double>();
        int cw = g.Width / 4, ch = g.Height / 4;
        for (int by = 0; by < 4; by++)
            for (int bx = 0; bx < 4; bx++)
            {
                var vals = new List<byte>();
                for (int y = by * ch; y < (by + 1) * ch; y += 2)
                    for (int x = bx * cw; x < (bx + 1) * cw; x += 2) vals.Add(g[x, y]);
                if (vals.Count == 0) continue;
                vals.Sort();
                paper.Add(vals[(int)(vals.Count * 0.9)]);
            }
        double uneven = paper.Count == 0 ? 0 : paper.Max() - paper.Min();
        var r = new ImageQualityReport
        {
            ShortSide = Math.Min(s.Width, s.Height), Mean = mean, Contrast = Math.Sqrt(var), Sharpness = lapVar, Unevenness = uneven,
        };
        if (r.ShortSide < 700) r.Problems.Add($"Imagem pequena ({s.Width}×{s.Height} px): fotografe mais perto ou com mais resolução.");
        if (mean < 90) r.Problems.Add("Foto escura: use mais luz.");
        if (r.Contrast < 25) r.Problems.Add("Pouco contraste entre as letras e o papel.");
        if (lapVar < 60) r.Problems.Add("Foto pouco nítida (tremida ou fora de foco): segure firme e espere o foco.");
        if (uneven > 110) r.Problems.Add("Iluminação desigual (sombra ou reflexo): fotografe sem sombra sobre a página.");
        return r;
    }
}
