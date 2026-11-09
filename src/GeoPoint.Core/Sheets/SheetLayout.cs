using GeoPoint.Core.Geometry;

namespace GeoPoint.Core.Sheets;

/// <summary>Retângulo no papel (mm, origem no canto inferior esquerdo da folha).</summary>
public readonly record struct PaperRect(double X0, double Y0, double X1, double Y1)
{
    public double Width => X1 - X0;
    public double Height => Y1 - Y0;
    public Vec2 Center => new((X0 + X1) / 2, (Y0 + Y1) / 2);
    public PaperRect Inset(double d) => new(X0 + d, Y0 + d, X1 - d, Y1 - d);
}

/// <summary>Campo do carimbo.</summary>
public sealed class SheetCell
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public PaperRect Rect { get; init; }
    /// <summary>Altura do texto do valor (mm).</summary>
    public double ValueMm { get; init; } = 2.2;
    public bool Bold { get; init; }
    public bool Centered { get; init; }
}

public enum SheetOrientation { Retrato = 0, Paisagem = 1 }

/// <summary>Folha A4 com moldura (margens ABNT NBR 10068: 25 mm à esquerda, 7 mm nas demais) e carimbo GeoPoint.</summary>
public sealed class SheetLayout
{
    public const double LeftMargin = 25, Margin = 7;

    public SheetOrientation Orientation { get; private init; }
    public double Width { get; private init; }
    public double Height { get; private init; }
    public PaperRect Frame { get; private init; }
    /// <summary>Área do carimbo (contorno externo).</summary>
    public PaperRect TitleBlock { get; private init; }
    /// <summary>Área livre para o desenho (dentro da moldura, fora do carimbo).</summary>
    public PaperRect Drawing { get; private init; }
    public List<SheetCell> Cells { get; } = new();
    /// <summary>Linhas internas do carimbo (separação dos campos).</summary>
    public List<(Vec2 A, Vec2 B)> Lines { get; } = new();

    public SheetCell Cell(string key) => Cells.First(c => c.Key == key);

    public static SheetLayout A4(SheetOrientation o) => o == SheetOrientation.Paisagem ? Landscape() : Portrait();

    private static SheetLayout Portrait()
    {
        const double W = 210, H = 297;
        var frame = new PaperRect(LeftMargin, Margin, W - Margin, H - Margin);
        double top = frame.Y0 + 64;                  // carimbo com 64 mm de altura
        var l = new SheetLayout
        {
            Orientation = SheetOrientation.Retrato, Width = W, Height = H, Frame = frame,
            TitleBlock = new PaperRect(frame.X0, frame.Y0, frame.X1, top),
            Drawing = new PaperRect(frame.X0, top, frame.X1, frame.Y1),
        };
        double xa = frame.X0, xm = frame.X0 + 103, xb = frame.X1, xs = frame.X0 + 58;
        double xr = xm + (xb - xm) / 2;
        // Bloco A (dados) – linhas de cima para baixo.
        double y = top;
        void Row(string key, string label, double h, double valueMm = 2.2, bool bold = false, double? split = null, string? key2 = null, string? label2 = null)
        {
            if (split is double sx)
            {
                l.Cells.Add(new SheetCell { Key = key, Label = label, Rect = new PaperRect(xa, y - h, sx, y), ValueMm = valueMm, Bold = bold });
                l.Cells.Add(new SheetCell { Key = key2!, Label = label2!, Rect = new PaperRect(sx, y - h, xm, y), ValueMm = valueMm, Bold = bold });
                l.Lines.Add((new Vec2(sx, y - h), new Vec2(sx, y)));
            }
            else l.Cells.Add(new SheetCell { Key = key, Label = label, Rect = new PaperRect(xa, y - h, xm, y), ValueMm = valueMm, Bold = bold });
            y -= h;
            l.Lines.Add((new Vec2(xa, y), new Vec2(xm, y)));
        }
        Row("titulo", "", 12, 3.2, true);
        Row("proprietario", "PROPRIETÁRIO(S)", 7);
        Row("imovel", "IMÓVEL / ENDEREÇO", 7);
        Row("municipio", "MUNICÍPIO / UF", 7, split: xs, key2: "matricula", label2: "MATRÍCULA / INSCRIÇÃO");
        Row("area", "ÁREA", 7, split: xs, key2: "perimetro", label2: "PERÍMETRO");
        Row("coordenada", "COORDENADA DE REFERÊNCIA", 8, 1.9);
        // Assinaturas (restante do bloco A).
        l.Cells.Add(new SheetCell { Key = "resp", Label = "RESPONSÁVEL TÉCNICO", Rect = new PaperRect(xa, frame.Y0, xs, y), ValueMm = 1.9, Centered = true });
        l.Cells.Add(new SheetCell { Key = "assprop", Label = "PROPRIETÁRIO(S)", Rect = new PaperRect(xs, frame.Y0, xm, y), ValueMm = 1.9, Centered = true });
        l.Lines.Add((new Vec2(xs, frame.Y0), new Vec2(xs, y)));
        // Bloco B: localização, escala/data, folha.
        l.Lines.Add((new Vec2(xm, frame.Y0), new Vec2(xm, top)));
        double yLoc = frame.Y0 + 24;
        l.Cells.Add(new SheetCell { Key = "localizacao", Label = "LOCALIZAÇÃO (SEM ESCALA)", Rect = new PaperRect(xm, yLoc, xb, top) });
        l.Lines.Add((new Vec2(xm, yLoc), new Vec2(xb, yLoc)));
        double yEsc = frame.Y0 + 12;
        l.Cells.Add(new SheetCell { Key = "escala", Label = "ESCALA", Rect = new PaperRect(xm, yEsc, xr, yLoc), ValueMm = 2.5, Bold = true, Centered = true });
        l.Cells.Add(new SheetCell { Key = "data", Label = "DATA", Rect = new PaperRect(xr, yEsc, xb, yLoc), ValueMm = 2.2, Centered = true });
        l.Lines.Add((new Vec2(xm, yEsc), new Vec2(xb, yEsc)));
        l.Lines.Add((new Vec2(xr, frame.Y0), new Vec2(xr, yLoc)));
        l.Cells.Add(new SheetCell { Key = "folha", Label = "FOLHA", Rect = new PaperRect(xm, frame.Y0, xr, yEsc), ValueMm = 2.5, Bold = true, Centered = true });
        l.Cells.Add(new SheetCell { Key = "prancha", Label = "PRANCHA", Rect = new PaperRect(xr, frame.Y0, xb, yEsc), ValueMm = 2.2, Centered = true });
        l.Lines.Add((new Vec2(frame.X0, top), new Vec2(frame.X1, top)));
        return l;
    }

