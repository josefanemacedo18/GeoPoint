using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using GeoPoint.Revit.Infrastructure;

namespace GeoPoint.Revit;

/// <summary>Aplicação externa: cria a aba "GeoPoint" na faixa de opções.</summary>
public sealed class App : IExternalApplication
{
    public const string TabName = "GeoPoint";
    private static readonly string AssemblyPath = Assembly.GetExecutingAssembly().Location;
    private static readonly string AssemblyFolder = Path.GetDirectoryName(AssemblyPath) ?? "";

    public Result OnStartup(UIControlledApplication app)
    {
        AppDomain.CurrentDomain.AssemblyResolve += ResolveLocal;
        try
        {
            try { app.CreateRibbonTab(TabName); } catch { /* já existe */ }

            var lotes = app.CreateRibbonPanel(TabName, "Lotes");
            Add(lotes, "Reconhecer\nLotes", typeof(Commands.CmdReconhecer), "Reconhece lotes a partir das linhas desenhadas (linhas de modelo, de detalhe ou CAD). Os encontros das linhas viram vértices.", IconKind.Recognize);
            Add(lotes, "Gerar\nLote", typeof(Commands.CmdGerarLote), "Gera lotes pelo formato (quadrado, retângulo, triângulo, trapézio, quadrilátero, em L, esquina com chanfro ou curva, polígono) digitando a medida de cada lado.", IconKind.Generate);
            Add(lotes, "Gerenciar\nLotes", typeof(Commands.CmdGerenciar), "Nome, quadra, uso, matrícula, confrontantes, testada, vértice inicial e sentido de cada lote.", IconKind.Manager);
            Add(lotes, "Atualizar\nLotes", typeof(Commands.CmdAtualizar), "Atualiza os lotes depois de editar as linhas no desenho.", IconKind.Sync);
            Add(lotes, "Fases", typeof(Commands.CmdFases), "Fases do projeto (ex.: situação atual → remembramento → desmembramento). Cada fase tem seus lotes, numeração de vértices, memoriais, pranchas e vista de trabalho.", IconKind.Phases);
            Add(lotes, "Dados do\nImóvel", typeof(Commands.CmdDadosImovel), "Proprietário, matrícula, município, RRT/ART e perfil de parâmetros urbanísticos do projeto.", IconKind.Info);

            var geo = app.CreateRibbonPanel(TabName, "Georreferência");
            Add(geo, "Definir\nReferência", typeof(Commands.CmdReferencia), "Informe a coordenada (Google Maps/Earth, QGIS, GPS ou UTM) de um vértice. As demais coordenadas são calculadas pelos azimutes e distâncias do desenho.", IconKind.Reference);
            Add(geo, "Importar\nKML/KMZ", typeof(Commands.CmdImportarKml), "Importa pontos (marcadores) do Google Earth/QGIS. Você clica onde fica um dos pontos (ou usa a referência do projeto) e todos aparecem no desenho para traçar as divisas com snap.", IconKind.Import);
            Add(geo, "Desenhar por\nAzimutes", typeof(Commands.CmdCaminhamento), "Desenha linhas a partir de azimutes/rumos e distâncias (ex.: colados de um memorial antigo), com erro de fechamento.", IconKind.Traverse);

            var ops = app.CreateRibbonPanel(TabName, "Operações");
            Add(ops, "Desmem-\nbramento", typeof(Commands.CmdDesmembramento), "Divide um lote: automático (partes iguais, áreas ou testadas definidas), pelas linhas que você desenhou ou por lotes já reconhecidos. Com prévia, lote remanescente, proprietários e anexação a lotes vizinhos.", IconKind.Split);
            Add(ops, "Remem-\nbramento", typeof(Commands.CmdRemembramento), "Une lotes contíguos em um único lote (remembramento/unificação), com prévia.", IconKind.Merge);
            Add(ops, "Área\nEdificável", typeof(Commands.CmdAreaEdificavel), "Calcula e desenha a área edificável a partir dos recuos de cada divisa.", IconKind.Setback);
            Add(ops, "Área\nEdificada", typeof(Commands.CmdAreaEdificada), "Edificações existentes desenhadas com linhas dentro do lote: área de projeção, pavimentos, área construída, taxa de ocupação e distância até cada divisa (no desenho, no memorial e nas pranchas).", IconKind.Building);

            var anot = app.CreateRibbonPanel(TabName, "Anotação");
            Add(anot, "Anotar\nLotes", typeof(Commands.CmdAnotar), "Distâncias e azimutes fora do lote, vértices com ponto preenchido, área e extensão de cada confrontante, na vista atual.", IconKind.Annotate);
            Add(anot, "Hachuras", typeof(Commands.CmdHachuras), "Hachura cada lote com uma cor (remanescente com hachura cruzada), na vista atual. As hachuras recebem os parâmetros GP_* (lote, quadra, uso, área…) para tabelas e etiquetas nativas do Revit.", IconKind.Hatch);
            Add(anot, "Vias", typeof(Commands.CmdVias), "Cadastro de vias, testadas e cruzamentos (2, 3 ou 4 vias) e desenho esquemático da rua (meio-fio, calçada e nome).", IconKind.Street);
            Add(anot, "Tabela de\nCoordenadas", typeof(Commands.CmdTabelaCoordenadas), "Cria uma vista de desenho com o quadro de coordenadas para colocar na prancha.", IconKind.Table);

            var docs = app.CreateRibbonPanel(TabName, "Documentos");
            Add(docs, "Memorial\nDescritivo", typeof(Commands.CmdMemorial), "Gera o memorial descritivo em Word (lote, desmembramento, remembramento ou loteamento).", IconKind.Memorial);
            Add(docs, "Anuência de\nConfrontantes", typeof(Commands.CmdAnuencia), "Declaração de anuência (limite) para cada confrontante, com a descrição da divisa comum, em Word.", IconKind.Signature);
            Add(docs, "Pranchas", typeof(Commands.CmdPranchas), "Pranchas por situação (atual, proposta, cada lote e total) pelo modelo escolhido: mapa com grade de coordenadas, norte, tabela de coordenadas, coordenada de referência, croqui de satélite e carimbo.", IconKind.Sheet);
            Add(docs, "Google Earth\n/ QGIS", typeof(Commands.CmdExportarSig), "Exporta KMZ/KML (Google Earth) e GeoJSON (QGIS) para conferir sobre imagem de satélite.", IconKind.Globe);
            Add(docs, "Planilha\nExcel", typeof(Commands.CmdExportarExcel), "Exporta lotes, coordenadas e segmentos para Excel.", IconKind.Excel);

            var chk = app.CreateRibbonPanel(TabName, "Verificação");
            Add(chk, "Verificar\nProjeto", typeof(Commands.CmdVerificar), "Confere geometria, confrontantes, sobreposições, títulos e parâmetros.", IconKind.Check);

            var cfg = app.CreateRibbonPanel(TabName, "GeoPoint");
            Add(cfg, "Configu-\nrações", typeof(Commands.CmdConfiguracoes), "Tolerâncias, formatos, sistemas de coordenadas, modelo do memorial, responsável técnico e perfis urbanísticos.", IconKind.Settings);
            Add(cfg, "Ajuda", typeof(Commands.CmdAjuda), "Sobre o GeoPoint e fluxo de trabalho recomendado.", IconKind.Help);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            Logger.Error("Falha ao iniciar o GeoPoint", ex);
            return Result.Failed;
        }
    }

