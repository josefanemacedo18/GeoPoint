using GeoPoint.Core.Model;

namespace GeoPoint.Core.Sheets;

/// <summary>
/// Regras das imagens de satélite das pranchas: qual imagem vai em cada prancha (a da fase ou, sem ela, a geral) e o recorte
/// que faz a imagem preencher exatamente o espaço de localização do carimbo.
/// </summary>
public static class LocationImages
{
    /// <summary>Lado maior guardado (1920 px: a resolução de um print Full HD, mais do que suficiente para o espaço da prancha).</summary>
    public const int MaxSide = 1920;

    /// <summary>Imagem da prancha da fase: a específica da fase ou, se ela não tiver, a geral (null = espaço em branco).</summary>
    public static SatelliteImage? For(ParcelProject p, string? faseId)
    {
        if (!string.IsNullOrEmpty(faseId) && p.SatelliteImages.FirstOrDefault(i => i.FaseId == faseId) is { } own) return own;
        return p.SatelliteImages.FirstOrDefault(i => i.FaseId.Length == 0);
    }

    /// <summary>Imagem guardada para o escopo (vazio = geral), sem cair na geral.</summary>
    public static SatelliteImage? Own(ParcelProject p, string? faseId) =>
        p.SatelliteImages.FirstOrDefault(i => i.FaseId == (faseId ?? ""));

    /// <summary>Põe a imagem no escopo dela, no lugar da anterior; devolve a que saiu (para apagar os bytes) ou null.</summary>
    public static SatelliteImage? Set(ParcelProject p, SatelliteImage img)
    {
        var old = Own(p, img.FaseId);
        if (old is not null) p.SatelliteImages.Remove(old);
        p.SatelliteImages.Add(img);
        return old;
    }

    /// <summary>Tira as imagens de fases que não existem mais; devolve as retiradas.</summary>
    public static List<SatelliteImage> Prune(ParcelProject p)
    {
        var ids = p.Fases.Select(f => f.Id).ToHashSet();
        var gone = p.SatelliteImages.Where(i => i.FaseId.Length > 0 && !ids.Contains(i.FaseId)).ToList();
        foreach (var g in gone) p.SatelliteImages.Remove(g);
        // Uma imagem por escopo (a mais recente fica).
        foreach (var dup in p.SatelliteImages.GroupBy(i => i.FaseId).Where(g => g.Count() > 1).SelectMany(g => g.OrderByDescending(i => i.Added).Skip(1)).ToList())
        {
            p.SatelliteImages.Remove(dup);
            gone.Add(dup);
        }
        return gone;
    }

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

    /// <summary>Tamanho reduzido para guardar (lado maior até <see cref="MaxSide"/>; imagens menores ficam como estão).</summary>
    public static (int W, int H) Reduce(int width, int height, int maxSide = MaxSide)
    {
        if (width <= 0 || height <= 0) return (0, 0);
        int big = Math.Max(width, height);
        if (big <= maxSide) return (width, height);
        double k = (double)maxSide / big;
        return (Math.Max(1, (int)Math.Round(width * k)), Math.Max(1, (int)Math.Round(height * k)));
    }

    /// <summary>Texto curto do escopo ("Geral (todas as pranchas)" ou o nome da fase).</summary>
    public static string ScopeLabel(ParcelProject p, string faseId) =>
        faseId.Length == 0 ? "Geral (todas as pranchas)" : p.Fases.FirstOrDefault(f => f.Id == faseId)?.Label ?? "Fase removida";
}
