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

## CI

GitHub Actionsでpush・プルリクエスト作成／更新時に`make setup`と`make test`を実行します。Actions画面からの手動実行も可能です。

`make test`はCoyoteですべてのテストを最大100回探索します。Coyoteの終了結果をそのまま返すため、競合検出を確認するテストが含まれることから、`make test`自体が失敗する場合があります。

`PreventWriteSkewWithPredicateLock`は、`ReaderWriterLockSlim`の昇格可能な読み取りロックで当直人数を確認し、書き込みロックへ昇格して退勤状態を更新します。昇格可能な読み取りを同時に1つに制限するため、デッドロック後のabortではなく、昇格時のデッドロックを予防する方式です。

`ReaderWriterLockSlim`はCoyote 1.7.11の自動制御対象外なので、`CoyoteReaderWriterLockAdapter`を経由して使います。ロック取得を`TryEnter…(0)`で試み、取得できなければ`Operation.PauseUntil`で解放通知まで操作を停止します。実際のロックは.NET標準のままで、取得・解放時の実行切り替えを明示するため、このテストも`--fuzz`なしで実行できます。

アダプター内部は`SkipRewriting`で自動書き換えから除外します。アダプター自身が`SchedulingPoint`と`Operation.PauseUntil`で同期を手動制御するためです。待機条件は解放回数の変化だけを確認し、ロックの取得は再開後の元のスレッドで行います。全アクセスをアダプター経由に限定する必要があり、CLR内部の待機キューや公平性の再現は対象外です。

追加のロック検証では、共有読み取りと昇格待ち、昇格可能な読み取りの排他性を各100回確認します。これらとwrite skew対策テストには`--partial-control none --fail-on-max-steps`を指定し、制御対象外の呼び出しや探索上限到達も失敗として扱います。また、2つのロックを逆順で取得する循環待ちを1回実行し、Coyoteが`Deadlock detected.`と報告することを確認します。この失敗トレースは`make replay TRACE="<traceのパス>" METHOD=DetectReaderWriterLockDeadlock`で再現できます。

生成されたトレースは、CIの成否にかかわらず`coyote-traces`アーティファクトとして7日間保存します。
