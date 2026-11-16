using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;
using GeoPoint.Core.Sheets;
using GeoPoint.Revit.UI;

namespace GeoPoint.Revit.Infrastructure;

/// <summary>
/// Galeria de imagens de satélite do projeto: grava o que mudou na galeria e as escolhas das pranchas, converte os dados antigos e
/// coloca/troca a imagem nas pranchas já geradas.
/// </summary>
public static class LocationImageService
{
    /// <summary>
    /// Dados antigos: as imagens da 1.7 (uma por fase/geral) viram galeria + padrões; o caminho de arquivo das versões anteriores vira
    /// a imagem geral guardada no projeto. Abre a própria transação se precisar gravar.
    /// </summary>
    public static void Prepare(GpSession s)
    {
        bool changed = LocationImages.Migrate(s.Project);
        string path = s.Project.Info.ImagemLocalizacao?.Trim() ?? "";
        byte[]? legacy = null;
        if (path.Length > 0)
        {
            if (!s.Project.ImageDefaults.ContainsKey("") && File.Exists(path))
            {
                try { legacy = ImageCodec.Normalize(ImageCodec.FromFile(path)); }
                catch (Exception ex) { Logger.Error("Migrar imagem de localização", ex); }
            }
            s.Project.Info.ImagemLocalizacao = "";
            changed = true;
        }
        if (!changed) return;
        using var t = new Transaction(s.Doc, "GeoPoint: galeria de imagens");
        t.Start();
        if (legacy is not null)
        {
            var dec = ImageCodec.Decode(legacy);
            var meta = new SatelliteImage { Nome = Path.GetFileNameWithoutExtension(path), Width = dec.PixelWidth, Height = dec.PixelHeight, Bytes = legacy.Length };
            ImageStore.Save(s.Doc, meta.Id, legacy);
            s.Project.SatelliteImages.Add(meta);
            s.Project.ImageDefaults[""] = new SheetImageChoice { ImageId = meta.Id };
        }
        ProjectStore.Save(s.Doc, s.Project);
        t.Commit();
    }

    /// <summary>
    /// Grava o resultado da galeria (imagens novas, renomeadas, removidas) e a escolha. Devolve as chaves das pranchas cuja imagem
    /// muda. Dentro de transação.
    /// </summary>
    public static List<string> Apply(GpSession s, GalleryResult r, string? sheetKey, string faseId, IEnumerable<string> keysOfFase)
    {
        var p = s.Project;
        var changed = new List<string>();
        foreach (var (meta, jpeg) in r.Added)
        {
            ImageStore.Save(s.Doc, meta.Id, jpeg);
            p.SatelliteImages.Add(meta);
        }
        foreach (var (id, name) in r.Renamed)
            if (LocationImages.Find(p, id) is { } img) img.Nome = name;
        foreach (var id in r.Removed)
        {
            // Pranchas que usavam a imagem (direto ou pelo padrão) mudam.
            changed.AddRange(p.Sheets.Where(g => LocationImages.Resolve(p, g.Key, g.FaseId)?.ImageId == id).Select(g => g.Key));
            LocationImages.Remove(p, id);
        }
        var c = r.Choice;
        switch (r.Action)
        {
            case GalleryAction.Sheet when sheetKey is not null && c is not null:
                p.SheetImages[sheetKey] = c.Clone();
                changed.Add(sheetKey);
                break;
            case GalleryAction.NoImage when sheetKey is not null:
                p.SheetImages[sheetKey] = new SheetImageChoice { ImageId = "", Locked = c?.Locked ?? false };
                changed.Add(sheetKey);
                break;
            case GalleryAction.Fase when c is not null:
                changed.AddRange(LocationImages.ApplyToFase(p, faseId, c, keysOfFase.Concat(p.Sheets.Where(g => g.FaseId == faseId).Select(g => g.Key))));
                // A prancha da janela também pode estar fixada: a escolha dela é a da fase.
                if (sheetKey is not null && p.SheetImages.TryGetValue(sheetKey, out var own) && own.Locked) { p.SheetImages[sheetKey] = c.Clone(); changed.Add(sheetKey); }
                break;
            case GalleryAction.Geral when c is not null:
                var g0 = c.Clone();
                g0.Locked = false;
                p.ImageDefaults[""] = g0;
                // Pranchas que caem na geral (sem própria nem padrão da fase) mudam.
                changed.AddRange(p.Sheets.Where(g => LocationImages.Origin(p, g.Key, g.FaseId) == "geral").Select(g => g.Key));
                if (sheetKey is not null && LocationImages.Origin(p, sheetKey, faseId) == "geral") changed.Add(sheetKey);
                break;
        }
        ImageStore.Prune(s.Doc, p.SatelliteImages.Select(i => i.Id));
        return changed.Distinct().ToList();
    }

