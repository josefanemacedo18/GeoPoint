using System;
using System.Runtime.InteropServices;

namespace GeoPoint.Revit.Infrastructure;

/// <summary>
/// Limpeza: a versão 1.5.3–1.5.5 guardava a chave da Google Maps Static API no Gerenciador de Credenciais do Windows.
/// Sem o croqui de satélite, a credencial é apagada (uma vez, silenciosamente).
/// </summary>
internal static class LegacyCredentials
{
    private const string GoogleKeyName = "GeoPoint/GoogleMapsStaticKey";
    private const uint CredTypeGeneric = 1;

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    public static void DeleteGoogleKey()
    {
        try { CredDelete(GoogleKeyName, CredTypeGeneric, 0); } catch { /* não existe ou sem permissão: nada a fazer */ }
    }
}
