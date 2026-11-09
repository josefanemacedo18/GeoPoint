using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;
using GeoPoint.Core.Settings;
using GeoPoint.Core.Topology;

namespace GeoPoint.Revit.Infrastructure;

/// <summary>Resultado do reconhecimento: rede planar e mapeamento para elementos do Revit.</summary>
public sealed class RecognitionResult
{
    public PlanarNetwork Network { get; init; } = null!;
    public List<SourceCurve> Sources { get; init; } = new();
    public List<string> Warnings { get; } = new();
    /// <summary>Faces que já correspondem a lotes existentes (id da face → lote).</summary>
    public Dictionary<int, LotRecord> Existing { get; } = new();

    public IEnumerable<string> SourcesOfTag(int tag) => new[] { Sources[tag].UniqueId };

    public IEnumerable<ElementId> ElementsOfTags(IEnumerable<int> tags) =>
        tags.Where(t => t >= 0 && t < Sources.Count).Select(t => Sources[t].Id).Distinct();
}

public static class LotServices
{
    public static NetworkOptions NetworkOptions(GeoPointSettings s) => new()
    {
        // O Revit não cria curvas menores que ~0,8 mm (ShortCurveTolerance): a tolerância nunca fica abaixo disso.
        Tolerance = Math.Max(s.Recognition.ToleranceMm / 1000.0, 0.0008),
        GapSearchDistance = s.Recognition.GapSearchMm / 1000.0,
        TinyFaceArea = s.Recognition.TinyFaceM2,
    };

    public static RecognitionResult Recognize(GpSession s, IEnumerable<Element> elements, ISet<string>? cadLayers)
    {
        var warnings = new List<string>();
        var sources = GeometryReader.Read(s.Doc, s.ActiveView, elements, cadLayers, warnings);
        if (sources.Count == 0) throw new InvalidOperationException("Nenhuma linha horizontal válida foi encontrada na seleção.");
        double z0 = sources[0].ZFeet;
        if (sources.Any(c => Math.Abs(c.ZFeet - z0) > 0.01))
            warnings.Add("As linhas selecionadas estão em alturas diferentes; o reconhecimento foi feito em planta (Z ignorado).");
        var inputs = sources.Select((c, i) => new InputCurve(c.Curve, i)).ToList();
        var net = PlanarNetwork.Build(inputs, NetworkOptions(s.Settings));
        var res = new RecognitionResult { Network = net, Sources = sources };
        res.Warnings.AddRange(warnings);

        // Faces que já são lotes ativos.
        foreach (var f in net.Faces)
        {
            foreach (var lot in s.Project.ActiveLots)
            {
                try
                {
                    if (LotBuilder.SameShape(new Region(f.Region.Outer), lot.ToRegion(false))) { res.Existing[f.Id] = lot; break; }
                }
                catch { }
            }
        }
        return res;
    }

    /// <summary>Cria um registro de lote a partir de uma face reconhecida.</summary>
    public static LotRecord CreateLot(RecognitionResult rec, NetFace face, GeoPointSettings settings, string nome, string numero, string quadra, LotUse uso)
    {
        var (outer, holes) = LotBuilder.FromFace(rec.Network, face, rec.SourcesOfTag, settings.Recognition.IncludeHoles, settings.Recognition.MergeCollinear);
        var tags = face.OuterEdges.SelectMany(e => e.Tags).Distinct().ToList();
        var srcs = tags.Select(t => rec.Sources[t]).ToList();
        var lot = new LotRecord
        {
            Nome = nome,
            Numero = numero,
            Quadra = quadra,
            Uso = uso,
            Outer = outer,
            Holes = holes,
            ZFeet = srcs.Count > 0 ? srcs[0].ZFeet : 0,
        };
        var kinds = srcs.Select(x => x.Kind).Distinct().ToList();
        lot.SourceKind = kinds.Count == 1 ? kinds[0] : "Mixed";
        lot.SourceViewId = srcs.FirstOrDefault(x => x.Kind == "DetailLine")?.ViewUniqueId ?? "";
        lot.SketchPlaneId = srcs.FirstOrDefault(x => x.Kind == "ModelLine")?.SketchPlaneUniqueId ?? "";
        var region = lot.ToRegion();
        lot.History.Add(new HistoryEntry { Description = "Criado pelo reconhecimento de linhas", Area = region.Area, Perimeter = region.Outer.Perimeter });
        return lot;
    }

    /// <summary>Resultado da atualização de um lote a partir das linhas de origem.</summary>
    public sealed class SyncItem
    {
        public LotRecord Lot { get; init; } = null!;
        public string Status { get; set; } = "";
        public double OldArea { get; init; }
        public double NewArea { get; set; }
        public double OldPerimeter { get; init; }
        public double NewPerimeter { get; set; }
        public List<EdgeDto>? NewOuter { get; set; }
        public List<List<EdgeDto>>? NewHoles { get; set; }
        public bool Changed { get; set; }
        public bool Failed { get; set; }
    }

