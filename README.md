# Unity Auto Build Steam Uploader
### 概要
Unity EditorからビルドをSteam にアップロード・デポを対象ブランチに配信する一連の処理を自動化します。

機能:
- Unity ゲーム本体の Windows x64 ビルドを実行
- Steamworks SDK を利用してSteamサーバーにビルドをアップロード
- 指定のSteamのブランチにデポを割り当て、オプションとしてSteamのデポ説明文にgitコミットメッセージを利用可能
- `Build Profile` を利用したビルドにも対応（Unity 2021.2 以降）
- Steamパスワードや API キーはローカル PC の `EditorPrefs` に保存し、Windows では DPAPI で暗号化

### 必要な準備
1. Steamworks SDK をダウンロードしておく
2. `Steamworks SDK/tools/ContentBuilder/builder/steamcmd.exe` が存在することを確認する
3. Steam App ID と Depot ID を設定する
4. Steam のユーザー名と必要に応じてパスワードを入力する
5. Steam Publisher Web API Key を設定する
6. `Build Output Path` を指定する

### 使用方法
1. Unity でプロジェクトを開く
2. メニューから `Tools > Steam Auto Publisher` を選択する
3. 各項目を入力する
   - `Steamworks SDK Path`
   - `Steam App ID`
   - `Steam Depot ID`（空欄なら `App ID + 1` を自動利用）
   - `Steam Username`
   - `Steam Password`（空欄なら `steamcmd` のキャッシュ認証を使用）
   - `Target Branch`
   - `Publisher Web API Key`
   - `Build Output Path`
4. 「ビルド・アップロード・ブランチ設定をすべて実行」を押す
5. もしくはビルド済みデータのみをアップロードしたい場合は「ビルド済みデータをアップロード (ビルドをスキップ)」を使う

### スクリプトの役割
#### `SteamAutoPublisher.cs`
`SteamAutoPublisher` は Unity の `EditorWindow` として定義されており、Steam への自動公開パイプラインを担います。

主な処理:
- `OnGUI()`: 設定項目と実行ボタンの UI を描画
- `LoadSettings()` / `SaveSettings()`: ローカル設定の読み書き
- `ExecutePipeline()`: ビルド → SteamPipe アップロード → ブランチ更新を順番に実行
- `PerformUnityBuild()`: Unity のビルドジョブを実行する
- `UploadToSteamPipe()`: `steamcmd` を使ってビルドを Steam に送る
- `PrepareAppBuildVdf()`: Steamの `AppBuild` 用 VDF を生成・更新する
- `SetSteamAppBranch()`: Steam Web API で配信するブランチを更新する
- `GetBuildDescription()`: Git の最新コミットメッセージをビルド説明に使用する

#### `SteamAutoPublisherPrefs.cs`
`SteamAutoPublisherPrefs` は、ローカル環境だけに保存する設定管理用ヘルパーです。

役割:
- 一般設定は `EditorPrefs` に保存
- パスワードや API キーは Windows の DPAPI で暗号化して保存
- プロジェクトごとの設定が混ざらないように `PlayerSettings.productGUID` をキーに含める
- REST API や steamcmd で使う重要情報をリポジトリに保存しない構造にしている

### 注意点
- `SteamPassword` と `Publisher Web API Key` は環境によっては暗号化保存ができない場合がある
- DPAPIによる秘密情報の保存はwindowsのみ利用できないことがある
- Steam のブランチ更新には承認待ちが発生する場合があり、Steam モバイルアプリで承認が必要になることがある
- リリース済みアプリの public ブランチ変更では、承認するアカウントのSteamIDが必須、未検証

## 付録
### ISteamPublisherService 用キーの発行方法
1. Steamworks パートナーサイト に管理者アカウントでログインします。
2. 画面上部メニューの 「ユーザーと権限（Users & Permissions）」 から 「グループ管理（Manage Groups）」 を選択します。
3. 既存のグループ（該当のApp ID/ゲームが含まれているグループ）を選択するか、必要に応じて「Web API管理用」の新しいグループを作成します。
    • 注意: キーはそのグループに紐づくすべてのゲームにアクセスできるようになるため、セキュリティ上、アプリごとにグループを分けることが推奨されています。
