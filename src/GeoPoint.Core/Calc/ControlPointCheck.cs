using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;

namespace GeoPoint.Core.Calc;

/// <summary>Comparação de um ponto de controle (KML/GPS) com o vértice mais próximo do desenho.</summary>
public sealed class ControlPointResult
{
    public ControlPoint Point { get; init; } = null!;
    /// <summary>Posição do ponto no desenho (m), pelas coordenadas e pela referência do projeto.</summary>
    public Vec2? Position { get; init; }
    public LotRecord? Lot { get; init; }
    public string VertexName { get; init; } = "";
    public Vec2? Vertex { get; init; }
    public double? Distance { get; init; }
}

public static class ControlPointCheck
{
    /// <summary>Posição do ponto de controle no desenho (null sem referência).</summary>
    public static Vec2? Position(ProjectContext ctx, ControlPoint cp) =>
        ctx.Coords.FromGeodetic(new GeodeticCoord(cp.Lat, cp.Lon, ctx.Project.Reference.Height));

    /// <summary>Para cada ponto de controle, o vértice mais próximo dos lotes ativos e a distância (m).</summary>
    public static List<ControlPointResult> Compare(ProjectContext ctx)
    {
        var verts = new List<(LotRecord lot, VertexReport v)>();
        foreach (var lot in ctx.Project.ActiveLots)
        {
            try { foreach (var v in ctx.Report(lot).Vertices) verts.Add((lot, v)); } catch { }
        }
        var res = new List<ControlPointResult>();
        foreach (var cp in ctx.Project.ControlPoints)
        {
            var pos = Position(ctx, cp);
            if (pos is null || verts.Count == 0) { res.Add(new ControlPointResult { Point = cp, Position = pos }); continue; }
            var best = verts.OrderBy(x => x.v.Internal.DistanceTo(pos.Value)).First();
            res.Add(new ControlPointResult
            {
                Point = cp, Position = pos, Lot = best.lot, VertexName = best.v.Name, Vertex = best.v.Internal,
                Distance = best.v.Internal.DistanceTo(pos.Value),
            });
        }
        return res;
    }
}