    private static readonly Dictionary<string, byte[]> Cache = new();

    /// <summary>Imagem da prancha (bytes e enquadramento): a dela, senão a da fase, senão a geral; null = espaço em branco.</summary>
    public static (string Id, byte[] Jpeg, SheetImageChoice Choice)? ForSheet(GpSession s, string? sheetKey, string? faseId)
    {
        var c = LocationImages.Resolve(s.Project, sheetKey, faseId);
        if (c is null) return null;
        if (!Cache.TryGetValue(c.ImageId, out var b))
        {
            b = ImageStore.Load(s.Doc, c.ImageId);
            if (b is null) return null;
            if (Cache.Count > 20) Cache.Clear();
            Cache[c.ImageId] = b;
        }
        return (c.ImageId, b, c);
    }

    /// <summary>Contorno dos lotes da prancha (norte verdadeiro para cima), para desenhar sobre a imagem.</summary>
    public static List<List<Vec2>> OutlineFor(GpSession s, IEnumerable<string> lotIds) =>
        LocationImages.Outline(s.NewContext(), lotIds.Select(s.Project.FindLot).Where(l => l is not null).Select(l => l!));

    /// <summary>Resumo para a janela de Pranchas.</summary>
    public static string Summary(ParcelProject p, string? sheetKey, string faseId, string sheetTitle)
    {
        int n = p.SatelliteImages.Count;
        string gal = n == 0 ? "Galeria vazia" : $"Galeria: {n} imagem(ns)";
        if (sheetKey is null) return gal + ". Escolha uma prancha na lista e clique no espaço da imagem na prévia.";
        var c = LocationImages.Resolve(p, sheetKey, faseId);
        string origin = LocationImages.Origin(p, sheetKey, faseId);
        string now = c is null ? (origin.Length > 0 ? "sem imagem (escolhido)" : "sem imagem") : $"{LocationImages.Find(p, c.ImageId)?.Nome} ({origin})";
        return $"{gal}. \"{sheetTitle}\": {now}. Clique no espaço da imagem na prévia para trocar.";
    }

    /// <summary>
    /// Troca a imagem nas pranchas já geradas (pela escolha atual de cada uma): apaga a anterior e coloca a nova no espaço gravado.
    /// Pranchas que não tinham sido editadas continuam "não editadas". Dentro de transação. Devolve quantas mudaram.
    /// </summary>
    public static int Replace(GpSession s, IEnumerable<GeneratedSheet> sheets, List<string> warnings)
    {
        int n = 0;
        foreach (var g in sheets)
        {
            if (ElementUtil.ByUniqueId(s.Doc, g.SheetId) is not ViewSheet sheet) continue;
            if (g.CroquiX1 - g.CroquiX0 < 1 || g.CroquiY1 - g.CroquiY0 < 1) { warnings.Add($"{g.Title}: prancha gerada antes da 1.8 – gere de novo para trocar a imagem pelo GeoPoint."); continue; }
            bool wasEdited = SheetRegistry.IsEdited(s, g);
            SheetImagePlacer.Clear(s.Doc, g.CroquiIds);
            g.CroquiIds.Clear();
            var img = ForSheet(s, g.Key, g.FaseId);
            if (img is { } im)
            {
                var r = new PaperRect(g.CroquiX0, g.CroquiY0, g.CroquiX1, g.CroquiY1);
                g.CroquiIds.AddRange(SheetImagePlacer.Place(s.Doc, sheet, r, im.Id, im.Jpeg, im.Choice, OutlineFor(s, g.LotIds), warnings));
            }
            s.Doc.Regenerate();
            if (!wasEdited) SheetRegistry.Refresh(s, g);
            n++;
        }
        return n;
    }
}