    public Result OnShutdown(UIControlledApplication app)
    {
        AppDomain.CurrentDomain.AssemblyResolve -= ResolveLocal;
        return Result.Succeeded;
    }

    /// <summary>Garante que dependências do GeoPoint sejam carregadas da pasta do plugin.</summary>
    private static Assembly? ResolveLocal(object? sender, ResolveEventArgs args)
    {
        try
        {
            var name = new AssemblyName(args.Name).Name;
            if (name is null || !name.StartsWith("GeoPoint", StringComparison.OrdinalIgnoreCase)) return null;
            var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
            if (loaded is not null) return loaded;
            var path = Path.Combine(AssemblyFolder, name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }
        catch { return null; }
    }

    private static void Add(RibbonPanel panel, string text, Type cmd, string tooltip, IconKind icon)
    {
        var data = new PushButtonData("GP_" + cmd.Name, text, AssemblyPath, cmd.FullName)
        {
            ToolTip = tooltip,
            LargeImage = Icons.Create(icon, 32),
            Image = Icons.Create(icon, 16),
        };
        panel.AddItem(data);
    }
}

public enum IconKind { Recognize, Manager, Sync, Info, Reference, Traverse, Split, Merge, Divide, Setback, Annotate, Table, Native, Memorial, Globe, Excel, Chart, Check, Topo, Settings, Help, Hatch, Street, Signature, Sheet, Phases, Import, Generate, Building }

/// <summary>Ícones vetoriais desenhados em código (sem arquivos externos).</summary>
public static class Icons
{
    private static readonly Brush Blue = Freeze(new SolidColorBrush(Color.FromRgb(0x1F, 0x5F, 0xA8)));
    private static readonly Brush Green = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0x8B, 0x57)));
    private static readonly Brush Orange = Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0x7B, 0x1A)));
    private static readonly Brush Light = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x8A)));
    private static readonly Brush Gray = Freeze(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)));

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    public static BitmapSource Create(IconKind kind, int size)
    {
        var dv = new DrawingVisual();
        double s = size / 32.0;
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(s, s));
            Draw(dc, kind);
            dc.Pop();
        }
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    private static Pen P(Brush b, double w) => Freeze(new Pen(b, w) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

    private static void Lot(DrawingContext dc, Brush fill)
    {
        var g = Geometry.Parse("M 4,26 L 6,8 L 22,5 L 28,24 Z");
        dc.DrawGeometry(fill, P(Blue, 2), g);
    }

    private static void Draw(DrawingContext dc, IconKind k)
    {
        switch (k)
        {
            case IconKind.Generate:
                dc.DrawGeometry(Light, P(Blue, 2), Geometry.Parse("M 4,28 L 4,10 L 18,10 L 18,28 Z"));
                dc.DrawGeometry(null, P(Gray, 1.2), Geometry.Parse("M 4,6 L 18,6 M 4,4 L 4,8 M 18,4 L 18,8"));
                dc.DrawGeometry(Green, null, Geometry.Parse("M 21,16 L 25,16 L 25,12 L 28,12 L 28,16 L 32,16 L 32,19 L 28,19 L 28,23 L 25,23 L 25,19 L 21,19 Z"));
                break;
            case IconKind.Phases:
                dc.DrawRectangle(Brushes.White, P(Gray, 1.4), new Rect(3, 13, 16, 16));
                dc.DrawRectangle(Brushes.White, P(Gray, 1.4), new Rect(8, 8, 16, 16));
                dc.DrawRectangle(Light, P(Blue, 1.8), new Rect(13, 3, 16, 16));
                break;
            case IconKind.Import:
                dc.DrawGeometry(Orange, P(Gray, 1), Geometry.Parse("M 12,22 C 8,15 5,12 5,8.5 A 7,7 0 1 1 19,8.5 C 19,12 16,15 12,22 Z"));
                dc.DrawEllipse(Brushes.White, null, new Point(12, 8.5), 2.8, 2.8);
                dc.DrawLine(P(Blue, 2.4), new Point(24, 14), new Point(24, 29));
                dc.DrawGeometry(Blue, null, Geometry.Parse("M 18,23 L 24,30 L 30,23 Z"));
                break;
            case IconKind.Hatch:
                Lot(dc, Brushes.White);
                dc.PushClip(Geometry.Parse("M 4,26 L 6,8 L 22,5 L 28,24 Z"));
                for (int i = -32; i < 32; i += 5) dc.DrawLine(P(Orange, 1.4), new Point(i, 32), new Point(i + 32, 0));
                dc.Pop();
                dc.DrawGeometry(null, P(Blue, 2), Geometry.Parse("M 4,26 L 6,8 L 22,5 L 28,24 Z"));
                break;
            case IconKind.Street:
                dc.DrawRectangle(Gray, null, new Rect(0, 11, 32, 10));
                dc.DrawRectangle(Light, null, new Rect(0, 7, 32, 4));
                dc.DrawRectangle(Light, null, new Rect(0, 21, 32, 4));
                for (int i = 1; i < 32; i += 8) dc.DrawLine(P(Brushes.White, 1.6), new Point(i, 16), new Point(i + 4, 16));
                break;
            case IconKind.Signature:
                dc.DrawRectangle(Brushes.White, P(Gray, 1.5), new Rect(5, 3, 22, 27));
                for (int i = 0; i < 3; i++) dc.DrawLine(P(Gray, 1.2), new Point(9, 8 + i * 4), new Point(23, 8 + i * 4));
                dc.DrawGeometry(null, P(Blue, 1.8), Geometry.Parse("M 8,25 C 11,19 13,27 16,22 C 18,19 19,26 24,23"));
                break;
            case IconKind.Sheet:
                dc.DrawRectangle(Brushes.White, P(Gray, 1.5), new Rect(6, 2, 20, 28));
                dc.DrawRectangle(null, P(Gray, 1), new Rect(8, 4, 16, 24));
                dc.DrawRectangle(Light, P(Gray, 1), new Rect(8, 22, 16, 6));
                dc.DrawGeometry(null, P(Blue, 1.6), Geometry.Parse("M 10,18 L 11,8 L 19,7 L 22,17 Z"));
                break;
            case IconKind.Recognize:
                Lot(dc, Light);
                foreach (var p in new[] { new Point(4, 26), new Point(6, 8), new Point(22, 5), new Point(28, 24) }) dc.DrawEllipse(Orange, null, p, 2.6, 2.6);
                break;
            case IconKind.Manager:
                Lot(dc, Light);
                dc.DrawRectangle(Brushes.White, P(Gray, 1.5), new Rect(14, 14, 16, 16));
                for (int i = 0; i < 3; i++) dc.DrawLine(P(Gray, 1.5), new Point(17, 18.5 + i * 4), new Point(27, 18.5 + i * 4));
                break;
            case IconKind.Sync:
                Lot(dc, Light);
                dc.DrawGeometry(null, P(Green, 2.5), Geometry.Parse("M 10,16 A 7,7 0 1 1 16,23"));
                dc.DrawGeometry(Green, null, Geometry.Parse("M 6,14 L 10,19 L 14,14 Z"));
                break;
            case IconKind.Info:
                dc.DrawEllipse(Blue, null, new Point(16, 16), 13, 13);
                dc.DrawRectangle(Brushes.White, null, new Rect(14.5, 13, 3, 11));
                dc.DrawEllipse(Brushes.White, null, new Point(16, 9), 2, 2);
                break;
            case IconKind.Reference:
                dc.DrawGeometry(Orange, P(Gray, 1), Geometry.Parse("M 16,30 C 10,20 6,16 6,11 A 10,10 0 1 1 26,11 C 26,16 22,20 16,30 Z"));
                dc.DrawEllipse(Brushes.White, null, new Point(16, 11), 4, 4);
                break;
            case IconKind.Traverse:
                dc.DrawGeometry(null, P(Blue, 2.5), Geometry.Parse("M 4,26 L 12,10 L 22,16 L 28,4"));
                foreach (var p in new[] { new Point(4, 26), new Point(12, 10), new Point(22, 16), new Point(28, 4) }) dc.DrawEllipse(Orange, null, p, 2.4, 2.4);
                break;
            case IconKind.Split:
                Lot(dc, Light);
                dc.DrawLine(P(Orange, 2.5), new Point(14, 6.5), new Point(16, 26));
                break;
            case IconKind.Merge:
                dc.DrawRectangle(Light, P(Blue, 2), new Rect(3, 8, 13, 16));
                dc.DrawRectangle(Light, P(Blue, 2), new Rect(16, 8, 13, 16));
                dc.DrawLine(P(Light, 3), new Point(16, 10), new Point(16, 22));
                break;
            case IconKind.Divide:
                dc.DrawRectangle(Light, P(Blue, 2), new Rect(3, 6, 26, 20));
                for (int i = 1; i < 4; i++) dc.DrawLine(P(Orange, 1.8), new Point(3 + i * 6.5, 6), new Point(3 + i * 6.5, 26));
                break;
            case IconKind.Building:
                dc.DrawRectangle(Light, P(Blue, 1.6), new Rect(2, 4, 28, 25));
                dc.PushClip(new RectangleGeometry(new Rect(8, 11, 15, 13)));
                for (int i = -16; i < 32; i += 3) dc.DrawLine(P(Gray, 1), new Point(i, 26), new Point(i + 16, 10));
                dc.Pop();
                dc.DrawRectangle(null, P(Gray, 2), new Rect(8, 11, 15, 13));
                dc.DrawLine(P(Orange, 1.4), new Point(2, 17.5), new Point(8, 17.5));
                break;
            case IconKind.Setback:
                dc.DrawRectangle(Light, P(Blue, 2), new Rect(3, 4, 26, 24));
                dc.DrawRectangle(Green, null, new Rect(9, 10, 14, 12));
                break;
            case IconKind.Annotate:
                Lot(dc, Light);
                dc.DrawText(new FormattedText("Az", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Gray, 1.0), new Point(9, 11));
                break;
            case IconKind.Table:
                dc.DrawRectangle(Brushes.White, P(Gray, 1.5), new Rect(3, 5, 26, 22));
                dc.DrawRectangle(Blue, null, new Rect(3, 5, 26, 5));
                for (int i = 1; i < 4; i++) dc.DrawLine(P(Gray, 1), new Point(3, 10 + i * 4.3), new Point(29, 10 + i * 4.3));
                dc.DrawLine(P(Gray, 1), new Point(12, 10), new Point(12, 27));
                break;
            case IconKind.Native:
                Lot(dc, Freeze(new SolidColorBrush(Color.FromRgb(0xC8, 0xE6, 0xC9))));
                dc.DrawGeometry(null, P(Green, 2), Geometry.Parse("M 9,16 L 14,21 L 23,11"));
                break;
            case IconKind.Memorial:
                dc.DrawGeometry(Brushes.White, P(Gray, 1.5), Geometry.Parse("M 7,3 L 21,3 L 27,9 L 27,29 L 7,29 Z"));
                dc.DrawRectangle(Blue, null, new Rect(3, 12, 14, 12));
                dc.DrawText(new FormattedText("W", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 10, Brushes.White, 1.0), new Point(5, 12.5));
                for (int i = 0; i < 3; i++) dc.DrawLine(P(Gray, 1.2), new Point(19, 15 + i * 4), new Point(24, 15 + i * 4));
                break;
            case IconKind.Globe:
                dc.DrawEllipse(Freeze(new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xD9))), P(Blue, 1.5), new Point(16, 16), 13, 13);
                dc.DrawGeometry(Green, null, Geometry.Parse("M 9,9 C 13,7 15,11 13,14 C 11,17 14,20 12,24 C 8,21 6,15 9,9 Z M 19,6 C 24,8 27,12 26,17 C 23,18 21,15 19,13 C 17,11 18,8 19,6 Z"));
                break;
            case IconKind.Excel:
                dc.DrawRectangle(Green, null, new Rect(3, 4, 26, 24));
                dc.DrawText(new FormattedText("X", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 16, Brushes.White, 1.0), new Point(10, 5));
                break;
            case IconKind.Chart:
                dc.DrawRectangle(Blue, null, new Rect(5, 16, 5, 12));
                dc.DrawRectangle(Green, null, new Rect(13, 9, 5, 19));
                dc.DrawRectangle(Orange, null, new Rect(21, 4, 5, 24));
                break;
            case IconKind.Check:
                dc.DrawEllipse(Green, null, new Point(16, 16), 13, 13);
                dc.DrawGeometry(null, P(Brushes.White, 3), Geometry.Parse("M 9,16 L 14,21 L 23,11"));
                break;
            case IconKind.Topo:
                dc.DrawGeometry(Freeze(new SolidColorBrush(Color.FromRgb(0xA5, 0xD6, 0xA7))), P(Green, 1.5), Geometry.Parse("M 2,28 L 10,14 L 16,20 L 22,8 L 30,28 Z"));
                break;
            case IconKind.Settings:
                dc.DrawEllipse(Gray, null, new Point(16, 16), 11, 11);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    dc.DrawLine(P(Gray, 4), new Point(16 + Math.Cos(a) * 10, 16 + Math.Sin(a) * 10), new Point(16 + Math.Cos(a) * 14, 16 + Math.Sin(a) * 14));
                }
                dc.DrawEllipse(Brushes.White, null, new Point(16, 16), 4.5, 4.5);
                break;
            case IconKind.Help:
                dc.DrawEllipse(Blue, null, new Point(16, 16), 13, 13);
                dc.DrawText(new FormattedText("?", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 18, Brushes.White, 1.0), new Point(11, 3));
                break;
        }
    }
}
