using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;

namespace GeoPoint.Core.Calc;

/// <summary>Rótulo do lote (o mesmo na vista do Revit, nas pranchas e nas prévias).</summary>
public static class LotLabels
{
    /// <summary>
    /// Nome; proprietário; matrícula; área; perímetro (sem CPF). Lotes resultantes de desmembramento/remembramento
    /// sem matrícula própria não herdam a do imóvel (só o remanescente).
    /// </summary>
    public static string Text(LotReport rep, ParcelProject project, ValueFormatter fmt, bool owner = true, bool matricula = true, bool perimeter = true)
    {
        var lot = rep.Lot;
        var lines = new List<string> { lot.IsRemanescente ? lot.DisplayName.ToUpperInvariant() : lot.DisplayName };
        if (owner)
        {
            string o = project.EffectiveOwner(lot);
            if (!string.IsNullOrWhiteSpace(o)) lines.Add(o.Trim().ToUpperInvariant());
        }
        if (matricula)
        {
            string m = project.EffectiveMatricula(lot);
            if (!string.IsNullOrWhiteSpace(m)) lines.Add("MATRÍCULA: " + m.Trim());
        }
        lines.Add("ÁREA = " + fmt.Area(rep.AreaLocal));
        if (perimeter) lines.Add("PERÍMETRO = " + fmt.Distance(rep.PerimeterLocal));
        return string.Join("\n", lines);
    }
}