    /// <summary>Recalcula a geometria dos lotes a partir das linhas de origem atuais (sem gravar).</summary>
    public static List<SyncItem> ComputeSync(GpSession s, IEnumerable<LotRecord> lots)
    {
        var list = lots.ToList();
        var result = new List<SyncItem>();
        var allUids = list.SelectMany(l => l.AllSources()).Distinct().ToList();
        var elements = allUids.Select(u => ElementUtil.ByUniqueId(s.Doc, u)).Where(e => e is not null).Select(e => e!).Distinct().ToList();
        RecognitionResult? rec = null;
        if (elements.Count > 0)
        {
            try { rec = Recognize(s, elements, null); }
            catch (Exception ex) { Logger.Error("Sincronização: falha ao reconhecer", ex); }
        }

        foreach (var lot in list)
        {
            Region old;
            try { old = lot.ToRegion(); }
            catch
            {
                result.Add(new SyncItem { Lot = lot, Status = "Geometria armazenada inválida.", Failed = true });
                continue;
            }
            var item = new SyncItem { Lot = lot, OldArea = old.Area, OldPerimeter = old.Outer.Perimeter };
            result.Add(item);
            var srcs = lot.AllSources().ToList();
            int missing = srcs.Count(u => ElementUtil.ByUniqueId(s.Doc, u) is null);
            if (srcs.Count == 0)
            {
                item.Status = "Lote sem linhas de origem (criado por operação) – mantido.";
                item.NewArea = old.Area; item.NewPerimeter = old.Outer.Perimeter;
                continue;
            }
            if (rec is null)
            {
                item.Status = "Linhas de origem não encontradas.";
                item.Failed = true;
                continue;
            }
            // Usa apenas as linhas de origem deste lote para refazer a face.
            var mine = new HashSet<string>(srcs);
            var inputs = new List<InputCurve>();
            var map = new List<SourceCurve>();
            foreach (var sc in rec.Sources)
            {
                if (!mine.Contains(sc.UniqueId)) continue;
                inputs.Add(new InputCurve(sc.Curve, map.Count));
                map.Add(sc);
            }
            var net = PlanarNetwork.Build(inputs, NetworkOptions(s.Settings));
            var p = old.InteriorPoint();
            var face = net.FindFace(p);
            if (face is null)
            {
                item.Status = missing > 0 ? $"{missing} linha(s) de origem apagada(s); o contorno não fecha mais." : "As linhas de origem não formam mais uma área fechada neste local.";
                item.Failed = true;
                continue;
            }
            var (outer, holes) = LotBuilder.FromFace(net, face, t => new[] { map[t].UniqueId }, s.Settings.Recognition.IncludeHoles || lot.Holes.Count > 0, s.Settings.Recognition.MergeCollinear);
            var tmp = new LotRecord { Outer = outer, Holes = holes };
            var nr = tmp.ToRegion();
            item.NewArea = nr.Area;
            item.NewPerimeter = nr.Outer.Perimeter;
            bool same = LotBuilder.SameShape(old, nr) && outer.Count == lot.Outer.Count;
            if (same)
            {
                item.Status = missing > 0 ? $"Sem alteração ({missing} linha(s) de origem apagada(s), mas o contorno continua fechado)." : "Sem alteração.";
            }
            else
            {
                item.Changed = true;
                item.NewOuter = outer;
                item.NewHoles = holes;
                item.Status = $"Geometria alterada: área {old.Area:0.00} → {nr.Area:0.00} m².";
            }
        }
        return result;
    }

    public static void ApplySync(SyncItem item)
    {
        if (!item.Changed || item.NewOuter is null) return;
        var lot = item.Lot;
        lot.EdgeInfos = LotBuilder.RemapEdgeInfos(lot.EdgeInfos, item.NewOuter);
        lot.Outer = item.NewOuter;
        lot.Holes = item.NewHoles ?? new();
        GeoPoint.Core.Topology.EdgeSplitter.Apply(lot);
        lot.Modified = DateTime.Now;
        lot.History.Add(new HistoryEntry { Description = "Atualizado a partir das linhas do desenho", Area = item.NewArea, Perimeter = item.NewPerimeter });
        if (lot.StartX is double sx && lot.StartY is double sy)
        {
            var target = new Vec2(sx, sy);
            if (!lot.ToRegion().Outer.Vertices.Any(v => v.DistanceTo(target) < 0.01)) { lot.StartX = null; lot.StartY = null; }
        }
    }

    /// <summary>Seleciona e enquadra no Revit os elementos de origem dos lotes.</summary>
    public static void SelectSources(GpSession s, IEnumerable<LotRecord> lots)
    {
        var ids = ElementUtil.ExistingIds(s.Doc, lots.SelectMany(l => l.AllSources()));
        if (ids.Count == 0) return;
        s.UiDoc.Selection.SetElementIds(ids);
        try { s.UiDoc.ShowElements(ids); } catch { }
    }

    /// <summary>Lotes cujas linhas de origem estão na seleção atual do Revit.</summary>
    public static HashSet<string> LotsFromSelection(GpSession s)
    {
        var sel = s.UiDoc.Selection.GetElementIds().Select(id => s.Doc.GetElement(id)?.UniqueId).Where(u => u is not null).ToHashSet();
        var res = new HashSet<string>();
        if (sel.Count == 0) return res;
        foreach (var l in s.Project.PhaseLots)
            if (l.AllSources().Any(u => sel.Contains(u))) res.Add(l.Id);
        return res;
    }
}
