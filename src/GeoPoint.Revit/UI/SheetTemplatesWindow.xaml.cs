using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GeoPoint.Core.Sheets;

namespace GeoPoint.Revit.UI;

/// <summary>Linha da tabela de blocos (edita o bloco do modelo).</summary>
public sealed class BlockRow : Notify
{
    public BlockRow(SheetBlock b) { Block = b; }
    public SheetBlock Block { get; }
    public string KindText => Block.Kind switch
    {
        SheetBlockKind.Identificacao => "Identificação",
        SheetBlockKind.Registro => "Registro/Localização",
        SheetBlockKind.QuadroAreas => "Quadro de áreas",
        SheetBlockKind.Assinaturas => "Assinaturas",
        SheetBlockKind.Convencoes => "Convenções",
        SheetBlockKind.Orientacao => "Orientação/Escala",
        SheetBlockKind.Croqui => "Imagem de localização",
        SheetBlockKind.Titulo => "Título e folha",
        SheetBlockKind.Escritorio => "Escritório (logomarca)",
        _ => "Observações",
    };
    public bool Visible { get => Block.Visible; set { Block.Visible = value; OnChanged(); } }
    public string Title { get => Block.Title; set { Block.Title = value ?? ""; OnChanged(); } }
    public double X { get => Block.X; set { Block.X = value; OnChanged(); } }
    public double Y { get => Block.Y; set { Block.Y = value; OnChanged(); } }
    public double W { get => Block.W; set { Block.W = value; OnChanged(); } }
    public double H { get => Block.H; set { Block.H = value; OnChanged(); } }
    public string Text { get => Block.Text; set { Block.Text = value ?? ""; OnChanged(); } }
}

public partial class SheetTemplatesWindow : Window
{
    private List<SheetTemplate> _all = new();
    private SheetTemplate? _cur;
    private bool _loading;

    public SheetTemplatesWindow(string? select)
    {
        InitializeComponent();
        foreach (var c in new[] { TNorth, TTableCorner })
            c.ItemsSource = new[] { "Inferior esquerdo", "Inferior direito", "Superior direito", "Superior esquerdo" };
        FolderHint.Text += $"\nPasta: {SheetTemplateLibrary.Folder}";
        Reload(select);
    }

    /// <summary>Modelo escolhido com "Usar este modelo" (null = fechou sem escolher).</summary>
    public string? ChosenName { get; private set; }

    private void Reload(string? select)
    {
        _all = SheetTemplateLibrary.LoadAll();
        List.ItemsSource = null;
        List.ItemsSource = _all;
        List.SelectedItem = _all.FirstOrDefault(t => string.Equals(t.Name, select, StringComparison.OrdinalIgnoreCase)) ?? _all.FirstOrDefault();
    }

    private static string N(double v) => v.ToString("0.##", Fmt.Pt);

    private void Load(SheetTemplate t)
    {
        _loading = true;
        _cur = t.Clone();
        TName.Text = _cur.Name; TDesc.Text = _cur.Description;
        TPaper.SelectedIndex = Math.Max(0, new[] { "A4", "A3", "A2", "A1" }.ToList().IndexOf(_cur.Paper.ToUpperInvariant()));
        TOrient.SelectedIndex = _cur.Orientation == SheetOrientation.Paisagem ? 1 : 0;
        TMarginL.Text = N(_cur.MarginLeft); TMargin.Text = N(_cur.Margin); TGap.Text = N(_cur.Gap);
        TGrid.IsChecked = _cur.Grid; TGridMm.Text = N(_cur.GridSpacingMm); TBand.Text = N(_cur.MapLabelBand);
        TGridColor.Text = string.Join(",", _cur.GridColor);
        TNorth.SelectedIndex = (int)_cur.NorthCorner; TNorthMm.Text = N(_cur.NorthSizeMm);
        TTables.IsChecked = _cur.TablesInsideMap; TTableCorner.SelectedIndex = (int)_cur.TableCorner;
        TDrawMargin.Text = N(_cur.DrawingMarginMm);
        TScales.Text = string.Join(", ", _cur.Scales);
        var st = _cur.Styles;
        SFont.Text = st.Font; STitulo.Text = N(st.TituloMm); SCab.Text = N(st.CabecalhoMm); SRot.Text = N(st.RotuloMm);
        SVal.Text = N(st.ValorMm); STab.Text = N(st.TabelaMm); SGrade.Text = N(st.GradeMm); SDes.Text = N(st.DesenhoMm);
        TOffice.Text = _cur.OfficeName; ShowLogo();
        Blocks.ItemsSource = _cur.Blocks.Select(b => new BlockRow(b)).ToList();
        History.ItemsSource = _cur.History.OrderByDescending(h => h.Version).ToList();
        _loading = false;
        RefreshPreview();
    }

