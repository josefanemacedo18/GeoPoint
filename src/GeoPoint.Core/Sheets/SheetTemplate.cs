using System.Text.Json;
using System.Text.Json.Serialization;
using GeoPoint.Core.Settings;

namespace GeoPoint.Core.Sheets;

/// <summary>Blocos do carimbo (modelo baseado na folha "SITUAÇÃO ATUAL – FOLHA 02").</summary>
public enum SheetBlockKind
{
    Identificacao, Registro, QuadroAreas, Assinaturas, Convencoes, Orientacao, Croqui, Titulo, Escritorio, Observacoes,
}

/// <summary>Um bloco do carimbo: posição no papel (mm, origem no canto inferior esquerdo da folha).</summary>
public sealed class SheetBlock
{
    public SheetBlockKind Kind { get; set; }
    /// <summary>Título impresso no cabeçalho do bloco (vazio = sem cabeçalho).</summary>
    public string Title { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public bool Visible { get; set; } = true;
    /// <summary>Texto livre (bloco Observações/Escritório).</summary>
    public string Text { get; set; } = "";

    [JsonIgnore] public PaperRect Rect => new(X, Y, X + W, Y + H);
    public SheetBlock Clone() => (SheetBlock)MemberwiseClone();
}

/// <summary>Altura (mm no papel) de cada tipo de texto da prancha.</summary>
public sealed class SheetTextStyles
{
    public string Font { get; set; } = "Arial";
    public double TituloMm { get; set; } = 3.5;
    public double CabecalhoMm { get; set; } = 1.9;
    public double RotuloMm { get; set; } = 1.6;
    public double ValorMm { get; set; } = 1.8;
    public double TabelaMm { get; set; } = 1.6;
    public double GradeMm { get; set; } = 1.8;
    /// <summary>Altura dos textos do desenho (distâncias, confrontantes...). 0 = a das Configurações de anotação.</summary>
    public double DesenhoMm { get; set; }
}

public enum MapCorner { InferiorEsquerdo = 0, InferiorDireito = 1, SuperiorDireito = 2, SuperiorEsquerdo = 3 }

/// <summary>Versão anterior de um modelo (histórico).</summary>
public sealed class SheetTemplateVersion
{
    public int Version { get; set; }
    public DateTime Date { get; set; }
    public string Note { get; set; } = "";
    public string Json { get; set; } = "";
}

/// <summary>
/// Modelo de prancha: papel, margens, blocos do carimbo, grade de coordenadas, norte, tabelas e estilos de texto.
/// Fica numa biblioteca (pasta "modelos-prancha") para ter vários modelos (ex.: um por escritório).
/// </summary>
public sealed class SheetTemplate
{
    public string Name { get; set; } = "GeoPoint – Padrão";
    public string Description { get; set; } = "";
    public int Version { get; set; } = 1;
    public DateTime Modified { get; set; } = DateTime.Now;
    public List<SheetTemplateVersion> History { get; set; } = new();

    /// <summary>"A4", "A3" ou "A2".</summary>
    public string Paper { get; set; } = "A4";
    public SheetOrientation Orientation { get; set; } = SheetOrientation.Retrato;
    /// <summary>Margem esquerda (encadernação) e demais margens (mm).</summary>
    public double MarginLeft { get; set; } = 25;
    public double Margin { get; set; } = 7;
    /// <summary>Faixa entre a moldura e o mapa, onde ficam os valores da grade (mm).</summary>
    public double MapLabelBand { get; set; } = 6;
    /// <summary>Espaço entre os blocos (mm).</summary>
    public double Gap { get; set; } = 1.5;

    public List<SheetBlock> Blocks { get; set; } = new();
    public SheetTextStyles Styles { get; set; } = new();

