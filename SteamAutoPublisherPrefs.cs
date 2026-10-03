using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;

/// <summary>
/// SteamAutoPublisher の設定をローカル（このPCのこのユーザー）にだけ保存する。
/// 通常の設定は EditorPrefs に、パスワードやAPIキーは Windows の DPAPI で暗号化してから保存する。
/// EditorPrefs は全プロジェクト共通のため、キーにプロジェクト固有のGUIDを含める。
/// リポジトリ内のファイルには何も書かないので、設定がコミットされることはない。
/// </summary>
internal static class SteamAutoPublisherPrefs
{
    private static string KeyPrefix => $"SteamAutoPublisher.{PlayerSettings.productGUID}.";

    public static string GetString(string key, string defaultValue)
    {
        return EditorPrefs.GetString(KeyPrefix + key, defaultValue);
    }

    public static void SetString(string key, string value)
    {
        EditorPrefs.SetString(KeyPrefix + key, value ?? "");
    }

    /// <summary>
    /// 暗号化して保存した値を読む。復号できない（別ユーザー・別PC・非Windows）場合は空文字。
    /// </summary>
    public static string GetSecret(string key)
    {
        string stored = EditorPrefs.GetString(KeyPrefix + key, "");
        if (string.IsNullOrEmpty(stored)) return "";

        try
        {
            byte[] decrypted = Dpapi.Unprotect(Convert.FromBase64String(stored));
            return decrypted == null ? "" : Encoding.UTF8.GetString(decrypted);
        }
        catch (FormatException)
        {
            return "";
        }
    }

    /// <summary>
    /// 値を暗号化して保存する。空文字なら保存済みの値を消す。暗号化できない環境では保存しない。
    /// </summary>
    public static void SetSecret(string key, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            EditorPrefs.DeleteKey(KeyPrefix + key);
            return;
        }

        byte[] encrypted = Dpapi.Protect(Encoding.UTF8.GetBytes(value));
        if (encrypted == null)
        {
            EditorPrefs.DeleteKey(KeyPrefix + key);
            return;
        }

        EditorPrefs.SetString(KeyPrefix + key, Convert.ToBase64String(encrypted));
    }

    public static bool CanStoreSecrets
    {
        get
        {
#if UNITY_EDITOR_WIN
            return true;
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Windows DPAPI（CurrentUser スコープ）。暗号化したWindowsユーザーでしか復号できない。
    /// System.Security.Cryptography.ProtectedData は Unity の API 互換レベルによっては使えないため、crypt32 を直接呼ぶ。
    /// </summary>
    private static class Dpapi
    {
#if UNITY_EDITOR_WIN
        private const int CryptProtectUiForbidden = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(
            ref DataBlob pDataIn, string szDataDescr, IntPtr pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(
            ref DataBlob pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        public static byte[] Protect(byte[] data) => Transform(data, protect: true);

        public static byte[] Unprotect(byte[] data) => Transform(data, protect: false);

        private static byte[] Transform(byte[] data, bool protect)
        {
            if (data == null || data.Length == 0) return null;

            var input = new DataBlob { cbData = data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
            var output = new DataBlob();
            try
            {
                Marshal.Copy(data, 0, input.pbData, data.Length);
                bool succeeded = protect
                    ? CryptProtectData(ref input, "SteamAutoPublisher", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output);
                if (!succeeded)
                {
                    UnityEngine.Debug.LogWarning($"[SteamAutoPublisher] 認証情報の{(protect ? "暗号化" : "復号")}に失敗しました（Win32エラー {Marshal.GetLastWin32Error()}）。");
                    return null;
                }

                var result = new byte[output.cbData];
                Marshal.Copy(output.pbData, result, 0, output.cbData);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(input.pbData);
                if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
            }
        }
#else
        public static byte[] Protect(byte[] data) => null;

        public static byte[] Unprotect(byte[] data) => null;
#endif
    }
}
