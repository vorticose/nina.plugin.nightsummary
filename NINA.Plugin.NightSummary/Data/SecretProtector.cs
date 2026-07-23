using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using NINA.Core.Utility;

namespace NINA.Plugin.NightSummary.Data {

    /// <summary>
    /// Encrypts secret settings values (SMTP password, webhook URL, API tokens) at
    /// rest using the Windows Data Protection API (DPAPI, CurrentUser scope). The
    /// in-memory settings object keeps plaintext; only the on-disk settings.json is
    /// protected.
    ///
    /// Dependency-free on purpose: NINA's plugin SDK does not copy package DLLs into
    /// the plugin folder (see the .csproj note), so referencing
    /// System.Security.Cryptography.ProtectedData would fail to resolve at runtime.
    /// We P/Invoke crypt32.dll directly, which is what that package does internally
    /// and is a Windows system library always present on the host.
    ///
    /// Protected values carry a version marker so legacy plaintext values are
    /// recognised and transparently re-encrypted on the next save. DPAPI blobs are
    /// bound to the current Windows user + machine, so a settings.json copied to a
    /// different account or PC will not decrypt — by design. The caller treats an
    /// undecryptable value as "unset" and preserves the original blob rather than
    /// destroying it.
    /// </summary>
    internal static class SecretProtector {

        private const string Marker = "dpapi:v1:";

        // Secondary entropy mixed into every blob. This is NOT the security boundary
        // (the Windows user key is) — it just binds blobs to this plugin so a blob
        // from another DPAPI-using app on the same account can't be swapped in.
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NINA.NightSummary.Settings.v1");

        private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

        /// <summary>True if the stored value is one this class produced (has the marker).</summary>
        public static bool IsProtected(string stored) =>
            !string.IsNullOrEmpty(stored) && stored.StartsWith(Marker, StringComparison.Ordinal);

        /// <summary>
        /// Encrypts plaintext to a marked, Base64-encoded DPAPI blob. Empty/null in
        /// returns "" (nothing to protect). On failure, falls back to returning the
        /// plaintext unchanged so a value is never lost to an encryption error.
        /// </summary>
        public static string Protect(string plaintext) {
            if (string.IsNullOrEmpty(plaintext)) return "";
            try {
                var cipher = ProtectBytes(Encoding.UTF8.GetBytes(plaintext));
                return Marker + Convert.ToBase64String(cipher);
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: Could not encrypt a settings secret ({ex.Message}); storing unprotected");
                return plaintext;
            }
        }

        /// <summary>
        /// Decrypts a stored value. Legacy plaintext (no marker) is returned as-is so
        /// existing settings.json files keep working. Returns null when a marked value
        /// cannot be decrypted (wrong user/machine or corruption) so the caller can
        /// distinguish "unusable, preserve the blob" from "genuinely empty".
        /// </summary>
        public static string Unprotect(string stored) {
            if (string.IsNullOrEmpty(stored)) return "";
            if (!IsProtected(stored)) return stored; // legacy plaintext
            try {
                var cipher = Convert.FromBase64String(stored.Substring(Marker.Length));
                return Encoding.UTF8.GetString(UnprotectBytes(cipher));
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: Could not decrypt a settings secret ({ex.Message}); treating as unset");
                return null;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DATA_BLOB {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string szDataDescr,
            ref DATA_BLOB pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr,
            ref DATA_BLOB pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        private static byte[] ProtectBytes(byte[] data)   => Crypt(data, encrypt: true);
        private static byte[] UnprotectBytes(byte[] data) => Crypt(data, encrypt: false);

        private static byte[] Crypt(byte[] data, bool encrypt) {
            var inBlob  = default(DATA_BLOB);
            var entBlob = default(DATA_BLOB);
            var outBlob = default(DATA_BLOB);
            var hData = GCHandle.Alloc(data, GCHandleType.Pinned);
            var hEnt  = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
            try {
                inBlob.cbData  = data.Length;    inBlob.pbData  = hData.AddrOfPinnedObject();
                entBlob.cbData = Entropy.Length; entBlob.pbData = hEnt.AddrOfPinnedObject();
                bool ok = encrypt
                    ? CryptProtectData(ref inBlob, null, ref entBlob, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob)
                    : CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entBlob, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob);
                if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
                var result = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
                return result;
            } finally {
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
                hData.Free();
                hEnt.Free();
            }
        }
    }
}
