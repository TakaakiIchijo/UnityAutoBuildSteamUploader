using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;

namespace AutoBuildSteamUploader
{
    internal static class AutoBuildSteamUploaderPrefs
    {
        private static string KeyPrefix => $"AutoBuildSteamUploader.{PlayerSettings.productGUID}.";

        public static string GetString(string key, string defaultValue)
        {
            return EditorPrefs.GetString(KeyPrefix + key, defaultValue);
        }

        public static void SetString(string key, string value)
        {
            EditorPrefs.SetString(KeyPrefix + key, value ?? "");
        }

        public static bool GetBool(string key, bool defaultValue)
        {
            return EditorPrefs.GetBool(KeyPrefix + key, defaultValue);
        }

        public static void SetBool(string key, bool value)
        {
            EditorPrefs.SetBool(KeyPrefix + key, value);
        }

        /// <summary>
        /// 暗号化して保存した値を読む。復号できない（別ユーザー・別PC・非対応OS）場合は空文字。
        /// </summary>
        public static string GetSecret(string key)
        {
#if UNITY_EDITOR_OSX
        return MacKeychain.Find(KeyPrefix.TrimEnd('.'), key) ?? "";
#else
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
#endif
        }

        /// <summary>
        /// 値を暗号化して保存する。空文字なら保存済みの値を消す。暗号化できない環境では保存しない。
        /// </summary>
        public static void SetSecret(string key, string value)
        {
#if UNITY_EDITOR_OSX
        MacKeychain.Save(KeyPrefix.TrimEnd('.'), key, value);
#else
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
#endif
        }

        public static bool CanStoreSecrets
        {
            get
            {
#if UNITY_EDITOR_WIN || UNITY_EDITOR_OSX
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
                        ? CryptProtectData(ref input, "AutoBuildSteamUploader", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                            CryptProtectUiForbidden, ref output)
                        : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                            CryptProtectUiForbidden, ref output);
                    if (!succeeded)
                    {
                        UnityEngine.Debug.LogWarning(
                            $"[AutoBuildSteamUploader] 認証情報の{(protect ? "暗号化" : "復号")}に失敗しました（Win32エラー {Marshal.GetLastWin32Error()}）。");
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

#if UNITY_EDITOR_OSX
    /// <summary>
    /// macOS のログインキーチェーンに汎用パスワードとして保存する（サービス名 = プロジェクト固有の接頭辞、アカウント名 = 設定キー）。
    /// 外部コマンド（security）だと値がプロセス引数に出るため、Security.framework を直接呼ぶ。
    /// </summary>
    private static class MacKeychain
    {
        private const string SecurityLib = "/System/Library/Frameworks/Security.framework/Security";
        private const string CoreFoundationLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const int ErrSecSuccess = 0;
        private const int ErrSecItemNotFound = -25300;

        [DllImport(SecurityLib)]
        private static extern int SecKeychainFindGenericPassword(
            IntPtr keychainOrArray,
            uint serviceNameLength, byte[] serviceName,
            uint accountNameLength, byte[] accountName,
            out uint passwordLength, out IntPtr passwordData,
            out IntPtr itemRef);

        [DllImport(SecurityLib)]
        private static extern int SecKeychainAddGenericPassword(
            IntPtr keychain,
            uint serviceNameLength, byte[] serviceName,
            uint accountNameLength, byte[] accountName,
            uint passwordLength, byte[] passwordData,
            IntPtr itemRef);

        [DllImport(SecurityLib)]
        private static extern int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attrList, uint length, byte[] data);

        [DllImport(SecurityLib)]
        private static extern int SecKeychainItemDelete(IntPtr itemRef);

        [DllImport(SecurityLib)]
        private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

        [DllImport(CoreFoundationLib)]
        private static extern void CFRelease(IntPtr cf);

        public static string Find(string service, string account)
        {
            byte[] serviceBytes = Encoding.UTF8.GetBytes(service);
            byte[] accountBytes = Encoding.UTF8.GetBytes(account);

            int status = SecKeychainFindGenericPassword(
                IntPtr.Zero,
                (uint)serviceBytes.Length, serviceBytes,
                (uint)accountBytes.Length, accountBytes,
                out uint length, out IntPtr data, out IntPtr item);
            if (status != ErrSecSuccess)
            {
                if (status != ErrSecItemNotFound) LogFailure("読み込み", status);
                return null;
            }

            try
            {
                var bytes = new byte[length];
                Marshal.Copy(data, bytes, 0, (int)length);
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                SecKeychainItemFreeContent(IntPtr.Zero, data);
                if (item != IntPtr.Zero) CFRelease(item);
            }
        }

        /// <summary>空文字なら削除、既存の項目があれば上書き、無ければ追加する。</summary>
        public static void Save(string service, string account, string value)
        {
            byte[] serviceBytes = Encoding.UTF8.GetBytes(service);
            byte[] accountBytes = Encoding.UTF8.GetBytes(account);

            int status = SecKeychainFindGenericPassword(
                IntPtr.Zero,
                (uint)serviceBytes.Length, serviceBytes,
                (uint)accountBytes.Length, accountBytes,
                out uint length, out IntPtr data, out IntPtr item);

            if (status == ErrSecSuccess)
            {
                try
                {
                    var current = new byte[length];
                    Marshal.Copy(data, current, 0, (int)length);
                    SecKeychainItemFreeContent(IntPtr.Zero, data);

                    if (string.IsNullOrEmpty(value))
                    {
                        status = SecKeychainItemDelete(item);
                        if (status != ErrSecSuccess) LogFailure("削除", status);
                        return;
                    }

                    byte[] valueBytes = Encoding.UTF8.GetBytes(value);
                    if (Encoding.UTF8.GetString(current) == value) return;

                    status =
 SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)valueBytes.Length, valueBytes);
                    if (status != ErrSecSuccess) LogFailure("更新", status);
                }
                finally
                {
                    if (item != IntPtr.Zero) CFRelease(item);
                }
                return;
            }

            if (status != ErrSecItemNotFound)
            {
                LogFailure("読み込み", status);
                return;
            }

            if (string.IsNullOrEmpty(value)) return;

            byte[] newBytes = Encoding.UTF8.GetBytes(value);
            status = SecKeychainAddGenericPassword(
                IntPtr.Zero,
                (uint)serviceBytes.Length, serviceBytes,
                (uint)accountBytes.Length, accountBytes,
                (uint)newBytes.Length, newBytes,
                IntPtr.Zero);
            if (status != ErrSecSuccess) LogFailure("保存", status);
        }

        private static void LogFailure(string operation, int status)
        {
            UnityEngine.Debug.LogWarning($"[AutoBuildSteamUploader] キーチェーンの{operation}に失敗しました（OSStatus {status}）。");
        }
    }
#endif
    }
}