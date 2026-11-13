using System.Linq;
using System.Windows;
using GeoPoint.Core.Model;
using GeoPoint.Core.Settings;

namespace GeoPoint.Revit.UI;

public partial class ProjectInfoWindow : Window
{
    private readonly ProjectInfo _info;

    public ProjectInfoWindow(ProjectInfo info, GeoPointSettings settings)
    {
        InitializeComponent();
        _info = info;
        Proprietario.Text = info.Proprietario;
        Documento.Text = info.Documento;
        Tipo.SelectedIndex = info.TipoImovel == "Rural" ? 1 : 0;
        Denominacao.Text = info.Denominacao;
        Endereco.Text = info.Endereco;
        Bairro.Text = info.Bairro;
        Municipio.Text = info.Municipio;
        UF.Text = info.UF;
        Cep.Text = info.Cep;
        Comarca.Text = info.Comarca;
        Cartorio.Text = info.Cartorio;
        Matricula.Text = info.Matricula;
        Inscricao.Text = info.InscricaoMunicipal;
        Ccir.Text = info.Ccir;
        Car.Text = info.Car;
        Finalidade.Text = info.Finalidade;
        RrtArt.Text = info.RrtArt;
        Situacao.ItemsSource = GeoPoint.Core.Model.ProjectInfo.Situacoes;
        Situacao.Text = info.Situacao;
        var names = settings.RuleProfiles.Select(p => p.Name).ToList();
        Profile.ItemsSource = names;
        var current = settings.FindProfile(info.RuleProfileName)?.Name;
        Profile.SelectedItem = current;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _info.Proprietario = Proprietario.Text.Trim();
        _info.Documento = Documento.Text.Trim();
        _info.TipoImovel = Tipo.SelectedIndex == 1 ? "Rural" : "Urbano";
        _info.Denominacao = Denominacao.Text.Trim();
        _info.Endereco = Endereco.Text.Trim();
        _info.Bairro = Bairro.Text.Trim();
        _info.Municipio = Municipio.Text.Trim();
        _info.UF = UF.Text.Trim().ToUpperInvariant();
        _info.Cep = Cep.Text.Trim();
        _info.Comarca = Comarca.Text.Trim();
        _info.Cartorio = Cartorio.Text.Trim();
        _info.Matricula = Matricula.Text.Trim();
        _info.InscricaoMunicipal = Inscricao.Text.Trim();
        _info.Ccir = Ccir.Text.Trim();
        _info.Car = Car.Text.Trim();
        _info.Finalidade = Finalidade.Text.Trim();
        _info.RrtArt = RrtArt.Text.Trim();
        _info.Situacao = Situacao.Text.Trim();
        _info.RuleProfileName = Profile.SelectedItem as string ?? "";
        DialogResult = true;
    }
}