    /// <summary>Linhas de grade (coordenadas) atrás do desenho, com os valores na borda do mapa.</summary>
    public bool Grid { get; set; } = true;
    /// <summary>Espaçamento desejado entre as linhas da grade no papel (mm); o valor real é "redondo".</summary>
    public double GridSpacingMm { get; set; } = 40;
    public byte[] GridColor { get; set; } = { 175, 175, 175 };
    public MapCorner NorthCorner { get; set; } = MapCorner.SuperiorEsquerdo;
    public double NorthSizeMm { get; set; } = 14;
    /// <summary>Tabela de coordenadas (e quadro de lotes) dentro do mapa, num canto livre.</summary>
    public bool TablesInsideMap { get; set; } = true;
    public MapCorner TableCorner { get; set; } = MapCorner.InferiorEsquerdo;
    /// <summary>Escalas permitidas (1:n). Vazio = escalas usuais.</summary>
    public List<int> Scales { get; set; } = new();
    /// <summary>Folga em volta do desenho para as anotações (mm no papel).</summary>
    public double DrawingMarginMm { get; set; } = 14;
    /// <summary>Logomarca embutida no modelo (base64). O modelo leva a imagem junto quando é exportado ou copiado para outro computador.</summary>
    public string LogoData { get; set; } = "";
    /// <summary>Extensão da logomarca embutida ("png", "jpg", "gif" ou "bmp").</summary>
    public string LogoExt { get; set; } = "";
    /// <summary>Arquivo da logomarca (versões até 1.5.2). Só é usado quando não há logomarca embutida.</summary>
    public string LogoPath { get; set; } = "";
    public string OfficeName { get; set; } = "";

    /// <summary>Tamanho máximo da logomarca embutida.</summary>
    public const int MaxLogoBytes = 3 * 1024 * 1024;

    /// <summary>Bytes da logomarca (embutida ou, nos modelos antigos, lida do arquivo); null = sem logomarca.</summary>
    public byte[]? LogoBytes()
    {
        if (!string.IsNullOrWhiteSpace(LogoData))
        {
            try { return Convert.FromBase64String(LogoData); } catch { return null; }
        }
        try { return !string.IsNullOrWhiteSpace(LogoPath) && File.Exists(LogoPath) ? File.ReadAllBytes(LogoPath) : null; } catch { return null; }
    }

    [JsonIgnore] public bool HasLogo => LogoBytes() is { Length: > 0 } b && ImageHeader.Size(b) is not null;

    /// <summary>Largura/altura da logomarca (0 = sem logomarca ou formato desconhecido).</summary>
    public double LogoAspect() => ImageHeader.Size(LogoBytes()) is { W: > 0, H: > 0 } z ? (double)z.W / z.H : 0;

    /// <summary>Embute a logomarca no modelo (null ou vazio = remove). Aceita PNG, JPG, GIF e BMP até 3 MB.</summary>
    public void SetLogo(byte[]? bytes)
    {
        LogoPath = "";
        if (bytes is null || bytes.Length == 0) { LogoData = ""; LogoExt = ""; return; }
        if (bytes.Length > MaxLogoBytes) throw new InvalidOperationException("A logomarca tem mais de 3 MB. Reduza a imagem (uma largura de 1000 px é suficiente para a prancha).");
        string ext = ImageHeader.Extension(bytes) ?? throw new InvalidOperationException("Formato de imagem não reconhecido. Use PNG, JPG, GIF ou BMP.");
        if (ImageHeader.Size(bytes) is null) throw new InvalidOperationException("Não foi possível ler o tamanho da imagem.");
        LogoData = Convert.ToBase64String(bytes);
        LogoExt = ext;
    }

    [JsonIgnore] public IReadOnlyList<int> ScaleList => Scales.Count > 0 ? Scales.OrderBy(x => x).ToList() : SheetLayout.StandardScales;

    public (double W, double H) PaperSize()
    {
        var (a, b) = Paper.ToUpperInvariant() switch
        {
            "A3" => (297.0, 420.0),
            "A2" => (420.0, 594.0),
            "A1" => (594.0, 841.0),
            _ => (210.0, 297.0),
        };
        return Orientation == SheetOrientation.Paisagem ? (b, a) : (a, b);
    }

    public SheetBlock? Block(SheetBlockKind k) => Blocks.FirstOrDefault(b => b.Kind == k && b.Visible);

    public SheetTemplate Clone() =>
        JsonSerializer.Deserialize<SheetTemplate>(JsonSerializer.Serialize(this, JsonOptions.Default), JsonOptions.Default)!;

    // ------------------------------------------------------------------ composição

