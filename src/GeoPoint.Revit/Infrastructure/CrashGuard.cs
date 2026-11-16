using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace GeoPoint.Revit.Infrastructure;

/// <summary>
/// Proteção contra erros nas janelas do GeoPoint. Um erro dentro de um clique ou do desenho de uma janela, sem tratamento,
/// atravessa o Revit e pode fechá-lo ("Ocorreu um erro fatal no Revit"). Aqui ele fica no registro, aparece numa mensagem e a
/// janela continua aberta. Só os erros do GeoPoint (com o GeoPoint na pilha de chamadas) são tratados; os do Revit e de outros
/// plugins seguem o caminho normal.
/// </summary>
public static class CrashGuard
{
    private static Dispatcher? _dispatcher;
    private static DateTime _lastMessage = DateTime.MinValue;
    private static bool _showing;

    /// <summary>Liga a proteção (na abertura do Revit, na linha de execução da interface).</summary>
    public static void Install()
    {
        if (_dispatcher is not null) return;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _dispatcher.UnhandledException += OnDispatcherError;
        AppDomain.CurrentDomain.UnhandledException += OnFatal;
        TaskScheduler.UnobservedTaskException += OnTaskError;
    }

    public static void Uninstall()
    {
        if (_dispatcher is null) return;
        _dispatcher.UnhandledException -= OnDispatcherError;
        AppDomain.CurrentDomain.UnhandledException -= OnFatal;
        TaskScheduler.UnobservedTaskException -= OnTaskError;
        _dispatcher = null;
    }

    /// <summary>O erro veio do código do GeoPoint (alguma chamada do GeoPoint na pilha, inclusive das exceções internas).</summary>
    public static bool IsOurs(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if ((e.StackTrace ?? "").Contains("GeoPoint.", StringComparison.Ordinal)) return true;
        return false;
    }

    private static void OnDispatcherError(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Cancelamentos (Esc numa seleção do Revit) seguem o caminho normal: o comando termina sem mensagem de erro.
        if (!IsOurs(e.Exception) || e.Exception is OperationCanceledException or Autodesk.Revit.Exceptions.OperationCanceledException) return;
        e.Handled = true;
        Logger.Error("Erro numa janela do GeoPoint (tratado; o Revit continua aberto)", e.Exception);
        // Uma mensagem por vez, e não repetida em sequência (um erro no desenho se repetiria a cada atualização da janela).
        if (_showing || (DateTime.Now - _lastMessage).TotalSeconds < 4) return;
        _showing = true;
        try
        {
            MessageBox.Show(
                "Ocorreu um erro nesta janela do GeoPoint. O Revit continua aberto e a ação que deu erro não foi concluída.\n\n" +
                e.Exception.Message + "\n\nOs detalhes ficaram no registro (envie para o suporte):\n" + Logger.Folder,
                "GeoPoint – erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { }
        finally
        {
            _showing = false;
            _lastMessage = DateTime.Now;
        }
    }

    /// <summary>Erro que vai fechar o Revit: pelo menos fica no registro, para saber onde foi.</summary>
    private static void OnFatal(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex && IsOurs(ex)) Logger.Error($"FALHA FATAL (o Revit vai fechar: {e.IsTerminating})", ex);
    }

    private static void OnTaskError(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        if (!IsOurs(e.Exception)) return;
        Logger.Error("Erro em tarefa em segundo plano do GeoPoint", e.Exception);
        e.SetObserved();
    }
}
