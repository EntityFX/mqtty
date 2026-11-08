using EntityFX.MqttY.Engines.Execution;

namespace EntityFX.Tests.Integration
{
    [TestClass]
    [TestCategory(TestCategories.Mechanism)]
    public class MetricsCollectorTests
    {
        [TestMethod]
        public void Snapshot_ComputesGoodputAndNearestRankQuantiles()
        {
            var collector = new MetricsCollector();
            collector.RecordScheduled(10);
            for (var i = 0; i < 10; i++)
            {
                collector.RecordAttempt();
                if (i == 9) collector.RecordRejected();
                collector.RecordLatencyTicks(i + 1);
            }

            collector.RecordUnique(8);
            collector.RecordError();

            var snapshot = collector.Snapshot("completed", measurementSeconds: 2,
                tickPeriod: TimeSpan.FromMilliseconds(0.1));

            Assert.AreEqual(10, snapshot.Scheduled);
            Assert.AreEqual(10, snapshot.Attempts);
            Assert.AreEqual(1, snapshot.Rejected);
            Assert.AreEqual(8, snapshot.Unique);
            Assert.AreEqual(4.0, snapshot.Goodput, 1e-12);
            Assert.AreEqual(0.5, snapshot.PublishP50Ms!.Value, 1e-12);
            Assert.AreEqual(1.0, snapshot.PublishP95Ms!.Value, 1e-12);
            Assert.AreEqual(1.0, snapshot.PublishP99Ms!.Value, 1e-12);
            Assert.AreEqual(1, snapshot.Errors);
            Assert.AreEqual("completed", snapshot.Status);
        }

        [TestMethod]
        public void Snapshot_WithNoLatencySamplesHasNoQuantiles()
        {
            var collector = new MetricsCollector();
            collector.RecordAttempt();
            collector.RecordUnique(1);

            var snapshot = collector.Snapshot("completed", 1, TimeSpan.FromMilliseconds(1));

            Assert.IsNull(snapshot.PublishP50Ms);
            Assert.IsNull(snapshot.PublishP95Ms);
            Assert.IsNull(snapshot.PublishP99Ms);
        }

        [TestMethod]
        public void ConcurrentRecording_IsLossless()
        {
            var collector = new MetricsCollector();
            Parallel.For(0, 10_000, _ =>
            {
                collector.RecordScheduled(1);
                collector.RecordAttempt();
                collector.RecordRejected();
                collector.RecordLatencyTicks(2);
            });

            Assert.AreEqual(10_000, collector.Scheduled);
            Assert.AreEqual(10_000, collector.Attempts);
            Assert.AreEqual(10_000, collector.Rejected);
            Assert.AreEqual(0.0002, collector.Snapshot("completed", 1, TimeSpan.FromTicks(1)).PublishP99Ms);
        }
    }
}
