using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;

namespace EntityFX.Tests.Integration
{
    [TestClass]
    public class BrokerRateLimiterTests
    {
        // Имитирует поступление attemptsPerTick публикаций в каждом тике.
        private static int CountAcquired(BrokerRateLimiter limiter, long totalTicks, int attemptsPerTick)
        {
            var acquired = 0;
            for (long t = 0; t < totalTicks; t++)
            {
                for (var i = 0; i < attemptsPerTick; i++)
                {
                    if (limiter.TryAcquire(t))
                    {
                        acquired++;
                    }
                }
            }
            return acquired;
        }

        [TestMethod]
        public void TryAcquire_ApproximatesConfiguredRate_LowRps()
        {
            var rps = 1000.0;
            var tickPeriod = TimeSpan.FromMilliseconds(0.1);
            var limiter = new BrokerRateLimiter(rps, tickPeriod);

            const long totalTicks = 100_000; // 10 виртуальных секунд.
            var acquired = CountAcquired(limiter, totalTicks, attemptsPerTick: 1);

            var expected = rps * (totalTicks * tickPeriod.TotalSeconds); // 10 000.
            Assert.AreEqual(expected, acquired, expected * 0.02);
        }

        [TestMethod]
        public void TryAcquire_SupportsRatesAboveTickFrequency()
        {
            // tickPeriod 0.1 мс => частота тиков 10 000/с; 80 000 RPS требует ~8 токенов/тик.
            var rps = 80_000.0;
            var tickPeriod = TimeSpan.FromMilliseconds(0.1);
            var limiter = new BrokerRateLimiter(rps, tickPeriod);

            const long totalTicks = 50_000; // 5 виртуальных секунд.
            // 80 000 RPS => ~8 токенов/тик, поэтому имитируем всплеск публикаций.
            var acquired = CountAcquired(limiter, totalTicks, attemptsPerTick: 16);

            var expected = rps * (totalTicks * tickPeriod.TotalSeconds); // 400 000.
            Assert.AreEqual(expected, acquired, expected * 0.02);
        }

        [TestMethod]
        public void TryAcquire_PassesWholeTokenOnly()
        {
            // 0.1 токена/тик -> первый токен накапливается только к 10-му тику.
            var limiter = new BrokerRateLimiter(1000.0, TimeSpan.FromMilliseconds(0.1));

            for (var t = 0; t < 10; t++)
            {
                Assert.IsFalse(limiter.TryAcquire(t));
            }
            Assert.IsTrue(limiter.TryAcquire(10));
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