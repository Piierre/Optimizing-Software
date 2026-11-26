using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OptimizingSoftware.Windows;

/// <summary>Éditeur et signature numérique d'un programme (résultats gardés en mémoire pour aller vite).</summary>
[SupportedOSPlatform("windows")]
public static partial class FileTrust
{
    private static readonly ConcurrentDictionary<string, (string? Publisher, bool? Signed)> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static (string? Publisher, bool? Signed) Check(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return (null, null);
        return Cache.GetOrAdd(path, p =>
        {
            string? publisher = null;
            try
            {
                publisher = FileVersionInfo.GetVersionInfo(p).CompanyName?.Trim();
                if (string.IsNullOrEmpty(publisher)) publisher = null;
            }
            catch (Exception)
            {
                // Fichier illisible.
            }
            // Les fichiers de Windows sont signés via des catalogues, pas dans le fichier : on ne conclut pas.
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            bool? signed = p.StartsWith(windows + "\\", StringComparison.OrdinalIgnoreCase) ? null : IsSigned(p);
            return (publisher, signed);
        });
    }

    /// <summary>Vérifie la signature Authenticode intégrée au fichier (sans aller sur Internet).</summary>
    public static unsafe bool? IsSigned(string path)
    {
        try
        {
            fixed (char* file = path)
            {
                var fileInfo = new WintrustFileInfo { cbStruct = (uint)sizeof(WintrustFileInfo), pcwszFilePath = (IntPtr)file };
                var data = new WintrustData
                {
                    cbStruct = (uint)sizeof(WintrustData),
                    dwUIChoice = 2,              // WTD_UI_NONE
                    fdwRevocationChecks = 0,     // WTD_REVOKE_NONE
                    dwUnionChoice = 1,           // WTD_CHOICE_FILE
                    pFile = (IntPtr)(&fileInfo),
                    dwProvFlags = 0x1000,        // WTD_CACHE_ONLY_URL_RETRIEVAL
                };
                var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
                var result = WinVerifyTrust(IntPtr.Zero, &action, &data);
                return result == 0;
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustFileInfo
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [LibraryImport("wintrust.dll")]
    private static unsafe partial int WinVerifyTrust(IntPtr hwnd, Guid* action, WintrustData* data);
}
