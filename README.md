# UnityAutoBuildSteamUploader

Automated Steam build publisher for Unity. This editor tool builds a Windows executable, uploads it to Steam Pipe via Steamworks SDK and `steamcmd`, and then updates the target Steam branch using the Steam Publisher Web API.

This repository contains a Unity editor script for automated Steam publishing and a small preference helper for securely storing credentials locally.

---

## 日本語 / Japanese

### 概要
`SteamAutoPublisher.cs` は Unity Editor 上で動作する `EditorWindow` です。Unity プロジェクトをビルドし、その実行ファイルを Steam にアップロードし、対象ブランチに配信する一連の処理を自動化します。

主な機能:
- Unity ゲーム本体の Windows x64 ビルドを実行
- Steamworks SDK の `tools/ContentBuilder` と `steamcmd.exe` を利用してアップロード
- `SetAppBuildLive` により Steam のブランチを更新
- ビルド説明として最新 git コミットメッセージを利用
- 既存のビルド出力ディレクトリを直接アップロード可能
- `Build Profile` を利用したビルドにも対応（Unity 2021.2 以降）
- パスワードや API キーはローカル PC の `EditorPrefs` に保存し、Windows では DPAPI で暗号化

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
- Windows 環境でないと `DPAPI` を使えないため、秘密情報の保存は利用できないことがある
- `steamcmd` で一度手動ログイン済みであれば、パスワード欄を空にしてログイン情報を利用する方法もある
- Steam のブランチ更新には承認待ちが発生する場合があり、Steam モバイルアプリで承認が必要になることがある

---

## English / 英語

### Overview
`SteamAutoPublisher.cs` is a Unity `EditorWindow` that automates the full Steam publishing workflow inside the Unity editor. It builds a Windows executable, uploads it to Steam Pipe through the Steamworks SDK and `steamcmd`, and updates the target branch using the Steam Publisher Web API.

Main features:
- Builds the Unity game as a Windows x64 executable
- Uploads content using the Steamworks SDK `tools/ContentBuilder` and `steamcmd.exe`
- Updates a Steam branch with `SetAppBuildLive`
- Uses the latest git commit message as the build description
- Supports uploading an existing build output folder without rebuilding
- Supports build profile-based builds on Unity 2021.2 and later
- Stores credentials locally in `EditorPrefs` and encrypts secrets with Windows DPAPI when available

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

## License
This project is provided as-is for personal and internal use. Please confirm the appropriate licensing and usage terms before using it in production or distributing it publicly.

---

This tool is intended for Steamworks-enabled Unity projects and assumes the Steamworks SDK and valid Steam credentials are already configured on the machine running the editor.
