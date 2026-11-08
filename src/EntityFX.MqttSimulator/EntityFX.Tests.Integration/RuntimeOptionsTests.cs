using EntityFX.MqttY;

namespace EntityFX.Tests.Integration
{
    [TestClass]
    [TestCategory(TestCategories.Mechanism)]
    [DoNotParallelize]
    public class RuntimeOptionsTests
    {
        private static readonly string[] Names =
        {
            "MQTTY_WORKERS", "MQTTY_BARRIER", "MQTTY_SPIN_ITERATIONS", "MQTTY_BARRIER_SPIN",
            "MQTTY_NODE_CHUNK", "MQTTY_NETWORK_CHUNK", "MQTTY_PARALLEL_PUBLISH",
            "MQTTY_ISOLATE_NETWORKS", "MQTTY_DISABLE_COUNTERS", "MQTTY_ACTIVE_ONLY",
            "MQTTY_REFRESH_STATS", "MQTTY_WORKER_STATS", "MQTTY_NODE_STATS", "MQTTY_NONATOMIC_ID"
        };

        [TestMethod]
        public void FromEnvironment_UsesDefaultsAndOverrides()
        {
            var previous = Names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
            try
            {
                foreach (var name in Names) Environment.SetEnvironmentVariable(name, null);

                var defaults = RuntimeOptions.FromEnvironment();
                Assert.IsNull(defaults.Workers);
                Assert.AreEqual(TickBarrierMode.Auto, defaults.Barrier);
                Assert.AreEqual(64, defaults.SpinIterations);
                Assert.AreEqual(64, defaults.BarrierSpinIterations);
                Assert.AreEqual(64, defaults.ResolveWorkers() > 0 ? 64 : -1);
                Assert.IsFalse(defaults.ParallelPublish);
                Assert.IsFalse(defaults.ActiveOnly);

                Environment.SetEnvironmentVariable("MQTTY_WORKERS", "7");
                Environment.SetEnvironmentVariable("MQTTY_BARRIER", "spin");
                Environment.SetEnvironmentVariable("MQTTY_SPIN_ITERATIONS", "5");
                Environment.SetEnvironmentVariable("MQTTY_BARRIER_SPIN", "0");
                Environment.SetEnvironmentVariable("MQTTY_NODE_CHUNK", "2");
                Environment.SetEnvironmentVariable("MQTTY_NETWORK_CHUNK", "3");
                Environment.SetEnvironmentVariable("MQTTY_PARALLEL_PUBLISH", "1");
                Environment.SetEnvironmentVariable("MQTTY_ISOLATE_NETWORKS", "1");
                Environment.SetEnvironmentVariable("MQTTY_DISABLE_COUNTERS", "1");
                Environment.SetEnvironmentVariable("MQTTY_ACTIVE_ONLY", "1");
                Environment.SetEnvironmentVariable("MQTTY_REFRESH_STATS", "1");
                Environment.SetEnvironmentVariable("MQTTY_WORKER_STATS", "1");
                Environment.SetEnvironmentVariable("MQTTY_NODE_STATS", "1");
                Environment.SetEnvironmentVariable("MQTTY_NONATOMIC_ID", "1");

                var overridden = RuntimeOptions.FromEnvironment();
                Assert.AreEqual(7, overridden.Workers);
                Assert.AreEqual(7, overridden.ResolveWorkers());
                Assert.AreEqual(TickBarrierMode.Spin, overridden.Barrier);
                Assert.AreEqual(5, overridden.SpinIterations);
                Assert.AreEqual(0, overridden.BarrierSpinIterations);
                Assert.AreEqual(2, overridden.NodeChunk);
                Assert.AreEqual(3, overridden.NetworkChunk);
                Assert.IsTrue(overridden.ParallelPublish);
                Assert.IsTrue(overridden.IsolateNetworks);
                Assert.IsTrue(overridden.DisableCounters);
                Assert.IsTrue(overridden.ActiveOnly);
                Assert.IsTrue(overridden.RefreshStats);
                Assert.IsTrue(overridden.WorkerStats);
                Assert.IsTrue(overridden.NodeStats);
                Assert.IsTrue(overridden.NonAtomicPacketId);

                Environment.SetEnvironmentVariable("MQTTY_WORKERS", "0");
                Environment.SetEnvironmentVariable("MQTTY_BARRIER", "unknown");
                Environment.SetEnvironmentVariable("MQTTY_SPIN_ITERATIONS", "-1");
                var invalid = RuntimeOptions.FromEnvironment();
                Assert.IsNull(invalid.Workers);
                Assert.AreEqual(TickBarrierMode.Auto, invalid.Barrier);
                Assert.AreEqual(64, invalid.SpinIterations);
            }
            finally
            {
                foreach (var (name, value) in previous) Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
