using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;

namespace EntityFX.Tests.Integration
{
    [TestClass]
    public class BrokerRateLimiterTests
    {
        [TestMethod]
        public void TryAcquire_AllowsFirstToken()
        {
            var limiter = new BrokerRateLimiter(1000.0, TimeSpan.FromMilliseconds(0.1));

            Assert.IsTrue(limiter.TryAcquire(0));
        }

        [TestMethod]
        public void TryAcquire_RejectsWithinInterval()
        {
            // 1000 RPS при тике 0.1 мс => 10 тиков на токен.
            var limiter = new BrokerRateLimiter(1000.0, TimeSpan.FromMilliseconds(0.1));

            Assert.IsTrue(limiter.TryAcquire(0));
            for (var tick = 1; tick < 10; tick++)
            {
                Assert.IsFalse(limiter.TryAcquire(tick));
            }
            Assert.IsTrue(limiter.TryAcquire(10));
        }

        [TestMethod]
        public void TryAcquire_UnlimitedRate_AlwaysAllows()
        {
            var limiter = new BrokerRateLimiter(double.MaxValue, TimeSpan.FromMilliseconds(0.1));

            Assert.IsTrue(limiter.TryAcquire(0));
            Assert.IsTrue(limiter.TryAcquire(1));
            Assert.IsTrue(limiter.TryAcquire(2));
        }

        [TestMethod]
        public void Update_ZeroRps_ClampsToSingleTick()
        {
            var limiter = new BrokerRateLimiter(0.0, TimeSpan.FromMilliseconds(0.1));

            Assert.IsTrue(limiter.TryAcquire(0));
            Assert.IsTrue(limiter.TryAcquire(1));
        }
    }

    [TestClass]
    public class BrokerProcessingQueueTests
    {
        private static INetworkPacket CreatePacket(long id)
        {
            return new NetworkPacket<int>(id, null, 0, "a", "b",
                NodeType.Client, NodeType.Server, 0, 1,
                Array.Empty<byte>(), "mqtt", 0, 1, Category: "Test");
        }

        [TestMethod]
        public void DrainReady_ReturnsPacket_AfterProcessingTicks()
        {
            var queue = new BrokerProcessingQueue();
            var packet = CreatePacket(1);

            queue.Enqueue(packet, processingTicks: 3);

            Assert.AreEqual(0, queue.DrainReady().Count);
            Assert.AreEqual(0, queue.DrainReady().Count);

            var ready = queue.DrainReady();

            Assert.AreEqual(1, ready.Count);
            Assert.AreSame(packet, ready[0]);
            Assert.AreEqual(0, queue.Count);
        }

        [TestMethod]
        public void Enqueue_ClampsProcessingTicks_ToAtLeastOne()
        {
            var queue = new BrokerProcessingQueue();

            queue.Enqueue(CreatePacket(1), processingTicks: 0);

            Assert.AreEqual(1, queue.DrainReady().Count);
        }

        [TestMethod]
        public void DrainReady_ZeroTicks_ClampsToSingleTick()
        {
            var queue = new BrokerProcessingQueue();
            queue.Enqueue(CreatePacket(1), processingTicks: -5);

            Assert.AreEqual(1, queue.DrainReady().Count);
        }
    }

    [TestClass]
    public class BrokerFailModelTests
    {
        [TestMethod]
        public void ShouldFail_AlwaysFalse_WhenProbabilityZero()
        {
            var model = new BrokerFailModel(42);

            for (var i = 0; i < 1000; i++)
            {
                Assert.IsFalse(model.ShouldFail(0.0));
            }
        }

        [TestMethod]
        public void ShouldFail_AlwaysTrue_WhenProbabilityOne()
        {
            var model = new BrokerFailModel(42);

            for (var i = 0; i < 1000; i++)
            {
                Assert.IsTrue(model.ShouldFail(1.0));
            }
        }

        [TestMethod]
        public void ShouldFail_IsDeterministic_ForSameSeed()
        {
            var a = new BrokerFailModel(7);
            var b = new BrokerFailModel(7);

            for (var i = 0; i < 100; i++)
            {
                Assert.AreEqual(a.ShouldFail(0.3), b.ShouldFail(0.3));
            }
        }

        [TestMethod]
        public void ShouldFail_ApproximatelyMatchesProbability()
        {
            var model = new BrokerFailModel(1000);
            var samples = 100_000;
            var failed = 0;

            for (var i = 0; i < samples; i++)
            {
                if (model.ShouldFail(0.25))
                {
                    failed++;
                }
            }

            var rate = failed / (double)samples;
            Assert.AreEqual(0.25, rate, 0.01);
        }
    }
}