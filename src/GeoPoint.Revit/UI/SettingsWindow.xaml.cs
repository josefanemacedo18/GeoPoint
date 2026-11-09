using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Office;
using GeoPoint.Core.Settings;
using GeoPoint.Core.Urban;
using GeoPoint.Revit.Infrastructure;

namespace GeoPoint.Revit.UI;

public partial class SettingsWindow : Window
{
    private readonly GeoPointSettings _s;
    private readonly ObservableCollection<RuleProfile> _profiles;
    private RuleProfile? _editing;
    private bool _loadingProfile;

    public SettingsWindow(GeoPointSettings current)
    {
        InitializeComponent();
        _s = current.Clone();
        var r = _s.Recognition;
        Tol.Text = Fmt.Raw(r.ToleranceMm, 3);
        Gap.Text = Fmt.Raw(r.GapSearchMm, 1);
        Tiny.Text = Fmt.Raw(r.TinyFaceM2, 4);
        Merge.IsChecked = r.MergeCollinear;
        Holes.IsChecked = r.IncludeHoles;

        var n = _s.Naming;
        var f = _s.Format;
        Prefix.Text = n.VertexPrefix;
        Digits.Text = n.VertexDigits.ToString();
        StartRule.SelectedIndex = (int)n.StartRule;
        Direction.SelectedIndex = n.Clockwise ? 0 : 1;
        DecDist.Text = f.DistanceDecimals.ToString();
        DecArea.Text = f.AreaDecimals.ToString();
        DecPlane.Text = f.PlaneCoordDecimals.ToString();
        DecGeo.Text = f.GeoSecondsDecimals.ToString();
        DecAz.Text = f.AzimuthSecondsDecimals.ToString();
        DecHa.Text = f.HectareDecimals.ToString();
        ZoneStyle.SelectedIndex = (int)f.ZoneStyle;
        West.SelectedIndex = f.WestAsO ? 0 : 1;
        Thousands.IsChecked = f.ThousandsSeparator;
        DecimalDeg.IsChecked = f.GeoDecimalDegrees;

        var m = _s.Memorial;
        TemplatePath.Text = m.TemplatePath;
        PhrasesPath.Text = m.PhrasesPath;
        OutputFolder.Text = m.OutputFolder;
        UpdateTemplateFields();

        var t = _s.Technician;
        TechName.Text = t.Nome;
        TechProf.Text = t.Profissao;
        TechCouncil.Text = t.Conselho;
        TechReg.Text = t.Registro;
        TechCompany.Text = t.Empresa;

        var tm = m.CustomTm;
        TmName.Text = tm.CustomName;
        TmCm.Text = Fmt.Raw(tm.CustomCentralMeridian, 6);
        TmK0.Text = tm.CustomK0.ToString("0.0000000", Fmt.Pt);
        TmFe.Text = Fmt.Raw(tm.CustomFalseEasting, 3);
        TmFn.Text = Fmt.Raw(tm.CustomFalseNorthing, 3);
        TmLat0.Text = Fmt.Raw(tm.CustomLatOrigin, 6);
        var p = m.Ptl;
        PtlAnchor.IsChecked = p.PtlUseAnchor;
        PtlLatLon.Text = p.PtlLat == 0 && p.PtlLon == 0 ? "" : $"{p.PtlLat.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture)}, {p.PtlLon.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture)}";
        PtlH.Text = Fmt.Raw(p.PtlHeight, 3);
        PtlX0.Text = Fmt.Raw(p.PtlFalseEasting, 3);
        PtlY0.Text = Fmt.Raw(p.PtlFalseNorthing, 3);
        PtlGrid.IsEnabled = !p.PtlUseAnchor;

        var a = _s.Annotation;
        TextMm.Text = Fmt.Raw(a.TextSizeMm, 2);
        OffsetMm.Text = Fmt.Raw(a.OffsetMm, 2);
        Font.Text = a.Font;
        KmlOpacity.Text = Fmt.Raw(_s.Kml.FillOpacity, 2);
        KmlWidth.Text = Fmt.Raw(_s.Kml.LineWidth, 1);

        _profiles = new ObservableCollection<RuleProfile>(_s.RuleProfiles.Select(x => x.Clone()));
        Profiles.ItemsSource = _profiles;
        Profiles.SelectedIndex = _profiles.Count > 0 ? 0 : -1;
    }

