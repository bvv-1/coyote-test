# Coyote で LiteDB の並行性問題を検証する

Microsoft Coyote を使って、LiteDB 5.0.21 における並行実行時の挙動を検証するサンプルです。次の問題を検証します。

- Lost update
- Read skew
- Write skew
- Phantom read

## 実行

```sh
make setup
make test
```

Coyoteの失敗トレースは次の形式で再現できます。

```sh
make replay TRACE="<traceのパス>" METHOD=DetectLostUpdate
```
