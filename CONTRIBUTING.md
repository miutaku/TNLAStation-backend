# コントリビューション

IssueやPull Requestを歓迎します。EPGStation互換性に影響する変更は、
対応する上流仕様や実際の応答をIssueまたはPRに添えてください。

## 開発手順

```sh
dotnet restore TNLAStation.sln
dotnet format TNLAStation.sln --verify-no-changes
dotnet test TNLAStation.sln
```

- `main` から作業ブランチを作る
- PRは1つの目的に絞り、関連Issueを記載する
- API、設定、migrationの変更にはテストと移行説明を付ける
- ログや設定から秘密情報を除去する
- セキュリティ上の問題は公開Issueにせず、[SECURITY.md](SECURITY.md)に従う

大きな仕様変更は先にIssueで方向性を確認してください。
リリースタグの作成はメンテナーが行います。
