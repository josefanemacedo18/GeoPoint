using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Colors = System.Windows.Media.Colors;
using WColor = System.Windows.Media.Color;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;
using GeoPoint.Core.Operations;
using GeoPoint.Revit.UI;

namespace GeoPoint.Revit.Infrastructure;

/// <summary>
/// Desmembramento desenhando as divisas com a ferramenta Linha do Revit: uma janela não modal mostra a prévia
/// (atualizada a cada linha criada, pelo evento DocumentChanged) e, ao concluir, o desmembramento continua com essas linhas.
/// </summary>
public sealed class LiveSplitSession : IExternalEventHandler
{
    private static LiveSplitSession? _current;

    private readonly Document _doc;
    private readonly string _viewUid;
    private readonly LotRecord _parent;
    private readonly DesmembramentoState _state;
    private readonly Func<GpSession, DesmembramentoState, Result> _continue;
    private readonly List<ElementId> _lines = new();
    private readonly ExternalEvent _event;
    private readonly LiveSplitWindow _win;
    private readonly Autodesk.Revit.ApplicationServices.Application _app;
    private LiveSplitRequest _pending = LiveSplitRequest.None;
    private bool _stopped;

    private LiveSplitSession(GpSession s, LotRecord parent, DesmembramentoState state, Func<GpSession, DesmembramentoState, Result> cont)
    {
        _doc = s.Doc;
        _viewUid = s.ActiveView.UniqueId;
        _parent = parent;
        _state = state;
        _continue = cont;
        _app = s.UiApp.Application;
        _event = ExternalEvent.Create(this);
        _win = new LiveSplitWindow();
        _win.Header.Text = $"Desenhe as novas divisas de {parent.DisplayName}";
        _win.Requested += r => { _pending = r; if (r == LiveSplitRequest.Cancel) Stop(); else _event.Raise(); };
        try { new WindowInteropHelper(_win).Owner = s.MainWindow; } catch { }
        // Linhas já escolhidas antes continuam valendo.
        foreach (var (_, uid) in state.LineCuts)
            if (ElementUtil.ByUniqueId(_doc, uid) is CurveElement ce && !_lines.Contains(ce.Id)) _lines.Add(ce.Id);
    }

    public string GetName() => "GeoPoint – desmembramento com a ferramenta Linha";

    /// <summary>Abre a prévia ao vivo e ativa a ferramenta Linha. O comando que chamou deve terminar em seguida.</summary>
    public static void Start(GpSession s, LotRecord parent, DesmembramentoState state, Func<GpSession, DesmembramentoState, Result> cont)
    {
        _current?.Stop();
        var ss = new LiveSplitSession(s, parent, state, cont);
        _current = ss;
        ss._app.DocumentChanged += ss.OnDocumentChanged;
        // Janela no canto direito da tela, para não cobrir o desenho.
        var wa = System.Windows.SystemParameters.WorkArea;
        ss._win.Left = wa.Right - ss._win.Width - 20;
        ss._win.Top = wa.Top + 80;
        ss._win.Show();
        ss.Refresh();
        ss.PostLineTool(parent.SourceKind == "DetailLine", s.UiApp);
    }

    private void PostLineTool(bool detail, UIApplication uiapp)
    {
        try
        {
            var id = RevitCommandId.LookupPostableCommandId(detail ? PostableCommand.DetailLine : PostableCommand.ModelLine);
            if (id is not null && uiapp.CanPostCommand(id)) uiapp.PostCommand(id);
        }
        catch (Exception ex) { Logger.Error("Ativar a ferramenta Linha", ex); }
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        if (_stopped) return;
        try
        {
            var doc = e.GetDocument();
            if (!doc.Equals(_doc)) return;
            bool changed = false;
            foreach (var id in e.GetAddedElementIds())
                if (doc.GetElement(id) is CurveElement ce && ce.CurveElementType is CurveElementType.ModelCurve or CurveElementType.DetailCurve && !PhaseViews.IsGeoPointLine(ce))
                { _lines.Add(id); changed = true; }
            foreach (var id in e.GetDeletedElementIds()) changed |= _lines.Remove(id);
            foreach (var id in e.GetModifiedElementIds()) changed |= _lines.Contains(id);
            if (changed) Refresh();
        }
        catch (Exception ex) { Logger.Error("Prévia do desmembramento", ex); }
    }

