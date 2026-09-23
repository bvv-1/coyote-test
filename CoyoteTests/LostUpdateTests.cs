using LiteDB;
using Microsoft.Coyote.Runtime;
using Microsoft.Coyote.Specifications;

namespace CoyoteTests;

public static class LostUpdateTests
{
    [Microsoft.Coyote.SystematicTesting.Test]
    public static async Task DetectLostUpdate()
    {
        using var database = new LiteDatabase(":memory:");
        var accounts = database.GetCollection<BsonDocument>("accounts");
        accounts.Insert(new BsonDocument { ["_id"] = 1, ["balance"] = 100 });

        void IncrementAccount()
        {
            database.BeginTrans();
            SchedulingPoint.Interleave(); // Coyoteに探索させる
            var balance = accounts.FindById(1)["balance"].AsInt32;
            SchedulingPoint.Interleave(); // Coyoteに探索させる
            accounts.Update(new BsonDocument { ["_id"] = 1, ["balance"] = balance + 10 });
            database.Commit();
        }

        // 2つの並行処理が同一口座の残高を読み取って個別に増額を書き戻し、一方の更新が上書きされて失われるロストアップデートを検出する。
        var firstIncrementTask = Task.Run(IncrementAccount);
        var secondIncrementTask = Task.Run(IncrementAccount);

        await firstIncrementTask;
        await secondIncrementTask;
        Specification.Assert(accounts.FindById(1)["balance"].AsInt32 == 120, "Lost Update");
    }

    [Microsoft.Coyote.SystematicTesting.Test]
    public static async Task PreventLostUpdate()
    {
        // 2つの処理が更新式を用いて同一口座を増額し、読み取り値の上書きを起こさずに双方の増額が反映されることを確認する。
        using var database = new LiteDatabase(":memory:");
        var accounts = database.GetCollection<BsonDocument>("accounts");
        accounts.Insert(new BsonDocument { ["_id"] = 1, ["balance"] = 100 });

        void IncrementAccount()
        {
            accounts.UpdateMany("{ balance: balance + 10 }", "_id = 1");
        }

        var firstIncrementTask = Task.Run(IncrementAccount);
        var secondIncrementTask = Task.Run(IncrementAccount);

        await Task.WhenAll(firstIncrementTask, secondIncrementTask);
        Specification.Assert(accounts.FindById(1)["balance"].AsInt32 == 120, "Lost Update");
    }
}
