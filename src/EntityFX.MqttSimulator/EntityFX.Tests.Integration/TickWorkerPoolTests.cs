using EntityFX.MqttY;
using EntityFX.MqttY.Network;

namespace EntityFX.Tests.Integration;

[TestClass]
[TestCategory(TestCategories.Regression)]
public class TickWorkerPoolTests
{
    [DataTestMethod]
    [DataRow(1, TickBarrierMode.Spin)]
    [DataRow(4, TickBarrierMode.Spin)]
    [DataRow(4, TickBarrierMode.Monitor)]
    [Timeout(30000)]
    public void AlternatingPhases_VisitExactlyOnceAndPublishBeforeReturning(int workers, TickBarrierMode barrier)
    {
        using var pool = new TickWorkerPool(workers, new RuntimeOptions { Barrier = barrier });
        var published = new int[137];
        var consumed = new int[211];
        for (var generation = 1; generation <= 400; generation++)
        {
            var current = generation;
            Array.Clear(published);
            Array.Clear(consumed);
            pool.Run(i =>
            {
                Assert.AreEqual(0, published[i]);
                published[i] = current;
                if (i % 11 == 0) Thread.SpinWait(40);
            }, published.Length, i =>
            {
                // Reading every producer verifies the phase fence, not just
                // mutual exclusion for the same work item.
                foreach (var value in published) Assert.AreEqual(current, value);
                Assert.AreEqual(0, consumed[i]);
                consumed[i] = current;
            }, consumed.Length, generation % 3 + 1, generation % 4 + 1);
            Assert.IsTrue(consumed.All(value => value == current));

            // The next publication immediately follows RunSingle. This also
            // stresses workers still leaving the previous final fence.
            pool.RunSingle(i => published[i] = -current, published.Length, 2);
            Assert.IsTrue(published.All(value => value == -current));
            pool.Run(null, 0, i => consumed[i] = published[i % published.Length], consumed.Length);
            Assert.IsTrue(consumed.All(value => value == -current));
        }
    }

    [DataTestMethod]
    [DataRow(3, TickBarrierMode.Spin)]
    [DataRow(5, TickBarrierMode.Spin)]
    [DataRow(12, TickBarrierMode.Spin)]
    [DataRow(5, TickBarrierMode.Monitor)]
    [Timeout(30000)]
    public void AlternatingRangeStrategies_PreserveFencesAndVisitEveryItemOnce(int workers, TickBarrierMode barrier)
    {
        using var pool = new TickWorkerPool(workers, new RuntimeOptions { Barrier = barrier });
        var published = new int[workers * 19 + 1];
        var visited = new int[workers * 64 + 1];

        for (var generation = 1; generation <= 150; generation++)
        {
            var current = generation;
            // Alternate below, at and above the owned-range threshold.
            var count = workers * 64 + generation % 3 - 1;
            Array.Clear(published);
            Array.Clear(visited);
            pool.Run(i =>
            {
                Interlocked.Increment(ref published[i]);
                if (i % 17 == 0) Thread.SpinWait(40);
            }, published.Length, i =>
            {
                foreach (var value in published) Assert.AreEqual(1, value);
                Interlocked.Increment(ref visited[i]);
            }, count, generation % 3 + 1, generation % 4 + 1);

            for (var i = 0; i < count; i++) Assert.AreEqual(1, visited[i]);
            pool.RunSingle(i => published[i] = current, published.Length, 2);
            Assert.IsTrue(published.All(value => value == current));
        }
    }

    [DataTestMethod]
    [DataRow(TickBarrierMode.Spin)]
    [DataRow(TickBarrierMode.Monitor)]
    [Timeout(30000)]
    public void Exceptions_FenceBeforeRethrowAndAllowNextGeneration(TickBarrierMode barrier)
    {
        using var pool = new TickWorkerPool(4, new RuntimeOptions { Barrier = barrier });
        var visited = new int[31];
        for (var generation = 0; generation < 100; generation++)
        {
            Assert.ThrowsException<AggregateException>(() => pool.RunSingle(
                i => throw new InvalidOperationException("single"), 16, 1));
            Assert.ThrowsException<AggregateException>(() => pool.Run(
                i => throw new InvalidOperationException("first"), 16,
                i => Assert.Fail("Failed phase 1 must skip phase 2."), 16));
            Assert.ThrowsException<AggregateException>(() => pool.Run(null, 0,
                i => throw new InvalidOperationException("second"), 16));
            Array.Clear(visited);
            pool.RunSingle(i => Interlocked.Increment(ref visited[i]), visited.Length, 1);
            Assert.IsTrue(visited.All(value => value == 1));
        }
    }
}
