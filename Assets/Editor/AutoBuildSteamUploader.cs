using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

#if UNITY_2021_2_OR_NEWER
using UnityEditor.Build.Profile;
#endif
#if UNITY_6000_3_OR_NEWER
using UnityEditor.Toolbars;
#endif

namespace AutoBuildSteamUploader
{
    public class AutoBuildSteamUploader : EditorWindow
    {
        // リリース済みアプリの public ブランチ変更では、承認するアカウントのSteamIDが必須

        // --- 設定項目 ---
        // Steamworks SDKのルート（この下に tools/ContentBuilder がある）
        private string steamSdkPath = DefaultSteamSdkPath();

        private string appId = "YOUR_APP_ID";

        // Windows で空の場合は AppID + 1（Steamworksで最初に作られるデポ）を使う。macOS では mac 用デポの指定が必須
        private string depotId = "";

        private string steamUsername = "YOUR_STEAM_USERNAME";

        // 空の場合は steamcmd にキャッシュされたログイン情報を使う
        private string steamPassword = "";

        private string targetBranch = "beta";

        // オンの場合、最新コミットのメッセージ（1行目）と短縮ハッシュをビルドの説明にする
        private bool useCommitMessageAsDescription = true;

        private string publisherApiKey = "YOUR_PUBLISHER_WEB_API_KEY";

        // デポ用VDFの FileExclusion（1行に1パターン）。配信しないデバッグ情報などを除外する
        private string fileExclusions = DefaultFileExclusions;

        private const string DefaultFileExclusions =
            "*.pdb\n*_BurstDebugInformation_DoNotShip*\n*_BackUpThisFolder_ButDontShipItWithYourGame*";

        // --- 追加された設定項目 ---
        private string customBuildPath = "";
#if UNITY_2021_2_OR_NEWER
        private BuildProfile selectedBuildProfile;
#endif

        // macOS のエディタでは macOS 向けにビルドし、SDK の builder_osx の steamcmd を使う
        private static bool IsMacEditor => Application.platform == RuntimePlatform.OSXEditor;

        private static string DefaultSteamSdkPath()
        {
            return IsMacEditor
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SteamSDK")
                : @"C:\SteamSDK";
        }

        [MenuItem("Tools/Auto Build Steam Uploader")]
        public static void ShowWindow()
        {
            GetWindow<AutoBuildSteamUploader>("Auto Build Steam Uploader");
        }

#if UNITY_6000_3_OR_NEWER
        // メインツールバー（再生ボタンの隣）に「ビルド・アップロード・ブランチ設定をすべて実行」を置く
        [MainToolbarElement("Steam/Publish", defaultDockPosition = MainToolbarDockPosition.Middle)]
        private static MainToolbarElement CreatePublishToolbarButton()
        {
            return new MainToolbarButton(new MainToolbarContent("Build&SendSteam"), RunFullPipelineFromToolbar);
        }
#endif

        private static void RunFullPipelineFromToolbar()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                UnityEngine.Debug.LogWarning("再生中・コンパイル中は Steam へのビルド・アップロードを実行できません。");
                return;
            }