    /// <summary>Moldura (dentro das margens).</summary>
    public PaperRect Frame()
    {
        var (w, h) = PaperSize();
        return new PaperRect(MarginLeft, Margin, w - Margin, h - Margin);
    }

    /// <summary>Área ocupada pelos blocos visíveis (união dos retângulos).</summary>
    public PaperRect? BlocksBounds()
    {
        var bs = Blocks.Where(b => b.Visible && b.W > 0 && b.H > 0).ToList();
        if (bs.Count == 0) return null;
        return new PaperRect(bs.Min(b => b.X), bs.Min(b => b.Y), bs.Max(b => b.X + b.W), bs.Max(b => b.Y + b.H));
    }

    /// <summary>
    /// Retângulo do mapa (onde fica a vista): a moldura menos a faixa dos blocos (embaixo no retrato, à direita na paisagem)
    /// e menos a faixa dos valores da grade.
    /// </summary>
    public PaperRect MapRect()
    {
        var f = Frame();
        double band = Grid ? MapLabelBand : Gap + 1;
        double x0 = f.X0, y0 = f.Y0, x1 = f.X1, y1 = f.Y1;
        if (BlocksBounds() is PaperRect bb)
        {
            // Os blocos ocupam uma faixa inteira: embaixo (retrato) ou à direita (paisagem).
            bool right = bb.Height > bb.Width * 0.9 && bb.X0 > f.X0 + f.Width * 0.4;
            if (right) x1 = bb.X0; else y0 = bb.Y1;
        }
        return new PaperRect(x0 + band, y0 + band, x1 - band, y1 - band);
    }

    // ------------------------------------------------------------------ modelos padrão

    public static SheetTemplate Default(SheetOrientation o = SheetOrientation.Retrato, string paper = "A4")
    {
        var t = new SheetTemplate
        {
            Name = o == SheetOrientation.Paisagem ? "GeoPoint – Paisagem" : "GeoPoint – Padrão",
            Description = "Modelo baseado na folha \"SITUAÇÃO ATUAL – FOLHA 02\": mapa com grade de coordenadas, norte, tabela de coordenadas, " +
                          "identificação e propriedade, registro e localização, quadro de áreas, assinaturas, convenções, orientação e croqui.",
            Paper = paper, Orientation = o,
        };
        t.Blocks = DefaultBlocks(t);
        return t;
    }

    /// <summary>Blocos na disposição padrão para o papel, a orientação e as margens do modelo.</summary>
    public static List<SheetBlock> DefaultBlocks(SheetTemplate t) => t.Orientation == SheetOrientation.Paisagem ? LandscapeBlocks(t) : PortraitBlocks(t);

    /// <summary>Fator de tamanho do papel em relação ao A4 (blocos maiores em A3/A2).</summary>
    private static double PaperFactor(SheetTemplate t) { var (w, h) = t.PaperSize(); return Math.Min(w, h) / 210.0; }

    private static List<SheetBlock> PortraitBlocks(SheetTemplate t)
    {
        var f = t.Frame();
        double g = t.Gap, pf = PaperFactor(t), band = 64 * pf;
        double x0 = f.X0 + g, y0 = f.Y0 + g, x1 = f.X1 - g, y1 = f.Y0 + band;
        double w = x1 - x0;
        double cL = w * 0.30, cM = w * 0.40, cR = w - cL - cM - 2 * g;
        double xL = x0, xM = xL + cL + g, xR = xM + cM + g;
        double hTop = (y1 - y0 - g) * 0.5, yMid = y0 + hTop + g;
        double titleH = 15 * pf;
        // Espaço da logomarca do escritório: entre o croqui e o título.
        double logoH = 14 * pf;
        double qW = cM * 0.4;
        double convW = cM * 0.5;
        return new List<SheetBlock>
        {
            new() { Kind = SheetBlockKind.Identificacao, Title = "IDENTIFICAÇÃO E PROPRIEDADE", X = xL, Y = yMid, W = cL, H = y1 - yMid },
            new() { Kind = SheetBlockKind.Registro, Title = "REGISTRO E LOCALIZAÇÃO", X = xL, Y = y0, W = cL, H = hTop },
            new() { Kind = SheetBlockKind.QuadroAreas, Title = "QUADRO DE ÁREAS E PERÍMETROS", X = xM, Y = yMid, W = qW, H = y1 - yMid },
            new() { Kind = SheetBlockKind.Assinaturas, Title = "QUADRO DE ASSINATURAS", X = xM + qW + g, Y = yMid, W = cM - qW - g, H = y1 - yMid },
            new() { Kind = SheetBlockKind.Convencoes, Title = "CONVENÇÕES TOPOGRÁFICAS", X = xM, Y = y0, W = convW, H = hTop },
            new() { Kind = SheetBlockKind.Orientacao, Title = "ORIENTAÇÃO", X = xM + convW + g, Y = y0, W = cM - convW - g, H = hTop },
            new() { Kind = SheetBlockKind.Croqui, Title = "", X = xR, Y = y0 + titleH + logoH + 2 * g, W = cR, H = y1 - y0 - titleH - logoH - 2 * g },
            new() { Kind = SheetBlockKind.Escritorio, Title = "", X = xR, Y = y0 + titleH + g, W = cR, H = logoH },
            new() { Kind = SheetBlockKind.Titulo, Title = "", X = xR, Y = y0, W = cR, H = titleH },
            new() { Kind = SheetBlockKind.Observacoes, Title = "OBSERVAÇÕES", Visible = false },
        };
    }