    private static SheetLayout Landscape()
    {
        const double W = 297, H = 210;
        var frame = new PaperRect(LeftMargin, Margin, W - Margin, H - Margin);
        double xa = frame.X1 - 82, xb = frame.X1, xs = xa + (xb - xa) / 2;
        var l = new SheetLayout
        {
            Orientation = SheetOrientation.Paisagem, Width = W, Height = H, Frame = frame,
            TitleBlock = new PaperRect(xa, frame.Y0, xb, frame.Y1),
            Drawing = new PaperRect(frame.X0, frame.Y0, xa, frame.Y1),
        };
        l.Lines.Add((new Vec2(xa, frame.Y0), new Vec2(xa, frame.Y1)));
        double y = frame.Y1;
        void Row(string key, string label, double h, double valueMm = 2.2, bool bold = false, bool split = false, string? key2 = null, string? label2 = null, bool centered = false)
        {
            if (split)
            {
                l.Cells.Add(new SheetCell { Key = key, Label = label, Rect = new PaperRect(xa, y - h, xs, y), ValueMm = valueMm, Bold = bold, Centered = centered });
                l.Cells.Add(new SheetCell { Key = key2!, Label = label2!, Rect = new PaperRect(xs, y - h, xb, y), ValueMm = valueMm, Bold = bold, Centered = centered });
                l.Lines.Add((new Vec2(xs, y - h), new Vec2(xs, y)));
            }
            else l.Cells.Add(new SheetCell { Key = key, Label = label, Rect = new PaperRect(xa, y - h, xb, y), ValueMm = valueMm, Bold = bold, Centered = centered });
            y -= h;
            if (y > frame.Y0 + 0.01) l.Lines.Add((new Vec2(xa, y), new Vec2(xb, y)));
        }
        Row("titulo", "", 16, 3.0, true);
        Row("proprietario", "PROPRIETÁRIO(S)", 9);
        Row("imovel", "IMÓVEL / ENDEREÇO", 9);
        Row("municipio", "MUNICÍPIO / UF", 8, split: true, key2: "matricula", label2: "MATRÍCULA / INSCRIÇÃO");
        Row("area", "ÁREA", 8, split: true, key2: "perimetro", label2: "PERÍMETRO");
        Row("coordenada", "COORDENADA DE REFERÊNCIA", 10, 1.9);
        Row("localizacao", "LOCALIZAÇÃO (SEM ESCALA)", 50);
        Row("resp", "RESPONSÁVEL TÉCNICO", 18, 1.9, centered: true);
        Row("assprop", "PROPRIETÁRIO(S)", 16, 1.9, centered: true);
        Row("escala", "ESCALA", 10, 2.5, true, split: true, key2: "data", label2: "DATA", centered: true);
        Row("folha", "FOLHA", y - frame.Y0, 2.5, true, split: true, key2: "prancha", label2: "PRANCHA", centered: true);
        return l;
    }

    /// <summary>Escalas usuais (1:n).</summary>
    public static readonly int[] StandardScales = { 100, 125, 150, 200, 250, 300, 400, 500, 600, 750, 1000, 1250, 1500, 2000, 2500, 3000, 4000, 5000, 7500, 10000, 15000, 20000, 25000, 50000 };

    /// <summary>Menor escala usual em que o desenho (m) cabe na área (mm) com a margem (mm) para as anotações.</summary>
    public static int ChooseScale(double widthM, double heightM, double areaWmm, double areaHmm, double marginMm)
    {
        foreach (int s in StandardScales)
        {
            double w = widthM * 1000 / s + 2 * marginMm, h = heightM * 1000 / s + 2 * marginMm;
            if (w <= areaWmm && h <= areaHmm) return s;
        }
        return StandardScales[^1];
    }
}
