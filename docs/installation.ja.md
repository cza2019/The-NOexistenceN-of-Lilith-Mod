# インストール・更新・削除

## インストール

1. Releasesから `The-NOexistenceN-of-Lilith-Mod-v0.3.0-win-x64.zip` をダウンロードし、公開されたSHA-256と照合します。
2. トレイから『The NOexistenceN of Lilith』を終了し、Steamの表示が「停止」でなくなるまで待ちます。
3. ZIPの全内容をゲームのルートへ展開します。`winhttp.dll`、`Lilith.exe`、`BepInEx`、`dotnet` がゲームディレクトリにある状態が正しい配置です。
4. Steamから起動します。初回のIL2CPPキャッシュ生成は時間がかかる場合があります。読み込み中に強制終了しないでください。
5. リリスまたはトレイアイコンを右クリックして「Mod設定」を開きます。「その他」でゲーム言語に従うか固定言語を選び、AI会話を使う場合は自分のモデルサービスのAPIを設定します。

BepInEx、Node.js、メディアブリッジは同梱済みで、グローバルなインストールやNodeの固定作業は不要です。Mod設定にはWindows 10/11標準のWindows PowerShell 5.1を使います。

## 更新

ゲームを完全に終了してから、新しいZIPで既存ファイルを上書きします。`%APPDATA%\LilithAI` にあるAPI設定、セッション、プレイヤー記憶は上書きされません。Mod言語を切り替えても履歴や長期記憶は翻訳されません。

## 削除

ゲーム終了後、同梱されたBepInEx／Doorstopファイルを削除するか、Steamでゲームファイルを確認します。API設定、会話、プレイヤー記憶も消す場合は `%APPDATA%\LilithAI` を手動で削除します。

## トラブルシューティング

- Modが読み込まれない：`winhttp.dll` と `Lilith.exe` が同じ階層か確認し、`BepInEx\LogOutput.log` を見ます。
- 設定が開かない：`BepInEx\plugins\companion\configure.ps1` と `BepInEx\plugins\locales` があるか確認します。
- 文字が欠ける：ゲーム本体を対応言語に切り替えてください。ModはゲームのTMPフォントとfallbackを利用します。
- ログ共有前に個人的な会話、プレイヤー名、ローカルパスを削除してください。
