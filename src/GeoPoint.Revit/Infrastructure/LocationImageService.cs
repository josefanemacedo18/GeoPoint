using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using GeoPoint.Core.Model;
using GeoPoint.Core.Sheets;
using GeoPoint.Revit.UI;

namespace GeoPoint.Revit.Infrastructure;

/// <summary>Grava as imagens de satélite escolhidas no projeto e monta o resumo mostrado na janela de Pranchas.</summary>
public static class LocationImageService
{
    /// <summary>Aplica as mudanças da janela (imagens novas e removidas). Abre a própria transação.</summary>
    public static void Apply(GpSession s, SatelliteImageChanges ch)
    {
        if (!ch.Any) return;
        using var t = new Transaction(s.Doc, "GeoPoint: imagens de satélite");
        t.Start();
        foreach (var scope in ch.RemovedScopes)
            if (LocationImages.Own(s.Project, scope) is { } old) s.Project.SatelliteImages.Remove(old);
        foreach (var (meta, jpeg) in ch.Added)
        {
            ImageStore.Save(s.Doc, meta.Id, jpeg);
            LocationImages.Set(s.Project, meta);
        }
        LocationImages.Prune(s.Project);
        ImageStore.Prune(s.Doc, s.Project.SatelliteImages.Select(i => i.Id));
        ProjectStore.Save(s.Doc, s.Project);
        t.Commit();
    }

    /// <summary>
    /// Projetos anteriores guardavam só o caminho da imagem de localização: se o arquivo ainda existe e não há imagem geral, ele
    /// passa a ser a imagem geral guardada no projeto. Abre a própria transação.
    /// </summary>
    public static void MigrateLegacy(GpSession s)
    {
        string path = s.Project.Info.ImagemLocalizacao?.Trim() ?? "";
        if (path.Length == 0) return;
        if (LocationImages.Own(s.Project, "") is not null || !File.Exists(path))
        {
            if (LocationImages.Own(s.Project, "") is not null) s.Project.Info.ImagemLocalizacao = "";
            return;
        }
        try
        {
            var jpeg = ImageCodec.Normalize(ImageCodec.FromFile(path));
            var dec = ImageCodec.Decode(jpeg);
            var ch = new SatelliteImageChanges();
            ch.Added.Add((new SatelliteImage { Nome = Path.GetFileName(path), Width = dec.PixelWidth, Height = dec.PixelHeight, Bytes = jpeg.Length }, jpeg));
            s.Project.Info.ImagemLocalizacao = "";
            Apply(s, ch);
        }
        catch (Exception ex) { Logger.Error("Migrar imagem de localização", ex); }
    }

    /// <summary>Resumo para a janela de Pranchas: qual imagem vai nas pranchas da fase ativa.</summary>
    public static string Summary(ParcelProject p)
    {
        var own = LocationImages.Own(p, p.Fase.Id);
        var gen = LocationImages.Own(p, "");
        int fases = p.SatelliteImages.Count(i => i.FaseId.Length > 0);
        string now = own is not null ? $"desta fase: {own.Nome}"
            : gen is not null ? $"geral: {gen.Nome}"
            : "nenhuma (o espaço fica em branco)";
        return $"Nas pranchas desta fase: {now}." + (gen is not null && own is not null ? $"  Geral: {gen.Nome}." : "") +
               (fases > 0 ? $"  {fases} fase(s) com imagem própria." : "");
    }

    /// <summary>Bytes da imagem das pranchas da fase (a da fase ou a geral); null = espaço em branco.</summary>
    public static (string Id, byte[] Jpeg)? ForSheets(GpSession s)
    {
        var img = LocationImages.For(s.Project, s.Project.Fase.Id);
        if (img is null) return null;
        var b = ImageStore.Load(s.Doc, img.Id);
        return b is null ? null : (img.Id, b);
    }
}
