# インストール・更新・削除

## インストール

1. Releasesから `The-NOexistenceN-of-Lilith-Mod-v0.3.4-win-x64.zip` をダウンロードし、公開されたSHA-256と照合します。
2. トレイから『The NOexistenceN of Lilith』を終了し、Steamの表示が「停止」でなくなるまで待ちます。
3. ZIPの全内容をゲームのルートへ展開します。`winhttp.dll`、`Lilith.exe`、`BepInEx`、`dotnet` がゲームディレクトリにある状態が正しい配置です。
4. Steamから起動します。初回のIL2CPPキャッシュ生成は時間がかかる場合があります。読み込み中に強制終了しないでください。
5. リリスまたはトレイアイコンを右クリックして「Mod設定」を開きます。「その他」でゲーム言語に従うか固定言語を選び、AI会話を使う場合は自分のモデルサービスのAPIを設定します。

BepInEx、Node.js、メディアブリッジは同梱済みで、グローバルなインストールやNodeの固定作業は不要です。Mod設定にはWindows 10/11標準のWindows PowerShell 5.1を使います。

ゲーム起動時に GitHub の最新正式版をバックグラウンドで一度確認します。最新版の場合や GitHub に接続できない場合は何も表示しません。新しい版がある場合だけ Release ページを開くか確認し、ファイルを自動ダウンロード・置換することはありません。

## Gemini の設定

1. Google AI Studio で自分の Gemini API Key を作成し、地域で Gemini API と Google Search grounding が利用可能か確認します。
2. 「Mod設定 → AI API」で「Google Gemini 公式」を選びます。公式 Base URL、`gemini-3.6-flash`、Interactions API、`low` 思考レベルが自動設定されます。
3. リアルタイム情報が必要なら「モデル内蔵検索」を選びます。検索オフ、Brave Search、SearXNG も選択でき、別ウィンドウと検索料金は Gemini が実際に検索した場合だけ発生します。
4. Google へ直接接続できない場合は、認証情報を含まない HTTP/HTTPS プロキシを設定します。設定テストとゲーム内要求は同じプロキシを使います。

結果ウィンドウは Windows 10/11 に通常含まれる WebView2 Evergreen Runtime を使用します。ない場合は同じ一回限りのページを既定ブラウザーで開きます。`store=false` は会話チェーンの保存のみを無効にし、無料枠のデータ利用と Search grounding 関連要求の 30 日保持には Google の規約が適用されます。

## 更新

ゲームを完全に終了してから、新しいZIPで既存ファイルを上書きします。`%APPDATA%\LilithAI` にあるAPI設定、セッション、プレイヤー記憶は上書きされません。Mod言語を切り替えても履歴や長期記憶は翻訳されません。

## 削除

ゲーム終了後、同梱されたBepInEx／Doorstopファイルを削除するか、Steamでゲームファイルを確認します。API設定、会話、プレイヤー記憶も消す場合は `%APPDATA%\LilithAI` を手動で削除します。

## トラブルシューティング

- Modが読み込まれない：`winhttp.dll` と `Lilith.exe` が同じ階層か確認し、`BepInEx\LogOutput.log` を見ます。
- 設定が開かない：`BepInEx\plugins\companion\configure.ps1` と `BepInEx\plugins\locales` があるか確認します。
- 文字が欠ける：ゲーム本体を対応言語に切り替えてください。ModはゲームのTMPフォントとfallbackを利用します。
- ログ共有前に個人的な会話、プレイヤー名、ローカルパスを削除してください。
- Gemini に接続できない場合は、地域、Key、Base URL、プロキシを確認してください。認証付き／SOCKS プロキシは使えません。
- 別の検索ウィンドウは Google Search が実行された場合だけ開きます。WebView2、既定ブラウザー、セキュリティソフトを確認してください。

更新ではゲームフォルダーのプログラムファイルを置き換えます。API キー、設定、会話履歴、長期記憶は `%APPDATA%\LilithAI` に保存され、ゲーム本来のセーブデータも上書きされません。このユーザーデータフォルダーは削除せず、必要なら全体を別途バックアップしてください。予備 DNS の自動再試行は既定で有効です。保存済み・手入力のモデル名は維持されます。