    /// <summary>Lê os campos para o modelo em edição. Devolve false (com mensagem) se algum número for inválido.</summary>
    private bool Collect()
    {
        if (_cur is null) return false;
        Blocks.CommitEdit(); Blocks.CommitEdit();
        double Num(TextBox b, string label, double min, double max)
        {
            if (!Fmt.TryNum(b.Text, out double v) || v < min || v > max) throw new FormatException($"{label}: informe um número entre {N(min)} e {N(max)}.");
            return v;
        }
        try
        {
            var t = _cur;
            t.Name = TName.Text.Trim();
            t.Description = TDesc.Text.Trim();
            t.Paper = (TPaper.SelectedItem as ComboBoxItem)?.Content as string ?? "A4";
            t.Orientation = TOrient.SelectedIndex == 1 ? SheetOrientation.Paisagem : SheetOrientation.Retrato;
            t.MarginLeft = Num(TMarginL, "Margem esquerda", 0, 60);
            t.Margin = Num(TMargin, "Margens", 0, 40);
            t.Gap = Num(TGap, "Espaço entre blocos", 0, 10);
            t.Grid = TGrid.IsChecked == true;
            t.GridSpacingMm = Num(TGridMm, "Espaçamento da grade", 10, 200);
            t.MapLabelBand = Num(TBand, "Faixa dos valores", 2, 20);
            var rgb = TGridColor.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => byte.TryParse(x, out var b) ? b : (byte)175).ToArray();
            t.GridColor = rgb.Length == 3 ? rgb : new byte[] { 175, 175, 175 };
            t.NorthCorner = (MapCorner)Math.Max(0, TNorth.SelectedIndex);
            t.NorthSizeMm = Num(TNorthMm, "Tamanho do norte", 6, 40);
            t.TablesInsideMap = TTables.IsChecked == true;
            t.TableCorner = (MapCorner)Math.Max(0, TTableCorner.SelectedIndex);
            t.DrawingMarginMm = Num(TDrawMargin, "Folga do desenho", 0, 60);
            t.Scales = TScales.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Replace("1:", "")).Select(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0).Where(v => v >= 10).Distinct().OrderBy(v => v).ToList();
            var st = t.Styles;
            st.Font = string.IsNullOrWhiteSpace(SFont.Text) ? "Arial" : SFont.Text.Trim();
            st.TituloMm = Num(STitulo, "Título", 1, 12); st.CabecalhoMm = Num(SCab, "Cabeçalho", 1, 8); st.RotuloMm = Num(SRot, "Rótulos", 1, 8);
            st.ValorMm = Num(SVal, "Valores", 1, 8); st.TabelaMm = Num(STab, "Tabelas", 1, 8); st.GradeMm = Num(SGrade, "Grade", 1, 8);
            st.DesenhoMm = Num(SDes, "Desenho", 0, 8);
            t.OfficeName = TOffice.Text.Trim();
            return true;
        }
        catch (FormatException ex)
        {
            Fmt.Msg(ex.Message, icon: MessageBoxImage.Warning);
            return false;
        }
    }

    private void RefreshPreview()
    {
        if (_cur is null || _loading) return;
        var probs = _cur.Validate();
        Problems.Text = probs.Count == 0 ? "" : "⚠ " + string.Join("  ", probs.Take(4));
        try { Preview.ShowSample(_cur); } catch (Exception ex) { Problems.Text = "Prévia: " + ex.Message; }
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (List.SelectedItem is SheetTemplate t) Load(t);
    }

    private void Preview_Click(object sender, RoutedEventArgs e) { if (Collect()) RefreshPreview(); }

    private void Blocks_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e) => Dispatcher.BeginInvoke(new Action(RefreshPreview));

    private void Paper_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _cur is null) return;
        if (!Collect()) return;
        RefreshPreview();
    }

    private void ResetBlocks_Click(object sender, RoutedEventArgs e)
    {
        if (!Collect() || _cur is null) return;
        // Recalcula os blocos para o papel/margens atuais, mantendo títulos, visibilidade e textos.
        var blocks = SheetTemplate.DefaultBlocks(_cur);
        foreach (var b in blocks)
        {
            var old = _cur.Blocks.FirstOrDefault(x => x.Kind == b.Kind);
            // Modelos até 1.5.2 tinham o bloco Escritório sem posição: agora ele vira o espaço da logomarca.
            if (old is not null && !(b.Kind == SheetBlockKind.Escritorio && old.W <= 0)) { b.Title = old.Title; b.Text = old.Text; if (b.W > 0) b.Visible = old.Visible; }
        }
        _cur.Blocks = blocks;
        Blocks.ItemsSource = _cur.Blocks.Select(b => new BlockRow(b)).ToList();
        RefreshPreview();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!Collect() || _cur is null) return;
        var probs = _cur.Validate();
        if (probs.Count > 0 && MessageBox.Show(this, "O modelo tem pendências:\n\n• " + string.Join("\n• ", probs) + "\n\nSalvar assim mesmo?", "GeoPoint", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var original = List.SelectedItem as SheetTemplate;
        try
        {
            // Renomeado: grava com o nome novo e remove o arquivo antigo.
            if (original is not null && !string.Equals(original.Name, _cur.Name, StringComparison.OrdinalIgnoreCase))
            {
                if (_all.Any(t => t != original && string.Equals(t.Name, _cur.Name, StringComparison.OrdinalIgnoreCase)))
                { Fmt.Msg("Já existe um modelo com esse nome.", icon: MessageBoxImage.Warning); return; }
                SheetTemplateLibrary.Save(_cur, Note.Text.Trim());
                SheetTemplateLibrary.Delete(original);
            }
            else SheetTemplateLibrary.Save(_cur, Note.Text.Trim());
            Note.Text = "";
            Reload(_cur.Name);
        }
        catch (Exception ex) { Fmt.Msg("Não foi possível salvar: " + ex.Message, icon: MessageBoxImage.Error); }
    }

    private string UniqueName(string baseName)
    {
        string n = baseName;
        for (int i = 2; _all.Any(t => string.Equals(t.Name, n, StringComparison.OrdinalIgnoreCase)); i++) n = $"{baseName} ({i})";
        return n;
    }

    private void Create(SheetTemplate t)
    {
        try
        {
            t.History.Clear();
            t.Version = 1;
            SheetTemplateLibrary.Save(t, "criado");
            Reload(t.Name);
        }
        catch (Exception ex) { Fmt.Msg("Não foi possível criar: " + ex.Message, icon: MessageBoxImage.Error); }
    }

    private void NewPortrait_Click(object sender, RoutedEventArgs e) { var t = SheetTemplate.Default(); t.Name = UniqueName("Novo modelo retrato"); Create(t); }
    private void NewLandscape_Click(object sender, RoutedEventArgs e) { var t = SheetTemplate.Default(SheetOrientation.Paisagem); t.Name = UniqueName("Novo modelo paisagem"); Create(t); }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not SheetTemplate t) return;
        var c = t.Clone();
        c.Name = UniqueName(t.Name + " – cópia");
        Create(c);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not SheetTemplate t) return;
        if (MessageBox.Show(this, $"Excluir o modelo \"{t.Name}\"? (Os modelos padrão do GeoPoint voltam automaticamente.)", "GeoPoint", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { SheetTemplateLibrary.Delete(t); Reload(null); }
        catch (Exception ex) { Fmt.Msg("Não foi possível excluir: " + ex.Message, icon: MessageBoxImage.Error); }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Importar modelo de prancha", Filter = "Modelo GeoPoint (*.json)|*.json" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var t = SheetTemplateLibrary.Import(dlg.FileName);
            if (_all.Any(x => string.Equals(x.Name, t.Name, StringComparison.OrdinalIgnoreCase))) t.Name = UniqueName(t.Name);
            SheetTemplateLibrary.Save(t, "importado");
            Reload(t.Name);
        }
        catch (Exception ex) { Fmt.Msg("Arquivo inválido: " + ex.Message, icon: MessageBoxImage.Warning); }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not SheetTemplate t) return;
        var dlg = new Microsoft.Win32.SaveFileDialog { Title = "Exportar modelo de prancha", Filter = "Modelo GeoPoint (*.json)|*.json", FileName = t.Name + ".json" };
        if (dlg.ShowDialog(this) != true) return;
        try { SheetTemplateLibrary.Export(t, dlg.FileName); }
        catch (Exception ex) { Fmt.Msg("Não foi possível exportar: " + ex.Message, icon: MessageBoxImage.Error); }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_cur is null || History.SelectedItem is not SheetTemplateVersion v) { Fmt.Msg("Selecione uma versão no histórico."); return; }
        try
        {
            var restored = SheetTemplateLibrary.Restore(_cur, v);
            SheetTemplateLibrary.Save(restored, $"restaurada a v{v.Version}");
            Reload(restored.Name);
        }
        catch (Exception ex) { Fmt.Msg("Não foi possível restaurar: " + ex.Message, icon: MessageBoxImage.Error); }
    }

    /// <summary>Mostra a situação da logomarca do modelo.</summary>
    private void ShowLogo()
    {
        if (_cur is null) { TLogo.Text = ""; return; }
        var bytes = _cur.LogoBytes();
        var size = ImageHeader.Size(bytes);
        if (bytes is null || size is null) { TLogo.Text = "(sem logomarca – o espaço fica em branco na prancha)"; return; }
        string where = _cur.LogoData.Length > 0 ? "guardada no modelo" : "arquivo " + System.IO.Path.GetFileName(_cur.LogoPath);
        TLogo.Text = $"{(ImageHeader.Extension(bytes) ?? "").ToUpperInvariant()} {size.Value.W}×{size.Value.H} px, {bytes.Length / 1024.0:0} KB – {where}";
        if (_cur.LogoData.Length == 0 && size is not null)
        {
            // Modelo antigo (só o caminho do arquivo): passa a guardar a imagem no modelo.
            try { _cur.SetLogo(bytes); TLogo.Text += " (será guardada no modelo ao salvar)"; } catch { }
        }
        var vis = _cur.Blocks.FirstOrDefault(b => b.Kind == SheetBlockKind.Escritorio);
        if (vis is { Visible: false }) TLogo.Text += " – ligue o bloco Escritório na tabela abaixo";
    }

    private void Logo_Click(object sender, RoutedEventArgs e)
    {
        if (_cur is null) return;
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Logomarca do escritório", Filter = "Imagens|*.png;*.jpg;*.jpeg;*.gif;*.bmp" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            if (!Collect()) return;
            _cur.SetLogo(System.IO.File.ReadAllBytes(dlg.FileName));
            // Sem bloco Escritório visível, a logomarca não aparece: liga o bloco (na posição padrão se ainda não tiver tamanho).
            var b = _cur.Blocks.FirstOrDefault(x => x.Kind == SheetBlockKind.Escritorio);
            if (b is not null && (!b.Visible || b.W <= 2 || b.H <= 2))
            {
                var d = SheetTemplate.DefaultBlocks(_cur).First(x => x.Kind == SheetBlockKind.Escritorio);
                if (b.W <= 2 || b.H <= 2) { b.X = d.X; b.Y = d.Y; b.W = d.W; b.H = d.H; }
                b.Visible = true;
                Blocks.ItemsSource = _cur.Blocks.Select(x => new BlockRow(x)).ToList();
            }
            ShowLogo();
            RefreshPreview();
        }
        catch (Exception ex) { Fmt.Msg(ex.Message, icon: MessageBoxImage.Warning); }
    }

    private void LogoRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_cur is null) return;
        if (!Collect()) return;
        _cur.SetLogo(null);
        ShowLogo();
        RefreshPreview();
    }

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        ChosenName = (List.SelectedItem as SheetTemplate)?.Name;
        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
