using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;

namespace GeoPoint.Core.Sheets;

/// <summary>
/// Galeria de imagens de satélite do projeto e a escolha de cada prancha: a imagem da prancha, senão a padrão da fase, senão a
/// geral; o recorte (enquadramento com zoom) que preenche o espaço de localização sem distorcer; o contorno do lote por cima.
/// </summary>
public static class LocationImages
{
    /// <summary>Lado maior guardado (1920 px: a resolução de um print Full HD, mais do que suficiente para o espaço da prancha).</summary>
    public const int MaxSide = 1920;

    /// <summary>Converte as imagens da 1.7 (uma por fase/geral) em galeria + padrões. Devolve true se mudou.</summary>
    public static bool Migrate(ParcelProject p)
    {
        if (p.ImagesVersion >= 2) return false;
        foreach (var img in p.SatelliteImages)
            if (!p.ImageDefaults.ContainsKey(img.FaseId)) p.ImageDefaults[img.FaseId] = new SheetImageChoice { ImageId = img.Id };
        p.ImagesVersion = 2;
        return true;
    }

    public static SatelliteImage? Find(ParcelProject p, string? id) => string.IsNullOrEmpty(id) ? null : p.SatelliteImages.FirstOrDefault(i => i.Id == id);

    /// <summary>Imagem da prancha: a escolhida para ela, senão a padrão da fase, senão a geral (null = espaço em branco).</summary>
    public static SheetImageChoice? Resolve(ParcelProject p, string? sheetKey, string? faseId)
    {
        if (!string.IsNullOrEmpty(sheetKey) && p.SheetImages.TryGetValue(sheetKey, out var own))
        {
            if (own.ImageId.Length == 0) return null;                 // "sem imagem nesta prancha"
            if (Find(p, own.ImageId) is not null) return own;
        }
        if (!string.IsNullOrEmpty(faseId) && p.ImageDefaults.TryGetValue(faseId, out var f) && Find(p, f.ImageId) is not null) return f;
        return p.ImageDefaults.TryGetValue("", out var g) && Find(p, g.ImageId) is not null ? g : null;
    }

    /// <summary>De onde vem a imagem da prancha ("desta prancha", "da fase", "geral"; "" = nenhuma).</summary>
    public static string Origin(ParcelProject p, string? sheetKey, string? faseId)
    {
        if (!string.IsNullOrEmpty(sheetKey) && p.SheetImages.TryGetValue(sheetKey, out var own))
        {
            if (own.ImageId.Length == 0) return "nenhuma (escolhido para esta prancha)";
            if (Find(p, own.ImageId) is not null) return "desta prancha";
        }
        if (!string.IsNullOrEmpty(faseId) && p.ImageDefaults.TryGetValue(faseId, out var f) && Find(p, f.ImageId) is not null) return "padrão da fase";
        return p.ImageDefaults.TryGetValue("", out var g) && Find(p, g.ImageId) is not null ? "geral" : "";
    }

    /// <summary>
    /// Usa a imagem em todas as pranchas da fase: vira o padrão da fase e as escolhas próprias das pranchas da fase saem, menos as
    /// fixadas. Devolve as chaves das pranchas que mudam.
    /// </summary>
    public static List<string> ApplyToFase(ParcelProject p, string faseId, SheetImageChoice choice, IEnumerable<string> keysOfFase)
    {
        p.ImageDefaults[faseId] = choice.Clone();
        p.ImageDefaults[faseId].Locked = false;
        var changed = new List<string>();
        foreach (var k in keysOfFase.Distinct())
        {
            if (p.SheetImages.TryGetValue(k, out var own) && own.Locked) continue;
            p.SheetImages.Remove(k);
            changed.Add(k);
        }
        return changed;
    }