    private static List<SheetBlock> LandscapeBlocks(SheetTemplate t)
    {
        var f = t.Frame();
        double g = t.Gap, pf = PaperFactor(t), colW = 74 * pf;
        double x1 = f.X1 - g, x0 = x1 - colW, y0 = f.Y0 + g, y1 = f.Y1 - g;
        double half = (colW - g) / 2;
        double y = y1;
        SheetBlock Take(SheetBlockKind k, string title, double h, double x, double w, bool advance = true)
        {
            var b = new SheetBlock { Kind = k, Title = title, X = x, Y = y - h, W = w, H = h };
            if (advance) y -= h + g;
            return b;
        }
        var list = new List<SheetBlock>
        {
            Take(SheetBlockKind.Identificacao, "IDENTIFICAÇÃO E PROPRIEDADE", 24 * pf, x0, colW),
            Take(SheetBlockKind.Registro, "REGISTRO E LOCALIZAÇÃO", 30 * pf, x0, colW),
            Take(SheetBlockKind.QuadroAreas, "QUADRO DE ÁREAS E PERÍMETROS", 22 * pf, x0, half, false),
            Take(SheetBlockKind.Assinaturas, "QUADRO DE ASSINATURAS", 22 * pf, x0 + half + g, half),
            Take(SheetBlockKind.Convencoes, "CONVENÇÕES TOPOGRÁFICAS", 30 * pf, x0, half, false),
            Take(SheetBlockKind.Orientacao, "ORIENTAÇÃO", 30 * pf, x0 + half + g, half),
        };
        double titleH = 15 * pf, logoH = 16 * pf;
        double yc = y0 + titleH + logoH + 2 * g;
        list.Add(new SheetBlock { Kind = SheetBlockKind.Croqui, Title = "", X = x0, Y = yc, W = colW, H = y - yc });
        // Espaço da logomarca do escritório: entre o croqui e o título.
        list.Add(new SheetBlock { Kind = SheetBlockKind.Escritorio, Title = "", X = x0, Y = y0 + titleH + g, W = colW, H = logoH });
        list.Add(new SheetBlock { Kind = SheetBlockKind.Titulo, Title = "", X = x0, Y = y0, W = colW, H = titleH });
        list.Add(new SheetBlock { Kind = SheetBlockKind.Observacoes, Title = "OBSERVAÇÕES", Visible = false });
        return list;
    }

