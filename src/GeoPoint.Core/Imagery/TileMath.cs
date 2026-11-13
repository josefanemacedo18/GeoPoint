using GeoPoint.Core.Geometry;

namespace GeoPoint.Core.Imagery;

/// <summary>Fornecedores de imagem de satélite para o croqui de localização.</summary>
public enum ImageryProvider { Esri = 0, Google = 1, Personalizado = 2, Nenhum = 3 }

/// <summary>Configuração do croqui automático.</summary>
public sealed class ImagerySettings
{
    public ImageryProvider Provider { get; set; } = ImageryProvider.Esri;
    /// <summary>URL de tiles XYZ com {z}, {x}, {y} (ou {q} = quadkey). Usada no fornecedor personalizado.</summary>
    public string CustomUrl { get; set; } = "";
    public string CustomAttribution { get; set; } = "";
    /// <summary>Chave da Google Maps Static API (fornecedor Google).</summary>
    public string GoogleKey { get; set; } = "";
    /// <summary>O usuário confirmou que tem direito de uso das imagens do fornecedor (termos de uso/licença).</summary>
    public bool AcceptedTerms { get; set; }
    /// <summary>Quanto de vizinhança mostrar: largura da imagem = largura do imóvel × este fator.</summary>
    public double Context { get; set; } = 3.0;
    public int MaxZoom { get; set; } = 19;
    /// <summary>Resolução do croqui na folha (pontos por polegada).</summary>
    public int Dpi { get; set; } = 220;

    public static string EsriUrl => "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}";
    public static string EsriAttribution => "Imagem: Esri, Maxar, Earthstar Geographics";

    public string Attribution => Provider switch
    {
        ImageryProvider.Esri => EsriAttribution,
        ImageryProvider.Google => "Imagem: Google",
        ImageryProvider.Personalizado => CustomAttribution,
        _ => "",
    };

    public string LicenseNote => Provider switch
    {
        ImageryProvider.Esri => "Esri World Imagery: uso sujeito aos termos da Esri (para uso comercial pode ser exigida conta/licença ArcGIS). A atribuição é impressa no croqui.",
        ImageryProvider.Google => "Google Maps Static API: exige chave de API, faturamento ativo e o cumprimento dos termos do Google Maps Platform (o logotipo/atribuição do Google fica na imagem).",
        ImageryProvider.Personalizado => "Servidor de tiles próprio ou de terceiros: confira se a licença permite imprimir as imagens em documentos.",
        _ => "",
    };
}

/// <summary>Contas de tiles Web Mercator (EPSG:3857, tiles de 256 px).</summary>
public static class TileMath
{
    public const int TileSize = 256;
    private const double MaxLat = 85.05112878;

    /// <summary>Pixel global (no zoom <paramref name="z"/>) de uma longitude/latitude.</summary>
    public static Vec2 ToPixel(double lonDeg, double latDeg, int z)
    {
        double lat = Math.Clamp(latDeg, -MaxLat, MaxLat) * Math.PI / 180;
        double n = TileSize * Math.Pow(2, z);
        double x = (lonDeg + 180) / 360 * n;
        double y = (1 - Math.Log(Math.Tan(lat) + 1 / Math.Cos(lat)) / Math.PI) / 2 * n;
        return new Vec2(x, y);
    }

    public static (double Lon, double Lat) FromPixel(Vec2 p, int z)
    {
        double n = TileSize * Math.Pow(2, z);
        double lon = p.X / n * 360 - 180;
        double lat = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * p.Y / n))) * 180 / Math.PI;
        return (lon, lat);
    }

    /// <summary>Quadkey (Bing / alguns servidores).</summary>
    public static string QuadKey(int x, int y, int z)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = z; i > 0; i--)
        {
            int digit = 0, mask = 1 << (i - 1);
            if ((x & mask) != 0) digit++;
            if ((y & mask) != 0) digit += 2;
            sb.Append(digit);
        }
        return sb.ToString();
    }

    public static string TileUrl(string template, int x, int y, int z) =>
        template.Replace("{z}", z.ToString()).Replace("{x}", x.ToString()).Replace("{y}", y.ToString()).Replace("{q}", QuadKey(x, y, z));
}