4. グループの個別ページに移動したら、画面内（または右側サイドバー）にある 「WebAPIキーの作成（Create WebAPI Key）」 をクリックします。
5. 必要な権限（Permissions）の選択を求められます。今回の自動ビルド設定（SetAppBranchBuild）を行うには、基本権限である 「General」 または 「General API」（もしくは該当するアプリ編集権限）にチェックを入れて保存します。
6. 変更を保存すると、そのグループのページ（右側サイドバーなど）に 32文字のパブリッシャーWeb APIキー が生成・表示されます。これをコピーしてUnityツールの設定欄に貼り付けます。

---

## English

Automates the entire process of building the game from the Unity Editor, uploading the build to Steam, and deploying the depot to the target branch.

Features:
- Executes a Windows x64 build of the Unity game.
- Uploads the build to Steam servers using the Steamworks SDK.
- Assigns the depot to a specified Steam branch and uses the latest Git commit message as the build description.
- Supports builds using `Build Profile` (Unity 2021.2 or later).
- Stores Steam passwords and API keys in the local PC's `EditorPrefs`, encrypted via DPAPI on Windows.

### Requirements
1. Download and install the Steamworks SDK
2. Ensure `Steamworks SDK/tools/ContentBuilder/builder/steamcmd.exe` exists
3. Configure the Steam App ID and Depot ID
4. Set the Steam username and optionally the password
5. Provide the Steam Publisher Web API Key
6. Specify the build output path

### How to use
1. Open the Unity project
2. Select `Tools > Steam Auto Publisher` from the menu
3. Fill in the required values:
   - `Steamworks SDK Path`
   - `Steam App ID`
   - `Steam Depot ID` (if empty, it automatically uses `App ID + 1`)
   - `Steam Username`
   - `Steam Password` (empty means use cached `steamcmd` login data)
   - `Target Branch`
   - `Publisher Web API Key`
   - `Build Output Path`
4. Click `Build, Upload, and Configure Branch`
5. If you only want to upload an already built folder, use `Upload existing build data (skip build)`

### Script responsibilities
#### `SteamAutoPublisher.cs`
`SteamAutoPublisher` is the main editor window and orchestrates the entire publishing pipeline.

Main responsibilities:
- `OnGUI()`: renders the configuration UI and buttons
- `LoadSettings()` / `SaveSettings()`: loads and saves local settings
- `ExecutePipeline()`: runs build → upload → branch update in sequence
- `PerformUnityBuild()`: executes the Unity build process
- `UploadToSteamPipe()`: uploads the build with `steamcmd`
- `PrepareAppBuildVdf()`: creates or updates the `AppBuild` VDF file
- `SetSteamAppBranch()`: updates the release branch using the Steam Web API
- `GetBuildDescription()`: uses the latest git commit message as the build description

#### `SteamAutoPublisherPrefs.cs`
`SteamAutoPublisherPrefs` is a helper that keeps settings in the local machine only and avoids committing sensitive information to the repository.

Responsibilities:
- Saves regular settings with `EditorPrefs`
- Encrypts passwords and API keys using Windows DPAPI
- Includes `PlayerSettings.productGUID` in the key so values remain project-specific
- Prevents secrets from being stored directly in source control

### Notes
- The `SteamPassword` and `Publisher Web API Key` may not be encryptable on every platform
- On non-Windows systems, DPAPI-based secret storage may not be available
- If `steamcmd` has already been logged in once, it is often possible to leave the password blank and reuse cached credentials
- Steam branch updates can require approval via the Steam mobile app or related Steam workflow

---

## Repository Files
- `SteamAutoPublisher.cs`: main Unity editor tool for build and deploy automation
- `SteamAutoPublisherPrefs.cs`: local secure storage for project-specific settings and secrets
- `README.md`: project usage documentation
