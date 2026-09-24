using LiteDB;
using Microsoft.Coyote.Runtime;
using Microsoft.Coyote.Specifications;

namespace CoyoteTests;

public static class WriteSkewTests
{
    [Microsoft.Coyote.SystematicTesting.Test]
    public static async Task DetectWriteSkew()
    {
        using var database = new LiteDatabase(":memory:");
        var doctors = database.GetCollection<BsonDocument>("doctors");
        doctors.Insert(new BsonDocument { ["_id"] = 1, ["onDuty"] = true });
        doctors.Insert(new BsonDocument { ["_id"] = 2, ["onDuty"] = true });

        // 2つの処理がそれぞれ当直者数を確認し、2人以上なら退勤するため、当直者が0人になる Write Skew を検出する。
        var firstLeaveTask = Task.Run(() => TryLeaveDuty(1));
        var secondLeaveTask = Task.Run(() => TryLeaveDuty(2));
        await Task.WhenAll(firstLeaveTask, secondLeaveTask);

        var onDutyDoctorCount = doctors.Count(Query.EQ("onDuty", true));
        Specification.Assert(onDutyDoctorCount >= 1, "Write Skew");

        void TryLeaveDuty(int doctorId)
        {
            database.BeginTrans();
            SchedulingPoint.Interleave(); // Coyoteに実行順序の切り替えを探索させる
            var currentlyOnCall = doctors.Count(Query.EQ("onDuty", true));
            SchedulingPoint.Interleave(); // Coyoteに実行順序の切り替えを探索させる
            if (currentlyOnCall >= 2)
            {
                doctors.Update(new BsonDocument { ["_id"] = doctorId, ["onDuty"] = false });
            }
            database.Commit();
        }
    }

    [Microsoft.Coyote.SystematicTesting.Test]
    public static async Task PreventWriteSkew()
    {
        using var database = new LiteDatabase(":memory:");
        var doctors = database.GetCollection<BsonDocument>("doctors");
        doctors.Insert(new BsonDocument { ["_id"] = 1, ["onDuty"] = true });
        doctors.Insert(new BsonDocument { ["_id"] = 2, ["onDuty"] = true });

        // 「onDuty = true」を保護する述語ロックを、並行する処理間で共有する。
        // ReaderWriterLockSlimでの待機をCoyoteに通知するアダプターを使用する。
        using var onDutyPredicateLock = new CoyoteReaderWriterLockAdapter();
        var firstLeaveTask = Task.Run(() => TryLeaveDuty(1));
        var secondLeaveTask = Task.Run(() => TryLeaveDuty(2));
        await Task.WhenAll(firstLeaveTask, secondLeaveTask);

        var onDutyDoctorCount = doctors.Count(Query.EQ("onDuty", true));
        Specification.Assert(onDutyDoctorCount >= 1, "Write Skew");

        // Two Phase Locking (2PL) を使用して、述語ロックを取得することで、Write Skew を防止する。
        void TryLeaveDuty(int doctorId)
        {
            SchedulingPoint.Interleave(); // Coyoteに実行順序の切り替えを探索させる
            // アップグレード可能な読み取りロックを同時に1つに制限し、書き込みロックへの昇格時の相互待ちを防ぐ。
            onDutyPredicateLock.EnterUpgradeableReadLock();
            try
            {
                database.BeginTrans();
                var currentlyOnCall = doctors.Count(Query.EQ("onDuty", true));
                SchedulingPoint.Interleave(); // Coyoteに実行順序の切り替えを探索させる
                if (currentlyOnCall >= 2)
                {
                    // 読み取りを保護したまま、書き込みロックへ昇格する。
                    onDutyPredicateLock.EnterWriteLock();
                    try
                    {
                        doctors.Update(new BsonDocument { ["_id"] = doctorId, ["onDuty"] = false });
                    }
                    finally
                    {
                        onDutyPredicateLock.ExitWriteLock();
                    }
                }
                database.Commit();
            }
            finally
            {
                onDutyPredicateLock.ExitUpgradeableReadLock();
            }
        }
    }
}