/// <summary>Enquadramento do croqui: área (lon/lat) com a proporção do bloco e o zoom dos tiles.</summary>
public sealed class CroquiFrame
{
    public int Zoom { get; init; }
    /// <summary>Canto superior esquerdo da imagem em pixels globais do zoom.</summary>
    public Vec2 Origin { get; init; }
    public int WidthPx { get; init; }
    public int HeightPx { get; init; }
    /// <summary>Escala final (pixels da imagem por pixel do zoom).</summary>
    public double Resample { get; init; } = 1;
    public List<(int X, int Y)> Tiles { get; } = new();

    /// <summary>Posição (pixels da imagem final) de uma longitude/latitude.</summary>
    public Vec2 ImagePixel(double lon, double lat) => (TileMath.ToPixel(lon, lat, Zoom) - Origin) * Resample;

    /// <summary>
    /// Escolhe o zoom e a janela para mostrar o polígono (lon/lat) com folga (<paramref name="context"/> × o tamanho do imóvel),
    /// na proporção do bloco (<paramref name="aspect"/> = largura/altura) e com <paramref name="widthPx"/> pixels de largura.
    /// </summary>
    public static CroquiFrame Fit(IReadOnlyList<(double Lon, double Lat)> poly, double aspect, int widthPx, double context = 3, int maxZoom = 19, double minSpanM = 120)
    {
        if (poly.Count == 0) throw new ArgumentException("Polígono vazio.");
        aspect = Math.Clamp(aspect, 0.2, 5);
        int heightPx = Math.Max(16, (int)Math.Round(widthPx / aspect));
        double latC = poly.Average(p => p.Lat);
        // Tamanho mínimo da janela (m) para mostrar a vizinhança mesmo em lotes pequenos.
        double mPerDegLat = 111320, mPerDegLon = 111320 * Math.Cos(latC * Math.PI / 180);
        double spanX = (poly.Max(p => p.Lon) - poly.Min(p => p.Lon)) * mPerDegLon;
        double spanY = (poly.Max(p => p.Lat) - poly.Min(p => p.Lat)) * mPerDegLat;
        double wantW = Math.Max(Math.Max(spanX, spanY * aspect) * context, minSpanM);
        // Metros por pixel no zoom z (Web Mercator): 156543.03 * cos(lat) / 2^z.
        double mppWanted = wantW / widthPx;
        int z = (int)Math.Floor(Math.Log2(156543.03392 * Math.Cos(latC * Math.PI / 180) / mppWanted));
        z = Math.Clamp(z, 1, maxZoom);
        double mppZ = 156543.03392 * Math.Cos(latC * Math.PI / 180) / Math.Pow(2, z);
        double resample = mppZ / mppWanted;          // >= 1 quando o zoom máximo limita
        var center = TileMath.ToPixel(poly.Average(p => p.Lon), latC, z);
        double wZ = widthPx / resample, hZ = heightPx / resample;
        var origin = new Vec2(center.X - wZ / 2, center.Y - hZ / 2);
        var f = new CroquiFrame { Zoom = z, Origin = origin, WidthPx = widthPx, HeightPx = heightPx, Resample = resample };
        int n = 1 << z;
        int tx0 = (int)Math.Floor(origin.X / TileMath.TileSize), tx1 = (int)Math.Floor((origin.X + wZ) / TileMath.TileSize);
        int ty0 = (int)Math.Floor(origin.Y / TileMath.TileSize), ty1 = (int)Math.Floor((origin.Y + hZ) / TileMath.TileSize);
        for (int ty = Math.Max(0, ty0); ty <= Math.Min(n - 1, ty1); ty++)
            for (int tx = tx0; tx <= tx1; tx++)
                f.Tiles.Add((tx, ty));   // x sem "dar a volta" (o chamador usa ((x % n) + n) % n na URL)
        return f;
    }
}
