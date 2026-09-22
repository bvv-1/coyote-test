using Microsoft.Coyote.Rewriting;
using Microsoft.Coyote.Runtime;

namespace CoyoteTests;

// ReaderWriterLockSlimの取得・解放・待機をCoyoteへ明示するテスト用アダプター。
// 全アクセスをこのクラス経由に限定し、保持中にawaitしないこと。
[SkipRewriting("アダプターがSchedulingPointとOperation.PauseUntilで同期を手動制御するため、自動書き換えしない。")]
internal sealed class CoyoteReaderWriterLockAdapter : IDisposable
{
    private readonly ReaderWriterLockSlim inner = new();
    private long releaseVersion;
    private int waitingCount;

    internal int WaitingCount => Volatile.Read(ref waitingCount);

    public void EnterReadLock() => Enter(() => inner.TryEnterReadLock(0), inner.EnterReadLock);
    public void EnterUpgradeableReadLock() => Enter(() => inner.TryEnterUpgradeableReadLock(0), inner.EnterUpgradeableReadLock);
    public void EnterWriteLock() => Enter(() => inner.TryEnterWriteLock(0), inner.EnterWriteLock);

    public void ExitReadLock() => Exit(inner.ExitReadLock);
    public void ExitUpgradeableReadLock() => Exit(inner.ExitUpgradeableReadLock);
    public void ExitWriteLock() => Exit(inner.ExitWriteLock);

    private void Enter(Func<bool> tryEnter, Action enter)
    {
        SchedulingPoint.Interleave(); // Coyoteに探索させる
        while (true)
        {
            var observedVersion = Volatile.Read(ref releaseVersion);
            if (tryEnter()) return;

            // 条件は別スレッドからも評価されるため、ここではロックを取得しない。
            // 再開後、元のスレッドで取得を試み、競合が残っていれば再び待機する。
            Interlocked.Increment(ref waitingCount);
            try
            {
                Operation.PauseUntil(new ReleaseCondition(this, observedVersion).IsSatisfied);
            }
            finally
            {
                Interlocked.Decrement(ref waitingCount);
            }

            // 通常実行やfuzzモードではPauseUntilは何もしないので、標準の待機を使う。
            if (Volatile.Read(ref releaseVersion) == observedVersion)
            {
                enter();
                return;
            }
        }
    }

    private void Exit(Action exit)
    {
        exit();
        Interlocked.Increment(ref releaseVersion);
        SchedulingPoint.Interleave(); // Coyoteに探索させる
    }

    public void Dispose() => inner.Dispose();

    // 自動生成のクロージャーは親クラスのSkipRewritingを継承しないため、明示的な型にする。
    [SkipRewriting("待機条件の評価ではスケジューリングを発生させない。")]
    private sealed class ReleaseCondition(CoyoteReaderWriterLockAdapter owner, long observedVersion)
    {
        public bool IsSatisfied() => Volatile.Read(ref owner.releaseVersion) != observedVersion;
    }
}