    /// <summary>Tira a imagem da galeria e das escolhas que a usavam; devolve a removida.</summary>
    public static SatelliteImage? Remove(ParcelProject p, string id)
    {
        var img = Find(p, id);
        if (img is null) return null;
        p.SatelliteImages.Remove(img);
        foreach (var k in p.SheetImages.Where(x => x.Value.ImageId == id).Select(x => x.Key).ToList()) p.SheetImages.Remove(k);
        foreach (var k in p.ImageDefaults.Where(x => x.Value.ImageId == id).Select(x => x.Key).ToList()) p.ImageDefaults.Remove(k);
        return img;
    }

    /// <summary>Quantas pranchas/padrões usam a imagem.</summary>
    public static int Uses(ParcelProject p, string id) =>
        p.SheetImages.Count(x => x.Value.ImageId == id) + p.ImageDefaults.Count(x => x.Value.ImageId == id);

    /// <summary>
    /// Recorte central que deixa a imagem com a proporção do espaço (<paramref name="aspect"/> = largura / altura) sem distorcer:
    /// a imagem preenche o espaço inteiro e só as sobras das bordas (laterais ou em cima e embaixo) ficam de fora.
    /// </summary>
    public static (int X, int Y, int W, int H) Cover(int width, int height, double aspect)
    {
        if (width <= 0 || height <= 0 || !(aspect > 0) || double.IsInfinity(aspect)) return (0, 0, Math.Max(0, width), Math.Max(0, height));
        double cur = (double)width / height;
        if (Math.Abs(cur - aspect) < 1e-6) return (0, 0, width, height);
        if (cur > aspect)
        {
            int w = Math.Max(1, (int)Math.Round(height * aspect));
            return ((width - w) / 2, 0, w, height);
        }
        int h = Math.Max(1, (int)Math.Round(width / aspect));
        return (0, (height - h) / 2, width, h);
    }

    /// <summary>Recorte com o enquadramento escolhido (centro e zoom), sempre dentro da imagem e na proporção do espaço.</summary>
    public static (int X, int Y, int W, int H) Crop(int width, int height, double aspect, SheetImageChoice? c)
    {
        var (_, _, cw0, ch0) = Cover(width, height, aspect);
        if (c is null) return Cover(width, height, aspect);
        double z = Math.Clamp(c.Zoom, 1, 8);
        int cw = Math.Max(1, (int)Math.Round(cw0 / z)), ch = Math.Max(1, (int)Math.Round(ch0 / z));
        double cx = Math.Clamp(c.CX, 0, 1) * width, cy = Math.Clamp(c.CY, 0, 1) * height;
        int x = (int)Math.Round(Math.Clamp(cx - cw / 2.0, 0, width - cw));
        int y = (int)Math.Round(Math.Clamp(cy - ch / 2.0, 0, height - ch));
        return (x, y, cw, ch);
    }

    /// <summary>Centro do recorte depois de arrastar a imagem <paramref name="dxFrac"/>/<paramref name="dyFrac"/> larguras do espaço.</summary>
    public static (double CX, double CY) Pan(int width, int height, double aspect, SheetImageChoice c, double dxFrac, double dyFrac)
    {
        var (_, _, cw, ch) = Crop(width, height, aspect, c);
        double cx = Math.Clamp(c.CX - dxFrac * cw / width, 0, 1), cy = Math.Clamp(c.CY - dyFrac * ch / height, 0, 1);
        // Mantém o recorte dentro da imagem (o centro não passa da metade do recorte).
        cx = Math.Clamp(cx, cw / 2.0 / width, 1 - cw / 2.0 / width);
        cy = Math.Clamp(cy, ch / 2.0 / height, 1 - ch / 2.0 / height);
        return (cx, cy);
    }

    /// <summary>Tamanho reduzido para guardar (lado maior até <see cref="MaxSide"/>; imagens menores ficam como estão).</summary>
    public static (int W, int H) Reduce(int width, int height, int maxSide = MaxSide)
    {
        if (width <= 0 || height <= 0) return (0, 0);
        int big = Math.Max(width, height);
        if (big <= maxSide) return (width, height);
        double k = (double)maxSide / big;
        return (Math.Max(1, (int)Math.Round(width * k)), Math.Max(1, (int)Math.Round(height * k)));
    }

