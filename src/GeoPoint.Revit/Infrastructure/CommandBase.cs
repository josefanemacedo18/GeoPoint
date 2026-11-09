using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Model;
using GeoPoint.Core.Settings;

namespace GeoPoint.Revit.Infrastructure;

/// <summary>Contexto de execução de um comando (documento, dados do GeoPoint e configurações).</summary>
public sealed class GpSession
{
    public GpSession(UIApplication uiapp)
    {
        UiApp = uiapp;
        UiDoc = uiapp.ActiveUIDocument ?? throw new InvalidOperationException("Abra um projeto do Revit antes de usar o GeoPoint.");
        Doc = UiDoc.Document;
        if (Doc.IsFamilyDocument) throw new InvalidOperationException("O GeoPoint funciona em projetos, não em famílias.");
        Settings = SettingsStore.Load();
        Project = ProjectStore.Load(Doc);
        // Abrir a vista de uma fase torna essa fase a ativa.
        try
        {
            var uid = UiDoc.ActiveView?.UniqueId;
            var f = uid is null ? null : Project.Fases.FirstOrDefault(x => x.ViewIds.Contains(uid));
            if (f is not null && f.Id != Project.Fase.Id) Project.ActiveFaseId = f.Id;
        }
        catch { }
    }

    public UIApplication UiApp { get; }
    public UIDocument UiDoc { get; }
    public Document Doc { get; }
    public GeoPointSettings Settings { get; private set; }
    public ParcelProject Project { get; }
    public View ActiveView => UiDoc.ActiveView;
    public IntPtr MainWindow => UiApp.MainWindowHandle;

    public ProjectContext NewContext() => new(Project, Settings);

    public void SaveSettings() => SettingsStore.Save(Settings);

    public void ReloadSettings() => Settings = SettingsStore.Load();

    /// <summary>Grava os dados do GeoPoint numa transação própria.</summary>
    public void SaveProject(string transactionName = "GeoPoint: atualizar dados")
    {
        using var t = new Transaction(Doc, transactionName);
        t.Start();
        ProjectStore.Save(Doc, Project);
        t.Commit();
    }
}

/// <summary>Base dos comandos: tratamento uniforme de erros, cancelamento e registro.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public abstract class CommandBase : IExternalCommand
{
    protected abstract string Title { get; }

    protected abstract Result Run(GpSession s);

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var s = new GpSession(commandData.Application);
            Logger.Info($"Comando: {GetType().Name}");
            return Run(s);
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }
        catch (OperationCanceledException)
        {
            return Result.Cancelled;
        }
        catch (Exception ex)
        {
            Logger.Error($"Erro em {GetType().Name}", ex);
            Ui.Error(Title, ex.Message, ex);
            return Result.Failed;
        }
    }
}

/// <summary>Utilitários de interface.</summary>
public static class Ui
{
    public static bool? ShowDialog(Window w, IntPtr owner)
    {
        try
        {
            if (owner != IntPtr.Zero) new WindowInteropHelper(w).Owner = owner;
        }
        catch { }
        w.WindowStartupLocation = owner != IntPtr.Zero ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        w.ShowInTaskbar = false;
        return w.ShowDialog();
    }

    public static void Info(string title, string text)
    {
        var td = new TaskDialog("GeoPoint") { MainInstruction = title, MainContent = text, CommonButtons = TaskDialogCommonButtons.Ok };
        td.Show();
    }

    public static void Warn(string title, string text)
    {
        var td = new TaskDialog("GeoPoint") { MainInstruction = title, MainContent = text, MainIcon = TaskDialogIcon.TaskDialogIconWarning, CommonButtons = TaskDialogCommonButtons.Ok };
        td.Show();
    }

    public static void Error(string title, string text, Exception? ex = null)
    {
        var td = new TaskDialog("GeoPoint")
        {
            MainInstruction = title,
            MainContent = text,
            MainIcon = TaskDialogIcon.TaskDialogIconError,
            CommonButtons = TaskDialogCommonButtons.Ok,
        };
        if (ex is not null) td.ExpandedContent = ex.ToString() + Environment.NewLine + Environment.NewLine + "Registro: " + Logger.Folder;
        td.Show();
    }

    public static bool Confirm(string title, string text, string yes = "Sim", string no = "Não")
    {
        var td = new TaskDialog("GeoPoint") { MainInstruction = title, MainContent = text };
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, yes);
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, no);
        return td.Show() == TaskDialogResult.CommandLink1;
    }

    public static void OpenFile(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Logger.Error("Falha ao abrir " + path, ex); }
    }

    public static void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true }); }
        catch { }
    }

    public static string? SaveFile(string title, string filter, string defaultName, string? folder)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { Title = title, Filter = filter, FileName = SafeFileName(defaultName), AddExtension = true, OverwritePrompt = true };
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder)) dlg.InitialDirectory = folder;
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public static string? OpenFile(string title, string filter, string? folder = null)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder)) dlg.InitialDirectory = folder;
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public static string? PickFolder(string title, string? initial)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = title };
        if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial)) dlg.InitialDirectory = initial;
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    public static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
        return name.Trim();
    }

    /// <summary>Escreve arquivo tratando arquivo aberto em outro programa.</summary>
    public static bool TryWrite(string path, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return true;
        }
        catch (IOException)
        {
            Warn("Arquivo em uso", $"Não foi possível gravar:\n{path}\n\nFeche o arquivo (Word/Excel/Google Earth) e tente novamente.");
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            Warn("Sem permissão", $"Sem permissão para gravar em:\n{path}");
            return false;
        }
    }
}