            // ウィンドウを開くと保存済みの設定が読み込まれる（OnEnable）
            var window = GetWindow<AutoBuildSteamUploader>("Auto Build Steam Uploader");
            bool confirmed = EditorUtility.DisplayDialog(
                "Steam Publish",
                $"ビルドして Steam にアップロードし、ブランチ「{window.targetBranch}」に設定します。\n\n出力先: {window.customBuildPath}",
                "実行",
                "キャンセル");
            if (confirmed)
            {
                window.ExecutePipeline(runUnityBuild: true);
            }
        }

        private void OnEnable()
        {
            LoadSettings();

            // 初期パスの設定（デフォルトはプロジェクト直下のBuild/）
            if (string.IsNullOrEmpty(customBuildPath))
            {
                string projectPath = Path.GetDirectoryName(Application.dataPath);
                customBuildPath = Path.Combine(projectPath, "Build");
            }
        }

        // 設定はこのPC・このユーザーのローカルにだけ保存する（パスワードとAPIキーは暗号化）
        private void LoadSettings()
        {
            steamSdkPath = AutoBuildSteamUploaderPrefs.GetString(nameof(steamSdkPath), steamSdkPath);
            appId = AutoBuildSteamUploaderPrefs.GetString(nameof(appId), appId);
            depotId = AutoBuildSteamUploaderPrefs.GetString(nameof(depotId), depotId);
            steamUsername = AutoBuildSteamUploaderPrefs.GetString(nameof(steamUsername), steamUsername);
            targetBranch = AutoBuildSteamUploaderPrefs.GetString(nameof(targetBranch), targetBranch);
            useCommitMessageAsDescription = AutoBuildSteamUploaderPrefs.GetBool(nameof(useCommitMessageAsDescription),
                useCommitMessageAsDescription);
            customBuildPath = AutoBuildSteamUploaderPrefs.GetString(nameof(customBuildPath), customBuildPath);
            fileExclusions = AutoBuildSteamUploaderPrefs.GetString(nameof(fileExclusions), fileExclusions);
            steamPassword = AutoBuildSteamUploaderPrefs.GetSecret(nameof(steamPassword));
            publisherApiKey = AutoBuildSteamUploaderPrefs.GetSecret(nameof(publisherApiKey));
#if UNITY_2021_2_OR_NEWER
            string profilePath =
                AssetDatabase.GUIDToAssetPath(AutoBuildSteamUploaderPrefs.GetString(nameof(selectedBuildProfile), ""));
            selectedBuildProfile = string.IsNullOrEmpty(profilePath)
                ? null
                : AssetDatabase.LoadAssetAtPath<BuildProfile>(profilePath);
#endif
        }

        private void SaveSettings()
        {
            AutoBuildSteamUploaderPrefs.SetString(nameof(steamSdkPath), steamSdkPath);
            AutoBuildSteamUploaderPrefs.SetString(nameof(appId), appId);
            AutoBuildSteamUploaderPrefs.SetString(nameof(depotId), depotId);
            AutoBuildSteamUploaderPrefs.SetString(nameof(steamUsername), steamUsername);
            AutoBuildSteamUploaderPrefs.SetString(nameof(targetBranch), targetBranch);
            AutoBuildSteamUploaderPrefs.SetBool(nameof(useCommitMessageAsDescription), useCommitMessageAsDescription);
            AutoBuildSteamUploaderPrefs.SetString(nameof(customBuildPath), customBuildPath);
            AutoBuildSteamUploaderPrefs.SetString(nameof(fileExclusions), fileExclusions);
            AutoBuildSteamUploaderPrefs.SetSecret(nameof(steamPassword), steamPassword);
            AutoBuildSteamUploaderPrefs.SetSecret(nameof(publisherApiKey), publisherApiKey);
#if UNITY_2021_2_OR_NEWER
            string profileGuid = selectedBuildProfile != null
                ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(selectedBuildProfile))
                : "";
            AutoBuildSteamUploaderPrefs.SetString(nameof(selectedBuildProfile), profileGuid);
