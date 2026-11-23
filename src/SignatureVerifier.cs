using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace ChipsetUpdater
{
    /// <summary>Checks that a downloaded file has a valid Authenticode signature from AMD.</summary>
    static class SignatureVerifier
    {
        public static bool IsSignedByAmd(string path, out string signer)
        {
            signer = null;
            if (!HasValidSignature(path)) return false;
            try
            {
                X509Certificate cert = X509Certificate.CreateFromSignedFile(path);
                signer = cert.Subject;
            }
            catch (Exception)
            {
                return false;
            }
            return Regex.IsMatch(signer, @"(^|,\s*)(CN|O)=""?Advanced Micro Devices", RegexOptions.IgnoreCase);
        }

        static readonly Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WinTrustFileInfo
        {
            public int cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WinTrustData
        {
            public int cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public int dwUIChoice;
            public int fdwRevocationChecks;
            public int dwUnionChoice;
            public IntPtr pFile;
            public int dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public int dwProvFlags;
            public int dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref WinTrustData data);

        static bool HasValidSignature(string path)
        {
            var file = new WinTrustFileInfo
            {
                cbStruct = Marshal.SizeOf(typeof(WinTrustFileInfo)),
                pcwszFilePath = path,
            };
            IntPtr pFile = Marshal.AllocHGlobal(file.cbStruct);
            try
            {
                Marshal.StructureToPtr(file, pFile, false);
                var data = new WinTrustData
                {
                    cbStruct = Marshal.SizeOf(typeof(WinTrustData)),
                    dwUIChoice = 2,          // WTD_UI_NONE
                    fdwRevocationChecks = 0, // WTD_REVOKE_NONE
                    dwUnionChoice = 1,       // WTD_CHOICE_FILE
                    pFile = pFile,
                    dwStateAction = 0,       // WTD_STATEACTION_IGNORE
                };
                return WinVerifyTrust(new IntPtr(-1), GenericVerifyV2, ref data) == 0;
            }
            finally
            {
                Marshal.DestroyStructure(pFile, typeof(WinTrustFileInfo));
                Marshal.FreeHGlobal(pFile);
            }
        }
    }
}
