using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Settings;
using GeoPoint.Core.Urban;

namespace GeoPoint.Revit.UI;

public partial class QuadroWindow : Window
{
    private readonly ProjectContext _ctx;
    private readonly GeoPointSettings _settings;
    private QuadroAreas? _q;
    private bool _loading = true;

    public QuadroWindow(ProjectContext ctx)
    {
        InitializeComponent();
        _ctx = ctx;
        _settings = ctx.Settings;
        Profile.ItemsSource = _settings.RuleProfiles.Select(p => p.Name).ToList();
        Profile.SelectedItem = ctx.Rules?.Name;
        _loading = false;
        Refresh();
    }

    public bool ProfileChanged { get; private set; }
    public bool ExportRequested { get; private set; }

    private void Refresh()
    {
        _ctx.Invalidate();
        _q = QuadroAreas.Build(_ctx);
        var rules = _ctx.Rules;
        ProfileInfo.Text = rules is null ? "" :
            $"{rules.Description}  Área mínima: {Fmt.N(rules.AreaMinimaLote, 2)} m²; testada mínima: {Fmt.N(rules.TestadaMinima, 2)} m; TO: {Fmt.N(rules.TaxaOcupacaoMax, 1)}%; CA: {Fmt.N(rules.CoeficienteAproveitamentoMax, 2)}.";
        Cats.ItemsSource = _q.Categorias.Select(c => new { c.Categoria, c.Quantidade, AreaText = Fmt.N(c.Area, 2), PctText = Fmt.N(c.Percentual, 2) })
            .Concat(new[] { new { Categoria = "TOTAL", Quantidade = _q.Categorias.Sum(c => c.Quantidade), AreaText = Fmt.N(_q.AreaTotal, 2), PctText = "" } })
            .ToList();
        var sb = new StringBuilder();
        if (_q.AreaGleba is double g) sb.AppendLine($"Área da gleba: {Fmt.N(g, 2)} m²");
        sb.AppendLine($"Áreas públicas (viário + verde + institucional): {Fmt.N(_q.AreaPublica, 2)} m² ({Fmt.N(_q.PercentualPublico, 2)}%)");
        foreach (var v in _q.Verificacoes) sb.AppendLine("✔ " + v);
        foreach (var p in _q.Problemas) sb.AppendLine("✖ " + p);
        int bad = _q.Lotes.Count(l => l.Problemas.Count > 0);
        sb.AppendLine(bad == 0 ? "✔ Todos os lotes atendem ao perfil." : $"✖ {bad} lote(s) com pendências (ver tabela).");
        Checks.Text = sb.ToString();
        LotsGrid.ItemsSource = _q.Lotes.Select(l => new
        {
            l.Nome, l.Uso,
            Area = Fmt.N(l.Area, 2),
            Testada = l.Testada > 0 ? Fmt.N(l.Testada, 2) : "–",
            Liquida = Fmt.N(l.AreaLiquida, 2),
            Edificavel = l.AreaEdificavel is double e ? Fmt.N(e, 2) : "–",
            Projecao = l.ProjecaoMaxima is double p ? Fmt.N(p, 2) : "–",
            Construcao = l.AreaConstruidaMaxima is double c ? Fmt.N(c, 2) : "–",
            Situacao = l.Problemas.Count == 0 ? "OK" : string.Join(" ", l.Problemas),
        }).ToList();
    }

    private void Profile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || Profile.SelectedItem is not string name) return;
        _ctx.Project.Info.RuleProfileName = name;
        ProfileChanged = true;
        Refresh();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_q is null) return;
        var sb = new StringBuilder("Especificação\tQuantidade\tÁrea (m²)\t%\n");
        foreach (var c in _q.Categorias) sb.Append($"{c.Categoria}\t{c.Quantidade}\t{Fmt.N(c.Area, 2)}\t{Fmt.N(c.Percentual, 2)}\n");
        sb.Append($"TOTAL\t{_q.Categorias.Sum(c => c.Quantidade)}\t{Fmt.N(_q.AreaTotal, 2)}\t\n\nLote\tUso\tÁrea (m²)\tTestada (m)\tSituação\n");
        foreach (var l in _q.Lotes) sb.Append($"{l.Nome}\t{l.Uso}\t{Fmt.N(l.Area, 2)}\t{Fmt.N(l.Testada, 2)}\t{(l.Problemas.Count == 0 ? "OK" : string.Join(" ", l.Problemas))}\n");
        try { Clipboard.SetText(sb.ToString()); Fmt.Msg("Quadro copiado. Cole no Excel ou no Word."); } catch { }
    }

    private void Excel_Click(object sender, RoutedEventArgs e)
    {
        ExportRequested = true;
        DialogResult = true;
    }
}