#endif
        }

        private void OnGUI()
        {
            GUILayout.Label("Steam 自動ビルド＆デプロイ設定", EditorStyles.boldLabel);
            if (!AutoBuildSteamUploaderPrefs.CanStoreSecrets)
            {
                EditorGUILayout.HelpBox("この環境ではパスワードとAPIキーを暗号化して保存できないため、エディタを閉じると消えます。", MessageType.Info);
            }

            EditorGUI.BeginChangeCheck();

            // 既存の設定
            steamSdkPath = EditorGUILayout.TextField("Steamworks SDK Path", steamSdkPath);
            appId = EditorGUILayout.TextField("Steam App ID", appId);
            depotId = EditorGUILayout.TextField(
                IsMacEditor
                    ? new GUIContent("Steam Depot ID (macOS)", "macOS 用デポのIDを指定してください（Windows 用デポに上書きしないため必須）")
                    : new GUIContent("Steam Depot ID", "空の場合は App ID + 1 を使用します"),
                depotId);
            steamUsername = EditorGUILayout.TextField("Steam Username", steamUsername);
            steamPassword = EditorGUILayout.PasswordField(
                new GUIContent("Steam Password", "空の場合は steamcmd にキャッシュされたログイン情報を使用します"),
                steamPassword);
            targetBranch = EditorGUILayout.TextField(
                new GUIContent("Target Branch", "デフォルトブランチは public（default も可）"),
                targetBranch);
            publisherApiKey = EditorGUILayout.PasswordField("Publisher Web API Key", publisherApiKey);
            useCommitMessageAsDescription = EditorGUILayout.Toggle(
                new GUIContent("最新コミットをビルドの説明にする", "最新コミットのメッセージ（1行目）と短縮ハッシュを使用します。オフの場合は製品名とバージョン"),
                useCommitMessageAsDescription);

            EditorGUILayout.Space(10);
            GUILayout.Label("ビルド・プロファイル設定", EditorStyles.boldLabel);

            // 1. 出力パス設定のUI
            EditorGUILayout.BeginHorizontal();
            customBuildPath = EditorGUILayout.TextField("Build Output Path", customBuildPath);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string folderPath = EditorUtility.OpenFolderPanel("Select Build Output Folder", customBuildPath, "");
                if (!string.IsNullOrEmpty(folderPath))
                {
                    customBuildPath = folderPath;
                    GUI.changed = true;
                }
            }

            EditorGUILayout.EndHorizontal();

            // 2. ビルドプロファイル設定のUI（Unity 2021.2以降のみ対応）
#if UNITY_2021_2_OR_NEWER
            selectedBuildProfile = (BuildProfile)EditorGUILayout.ObjectField(
                new GUIContent("Build Profile (Optional)", "指定しない場合は現在のビルド設定を使用します"),
                selectedBuildProfile,
                typeof(BuildProfile),
                false
            );
#else
        EditorGUILayout.HelpBox("Build Profileの指定は Unity 2021.2 以降で利用可能です。", MessageType.None);
