using System;
using System.Windows;

namespace GeoPoint.Revit.UI;

public enum LiveSplitRequest { None, Finish, Cancel, Undo, ModelLine, DetailLine }

/// <summary>Janela não modal com a prévia do desmembramento enquanto o usuário desenha com a ferramenta Linha.</summary>
public partial class LiveSplitWindow : Window
{
    public LiveSplitWindow() { InitializeComponent(); }

    /// <summary>Pedido do usuário (tratado pelo GeoPoint num contexto válido do Revit).</summary>
    public event Action<LiveSplitRequest>? Requested;

    /// <summary>true quando a janela foi fechada pelo próprio GeoPoint (não é cancelamento).</summary>
    public bool ClosingByCode { get; set; }

    private void Model_Click(object sender, RoutedEventArgs e) => Requested?.Invoke(LiveSplitRequest.ModelLine);
    private void Detail_Click(object sender, RoutedEventArgs e) => Requested?.Invoke(LiveSplitRequest.DetailLine);
    private void Undo_Click(object sender, RoutedEventArgs e) => Requested?.Invoke(LiveSplitRequest.Undo);
    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        FinishButton.IsEnabled = false;
        Summary.Text = "Concluindo… (se a ferramenta Linha ainda estiver ativa, pressione Esc no Revit)";
        Requested?.Invoke(LiveSplitRequest.Finish);
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Requested?.Invoke(LiveSplitRequest.Cancel);

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!ClosingByCode) Requested?.Invoke(LiveSplitRequest.Cancel);
        base.OnClosing(e);
    }
}
