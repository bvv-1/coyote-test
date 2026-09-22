using LiteDB;
using Microsoft.Coyote.Runtime;
using Microsoft.Coyote.Specifications;

namespace CoyoteTests;

public static class WriteSkewTests
{
    [Microsoft.Coyote.SystematicTesting.Test]
    public static async Task DetectWriteSkew()
    {
        // 2つの処理がそれぞれ当直者数を確認し、2人以上なら退勤するため、当直者が0人になるライトスキューを検出する。
        using var database = new LiteDatabase(":memory:");
        var doctors = database.GetCollection<BsonDocument>("doctors");
        var doctorIds = new[] { 1, 2 };
        foreach (var doctorId in doctorIds)
        {
            doctors.Insert(new BsonDocument { ["_id"] = doctorId, ["onDuty"] = true });
        }

        var leaveTasks = doctorIds
            .Select(id => Task.Run(() => TryLeaveDuty(id)))
            .ToArray();
        await Task.WhenAll(leaveTasks);

        var onDutyDoctorCount = doctors.Count(Query.EQ("onDuty", true));
        Specification.Assert(onDutyDoctorCount >= 1, "Write Skew");

        bool TryLeaveDuty(int doctorId)
        {
            SchedulingPoint.Interleave(); // Coyoteに実行順序の切り替えを探索させる
            var onDutyCount = doctors.Count(Query.EQ("onDuty", true));
            SchedulingPoint.Interleave(); // Coyoteに実行順序の切り替えを探索させる
            return onDutyCount >= 2 &&
                doctors.Update(new BsonDocument { ["_id"] = doctorId, ["onDuty"] = false });
        }
    }

    [Microsoft.Coyote.SystematicTesting.Test]
    public static async Task PreventWriteSkew()
    {
        using var database = new LiteDatabase(":memory:");
        var doctors = database.GetCollection<BsonDocument>("doctors");
        var doctorIds = new[] { 1, 2 };
        foreach (var doctorId in doctorIds)
        {
            doctors.Insert(new BsonDocument { ["_id"] = doctorId, ["onDuty"] = true });
        }

        // 検索述語「onDuty = true」を保護する述語ロックを、並行する処理間で共有する。
        // ReaderWriterLockSlimでの待機をCoyoteに通知するアダプターを使用する。
        using var onDutyPredicateLock = new CoyoteReaderWriterLockAdapter();
        var leaveTasks = doctorIds
            .Select(id => Task.Run(() => TryLeaveDuty(id)))
            .ToArray();
        await Task.WhenAll(leaveTasks);

        var onDutyDoctorCount = doctors.Count(Query.EQ("onDuty", true));
        Specification.Assert(onDutyDoctorCount >= 1, "Write Skew");

        // ロックの取得と解放を同じスレッドで行うため、ロック保持中はawaitしない。
        bool TryLeaveDuty(int doctorId)
        {
            SchedulingPoint.Interleave(); // Coyoteに実行順序の切り替えを探索させる
            // アップグレード可能な読み取りロックを同時に1つに制限し、書き込みロックへの昇格時の相互待ちを防ぐ。
            onDutyPredicateLock.EnterUpgradeableReadLock();
            try
            {
                var onDutyCount = doctors.Count(Query.EQ("onDuty", true));
                SchedulingPoint.Interleave(); // Coyoteに実行順序の切り替えを探索させる
                if (onDutyCount < 2) return false;

                // 読み取りを保護したまま、書き込みロックへ昇格する。
                onDutyPredicateLock.EnterWriteLock();
                try
                {
                    return doctors.Update(new BsonDocument { ["_id"] = doctorId, ["onDuty"] = false });
                }
                finally
                {
                    onDutyPredicateLock.ExitWriteLock();
                }
            }
            finally
            {
                onDutyPredicateLock.ExitUpgradeableReadLock();
            }
        }
    }
}
