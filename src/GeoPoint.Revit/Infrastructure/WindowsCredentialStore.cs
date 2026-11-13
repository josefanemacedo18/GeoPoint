using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using GeoPoint.Core.Settings;

namespace GeoPoint.Revit.Infrastructure;

/// <summary>
/// Cofre de segredos no Gerenciador de Credenciais do Windows (credencial genérica do usuário).
/// A chave fica protegida pela conta do Windows e não aparece no arquivo de configurações.
/// </summary>
internal sealed class WindowsCredentialStore : ISecretStore
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    public string? Read(string name)
    {
        if (!CredRead(name, CredTypeGeneric, 0, out var p)) return null;
        try
        {
            var c = Marshal.PtrToStructure<Credential>(p);
            if (c.CredentialBlobSize == 0 || c.CredentialBlob == IntPtr.Zero) return "";
            return Marshal.PtrToStringUni(c.CredentialBlob, (int)c.CredentialBlobSize / 2);
        }
        finally { CredFree(p); }
    }

    public void Write(string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            if (!CredDelete(name, CredTypeGeneric, 0) && Marshal.GetLastWin32Error() != ErrorNotFound)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return;
        }
        if (Read(name) == value) return;
        var bytes = Encoding.Unicode.GetBytes(value);
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var c = new Credential
            {
                Type = CredTypeGeneric, TargetName = name, Comment = "GeoPoint (Revit) – chave da Google Maps Static API",
                CredentialBlobSize = (uint)bytes.Length, CredentialBlob = blob, Persist = CredPersistLocalMachine, UserName = Environment.UserName,
            };
            if (!CredWrite(ref c, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            // Apaga a cópia da chave na memória não gerenciada.
            for (int i = 0; i < bytes.Length; i++) Marshal.WriteByte(blob, i, 0);
            Marshal.FreeHGlobal(blob);
        }
    }
}
