# リリース

このリポジトリはSemantic Versioningを採用する。`main` のCI成功を確認後、
メンテナーが `vX.Y.Z` タグをpushすると、`Release` workflowがbackendと
ffmpeg-workerの両イメージをGHCRへ公開し、GitHub Releaseを自動生成する。

```sh
git switch main
git pull --ff-only
git tag -a v1.2.3 -m "v1.2.3"
git push origin v1.2.3
```

正式版には `1.2.3`、`1.2`、`1`、`latest` が付く。
`v1.2.3-rc.1` のようなプレリリースは完全バージョンだけを公開し、
`latest` を更新しない。公開済みタグは上書きせず、修正は新しいPATCH版で出す。
