using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using GeoPoint.Core.Settings;
using GeoPoint.Core.Sheets;

namespace GeoPoint.Revit.Infrastructure;

/// <summary>
/// Imagens de satélite guardadas dentro do arquivo do Revit (Extensible Storage): um registro por imagem, separado dos dados do
/// projeto, para o JSON dos lotes continuar leve.
/// </summary>
public static class ImageStore
{
    private static readonly Guid SchemaGuid = new("C3A1F0D2-5B7E-4E21-9F64-1A2B3C4D5E6F");
    private const string FieldId = "ImageId";
    private const string FieldData = "Data";

    private static Schema GetSchema()
    {
        var s = Schema.Lookup(SchemaGuid);
        if (s is not null) return s;
        var b = new SchemaBuilder(SchemaGuid);
        b.SetSchemaName("GeoPointImage");
        b.SetVendorId("GEOPOINT");
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Public);
        b.SetDocumentation("Imagem de satélite das pranchas do GeoPoint (JPEG em base64).");
        b.AddSimpleField(FieldId, typeof(string));
        b.AddSimpleField(FieldData, typeof(string));
        return b.Finish();
    }

    private static IEnumerable<(DataStorage Ds, Entity E)> All(Document doc)
    {
        var schema = Schema.Lookup(SchemaGuid);
        if (schema is null) yield break;
        foreach (var ds in new FilteredElementCollector(doc).OfClass(typeof(DataStorage)).Cast<DataStorage>())
        {
            Entity? e = null;
            try { e = ds.GetEntity(schema); } catch { }
            if (e is not null && e.IsValid()) yield return (ds, e);
        }
    }

    public static byte[]? Load(Document doc, string id)
    {
        try
        {
            foreach (var (_, e) in All(doc))
                if (e.Get<string>(FieldId) == id) return Convert.FromBase64String(e.Get<string>(FieldData));
        }
        catch (Exception ex) { Logger.Error("Ler imagem do projeto", ex); }
        return null;
    }

    /// <summary>Grava (ou substitui) a imagem. Dentro de transação.</summary>
    public static void Save(Document doc, string id, byte[] jpeg)
    {
        var schema = GetSchema();
        var ds = All(doc).Where(x => x.E.Get<string>(FieldId) == id).Select(x => x.Ds).FirstOrDefault() ?? DataStorage.Create(doc);
        var e = new Entity(schema);
        e.Set(FieldId, id);
        e.Set(FieldData, Convert.ToBase64String(jpeg));
        ds.SetEntity(e);
    }

    /// <summary>Apaga os registros de imagem que o projeto não usa mais. Dentro de transação.</summary>
    public static int Prune(Document doc, IEnumerable<string> keepIds)
    {
        var keep = keepIds.ToHashSet();
        var del = All(doc).Where(x => !keep.Contains(x.E.Get<string>(FieldId))).Select(x => x.Ds.Id).ToList();
        if (del.Count > 0) doc.Delete(del);
        return del.Count;
    }
}

/// <summary>Leitura, redução, recorte e gravação das imagens de satélite (WPF, sem bibliotecas externas).</summary>
public static class ImageCodec
{
    public static BitmapSource FromFile(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    public static BitmapSource Decode(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Imagem pronta para guardar: lado maior até 1920 px, JPEG qualidade 90 (um print Full HD fica com uns 300–600 KB).</summary>
    public static byte[] Normalize(BitmapSource src)
    {
        var (w, h) = LocationImages.Reduce(src.PixelWidth, src.PixelHeight);
        BitmapSource img = src;
        if (w != src.PixelWidth || h != src.PixelHeight)
            img = new TransformedBitmap(src, new ScaleTransform((double)w / src.PixelWidth, (double)h / src.PixelHeight));
        // Sem transparência (print colado vem em BGRA): fundo branco.
        if (img.Format != PixelFormats.Bgr24 && img.Format != PixelFormats.Bgr32)
            img = new FormatConvertedBitmap(img, PixelFormats.Bgr24, null, 0);
        return Jpeg(img);
    }

    private static byte[] Jpeg(BitmapSource img, int quality = 90)
    {
        var enc = new JpegBitmapEncoder { QualityLevel = quality };
        enc.Frames.Add(BitmapFrame.Create(img));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Recorte central com a proporção do espaço (a imagem preenche o espaço inteiro, sem distorcer).</summary>
    public static BitmapSource Cover(BitmapSource src, double aspect)
    {
        var (x, y, w, h) = LocationImages.Cover(src.PixelWidth, src.PixelHeight, aspect);
        if (x == 0 && y == 0 && w == src.PixelWidth && h == src.PixelHeight) return src;
        var c = new CroppedBitmap(src, new System.Windows.Int32Rect(x, y, w, h));
        c.Freeze();
        return c;
    }

    /// <summary>
    /// Arquivo JPEG recortado na proporção do espaço da prancha, na pasta de cache (o Revit insere a imagem a partir de um arquivo e
    /// guarda uma cópia no modelo). Mesmo id e mesma proporção = mesmo arquivo (o tipo de imagem do Revit é reaproveitado).
    /// </summary>
    public static string CroppedFile(byte[] jpeg, string id, double aspect)
    {
        string folder = Path.Combine(SettingsStore.DefaultFolder, "imagens");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"localizacao-{id[..Math.Min(12, id.Length)]}-{Math.Round(aspect * 1000):0}.jpg");
        if (File.Exists(path)) return path;
        var cropped = Cover(Decode(jpeg), aspect);
        File.WriteAllBytes(path, Jpeg(cropped, 92));
        return path;
    }
}
