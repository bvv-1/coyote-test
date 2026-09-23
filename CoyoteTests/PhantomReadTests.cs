using LiteDB;
using Microsoft.Coyote.Runtime;
using Microsoft.Coyote.Specifications;

namespace CoyoteTests;

public static class PhantomReadTests
{
    [Microsoft.Coyote.SystematicTesting.Test]
    public static async Task DetectPhantomRead()
    {
        using var database = new LiteDatabase(":memory:");
        var accounts = database.GetCollection<BsonDocument>("accounts");
        accounts.Insert(new BsonDocument { ["_id"] = 1, ["balance"] = 100 });
        accounts.Insert(new BsonDocument { ["_id"] = 2, ["balance"] = 100 });

        // 検索の間に条件を満たす口座が追加され、検索結果の件数が変化する Phantom Read を検出する。
        var insertTask = Task.Run(() =>
        {
            accounts.Insert(new BsonDocument { ["_id"] = 3, ["balance"] = 100 });
        });

        var readTask = Task.Run(() =>
        {
            database.BeginTrans();
            SchedulingPoint.Interleave(); // Coyoteに探索させる
            var firstCount = accounts.Count(Query.GTE("balance", 100));
            SchedulingPoint.Interleave(); // Coyoteに探索させる
            var secondCount = accounts.Count(Query.GTE("balance", 100));
            database.Commit();

            return (firstCount, secondCount);
        });

        await insertTask;
        var (firstCount, secondCount) = await readTask;
        Specification.Assert(firstCount == secondCount, "Phantom Read");
    }
}
