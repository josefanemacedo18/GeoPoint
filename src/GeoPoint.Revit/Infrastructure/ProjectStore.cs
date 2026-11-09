using System;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using GeoPoint.Core.Model;
using GeoPoint.Core.Settings;

namespace GeoPoint.Revit.Infrastructure;

/// <summary>Grava e lê os dados do GeoPoint no próprio arquivo do Revit (Extensible Storage).</summary>
public static class ProjectStore
{
    private static readonly Guid SchemaGuid = new("6B1E2C64-0E7A-4C55-9A0D-3D5D3B1F2A91");
    private const string FieldJson = "Json";
    private const string FieldVersion = "Version";

    private static Schema GetSchema()
    {
        var s = Schema.Lookup(SchemaGuid);
        if (s is not null) return s;
        var b = new SchemaBuilder(SchemaGuid);
        b.SetSchemaName("GeoPointData");
        b.SetVendorId("GEOPOINT");
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Public);
        b.SetDocumentation("Dados do plugin GeoPoint (lotes, referência geodésica, operações).");
        b.AddSimpleField(FieldJson, typeof(string));
        b.AddSimpleField(FieldVersion, typeof(int));
        return b.Finish();
    }

    private static DataStorage? Find(Document doc, Schema schema)
    {
        foreach (var ds in new FilteredElementCollector(doc).OfClass(typeof(DataStorage)).Cast<DataStorage>())
        {
            try
            {
                var e = ds.GetEntity(schema);
                if (e is not null && e.IsValid()) return ds;
            }
            catch { }
        }
        return null;
    }

    public static ParcelProject Load(Document doc)
    {
        ParcelProject? project = null;
        try
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema is not null)
            {
                var ds = Find(doc, schema);
                if (ds is not null)
                {
                    var json = ds.GetEntity(schema).Get<string>(FieldJson);
                    if (!string.IsNullOrWhiteSpace(json))
                        project = JsonSerializer.Deserialize<ParcelProject>(json, JsonOptions.Default);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Falha ao ler dados do GeoPoint do modelo.", ex);
            throw new InvalidOperationException("Os dados do GeoPoint gravados neste modelo não puderam ser lidos. Detalhes no registro em " + Logger.Folder, ex);
        }
        project ??= new ParcelProject();
        Normalize(project);
        project.Frame = CaptureFrame(doc);
        return project;
    }

    private static void Normalize(ParcelProject p)
    {
        p.Reference ??= new GeoReferenceRecord();
        p.Info ??= new GeoPoint.Core.Model.ProjectInfo();
        p.Lots ??= new();
        p.Operations ??= new();
        p.Fases ??= new();
        p.Vias ??= new();
        p.Cruzamentos ??= new();
        p.ControlPoints ??= new();
        p.Sheets ??= new();
        p.StreetDrawings ??= new();
        foreach (var f in p.Fases)
        {
            f.ViewIds ??= new();
            f.HiddenByView ??= new();
            f.Vertices ??= new();
            f.QuadraVertexCounters ??= new();
        }
        _ = p.Fase; // migra projetos antigos para a Fase 0
        foreach (var l in p.Lots)
        {
            l.Outer ??= new();
            l.Holes ??= new();
            l.EdgeInfos ??= new();
            l.Annotations ??= new();
            l.History ??= new();
            l.VertexElevations ??= new();
            l.FilledRegionIds ??= new();
            l.AuxElementIds ??= new();
            foreach (var e in l.Outer) e.Src ??= new();
        }
    }

    /// <summary>Deve ser chamado dentro de uma transação aberta.</summary>
    public static void Save(Document doc, ParcelProject project)
    {
        _ = project.Fase; // lotes e operações novos ficam na fase ativa
        var schema = GetSchema();
        var ds = Find(doc, schema) ?? DataStorage.Create(doc);
        var entity = new Entity(schema);
        entity.Set(FieldJson, JsonSerializer.Serialize(project, JsonOptions.Compact));
        entity.Set(FieldVersion, project.SchemaVersion);
        ds.SetEntity(entity);
    }

    /// <summary>Quadro das coordenadas compartilhadas (norte verdadeiro) lido do projeto.</summary>
    public static SharedFrame CaptureFrame(Document doc)
    {
        var f = new SharedFrame();
        try
        {
            var loc = doc.ActiveProjectLocation;
            var p0 = loc.GetProjectPosition(XYZ.Zero);
            f.OriginE = p0.EastWest * RvtUnits.FtToM;
            f.OriginN = p0.NorthSouth * RvtUnits.FtToM;
            f.OriginElevation = p0.Elevation * RvtUnits.FtToM;
            f.ReportedAngleRad = p0.Angle;
            try
            {
                // Direção do eixo X interno no sistema compartilhado (Y = norte verdadeiro), como em
                // "Informar coordenadas compartilhadas" do Revit.
                var p1 = loc.GetProjectPosition(new XYZ(100, 0, 0));
                double dE = p1.EastWest - p0.EastWest, dN = p1.NorthSouth - p0.NorthSouth;
                if (Math.Abs(dE) + Math.Abs(dN) > 1e-9)
                {
                    f.AngleRad = Math.Atan2(dN, dE);
                    f.Method = "coordenadas compartilhadas de dois pontos";
                }
                else
                {
                    f.AngleRad = p0.Angle;
                    f.Method = "ângulo do ProjectPosition";
                }
            }
            catch
            {
                f.AngleRad = p0.Angle;
                f.Method = "ângulo do ProjectPosition";
            }
            Logger.Info($"Norte verdadeiro: rotação {f.AngleRad * 180 / Math.PI:0.######}° ({f.Method}); ProjectPosition.Angle = {p0.Angle * 180 / Math.PI:0.######}°");
        }
        catch (Exception ex)
        {
            Logger.Error("Não foi possível ler a localização do projeto.", ex);
        }
        return f;
    }
}