    /// <summary>Confere o modelo; devolve os problemas (vazio = ok).</summary>
    public List<string> Validate()
    {
        var res = new List<string>();
        if (string.IsNullOrWhiteSpace(Name)) res.Add("O modelo precisa de um nome.");
        var (w, h) = PaperSize();
        var f = Frame();
        if (f.Width < 50 || f.Height < 50) res.Add("Margens grandes demais para o papel.");
        foreach (var b in Blocks.Where(b => b.Visible))
        {
            if (b.W <= 2 || b.H <= 2) res.Add($"Bloco {b.Kind}: largura e altura precisam ser maiores que 2 mm.");
            else if (b.X < -0.01 || b.Y < -0.01 || b.X + b.W > w + 0.01 || b.Y + b.H > h + 0.01) res.Add($"Bloco {b.Kind}: fora do papel.");
        }
        var vis = Blocks.Where(b => b.Visible && b.W > 0 && b.H > 0).ToList();
        for (int i = 0; i < vis.Count; i++)
            for (int j = i + 1; j < vis.Count; j++)
            {
                var a = vis[i].Rect; var c = vis[j].Rect;
                double ox = Math.Min(a.X1, c.X1) - Math.Max(a.X0, c.X0), oy = Math.Min(a.Y1, c.Y1) - Math.Max(a.Y0, c.Y0);
                if (ox > 0.5 && oy > 0.5) res.Add($"Os blocos {vis[i].Kind} e {vis[j].Kind} se sobrepõem.");
            }
        var m = MapRect();
        if (m.Width < 40 || m.Height < 40) res.Add("Sobra pouco espaço para o mapa (menos de 4 cm).");
        foreach (var (name, v) in new[] { ("título", Styles.TituloMm), ("cabeçalho", Styles.CabecalhoMm), ("rótulo", Styles.RotuloMm), ("valor", Styles.ValorMm), ("tabela", Styles.TabelaMm), ("grade", Styles.GradeMm) })
            if (v < 1.2) res.Add($"Texto de {name} com {v:0.0} mm: abaixo de 1,2 mm fica ilegível na impressão.");
        return res;
    }
}

/// <summary>Biblioteca de modelos de prancha (arquivos JSON na pasta "modelos-prancha").</summary>
public static class SheetTemplateLibrary
{
    public const int MaxHistory = 30;

    public static string Folder => Path.Combine(SettingsStore.DefaultFolder, "modelos-prancha");

    public static string FileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return Path.Combine(Folder, (clean.Length == 0 ? "modelo" : clean) + ".json");
    }

    /// <summary>Todos os modelos (cria os padrões na primeira vez).</summary>
    public static List<SheetTemplate> LoadAll(string? folder = null)
    {
        folder ??= Folder;
        var res = new List<SheetTemplate>();
        try
        {
            Directory.CreateDirectory(folder);
            foreach (var f in Directory.GetFiles(folder, "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var t = JsonSerializer.Deserialize<SheetTemplate>(File.ReadAllText(f), JsonOptions.Default);
                    if (t is not null && !string.IsNullOrWhiteSpace(t.Name)) res.Add(Normalize(t));
                }
                catch { /* arquivo inválido: ignora */ }
            }
        }
        catch { }
        if (!res.Any(t => t.Name == SheetTemplate.Default().Name)) res.Insert(0, SheetTemplate.Default());
        if (!res.Any(t => t.Name == SheetTemplate.Default(SheetOrientation.Paisagem).Name)) res.Add(SheetTemplate.Default(SheetOrientation.Paisagem));
        return res;
    }

    public static SheetTemplate Find(string? name, SheetOrientation fallback = SheetOrientation.Retrato)
    {
        var all = LoadAll();
        return all.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
               ?? all.FirstOrDefault(t => t.Orientation == fallback) ?? SheetTemplate.Default(fallback);
    }

