using System.Collections.Generic;
using System.Linq;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Settings;

namespace GeoPoint.Revit.UI;

/// <summary>Sistema de coordenadas oferecido nas telas (rótulo + definição).</summary>
public sealed class CrsChoice : Notify
{
    private bool _checked;
    public CrsChoice(string key, string label, CrsDef def) { Key = key; Label = label; Def = def; }
    public string Key { get; }
    public string Label { get; }
    public CrsDef Def { get; }
    public bool Checked { get => _checked; set => Set(ref _checked, value); }
    public override string ToString() => Label;

    public static List<CrsChoice> Build(GeoPointSettings s) => new()
    {
        new("utm", "UTM – SIRGAS 2000 (fuso automático)", CrsDef.Utm()),
        new("geo", "Geográficas (lat/long) – SIRGAS 2000", CrsDef.Geographic()),
        new("utm-sad", "UTM – SAD 69 (documentos antigos)", CrsDef.Utm(DatumId.Sad69)),
        new("utm-ca", "UTM – Córrego Alegre (documentos antigos)", CrsDef.Utm(DatumId.CorregoAlegre)),
        new("geo-sad", "Geográficas – SAD 69", CrsDef.Geographic(DatumId.Sad69)),
        new("ptl", "Plano Topográfico Local (NBR 14166)", s.Memorial.Ptl.Clone()),
        new("tm", $"{s.Memorial.CustomTm.CustomName} (TM personalizada, MC {s.Memorial.CustomTm.CustomCentralMeridian:0.##}°)", s.Memorial.CustomTm.Clone()),
        new("local", "Coordenadas locais do projeto (Revit)", CrsDef.ProjectLocalDef()),
    };

    /// <summary>Encontra a opção equivalente a uma definição gravada.</summary>
    public static CrsChoice? Match(List<CrsChoice> list, CrsDef d) => list.FirstOrDefault(c =>
        c.Def.Kind == d.Kind && (d.Kind is CrsKind.LocalTopographic or CrsKind.CustomTm or CrsKind.ProjectLocal || c.Def.Datum == d.Datum));
}
