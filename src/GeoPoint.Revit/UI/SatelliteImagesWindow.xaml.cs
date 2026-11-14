using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GeoPoint.Core.Model;
using GeoPoint.Core.Sheets;
using GeoPoint.Revit.Infrastructure;

namespace GeoPoint.Revit.UI;

/// <summary>Imagem de um escopo (geral ou fase) na janela.</summary>
public sealed class ImageScopeRow : Notify
{
    private ImageSource? _thumb;
    private string _status = "", _empty = "";
    private bool _hasOwn;
    public string FaseId { get; init; } = "";
    public string Label { get; init; } = "";
    public bool IsActiveFase { get; init; }
    public double ThumbW { get; init; } = 200;
    public double ThumbH { get; init; } = 150;
    public Brush Back => IsActiveFase ? new SolidColorBrush(Color.FromRgb(0xEA, 0xF2, 0xFB)) : Brushes.White;
    public ImageSource? Thumb { get => _thumb; set => Set(ref _thumb, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public string Empty { get => _empty; set => Set(ref _empty, value); }
    public bool HasOwn { get => _hasOwn; set => Set(ref _hasOwn, value); }
    /// <summary>Imagem guardada hoje (null = nenhuma).</summary>
    public SatelliteImage? Current { get; set; }
    /// <summary>Imagem nova escolhida nesta janela (bytes JPEG prontos para guardar).</summary>
    public byte[]? NewBytes { get; set; }
    public SatelliteImage? NewMeta { get; set; }
    public bool Removed { get; set; }
}

/// <summary>Resultado da janela: imagens novas (com os bytes) e escopos que ficaram sem imagem.</summary>
public sealed class SatelliteImageChanges
{
    public List<(SatelliteImage Meta, byte[] Jpeg)> Added { get; } = new();
    public List<string> RemovedScopes { get; } = new();
    public bool Any => Added.Count > 0 || RemovedScopes.Count > 0;
}

public partial class SatelliteImagesWindow : Window
{
    private readonly ObservableCollection<ImageScopeRow> _rows = new();
    private readonly double _aspect;

    /// <param name="load">Lê os bytes de uma imagem guardada (miniatura).</param>
    /// <param name="aspect">Proporção do espaço de localização do modelo de prancha (largura / altura).</param>
    public SatelliteImagesWindow(ParcelProject project, Func<string, byte[]?> load, double aspect)
    {
        InitializeComponent();
        _aspect = aspect > 0.2 && aspect < 5 ? aspect : 4.0 / 3;
        double tw = _aspect >= 1 ? 200 : 150 * _aspect, th = _aspect >= 1 ? 200 / _aspect : 150;
        var scopes = new List<(string Id, string Label, bool Active)> { ("", "Geral – todas as pranchas", false) };
        string active = project.Fase.Id;
        foreach (var f in project.Fases.OrderBy(f => f.Numero))
            scopes.Add((f.Id, f.Label + (f.Id == active ? "  (fase ativa: as pranchas geradas agora)" : ""), f.Id == active));
        foreach (var (id, label, act) in scopes)
        {
            var row = new ImageScopeRow { FaseId = id, Label = label, IsActiveFase = act, ThumbW = tw, ThumbH = th, Current = LocationImages.Own(project, id) };
            if (row.Current is { } cur && load(cur.Id) is byte[] b)
            {
                try { row.Thumb = ImageCodec.Cover(ImageCodec.Decode(b), _aspect); } catch { }
            }
            _rows.Add(row);
        }
        Rows.ItemsSource = _rows;
        RefreshStatus();
    }

    public SatelliteImageChanges Changes { get; } = new();

    private void RefreshStatus()
    {
        var general = _rows[0];
        bool generalHas = !general.Removed && (general.NewMeta is not null || general.Current is not null);
        string generalName = general.NewMeta?.Nome ?? general.Current?.Nome ?? "";
        foreach (var r in _rows)
        {
            var meta = r.Removed ? null : r.NewMeta ?? r.Current;
            r.HasOwn = meta is not null;
            if (meta is not null)
            {
                r.Status = $"{meta.Nome} – {meta.Width}×{meta.Height} px{(r.NewMeta is not null ? " (nova, guardada ao clicar OK)" : "")}. " +
                           "A miniatura mostra o recorte que entra na prancha.";
                r.Empty = "";
            }
            else if (r.FaseId.Length > 0)
            {
                r.Status = generalHas ? $"Sem imagem própria: as pranchas desta fase usam a geral ({generalName})." : "Sem imagem: o espaço da prancha fica em branco.";
                r.Empty = generalHas ? "usa a geral" : "sem imagem";
                r.Thumb = generalHas ? general.Thumb : null;
            }
            else
            {
                r.Status = "Sem imagem geral: as fases sem imagem própria ficam com o espaço em branco.";
                r.Empty = "sem imagem";
                r.Thumb = null;
            }
        }
    }

    private void Use(ImageScopeRow row, BitmapSource src, string name)
    {
        byte[] jpeg;
        try { jpeg = ImageCodec.Normalize(src); }
        catch (Exception ex) { Fmt.Msg("Não foi possível usar a imagem: " + ex.Message, icon: MessageBoxImage.Warning); return; }
        var dec = ImageCodec.Decode(jpeg);
        row.NewBytes = jpeg;
        row.NewMeta = new SatelliteImage { FaseId = row.FaseId, Nome = name, Width = dec.PixelWidth, Height = dec.PixelHeight, Bytes = jpeg.Length };
        row.Removed = false;
        row.Thumb = ImageCodec.Cover(dec, _aspect);
        RefreshStatus();
    }

    private void Pick_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ImageScopeRow row) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Imagem de satélite – " + row.Label,
            Filter = "Imagens (PNG, JPG, BMP, TIF)|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|Todos|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        try { Use(row, ImageCodec.FromFile(dlg.FileName), System.IO.Path.GetFileName(dlg.FileName)); }
        catch (Exception ex) { Fmt.Msg("Não foi possível abrir a imagem: " + ex.Message, icon: MessageBoxImage.Warning); }
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ImageScopeRow row) return;
        BitmapSource? img = null;
        try
        {
            // PNG primeiro (Win+Shift+S, navegadores): o bitmap genérico da área de transferência às vezes vem com o canal alfa zerado.
            if (Clipboard.ContainsData("PNG") && Clipboard.GetData("PNG") is System.IO.MemoryStream png)
            {
                var dec = BitmapDecoder.Create(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                img = dec.Frames[0];
            }
            else if (Clipboard.ContainsImage()) img = Clipboard.GetImage();
            else if (Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList().Cast<string>().FirstOrDefault() is string f)
                img = ImageCodec.FromFile(f);
        }
        catch (Exception ex) { Logger.Error("Colar imagem", ex); }
        if (img is null)
        {
            Fmt.Msg("Não há imagem copiada.\n\nTire o print (Print Screen ou Win+Shift+S) ou use \"Copiar imagem\" no navegador e clique em Colar print de novo.",
                icon: MessageBoxImage.Information);
            return;
        }
        Use(row, img, $"Print colado em {DateTime.Now:dd/MM/yyyy HH:mm}");
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ImageScopeRow row) return;
        row.NewBytes = null;
        row.NewMeta = null;
        row.Removed = row.Current is not null;
        row.Thumb = null;
        RefreshStatus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _rows)
        {
            if (r.NewMeta is not null && r.NewBytes is not null) Changes.Added.Add((r.NewMeta, r.NewBytes));
            else if (r.Removed) Changes.RemovedScopes.Add(r.FaseId);
        }
        DialogResult = true;
    }
}