#endif

            EditorGUILayout.Space(10);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("アップロード除外（1行に1パターン）", "デポ用VDFの FileExclusion。VDFはアップロードのたびに設定から生成し直します"),
                EditorStyles.boldLabel);
            if (GUILayout.Button("既定値に戻す", GUILayout.Width(90)))
            {
                fileExclusions = DefaultFileExclusions;
                GUI.FocusControl(null);
                GUI.changed = true;
            }

            EditorGUILayout.EndHorizontal();
            fileExclusions = EditorGUILayout.TextArea(fileExclusions, GUILayout.MinHeight(50));

            if (EditorGUI.EndChangeCheck())
            {
                SaveSettings();
            }

            EditorGUILayout.Space(15);

            if (GUILayout.Button("ビルド・アップロード・ブランチ設定をすべて実行", GUILayout.Height(40)))
            {
                ExecutePipeline(runUnityBuild: true);
            }

            EditorGUILayout.Space(5);

            // 🌟 追加：ビルド済みデータアップロードボタン（新規追加箇所）
            GUI.backgroundColor = new Color(0.3f, 0.6f, 0.9f); // ボタンを青色に
            if (GUILayout.Button("ビルド済みデータをアップロード (ビルドをスキップ)", GUILayout.Height(30)))
            {
                ExecutePipeline(runUnityBuild: false);
            }

            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(5);
            if (GUILayout.Button("VDFファイルだけを生成（アップロードしない）"))
            {
                string scriptsFolder = Path.Combine(steamSdkPath, "tools", "ContentBuilder", "scripts");
                string appVdfPath = PrepareAppBuildVdf(scriptsFolder, customBuildPath, GetBuildDescription());
                if (appVdfPath != null) EditorUtility.RevealInFinder(appVdfPath);
            }
        }

        private async void ExecutePipeline(bool runUnityBuild)
        {
            try
            {
                string buildFolder = "";

                if (runUnityBuild)
                {
                    // 1. Unityのビルド実行
                    string finalExePath = PerformUnityBuild();
                    if (string.IsNullOrEmpty(finalExePath)) return;
                    buildFolder = Path.GetDirectoryName(finalExePath);
                }
                else
                {
                    // ビルドスキップ時は、設定されているカスタムパスをそのまま使用
                    if (string.IsNullOrEmpty(customBuildPath) || !Directory.Exists(customBuildPath))
                    {
                        UnityEngine.Debug.LogError($"指定された Build Output Path が存在しません: {customBuildPath}");
                        return;
                    }

                    buildFolder = customBuildPath;
                    UnityEngine.Debug.Log($"Unityビルドをスキップします。既存のフォルダを使用: {buildFolder}");
                }

                // ビルドの説明には最新コミットのメッセージを使う
                string buildDescription = GetBuildDescription();
                UnityEngine.Debug.Log($"ビルドの説明: {buildDescription}");

                // 2. SteamPipe (steamcmd) でアップロード
                UnityEngine.Debug.Log("SteamPipeへのアップロードを開始します...");
                int buildId = await UploadToSteamPipe(buildFolder, buildDescription);
                if (buildId == 0)
                {
                    UnityEngine.Debug.LogError("SteamPipeへのアップロードに失敗、またはBuildIDの取得ができませんでした。");
                    return;
                }

                // 3. Web API を叩いてデポ（ビルド）をアプリブランチに設定
                UnityEngine.Debug.Log($"Build ID: {buildId} を ブランチ: {targetBranch} に自動設定中...");
                bool apiSuccess = await SetSteamAppBranch(buildId, buildDescription);

                if (apiSuccess)
                {
                    UnityEngine.Debug.Log("<color=green>【成功】すべての工程が完了しました！Steam上でビルドが配信されています。</color>");
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"パイプライン実行中にエラーが発生しました: {e.Message}");
            }
        }

        // --- 1. Unityビルド ---
        // --- 1. Unityビルド（修正版） ---
        private string PerformUnityBuild()
        {
            if (string.IsNullOrEmpty(customBuildPath))
            {
                UnityEngine.Debug.LogError("出力パスが空です。");
                return null;
            }

            // macOS ではアプリバンドル（.app）、Windows では .exe を出力する
            BuildTarget buildTarget = IsMacEditor ? BuildTarget.StandaloneOSX : BuildTarget.StandaloneWindows64;
            string exePath = Path.Combine(customBuildPath, Application.productName + (IsMacEditor ? ".app" : ".exe"));
            UnityEngine.Debug.Log($"Unityビルドを開始します（{buildTarget}）... 出力先: {exePath}");

            BuildReport report;

#if UNITY_2021_2_OR_NEWER
            // プロファイルが指定されている場合は、BuildPlayerWithProfileOptionsを使用する
            if (selectedBuildProfile != null)
            {
                UnityEngine.Debug.Log($"ビルドプロファイル適用: {selectedBuildProfile.name}");

                // 専用のプロファイル用オプション構造体を作成
                BuildPlayerWithProfileOptions profileOptions = new BuildPlayerWithProfileOptions
                {
                    buildProfile = selectedBuildProfile,
                    locationPathName = exePath, // 画面で指定したカスタムパスを上書き
                    options = BuildOptions.None
                };

                // プロファイル用オプションを引数に渡してビルドを実行
                report = BuildPipeline.BuildPlayer(profileOptions);
            }
            else
#endif
            {
                // プロファイルがない場合は、従来通りのBuildPlayerOptionsを使用
                BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
                buildPlayerOptions.scenes = GetScenePaths();
                buildPlayerOptions.locationPathName = exePath;
                buildPlayerOptions.target = buildTarget;
                buildPlayerOptions.options = BuildOptions.None;

                report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            }

            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                UnityEngine.Debug.Log($"Unityビルド成功: {summary.totalSize} バイト");
                return exePath;
            }
            else
            {
                UnityEngine.Debug.LogError("Unityビルドに失敗しました。");
                return null;
            }
        }

        // --- 2. SteamPipeへの自動送信 ---
        private Task<int> UploadToSteamPipe(string buildFolder, string buildDescription)
        {
            var tcs = new TaskCompletionSource<int>();

            string contentBuilderPath = Path.Combine(steamSdkPath, "tools", "ContentBuilder");
            string steamCmdExe = IsMacEditor
                ? Path.Combine(contentBuilderPath, "builder_osx", "steamcmd.sh")
                : Path.Combine(contentBuilderPath, "builder", "steamcmd.exe");

            if (!File.Exists(steamCmdExe))
            {
                UnityEngine.Debug.LogError($"steamcmd が見つかりません。Steamworks SDK Path を確認してください: {steamCmdExe}");
                tcs.SetResult(0);
                return tcs.Task;
            }

            if (Directory.GetFiles(buildFolder, "*", SearchOption.AllDirectories).Length == 0)
            {
                UnityEngine.Debug.LogError($"アップロード対象のビルドフォルダが空です: {buildFolder}");
                tcs.SetResult(0);
                return tcs.Task;
            }

            string appVdfPath = PrepareAppBuildVdf(Path.Combine(contentBuilderPath, "scripts"), buildFolder,
                buildDescription);
            if (appVdfPath == null)
            {
                tcs.SetResult(0);
                return tcs.Task;
            }

            // パスワードが空ならユーザー名だけでログインし、steamcmd にキャッシュされた認証情報を使う
            // （事前に steamcmd を手動で起動し、一度パスワードと Steam Guard でログインしておく）
            string login = string.IsNullOrEmpty(steamPassword)
                ? $"+login \"{steamUsername}\""
                : $"+login \"{steamUsername}\" \"{steamPassword}\"";
            string arguments = $"{login} +run_app_build \"{appVdfPath}\" +quit";

            // macOS の steamcmd.sh は実行権限が無い場合（Windowsで展開したSDKなど）があるため bash 経由で起動する
            ProcessStartInfo processInfo = IsMacEditor
                ? new ProcessStartInfo("/bin/bash", $"\"{steamCmdExe}\" {arguments}")
                : new ProcessStartInfo(steamCmdExe, arguments);
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;
            processInfo.RedirectStandardError = true;
            processInfo.CreateNoWindow = true;
            processInfo.StandardOutputEncoding = Encoding.UTF8;
            processInfo.StandardErrorEncoding = Encoding.UTF8;

            Process process = new Process { StartInfo = processInfo };
            int extractedBuildId = 0;

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    UnityEngine.Debug.Log($"[SteamCmd] {e.Data}");
                    if (e.Data.Contains("empty depot"))
                    {
                        UnityEngine.Debug.LogError(
                            $"デポにファイルが1つもアップロードされていません。デポ用VDFの ContentRoot / FileMapping を確認してください（{Path.Combine(contentBuilderPath, "output")} のログに参照先フォルダが出ます）。");
                    }

                    // 例: "Successfully finished AppID 480 build (BuildID 1234567)."
                    Match match = BuildIdPattern.Match(e.Data);
                    if (match.Success)
                    {
                        int.TryParse(match.Groups[1].Value, out extractedBuildId);
                    }
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null) UnityEngine.Debug.LogError($"[SteamCmd Error] {e.Data}");
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            process.Exited += (sender, args) =>
            {
                // 非同期の標準出力を最後まで読み切ってから BuildID を確定させる
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    string hint = process.ExitCode == 5
                        ? "（ログイン失敗: ユーザー名・パスワードを確認するか、steamcmd で一度手動ログインしてパスワード欄を空にしてください）"
                        : "";
                    UnityEngine.Debug.LogError($"steamcmd が終了コード {process.ExitCode} で終了しました。{hint}");
                }

                process.Dispose();
                tcs.SetResult(extractedBuildId);
            };
            process.EnableRaisingEvents = true;

            return tcs.Task;
        }

        private static readonly Regex BuildIdPattern = new Regex(@"BuildID\s+(\d+)");

        /// <summary>
        /// 最新コミットのメッセージ（1行目）と短縮ハッシュをビルドの説明にする。
        /// オプションがオフ、または git が使えない場合は製品名とバージョン。
        /// </summary>
        private string GetBuildDescription()
        {
            string fallback = $"{Application.productName} {Application.version}";
            if (!useCommitMessageAsDescription) return fallback;

            try
            {
                var gitInfo = new ProcessStartInfo("git", "log -1 --format=\"%s (%h)\"")
                {
                    WorkingDirectory = Path.GetDirectoryName(Application.dataPath),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using (Process git = Process.Start(gitInfo))
                {
                    string output = git.StandardOutput.ReadToEnd().Trim();
                    git.WaitForExit();
                    if (git.ExitCode != 0 || string.IsNullOrEmpty(output))
                    {
                        UnityEngine.Debug.LogWarning($"最新コミットを取得できませんでした。説明には \"{fallback}\" を使います。");
                        return fallback;
                    }

                    // VDFの文字列を壊さないよう、ダブルクォートとバックスラッシュを置き換える
                    return output.Replace('"', '\'').Replace('\\', '/');
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"git の実行に失敗しました（{e.Message}）。説明には \"{fallback}\" を使います。");
                return fallback;
            }
        }

        private string ResolveDepotId()
        {
            if (!string.IsNullOrWhiteSpace(depotId)) return depotId.Trim();
            // AppID + 1 は通常 Windows 用デポのため、macOS では推測しない
            if (IsMacEditor) return null;
            return long.TryParse(appId, out long id) ? (id + 1).ToString() : null;
        }

        /// <summary>
        /// 設定からアプリ用VDFとデポ用VDFを毎回生成する（既存のファイルは上書き）。
        /// ContentRoot はデポ用VDFには書かず、アプリ用VDFの1か所だけにする（デポ側にあるとそちらが優先されるため）。
        /// </summary>
        private string PrepareAppBuildVdf(string scriptsFolder, string buildFolder, string buildDescription)
        {
            if (!long.TryParse(appId, out _))
            {
                UnityEngine.Debug.LogError($"Steam App ID が数値ではありません: {appId}");
                return null;
            }

            string resolvedDepotId = ResolveDepotId();
            if (resolvedDepotId == null || !long.TryParse(resolvedDepotId, out _))
            {
                UnityEngine.Debug.LogError(IsMacEditor
                    ? "macOS 用の Steam Depot ID を指定してください。"
                    : $"Steam Depot ID を決定できません: {depotId}");
                return null;
            }

            // VDF内のバックスラッシュはエスケープ扱いになり得るため、パスはすべてスラッシュ区切りの絶対パスで書く
            string contentRoot = ToVdfPath(buildFolder) + "/";
            string buildOutput = ToVdfPath(Path.Combine(scriptsFolder, "..", "output")) + "/";
            // macOS 用は別ファイルにし、SDK フォルダを共有していても Windows 用の設定を上書きしない
            string appVdfPath = Path.Combine(scriptsFolder, IsMacEditor ? $"app_{appId}_osx.vdf" : $"app_{appId}.vdf");
            string depotVdfPath = Path.Combine(scriptsFolder, $"depot_{resolvedDepotId}.vdf");

            Directory.CreateDirectory(scriptsFolder);
            File.WriteAllText(depotVdfPath, BuildDepotVdf(resolvedDepotId), new UTF8Encoding(false));
            File.WriteAllText(appVdfPath,
                BuildAppVdf(resolvedDepotId, ToVdfPath(depotVdfPath), contentRoot, buildOutput, buildDescription),
                new UTF8Encoding(false));
            UnityEngine.Debug.Log(
                $"VDFを生成しました（Depot ID: {resolvedDepotId}, ContentRoot: {contentRoot}）\n{appVdfPath}\n{depotVdfPath}");
            return appVdfPath;
        }

        private static string ToVdfPath(string path)
        {
            return Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
        }

        private string BuildAppVdf(string resolvedDepotId, string depotVdfPath, string contentRoot, string buildOutput,
            string buildDescription)
        {
            return
                $@"""AppBuild""
{{
	""AppID"" ""{appId}""
	""Desc"" ""{buildDescription}""
	""ContentRoot"" ""{contentRoot}""
	""BuildOutput"" ""{buildOutput}""
	""SetLive"" """"
	""Preview"" ""0""
	""Local"" """"
	""Depots""
	{{
		""{resolvedDepotId}"" ""{depotVdfPath}""
	}}
}}
";
        }

        private string BuildDepotVdf(string resolvedDepotId)
        {
            var sb = new StringBuilder();
            sb.AppendLine("\"DepotBuildConfig\"");
            sb.AppendLine("{");
            sb.AppendLine($"\t\"DepotID\" \"{resolvedDepotId}\"");
            sb.AppendLine("\t\"FileMapping\"");
            sb.AppendLine("\t{");
            sb.AppendLine("\t\t\"LocalPath\" \"*\"");
            sb.AppendLine("\t\t\"DepotPath\" \".\"");
            sb.AppendLine("\t\t\"recursive\" \"1\"");
            sb.AppendLine("\t}");
            foreach (string exclusion in GetFileExclusions())
            {
                sb.AppendLine($"\t\"FileExclusion\" \"{exclusion}\"");
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        private string[] GetFileExclusions()
        {
            var result = new System.Collections.Generic.List<string>();
            foreach (string line in fileExclusions.Split('\n'))
            {
                // VDFの文字列を壊さないよう、ダブルクォートは除き、パス区切りはスラッシュにそろえる
                string pattern = line.Trim().Replace("\"", "").Replace('\\', '/');
                if (pattern.Length > 0) result.Add(pattern);
            }

            return result.ToArray();
        }

        // --- 3. Steamworks Web API を使ったブランチの即時更新 ---
        private async Task<bool> SetSteamAppBranch(int buildId, string buildDescription)
        {
            // https://partner.steamgames.com/doc/webapi/ISteamApps#SetAppBuildLive
            string url = "https://partner.steam-api.com/ISteamApps/SetAppBuildLive/v2/";

            // デフォルトブランチは betakey "public" で指定する
            string betaKey = targetBranch.Trim();
            if (string.Equals(betaKey, "default", StringComparison.OrdinalIgnoreCase)) betaKey = "public";

            var parameters =
                new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>
                {
                    new System.Collections.Generic.KeyValuePair<string, string>("key", publisherApiKey),
                    new System.Collections.Generic.KeyValuePair<string, string>("appid", appId),
                    new System.Collections.Generic.KeyValuePair<string, string>("buildid", buildId.ToString()),
                    new System.Collections.Generic.KeyValuePair<string, string>("betakey", betaKey),
                    new System.Collections.Generic.KeyValuePair<string, string>("description", buildDescription)
                };

            using (HttpClient client = new HttpClient())
            {
                var content = new FormUrlEncodedContent(parameters);

                try
                {
                    HttpResponseMessage response = await client.PostAsync(url, content);
                    string responseString = await response.Content.ReadAsStringAsync();

                    if (response.StatusCode == System.Net.HttpStatusCode.Created)
                    {
                        UnityEngine.Debug.LogWarning(
                            $"[Steam API] ブランチ変更の承認待ちです。Steamモバイルアプリで承認してください。レスポンス: {responseString}");
                        return true;
                    }

                    if (response.IsSuccessStatusCode)
                    {
                        UnityEngine.Debug.Log($"[Steam API] ブランチの更新に成功しました。レスポンス: {responseString}");
                        return true;
                    }
                    else
                    {
                        UnityEngine.Debug.LogError(
                            $"[Steam API Error] ステータスコード: {response.StatusCode}\n詳細: {responseString}");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[Steam API Exception] {ex.Message}");
                    return false;
                }
            }
        }

        private string[] GetScenePaths()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            string[] scenePaths = new string[scenes.Length];
            for (int i = 0; i < scenes.Length; i++)
            {
                scenePaths[i] = scenes[i].path;
            }

            return scenePaths;
        }
    }
}