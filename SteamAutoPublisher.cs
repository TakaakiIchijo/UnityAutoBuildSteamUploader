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

public class SteamAutoPublisher : EditorWindow
{
    // --- 設定項目 ---
    // Steamworks SDKのルート（この下に tools/ContentBuilder がある）
    private string steamSdkPath = @"C:\SteamSDK";
    private string appId = "YOUR_APP_ID";
    // 空の場合は AppID + 1（Steamworksで最初に作られるデポ）を使う
    private string depotId = "";
    private string steamUsername = "YOUR_STEAM_USERNAME";
    // 空の場合は steamcmd にキャッシュされたログイン情報を使う
    private string steamPassword = "";
    private string targetBranch = "beta";
    private string publisherApiKey = "YOUR_PUBLISHER_WEB_API_KEY";

    // --- 追加された設定項目 ---
    private string customBuildPath = ""; 
#if UNITY_2021_2_OR_NEWER
    private BuildProfile selectedBuildProfile;
#endif

    [MenuItem("Tools/Steam Auto Publisher")]
    public static void ShowWindow()
    {
        GetWindow<SteamAutoPublisher>("Steam Publisher");
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
        var window = GetWindow<SteamAutoPublisher>("Steam Publisher");
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
        steamSdkPath = SteamAutoPublisherPrefs.GetString(nameof(steamSdkPath), steamSdkPath);
        appId = SteamAutoPublisherPrefs.GetString(nameof(appId), appId);
        depotId = SteamAutoPublisherPrefs.GetString(nameof(depotId), depotId);
        steamUsername = SteamAutoPublisherPrefs.GetString(nameof(steamUsername), steamUsername);
        targetBranch = SteamAutoPublisherPrefs.GetString(nameof(targetBranch), targetBranch);
        customBuildPath = SteamAutoPublisherPrefs.GetString(nameof(customBuildPath), customBuildPath);
        steamPassword = SteamAutoPublisherPrefs.GetSecret(nameof(steamPassword));
        publisherApiKey = SteamAutoPublisherPrefs.GetSecret(nameof(publisherApiKey));
#if UNITY_2021_2_OR_NEWER
        string profilePath = AssetDatabase.GUIDToAssetPath(SteamAutoPublisherPrefs.GetString(nameof(selectedBuildProfile), ""));
        selectedBuildProfile = string.IsNullOrEmpty(profilePath) ? null : AssetDatabase.LoadAssetAtPath<BuildProfile>(profilePath);
#endif
    }

