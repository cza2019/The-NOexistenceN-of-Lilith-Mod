# 『The NOexistenceN of Lilith』Mod

[简体中文](README.md) · [繁體中文](README.zh-TW.md) · [English](README.en.md) · [日本語](README.ja.md)

Steamのデスクトップペット『The NOexistenceN of Lilith』に、生成AIとの会話、プレイヤーの長期記憶、原作知識のローカル検索、関連するPC状態、外部音楽との連動を追加します。プレイヤーは自分のモデルサービスを設定するか、AIを設定せず音楽同期だけを利用できます。

> 現在のバージョン：0.3.0 · Windows 10/11 x64

## 主な機能

- リリスは『The NOexistenceN of you AND me』のエンディング1（Happy Ending）後の存在として一貫して会話します。
- 一つのパッケージで簡体字中国語、繁体字中国語、English、日本語をサポート。既定ではゲーム言語に従い、Mod設定で上書きできます。
- OpenAI互換APIとDeepSeek、Kimi、Qwen、GLM、Volcengine Arkのプリセット。
- 会話履歴とプレイヤーの長期記憶をローカル保存。言語を切り替えても原文を翻訳・複製しません。
- 質問に関係する場合だけ、ローカル時刻と許可された音楽アプリの情報を付加します。
- モデル内蔵検索、Brave Search、SearXNGを任意で利用できます。検索失敗は通常会話を妨げません。
- Unity内チャット、頭上の返答、履歴、原生台詞のクリック、音楽アプリ起動、原生音楽モーションとの同期。

## インストール

1. GitHub Releasesから `The-NOexistenceN-of-Lilith-Mod-v0.3.0-win-x64.zip` をダウンロードします。
2. 『The NOexistenceN of Lilith』を完全に終了します。
3. ZIPの全内容をゲームのルートへ展開し、`winhttp.dll` と `Lilith.exe` を同じ階層に置きます。
4. Steamから起動します。BepInExの初回起動は少し長くなることがあります。
5. リリスまたはトレイアイコンを右クリックし、「Mod設定」で言語と自分のモデルAPIを設定します。

パッケージにはBepInEx 6 IL2CPP、Unity基本ライブラリ、Node.js 24 LTS、自己完結型メディアブリッジが含まれます。別途インストールは不要です。詳しくは[インストール・更新・削除](docs/installation.ja.md)を参照してください。

## プライバシーとライセンス

- APIキーは `%APPDATA%\LilithAI\config.json` にのみ保存されます。
- 会話と有効にした一時コンテキストは、プレイヤーが設定したモデル／検索サービスへ送信されます。
- ブラウザ動画、クリップボード、アクティブウィンドウ、ファイル、バッテリー、ネットワーク状態は読み取りません。
- 独自コードは [MIT License](LICENSE) です。キャラクター素材と第三者コンポーネントは [素材声明](ASSET-NOTICE.md) と [Third-Party Notices](THIRD-PARTY-NOTICES.md) を参照してください。
- ゲーム本体は含まれません。Steamで『The NOexistenceN of Lilith』を正規に所有・インストールする必要があります。