    public GeoPointSettings Result => _s;

    private void UpdateTemplateFields()
    {
        try
        {
            byte[] bytes = !string.IsNullOrWhiteSpace(TemplatePath.Text) && File.Exists(TemplatePath.Text)
                ? File.ReadAllBytes(TemplatePath.Text)
                : MemorialGenerator.DefaultTemplate();
            var list = DocxTemplate.ListPlaceholders(bytes);
            TemplateFields.Text = (string.IsNullOrWhiteSpace(TemplatePath.Text) ? "Modelo padrão. " : "") + "Marcadores encontrados: " + string.Join(", ", list) +
                                  (list.Contains("CORPO") ? "" : "  (sem {{CORPO}}: a descrição será inserida no fim do documento)");
        }
        catch (Exception ex)
        {
            TemplateFields.Text = "Não foi possível ler o modelo: " + ex.Message;
        }
    }

    private void TemplateExport_Click(object sender, RoutedEventArgs e)
    {
        var path = Ui.SaveFile("Exportar modelo padrão", "Documento do Word (*.docx)|*.docx", "Modelo memorial GeoPoint.docx", SettingsStore.DefaultFolder);
        if (path is null) return;
        if (Ui.TryWrite(path, MemorialGenerator.DefaultTemplate()))
        {
            if (MessageBox.Show("Modelo exportado. Usar este arquivo como modelo do memorial (depois de editá-lo no Word)?", "GeoPoint", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                TemplatePath.Text = path;
                UpdateTemplateFields();
            }
            Ui.OpenFile(path);
        }
    }

    private void TemplatePick_Click(object sender, RoutedEventArgs e)
    {
        var path = Ui.OpenFile("Escolher modelo do memorial", "Documento do Word (*.docx)|*.docx");
        if (path is null) return;
        TemplatePath.Text = path;
        UpdateTemplateFields();
    }

    private void TemplateDefault_Click(object sender, RoutedEventArgs e)
    {
        TemplatePath.Text = "";
        UpdateTemplateFields();
    }

    private void PhrasesExport_Click(object sender, RoutedEventArgs e)
    {
        var path = Ui.SaveFile("Exportar frases padrão", "JSON (*.json)|*.json", "frases-memorial.json", SettingsStore.DefaultFolder);
        if (path is null) return;
        try
        {
            new MemorialPhrases().Save(path);
            PhrasesPath.Text = path;
            Ui.OpenFile(path);
        }
        catch (Exception ex) { Fmt.Msg("Não foi possível salvar: " + ex.Message, icon: MessageBoxImage.Warning); }
    }

    private void PhrasesPick_Click(object sender, RoutedEventArgs e)
    {
        var path = Ui.OpenFile("Escolher frases do memorial", "JSON (*.json)|*.json");
        if (path is not null) PhrasesPath.Text = path;
    }

    private void PhrasesDefault_Click(object sender, RoutedEventArgs e) => PhrasesPath.Text = "";

    private void OutputPick_Click(object sender, RoutedEventArgs e)
    {
        var d = Ui.PickFolder("Pasta padrão dos memoriais", OutputFolder.Text);
        if (d is not null) OutputFolder.Text = d;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(SettingsStore.DefaultFolder);
        Ui.OpenFile(SettingsStore.DefaultFolder);
    }

    private void Rtm_Click(object sender, RoutedEventArgs e)
    {
        TmName.Text = "RTM";
        TmK0.Text = "0,9999950";
        TmFe.Text = "400000";
        TmFn.Text = "5000000";
        TmLat0.Text = "0";
    }

    private void Ltm_Click(object sender, RoutedEventArgs e)
    {
        TmName.Text = "LTM";
        TmK0.Text = "0,9999950";
        TmFe.Text = "200000";
        TmFn.Text = "5000000";
        TmLat0.Text = "0";
    }

    private void PtlAnchor_Changed(object sender, RoutedEventArgs e)
    {
        if (PtlGrid is not null) PtlGrid.IsEnabled = PtlAnchor.IsChecked != true;
    }

    // ---------------------------------------------------------------- perfis

    private void Profiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingProfile) return;
        if (_editing is not null && !CommitProfile())
        {
            _loadingProfile = true;
            Profiles.SelectedItem = _editing;
            _loadingProfile = false;
            return;
        }
        _editing = Profiles.SelectedItem as RuleProfile;
        ProfileGrid.IsEnabled = _editing is not null;
        if (_editing is null) return;
        var p = _editing;
        PName.Text = p.Name;
        PDesc.Text = p.Description;
        PArea.Text = Fmt.Raw(p.AreaMinimaLote, 2);
        PTest.Text = Fmt.Raw(p.TestadaMinima, 2);
        PTo.Text = Fmt.Raw(p.TaxaOcupacaoMax, 2);
        PCa.Text = Fmt.Raw(p.CoeficienteAproveitamentoMax, 3);
        PPub.Text = Fmt.Raw(p.PercentualMinAreasPublicas, 2);
        PVerde.Text = Fmt.Raw(p.PercentualMinAreaVerde, 2);
        PInst.Text = Fmt.Raw(p.PercentualMinInstitucional, 2);
        PRf.Text = Fmt.Raw(p.RecuoFrontal, 2);
        PRl.Text = Fmt.Raw(p.RecuoLateral, 2);
        PRb.Text = Fmt.Raw(p.RecuoFundos, 2);
        PTol.Text = Fmt.Raw(p.ToleranciaAreaTitulo, 2);
        PFmp.Text = Fmt.Raw(p.FracaoMinimaRuralHa, 4);
    }

    private bool CommitProfile()
    {
        var p = _editing;
        if (p is null) return true;
        if (string.IsNullOrWhiteSpace(PName.Text)) { Fmt.Msg("O perfil precisa de um nome."); return false; }
        if (_profiles.Any(x => x != p && x.Name.Equals(PName.Text.Trim(), StringComparison.OrdinalIgnoreCase))) { Fmt.Msg("Já existe um perfil com este nome."); return false; }
        double v;
        bool Read(TextBox b, string label, out double val) => Fmt.ReadNum(b, label, out val, 0);
        if (!Read(PArea, "Área mínima", out v)) return false; p.AreaMinimaLote = v;
        if (!Read(PTest, "Testada mínima", out v)) return false; p.TestadaMinima = v;
        if (!Read(PTo, "Taxa de ocupação", out v)) return false; p.TaxaOcupacaoMax = v;
        if (!Read(PCa, "Coeficiente de aproveitamento", out v)) return false; p.CoeficienteAproveitamentoMax = v;
        if (!Read(PPub, "Áreas públicas", out v)) return false; p.PercentualMinAreasPublicas = v;
        if (!Read(PVerde, "Área verde", out v)) return false; p.PercentualMinAreaVerde = v;
        if (!Read(PInst, "Área institucional", out v)) return false; p.PercentualMinInstitucional = v;
        if (!Read(PRf, "Recuo frontal", out v)) return false; p.RecuoFrontal = v;
        if (!Read(PRl, "Recuo lateral", out v)) return false; p.RecuoLateral = v;
        if (!Read(PRb, "Recuo de fundos", out v)) return false; p.RecuoFundos = v;
        if (!Read(PTol, "Tolerância do título", out v)) return false; p.ToleranciaAreaTitulo = v;
        if (!Read(PFmp, "Fração mínima rural", out v)) return false; p.FracaoMinimaRuralHa = v;
        p.Name = PName.Text.Trim();
        p.Description = PDesc.Text.Trim();
        Profiles.Items.Refresh();
        return true;
    }

    private void ProfileNew_Click(object sender, RoutedEventArgs e)
    {
        if (!CommitProfile()) return;
        int k = 1;
        string name;
        do name = $"Novo perfil {k++}"; while (_profiles.Any(p => p.Name == name));
        var np = new RuleProfile { Name = name, ToleranciaAreaTitulo = 5 };
        _profiles.Add(np);
        Profiles.SelectedItem = np;
    }

    private void ProfileDup_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is null || !CommitProfile()) return;
        var c = _editing.Clone();
        int k = 2;
        string name;
        do name = $"{_editing.Name} ({k++})"; while (_profiles.Any(p => p.Name == name));
        c.Name = name;
        _profiles.Add(c);
        Profiles.SelectedItem = c;
    }

    private void ProfileDel_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is null) return;
        if (_profiles.Count <= 1) { Fmt.Msg("Mantenha pelo menos um perfil."); return; }
        var del = _editing;
        _editing = null;
        _profiles.Remove(del);
        Profiles.SelectedIndex = 0;
    }

    // ---------------------------------------------------------------- salvar

    private static bool ReadInt(TextBox b, string label, int min, int max, out int v)
    {
        if (!int.TryParse(b.Text.Trim(), out v) || v < min || v > max)
        {
            Fmt.Msg($"\"{label}\": informe um número inteiro entre {min} e {max}.", icon: MessageBoxImage.Warning);
            b.Focus();
            return false;
        }
        return true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!CommitProfile()) return;
        var r = _s.Recognition;
        if (!Fmt.ReadNum(Tol, "Tolerância", out double tol, 0.01, 100)) return;
        if (!Fmt.ReadNum(Gap, "Distância de falha", out double gap, 0, 10000)) return;
        if (!Fmt.ReadNum(Tiny, "Área mínima de alerta", out double tiny, 0)) return;
        r.ToleranceMm = tol; r.GapSearchMm = gap; r.TinyFaceM2 = tiny;
        r.MergeCollinear = Merge.IsChecked == true;
        r.IncludeHoles = Holes.IsChecked == true;

        var n = _s.Naming;
        var f = _s.Format;
        if (!ReadInt(Digits, "Dígitos", 1, 5, out int digits)) return;
        if (!ReadInt(DecDist, "Decimais de distância", 0, 6, out int dd)) return;
        if (!ReadInt(DecArea, "Decimais de área", 0, 6, out int da)) return;
        if (!ReadInt(DecPlane, "Decimais de coordenadas", 0, 6, out int dp)) return;
        if (!ReadInt(DecGeo, "Decimais dos segundos (lat/long)", 0, 6, out int dg)) return;
        if (!ReadInt(DecAz, "Decimais dos segundos (azimute)", 0, 4, out int dz)) return;
        if (!ReadInt(DecHa, "Decimais em hectares", 0, 8, out int dh)) return;
        n.VertexPrefix = Prefix.Text;
        n.VertexDigits = digits;
        n.StartRule = (StartVertexRule)Math.Max(0, StartRule.SelectedIndex);
        n.Clockwise = Direction.SelectedIndex != 1;
        f.DistanceDecimals = dd; f.AreaDecimals = da; f.PlaneCoordDecimals = dp; f.GeoSecondsDecimals = dg; f.AzimuthSecondsDecimals = dz; f.HectareDecimals = dh;
        f.ZoneStyle = (ZoneLabelStyle)Math.Max(0, ZoneStyle.SelectedIndex);
        f.WestAsO = West.SelectedIndex != 1;
        f.ThousandsSeparator = Thousands.IsChecked == true;
        f.GeoDecimalDegrees = DecimalDeg.IsChecked == true;

        var m = _s.Memorial;
        m.TemplatePath = TemplatePath.Text.Trim();
        m.PhrasesPath = PhrasesPath.Text.Trim();
        m.OutputFolder = OutputFolder.Text.Trim();

        var t = _s.Technician;
        t.Nome = TechName.Text.Trim();
        t.Profissao = TechProf.Text.Trim();
        t.Conselho = TechCouncil.Text.Trim();
        t.Registro = TechReg.Text.Trim();
        t.Empresa = TechCompany.Text.Trim();

        if (!Fmt.ReadNum(TmCm, "Meridiano central", out double cm, -180, 180)) return;
        if (!Fmt.ReadNum(TmK0, "Fator de escala k0", out double k0, 0.9, 1.1)) return;
        if (!Fmt.ReadNum(TmFe, "Falso este", out double fe)) return;
        if (!Fmt.ReadNum(TmFn, "Falso norte", out double fn)) return;
        if (!Fmt.ReadNum(TmLat0, "Latitude de origem", out double lat0, -90, 90)) return;
        var tm = m.CustomTm;
        tm.Kind = CrsKind.CustomTm;
        tm.CustomName = string.IsNullOrWhiteSpace(TmName.Text) ? "TM" : TmName.Text.Trim();
        tm.CustomCentralMeridian = cm; tm.CustomK0 = k0; tm.CustomFalseEasting = fe; tm.CustomFalseNorthing = fn; tm.CustomLatOrigin = lat0;
        // Atualiza a TM personalizada já escolhida para o memorial.
        foreach (var d in m.CoordinateSystems.Where(d => d.Kind == CrsKind.CustomTm))
        {
            d.CustomName = tm.CustomName; d.CustomCentralMeridian = cm; d.CustomK0 = k0; d.CustomFalseEasting = fe; d.CustomFalseNorthing = fn; d.CustomLatOrigin = lat0;
        }

        var p = m.Ptl;
        p.Kind = CrsKind.LocalTopographic;
        p.PtlUseAnchor = PtlAnchor.IsChecked == true;
        if (!p.PtlUseAnchor)
        {
            if (!AngleText.TryParseLatLon(PtlLatLon.Text, out double plat, out double plon)) { Fmt.Msg("Origem do Plano Topográfico Local: informe latitude e longitude.", icon: MessageBoxImage.Warning); return; }
            if (!Fmt.ReadNum(PtlH, "Altitude do plano", out double ph, -500, 9000)) return;
            p.PtlLat = plat; p.PtlLon = plon; p.PtlHeight = ph;
        }
        if (!Fmt.ReadNum(PtlX0, "X da origem", out double x0)) return;
        if (!Fmt.ReadNum(PtlY0, "Y da origem", out double y0)) return;
        p.PtlFalseEasting = x0; p.PtlFalseNorthing = y0;
        foreach (var d in m.CoordinateSystems.Where(d => d.Kind == CrsKind.LocalTopographic))
        {
            d.PtlUseAnchor = p.PtlUseAnchor; d.PtlLat = p.PtlLat; d.PtlLon = p.PtlLon; d.PtlHeight = p.PtlHeight; d.PtlFalseEasting = x0; d.PtlFalseNorthing = y0;
        }

        var a = _s.Annotation;
        if (!Fmt.ReadNum(TextMm, "Altura do texto", out double tmm, 0.5, 20)) return;
        if (!Fmt.ReadNum(OffsetMm, "Afastamento", out double omm, 0, 50)) return;
        if (!Fmt.ReadNum(KmlOpacity, "Opacidade", out double op, 0, 1)) return;
        if (!Fmt.ReadNum(KmlWidth, "Espessura", out double lw, 0.5, 20)) return;
        a.TextSizeMm = tmm; a.OffsetMm = omm; a.Font = string.IsNullOrWhiteSpace(Font.Text) ? "Arial" : Font.Text.Trim();
        _s.Kml.FillOpacity = op; _s.Kml.LineWidth = lw;

        _s.RuleProfiles = _profiles.ToList();
        if (_s.FindProfile(_s.DefaultRuleProfile)?.Name != _s.DefaultRuleProfile) _s.DefaultRuleProfile = _s.RuleProfiles[0].Name;
        DialogResult = true;
    }
}