    private void SaveSettings()
    {
        SteamAutoPublisherPrefs.SetString(nameof(steamSdkPath), steamSdkPath);
        SteamAutoPublisherPrefs.SetString(nameof(appId), appId);
        SteamAutoPublisherPrefs.SetString(nameof(depotId), depotId);
        SteamAutoPublisherPrefs.SetString(nameof(steamUsername), steamUsername);
        SteamAutoPublisherPrefs.SetString(nameof(targetBranch), targetBranch);
        SteamAutoPublisherPrefs.SetString(nameof(customBuildPath), customBuildPath);
        SteamAutoPublisherPrefs.SetSecret(nameof(steamPassword), steamPassword);
        SteamAutoPublisherPrefs.SetSecret(nameof(publisherApiKey), publisherApiKey);
#if UNITY_2021_2_OR_NEWER
        string profileGuid = selectedBuildProfile != null
            ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(selectedBuildProfile))
            : "";
        SteamAutoPublisherPrefs.SetString(nameof(selectedBuildProfile), profileGuid);
#endif
    }

    private void OnGUI()
    {
        GUILayout.Label("Steam 自動ビルド＆デプロイ設定", EditorStyles.boldLabel);
        if (!SteamAutoPublisherPrefs.CanStoreSecrets)
        {
            EditorGUILayout.HelpBox("この環境ではパスワードとAPIキーを暗号化して保存できないため、エディタを閉じると消えます。", MessageType.Info);
        }

        EditorGUI.BeginChangeCheck();

        // 既存の設定
        steamSdkPath = EditorGUILayout.TextField("Steamworks SDK Path", steamSdkPath);
        appId = EditorGUILayout.TextField("Steam App ID", appId);
        depotId = EditorGUILayout.TextField(
            new GUIContent("Steam Depot ID", "空の場合は App ID + 1 を使用します"),
            depotId);
        steamUsername = EditorGUILayout.TextField("Steam Username", steamUsername);
        steamPassword = EditorGUILayout.PasswordField(
            new GUIContent("Steam Password", "空の場合は steamcmd にキャッシュされたログイン情報を使用します"),
            steamPassword);
        targetBranch = EditorGUILayout.TextField(
            new GUIContent("Target Branch", "デフォルトブランチは public（default も可）"),
            targetBranch);
        publisherApiKey = EditorGUILayout.PasswordField("Publisher Web API Key", publisherApiKey);

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
    }

    private async void ExecutePipeline(bool  runUnityBuild)
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

        string exePath = Path.Combine(customBuildPath, Application.productName + ".exe");
        UnityEngine.Debug.Log($"Unityビルドを開始します... 出力先: {exePath}");

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
            buildPlayerOptions.target = BuildTarget.StandaloneWindows64;
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
        string steamCmdExe = Path.Combine(contentBuilderPath, "builder", "steamcmd.exe");

        if (!File.Exists(steamCmdExe))
        {
            UnityEngine.Debug.LogError($"steamcmd.exe が見つかりません。Steamworks SDK Path を確認してください: {steamCmdExe}");
            tcs.SetResult(0);
            return tcs.Task;
        }

        if (Directory.GetFiles(buildFolder, "*", SearchOption.AllDirectories).Length == 0)
        {
            UnityEngine.Debug.LogError($"アップロード対象のビルドフォルダが空です: {buildFolder}");
            tcs.SetResult(0);
            return tcs.Task;
        }

        string appVdfPath = PrepareAppBuildVdf(Path.Combine(contentBuilderPath, "scripts"), buildFolder, buildDescription);
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

        ProcessStartInfo processInfo = new ProcessStartInfo(steamCmdExe, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, 
            StandardErrorEncoding = Encoding.UTF8
        };

        Process process = new Process { StartInfo = processInfo };
        int extractedBuildId = 0;

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                UnityEngine.Debug.Log($"[SteamCmd] {e.Data}");
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
    private static readonly Regex ContentRootPattern = new Regex("(\"ContentRoot\"\\s*\")[^\"]*(\")", RegexOptions.IgnoreCase);
    private static readonly Regex DescPattern = new Regex("(\"Desc\"\\s*\")[^\"]*(\")", RegexOptions.IgnoreCase);

    /// <summary>
    /// 最新コミットのメッセージ（1行目）と短縮ハッシュをビルドの説明にする。git が使えない場合は製品名とバージョン。
    /// </summary>
    private static string GetBuildDescription()
    {
        string fallback = $"{Application.productName} {Application.version}";
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
        return long.TryParse(appId, out long id) ? (id + 1).ToString() : null;
    }

    /// <summary>
    /// アプリ用VDFを用意し、ContentRoot をアップロード対象のビルドフォルダに向ける。
    /// 初回（ファイルが無いとき）はデポ定義込みで新規作成する。
    /// 既存ファイルは ContentRoot だけを書き換え、手で追記した除外設定などは残す。
    /// </summary>
    private string PrepareAppBuildVdf(string scriptsFolder, string buildFolder, string buildDescription)
    {
        if (!long.TryParse(appId, out _))
        {
            UnityEngine.Debug.LogError($"Steam App ID が数値ではありません: {appId}");
            return null;
        }

        // VDF内のバックスラッシュはエスケープ扱いになり得るため、スラッシュ区切りの絶対パスで書く
        string contentRoot = Path.GetFullPath(buildFolder).Replace('\\', '/').TrimEnd('/') + "/";
        string appVdfPath = Path.Combine(scriptsFolder, $"app_{appId}.vdf");

        if (!File.Exists(appVdfPath))
        {
            string resolvedDepotId = ResolveDepotId();
            if (resolvedDepotId == null)
            {
                UnityEngine.Debug.LogError("Steam Depot ID を決定できません。");
                return null;
            }

            Directory.CreateDirectory(scriptsFolder);
            File.WriteAllText(appVdfPath, BuildAppVdf(resolvedDepotId, contentRoot, buildDescription), new UTF8Encoding(false));
            UnityEngine.Debug.Log($"アプリ用VDFを新規作成しました（Depot ID: {resolvedDepotId}）: {appVdfPath}");
            return appVdfPath;
        }

        string vdf = File.ReadAllText(appVdfPath);
        if (!ContentRootPattern.IsMatch(vdf))
        {
            UnityEngine.Debug.LogError($"既存のVDFに \"ContentRoot\" がありません。追記するか、ファイルを削除して再生成してください: {appVdfPath}");
            return null;
        }

        string updated = ContentRootPattern.Replace(vdf, m => m.Groups[1].Value + contentRoot + m.Groups[2].Value, 1);
        if (DescPattern.IsMatch(updated))
        {
            updated = DescPattern.Replace(updated, m => m.Groups[1].Value + buildDescription + m.Groups[2].Value, 1);
        }
        else
        {
            UnityEngine.Debug.LogWarning($"既存のVDFに \"Desc\" が無いため、ビルドの説明を設定できません: {appVdfPath}");
        }

        if (updated != vdf)
        {
            File.WriteAllText(appVdfPath, updated, new UTF8Encoding(false));
        }
        UnityEngine.Debug.Log($"VDFの ContentRoot を設定しました: {contentRoot}");
        return appVdfPath;
    }

    private string BuildAppVdf(string resolvedDepotId, string contentRoot, string buildDescription)
    {
        // BuildOutput は VDF の置き場所からの相対パス（SDK同梱の ContentBuilder/output）
        return
$@"""AppBuild""
{{
	""AppID"" ""{appId}""
	""Desc"" ""{buildDescription}""
	""ContentRoot"" ""{contentRoot}""
	""BuildOutput"" ""../output/""
	""Depots""
	{{
		""{resolvedDepotId}""
		{{
			""FileMapping""
			{{
				""LocalPath"" ""*""
				""DepotPath"" "".""
				""recursive"" ""1""
			}}
			""FileExclusion"" ""*.pdb""
			""FileExclusion"" ""*_BurstDebugInformation_DoNotShip*""
			""FileExclusion"" ""*_BackUpThisFolder_ButDontShipItWithYourGame*""
		}}
	}}
}}
";
    }

    // --- 3. Steamworks Web API を使ったブランチの即時更新 ---
    private async Task<bool> SetSteamAppBranch(int buildId, string buildDescription)
    {
        // https://partner.steamgames.com/doc/webapi/ISteamApps#SetAppBuildLive
        string url = "https://partner.steam-api.com/ISteamApps/SetAppBuildLive/v2/";

        // デフォルトブランチは betakey "public" で指定する
        string betaKey = targetBranch.Trim();
        if (string.Equals(betaKey, "default", StringComparison.OrdinalIgnoreCase)) betaKey = "public";

        var parameters = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>
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
                    UnityEngine.Debug.LogWarning($"[Steam API] ブランチ変更の承認待ちです。Steamモバイルアプリで承認してください。レスポンス: {responseString}");
                    return true;
                }

                if (response.IsSuccessStatusCode)
                {
                    UnityEngine.Debug.Log($"[Steam API] ブランチの更新に成功しました。レスポンス: {responseString}");
                    return true;
                }
                else
                {
                    UnityEngine.Debug.LogError($"[Steam API Error] ステータスコード: {response.StatusCode}\n詳細: {responseString}");
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
