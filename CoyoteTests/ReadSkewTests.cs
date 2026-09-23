using LiteDB;
using Microsoft.Coyote.Runtime;
using Microsoft.Coyote.Specifications;

namespace CoyoteTests;

public static class ReadSkewTests
{
    [Microsoft.Coyote.SystematicTesting.Test]
    public static async Task DetectReadSkew()
    {
        using var database = new LiteDatabase(":memory:");
        var accounts = database.GetCollection<BsonDocument>("accounts");
        accounts.Insert(new BsonDocument { ["_id"] = 1, ["balance"] = 100 });
        accounts.Insert(new BsonDocument { ["_id"] = 2, ["balance"] = 100 });

        // 送金処理における2口座の更新中間状態を読み取り、時点の異なる残高の組み合わせが観測される Read Skew を検出する。
        var transferTask = Task.Run(() =>
        {
            database.BeginTrans();
            accounts.Update(new BsonDocument { ["_id"] = 1, ["balance"] = 90 });
            accounts.Update(new BsonDocument { ["_id"] = 2, ["balance"] = 110 });
            database.Commit();
        });

        var readTask = Task.Run(() =>
        {
            database.BeginTrans();
            SchedulingPoint.Interleave(); // Coyoteに探索させる
            var firstBalance = accounts.FindById(1)["balance"].AsInt32;
            SchedulingPoint.Interleave(); // Coyoteに探索させる
            var secondBalance = accounts.FindById(2)["balance"].AsInt32;
            database.Commit();

            return (firstBalance, secondBalance);
        });

        await Task.WhenAll(transferTask, readTask);
        var (firstBalance, secondBalance) = await readTask;
        Specification.Assert(firstBalance + secondBalance == 200, "Read Skew");
    }
}