    /// <summary>Lê as linhas desenhadas e mostra as partes resultantes.</summary>
    private List<(Curve2 Curve, string Uid)> ReadCuts()
    {
        var view = ElementUtil.ByUniqueId(_doc, _viewUid) as View;
        var els = _lines.Select(id => _doc.GetElement(id)).Where(x => x is not null).Select(x => x!).ToList();
        if (els.Count == 0 || view is null) return new();
        var warnings = new List<string>();
        return GeometryReader.Read(_doc, view, els, null, warnings).Select(c => (c.Curve, c.UniqueId)).ToList();
    }

    private void Refresh()
    {
        var cuts = ReadCuts();
        var region = _parent.ToRegion();
        var items = new List<PreviewItem> { PreviewItem.FromRegion(region, Colors.Transparent, null, false, WColor.FromRgb(90, 90, 90), 2) };
        string text;
        try
        {
            var pieces = cuts.Count == 0 ? new List<PlannedPiece>() : SubdivisionPlanner.OrderReading(SubdivisionPlanner.FromCuts(_parent, cuts.Select(c => (c.Curve, (string?)c.Uid)).ToList()));
            for (int i = 0; i < pieces.Count; i++)
            {
                var color = PreviewCanvas.Palette[i % PreviewCanvas.Palette.Length];
                items.Add(PreviewItem.FromRegion(pieces[i].Region, color, $"{i + 1}\n{Fmt.N(pieces[i].Area, 2)} m²", true, color, 1.2));
            }
            foreach (var (c, _) in cuts) items.Add(new PreviewItem { Line = c.Densify(0.05), Stroke = WColor.FromRgb(220, 40, 30), StrokeWidth = 2, Dashed = true });
            double sum = pieces.Sum(p => p.Area);
            text = cuts.Count == 0 ? "Nenhuma linha desenhada ainda."
                : pieces.Count < 2 ? $"{cuts.Count} linha(s) desenhada(s), mas o lote ainda não foi dividido: as divisas devem cruzar o lote de um lado a outro."
                : $"{cuts.Count} linha(s) • {pieces.Count} partes • soma {Fmt.N(sum, 2)} m² de {Fmt.N(region.Area, 2)} m²" + (Math.Abs(sum - region.Area) > ParcelOperations.AreaTolerance ? " (há área fora das partes)" : " (confere)");
        }
        catch (Exception ex) { text = "Não foi possível dividir: " + ex.Message; }
        _win.Preview.SetItems(items, 0);
        _win.Summary.Text = text;
    }

    public void Execute(UIApplication app)
    {
        var req = _pending;
        _pending = LiveSplitRequest.None;
        if (_stopped) return;
        try
        {
            switch (req)
            {
                case LiveSplitRequest.ModelLine: PostLineTool(false, app); break;
                case LiveSplitRequest.DetailLine: PostLineTool(true, app); break;
                case LiveSplitRequest.Undo:
                {
                    var last = _lines.LastOrDefault();
                    if (last is null) break;
                    using var t = new Transaction(_doc, "GeoPoint: desfazer divisa");
                    t.Start();
                    if (_doc.GetElement(last) is not null) _doc.Delete(last);
                    t.Commit();
                    _lines.Remove(last);
                    Refresh();
                    break;
                }
                case LiveSplitRequest.Finish:
                {
                    var cuts = ReadCuts();
                    Stop();
                    _state.LineCuts = cuts;
                    _state.Modo = DesmModo.Linhas;
                    _state.ParentId = _parent.Id;
                    var s = new GpSession(app);
                    _continue(s, _state);
                    break;
                }
            }
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Error("Desmembramento com a ferramenta Linha", ex);
            Ui.Error("Desmembramento", ex.Message, ex);
        }
    }

    private void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        try { _app.DocumentChanged -= OnDocumentChanged; } catch { }
        try { _win.ClosingByCode = true; _win.Close(); } catch { }
        if (_current == this) _current = null;
    }
}
