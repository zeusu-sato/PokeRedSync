# PokeRedSync

日本語版ポケモン赤の Palm OS 版で、複数の Palm 端末のあいだでセーブを受け渡すための、Windows 用の小さな常駐ツールです。

HotSync のあと、端末ごとの Backup フォルダにあるセーブを見て、ほかの端末の Install フォルダに置きます。次の HotSync で、ほかの端末のセーブがその端末に届きます。ゲームのデータやセーブそのものは、このリポジトリには含まれません。

## 入れ方

1. [Releases](https://github.com/zeusu-sato/PokeRedSync/releases) から `PokeRedSync-Setup-<版>.exe` をダウンロードして実行します。管理者の権限は要りません。
2. インストールが終わると、通知領域にアイコンが出ます。Windows の起動時にも自動で起動します。

削除は、Windows の「アプリ」から PokeRedSync を選びます。ログ（`%LOCALAPPDATA%\PokeRedSync`）と HotSync のフォルダは残ります。

## 更新

- 自動: 起動の 1 分後と、その後 6 時間ごとに Releases を確認し、新しい版があれば入れ替えます。通知領域のアイコンのメニューの「自動更新」で止められます。
- 手動: メニューの「更新を確認」を選びます。

更新の確認で送るのは、GitHub への問い合わせとダウンロードだけです。利用者の情報は送りません。ダウンロードしたものは、このリポジトリの Releases にあるチェックサムと照合してから入れます。

## ライセンス

MIT License