    /// <summary>Grava o modelo; a versão anterior (se houver) vai para o histórico.</summary>
    public static void Save(SheetTemplate t, string note = "", string? folder = null)
    {
        folder ??= Folder;
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, Path.GetFileName(FileName(t.Name)));
        if (File.Exists(path))
        {
            try
            {
                var old = JsonSerializer.Deserialize<SheetTemplate>(File.ReadAllText(path), JsonOptions.Default);
                if (old is not null)
                {
                    var oldHistory = old.History;
                    old.History = new();
                    // O histórico guarda o layout; a logomarca não entra (é sempre a atual) para o arquivo não crescer.
                    old.LogoData = ""; old.LogoExt = "";
                    t.History = oldHistory.Concat(t.History).GroupBy(h => h.Version).Select(g => g.First()).OrderBy(h => h.Version).ToList();
                    t.History.Add(new SheetTemplateVersion { Version = old.Version, Date = old.Modified, Note = note, Json = JsonSerializer.Serialize(old, JsonOptions.Compact) });
                    t.Version = Math.Max(t.Version, old.Version + 1);
                }
            }
            catch { }
        }
        while (t.History.Count > MaxHistory) t.History.RemoveAt(0);
        t.Modified = DateTime.Now;
        File.WriteAllText(path, JsonSerializer.Serialize(t, JsonOptions.Default));
    }

    public static void Delete(SheetTemplate t, string? folder = null)
    {
        folder ??= Folder;
        string path = Path.Combine(folder, Path.GetFileName(FileName(t.Name)));
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Volta para uma versão do histórico (a atual também vai para o histórico).</summary>
    public static SheetTemplate Restore(SheetTemplate current, SheetTemplateVersion v)
    {
        var old = JsonSerializer.Deserialize<SheetTemplate>(v.Json, JsonOptions.Default) ?? throw new InvalidOperationException("Versão inválida.");
        old.Name = current.Name;
        old.History = current.History.ToList();
        old.Version = current.Version;
        old.LogoData = current.LogoData; old.LogoExt = current.LogoExt; old.LogoPath = current.LogoPath;
        return Normalize(old);
    }

    public static SheetTemplate Import(string path)
    {
        var t = JsonSerializer.Deserialize<SheetTemplate>(File.ReadAllText(path), JsonOptions.Default) ?? throw new InvalidOperationException("Arquivo de modelo inválido.");
        return Normalize(t);
    }

    /// <summary>
    /// Arquivo da logomarca para o Revit inserir (a imagem embutida é gravada uma vez numa pasta de cache, com nome pelo conteúdo,
    /// para todas as pranchas usarem o mesmo tipo de imagem). null = modelo sem logomarca.
    /// </summary>
    public static string? LogoFile(SheetTemplate t, string? folder = null)
    {
        if (string.IsNullOrWhiteSpace(t.LogoData))
            return !string.IsNullOrWhiteSpace(t.LogoPath) && File.Exists(t.LogoPath) ? t.LogoPath : null;
        var bytes = t.LogoBytes();
        string? ext = ImageHeader.Extension(bytes);
        if (bytes is null || ext is null) return null;
        folder ??= Path.Combine(SettingsStore.DefaultFolder, "logomarcas");
        Directory.CreateDirectory(folder);
        string hash;
        using (var sha = System.Security.Cryptography.SHA1.Create()) hash = Convert.ToHexString(sha.ComputeHash(bytes))[..16].ToLowerInvariant();
        string path = Path.Combine(folder, $"logo-{hash}.{ext}");
        if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
        return path;
    }

    public static void Export(SheetTemplate t, string path) => File.WriteAllText(path, JsonSerializer.Serialize(t, JsonOptions.Default));

    /// <summary>Completa modelos antigos ou editados à mão (blocos que faltam ficam ocultos).</summary>
    public static SheetTemplate Normalize(SheetTemplate t)
    {
        t.Styles ??= new SheetTextStyles();
        t.Blocks ??= new();
        t.History ??= new();
        t.Scales ??= new();
        t.GridColor = t.GridColor is { Length: 3 } ? t.GridColor : new byte[] { 175, 175, 175 };
        foreach (SheetBlockKind k in Enum.GetValues(typeof(SheetBlockKind)))
            if (!t.Blocks.Any(b => b.Kind == k))
            {
                var d = SheetTemplate.Default(t.Orientation, t.Paper).Blocks.First(b => b.Kind == k).Clone();
                d.Visible = false;
                t.Blocks.Add(d);
            }
        // Espaço da imagem de localização sem título (versões até 1.5.5 tinham "CROQUI DE LOCALIZAÇÃO").
        foreach (var b in t.Blocks.Where(b => b.Kind == SheetBlockKind.Croqui && b.Title.Trim().Equals("CROQUI DE LOCALIZAÇÃO", StringComparison.OrdinalIgnoreCase)))
            b.Title = "";
        if (t.GridSpacingMm < 10) t.GridSpacingMm = 40;
        if (t.Gap < 0) t.Gap = 0;
        return t;
    }
}
