using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;

namespace GeoPoint.Revit.Infrastructure;

/// <summary>
/// Confere se o norte usado pelo GeoPoint coincide com o "para cima" da vista em que o usuário desenhou.
/// Quando o norte verdadeiro gravado no Revit está girado em relação à tela, pergunta uma vez qual norte usar.
/// </summary>
public static class NorthGuard
{
    /// <summary>Rotação que leva o "para cima" da vista ao norte (+Y do plano leste/norte).</summary>
    public static double ViewRotation(View v)
    {
        var up = v.UpDirection;
        return AngleUtil.NormPi(Math.PI / 2 - Math.Atan2(up.Y, up.X));
    }

    private static bool UsableView(View v) =>
        v is not ViewSheet && ElementUtil.IsPlanLike(v) && Math.Abs(v.UpDirection.Z) < 0.5;

    /// <summary>Azimute (pelo norte do Revit) para onde aponta o topo da vista.</summary>
    public static double TopAzimuthByRevit(GpSession s, View v)
    {
        var up = v.UpDirection;
        return CoordinateService.AzimuthOfEnuVector(new Vec2(up.X, up.Y).Rotate(s.Project.Frame.AngleRad));
    }

    public static string DirectionName(double az)
    {
        string[] n = { "Norte", "Nordeste", "Leste", "Sudeste", "Sul", "Sudoeste", "Oeste", "Noroeste" };
        return n[(int)Math.Round(((az % 360) + 360) % 360 / 45.0) % 8];
    }

    /// <summary>Aviso para a janela de referência quando o norte do Revit não aponta para o topo da tela.</summary>
    public static string MismatchText(GpSession s)
    {
        var v = s.ActiveView;
        if (!UsableView(v)) return "";
        double az = TopAzimuthByRevit(s, v);
        if (Math.Min(az, 360 - az) < 0.5) return "";
        return $"Atenção: o norte verdadeiro gravado no Revit NÃO aponta para o topo da tela. Nesta vista o topo aponta para {AngleText.FormatDms(az, 0, AngleKind.Azimuth)} ({DirectionName(az)}). " +
               "Se você desenhou com o norte para cima, mantenha a primeira opção; senão o desenho aparece girado no Google Earth e nos azimutes.";
    }

    /// <summary>
    /// Antes de gerar coordenadas/azimutes: se a referência usa o norte do Revit e ele não aponta para o topo da tela,
    /// pergunta (uma vez) qual norte usar. Devolve false se o usuário cancelar.
    /// </summary>
    public static bool Ensure(GpSession s)
    {
        var r = s.Project.Reference;
        if (!r.IsSet || r.NorthConfirmed || r.Mode != OrientationMode.ProjectTrueNorth) return true;
        var v = s.ActiveView;
        if (!UsableView(v)) return true;
        double az = TopAzimuthByRevit(s, v);
        if (Math.Min(az, 360 - az) < 0.5)
        {
            r.NorthConfirmed = true;
            TrySave(s);
            return true;
        }
        var td = new TaskDialog("GeoPoint – Qual é o norte do seu desenho?")
        {
            MainInstruction = "O norte verdadeiro gravado no Revit não aponta para o topo da tela",
            MainContent = $"Na vista \"{v.Name}\" o topo da tela aponta para {AngleText.FormatDms(az, 0, AngleKind.Azimuth)} ({DirectionName(az)}) " +
                          "pelo norte verdadeiro gravado no projeto (Gerenciar › Localização do projeto).\n\n" +
                          "Se você desenhou com o norte para cima na tela, use a primeira opção: os azimutes, as coordenadas, o Google Earth e as pranchas ficam como você vê na tela.",
            AllowCancellation = true,
            CommonButtons = TaskDialogCommonButtons.Cancel,
        };
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "O topo da tela é o norte (foi assim que desenhei)", "Recomendado. A rotação é gravada no GeoPoint; o arquivo do Revit não é alterado.");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Usar o norte verdadeiro gravado no Revit", $"O desenho é girado {AngleText.FormatDms(Math.Min(az, 360 - az), 0, AngleKind.Plain)} em relação à tela.");
        var res = td.Show();
        if (res == TaskDialogResult.CommandLink1)
        {
            r.Mode = OrientationMode.ViewUp;
            r.RotationRad = ViewRotation(v);
            r.NorthConfirmed = true;
            Logger.Info($"Norte: topo da vista \"{v.Name}\" adotado como norte (diferença de {az:0.####}° para o norte do Revit).");
            TrySave(s);
            return true;
        }
        if (res == TaskDialogResult.CommandLink2)
        {
            r.NorthConfirmed = true;
            TrySave(s);
            return true;
        }
        return false;
    }

    private static void TrySave(GpSession s)
    {
        try { s.SaveProject("GeoPoint: orientação do norte"); }
        catch (Exception ex) { Logger.Error("Salvar orientação do norte", ex); }
    }

    /// <summary>Orientação da vista copiada para a prancha que deixa o norte do GeoPoint mais próximo do topo (0 = projeto, 1 = verdadeiro do Revit).</summary>
    public static int BestPlanNorth(ProjectContext ctx, out double residualDeg)
    {
        var n = ctx.Coords.DirectionFromAzimuth(0);                    // norte do GeoPoint no desenho
        var revitTrue = new Vec2(0, 1).Rotate(-ctx.Project.Frame.AngleRad); // norte do Revit no desenho
        double dProject = Math.Abs(AngleUtil.NormPi(Math.Atan2(n.Y, n.X) - Math.PI / 2));
        double dTrue = Math.Abs(AngleUtil.NormPi(Math.Atan2(n.Y, n.X) - Math.Atan2(revitTrue.Y, revitTrue.X)));
        residualDeg = AngleUtil.ToDeg(Math.Min(dProject, dTrue));
        return dTrue + 1e-9 < dProject ? 1 : 0;
    }
}