    /// <summary>
    /// Contornos dos lotes com o norte verdadeiro para cima (como a imagem de satélite), em metros, centrados na origem: pelas
    /// coordenadas geográficas dos vértices (projeto georreferenciado; a UTM giraria o desenho pela convergência meridiana) ou pelas
    /// do desenho.
    /// </summary>
    public static List<List<Vec2>> Outline(ProjectContext ctx, IEnumerable<LotRecord> lots)
    {
        var rings = new List<List<Vec2>>();
        var reps = new List<LotReport>();
        foreach (var lot in lots) { try { reps.Add(ctx.Report(lot)); } catch { } }
        var geo = reps.SelectMany(r => r.Vertices).Where(v => v.Geo is not null).Select(v => v.Geo!.Value).ToList();
        bool useGeo = geo.Count > 0 && reps.All(r => r.Vertices.All(v => v.Geo is not null));
        double lat0 = useGeo ? geo.Average(g => g.LatDeg) : 0, lon0 = useGeo ? geo.Average(g => g.LonDeg) : 0;
        // Raios de curvatura do elipsoide GRS80 na latitude média (meridiano e primeiro vertical): metros por grau.
        const double a = 6378137.0, e2 = 0.00669438002290;
        double sin = Math.Sin(lat0 * Math.PI / 180), w = 1 - e2 * sin * sin;
        double nRad = a / Math.Sqrt(w), mRad = a * (1 - e2) / (w * Math.Sqrt(w));
        double kx = nRad * Math.Cos(lat0 * Math.PI / 180) * Math.PI / 180, ky = mRad * Math.PI / 180;
        foreach (var r in reps)
        {
            var pts = r.Vertices.Select(v => useGeo ? new Vec2((v.Geo!.Value.LonDeg - lon0) * kx, (v.Geo!.Value.LatDeg - lat0) * ky) : v.Internal).ToList();
            if (pts.Count >= 3) rings.Add(pts);
        }
        if (rings.Count == 0) return rings;
        var bb = BBox.Of(rings.SelectMany(x => x));
        var c = bb.Center;
        return rings.Select(r => r.Select(p => p - c).ToList()).ToList();
    }

    /// <summary>
    /// Contorno dentro do espaço (papel, mm, y para cima): largura = <see cref="SheetImageChoice.OutlineSize"/> × a do espaço (sem
    /// passar da altura), deslocado pelo <see cref="SheetImageChoice.OutlineDX"/>/<see cref="SheetImageChoice.OutlineDY"/>.
    /// </summary>
    public static List<List<Vec2>> OutlineInRect(IReadOnlyList<List<Vec2>> rings, PaperRect r, SheetImageChoice c)
    {
        var res = new List<List<Vec2>>();
        if (rings.Count == 0 || r.Width <= 0 || r.Height <= 0) return res;
        var bb = BBox.Of(rings.SelectMany(x => x));
        double w = Math.Max(bb.Width, 1e-6), h = Math.Max(bb.Height, 1e-6);
        double k = Math.Clamp(c.OutlineSize, 0.05, 0.95) * r.Width / w;
        if (h * k > r.Height * 0.95) k = r.Height * 0.95 / h;
        var center = new Vec2(r.Center.X + c.OutlineDX * r.Width, r.Center.Y - c.OutlineDY * r.Height);
        foreach (var ring in rings) res.Add(ring.Select(p => center + p * k).ToList());
        return res;
    }

    /// <summary>Texto curto do escopo ("Geral (todas as pranchas)" ou o nome da fase).</summary>
    public static string ScopeLabel(ParcelProject p, string faseId) =>
        faseId.Length == 0 ? "Geral (todas as pranchas)" : p.Fases.FirstOrDefault(f => f.Id == faseId)?.Label ?? "Fase removida";
}
