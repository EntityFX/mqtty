using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;

namespace EntityFX.Tests.Integration
{
    [TestClass]
    [TestCategory(TestCategories.Mechanism)]
    public class BrokerRateLimiterTests
    {
        [TestMethod]
        public void Reservation_DefersBurstWithoutRejectingOrAccumulatingIdleCredit()
        {
            var limiter = new BrokerRateLimiter(400, TimeSpan.FromMilliseconds(1));
            CollectionAssert.AreEqual(new[] { 0, 3, 5, 8, 10 }, Enumerable.Range(0, 5).Select(_ => limiter.Reserve(100)).ToArray());
            Assert.AreEqual(0, limiter.Reserve(10000));
            Assert.AreEqual(3, limiter.Reserve(10000));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => limiter.Reserve(9999));
        }

        [TestMethod]
        public void RateLimiter_RejectsInvalidRateAndTickValues()
        {
            foreach (var invalid in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity })
                Assert.ThrowsException<ArgumentOutOfRangeException>(() => new BrokerRateLimiter(invalid, TimeSpan.FromSeconds(1)));
            foreach (var invalid in new[] { 0, -1 })
                Assert.ThrowsException<ArgumentOutOfRangeException>(() => new BrokerRateLimiter(1, TimeSpan.FromTicks(invalid)));
            var limiter = new BrokerRateLimiter(1, TimeSpan.FromSeconds(1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => limiter.TryAcquire(-1));
            limiter.TryAcquire(10);
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => limiter.TryAcquire(9));
        }

        [TestMethod]
        public void TryAcquire_LongIdleGrantsOnlyOneBoundedBurst()
        {
            var limiter = new BrokerRateLimiter(8, TimeSpan.FromSeconds(1));
            Assert.IsFalse(limiter.TryAcquire(0));
            // Idle credit is bounded to one tick plus one message, not all idle time.
            for (var i = 0; i < 9; i++) Assert.IsTrue(limiter.TryAcquire(1000000));
            Assert.IsFalse(limiter.TryAcquire(1000000));
            for (var i = 0; i < 8; i++) Assert.IsTrue(limiter.TryAcquire(1000001));
            Assert.IsFalse(limiter.TryAcquire(1000001));
        }

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

        [DataTestMethod]
        [DataRow(3333.3)]
        [DataRow(9999.9)]
        [DataRow(10000.1)]
        [DataRow(36087.894937787474)]
        [DataRow(54981.25)]
        public void TryAcquire_FractionalCapacityIsPreservedUnderSustainedOverload(double rps)
        {
            var tickPeriod = TimeSpan.FromMilliseconds(0.1);
            var limiter = new BrokerRateLimiter(rps, tickPeriod);
            const int ticks = 100_000;
            var attemptsPerTick = (int)Math.Ceiling(rps * tickPeriod.TotalSeconds) + 2;
            var admitted = CountAcquired(limiter, ticks + 1, attemptsPerTick);
            var expected = rps * ticks * tickPeriod.TotalSeconds;

            Assert.AreEqual(expected, admitted, 1.0,
                "Overload must consume the fractional capacity without exceeding the configured rate.");
        }

        [DataTestMethod]
        [DataRow(3333.3)]
        [DataRow(10000.1)]
        [DataRow(36087.894937787474)]
        public void TryAcquire_TickBatchesAtConfiguredRateAreNotRejected(double rps)
        {
            var tokensPerTick = rps * 0.0001;
            var limiter = new BrokerRateLimiter(rps, TimeSpan.FromMilliseconds(0.1));
            long previousOffered = 0;
            for (var tick = 1; tick <= 100_000; tick++)
            {
                var offered = (long)Math.Floor(tick * tokensPerTick);
                for (var i = previousOffered; i < offered; i++)
                    Assert.IsTrue(limiter.TryAcquire(tick), $"Spurious rejection at tick {tick}, RPS {rps}");
                previousOffered = offered;
            }
        }

        [TestMethod]
        public void TryAcquire_FractionalRateStillBoundsIdleBurstAndSubsequentOverload()
        {
            var limiter = new BrokerRateLimiter(36087.894937787474, TimeSpan.FromMilliseconds(0.1));
            const long idleTick = 1_000_000;
            var admitted = 0;
            for (var i = 0; i < 100; i++)
                if (limiter.TryAcquire(idleTick)) admitted++;
            Assert.AreEqual(4, admitted, "Long idle must not accumulate unbounded capacity.");
            for (var tick = idleTick + 1; tick <= idleTick + 10_000; tick++)
            {
                for (var i = 0; i < 10; i++)
                    if (limiter.TryAcquire(tick)) admitted++;
                Assert.IsTrue(admitted <= Math.Floor(limiter.BurstTokens +
                    (tick - idleTick) * 3.6087894937787474 + 1e-9),
                    "Admission must stay within initial burst plus accrued capacity.");
            }
        }

        [TestMethod]
        public void Update_ResetsCreditAndUsesNewFractionalRate()
        {
            var limiter = new BrokerRateLimiter(80_000, TimeSpan.FromMilliseconds(0.1));
            Assert.IsTrue(limiter.TryAcquire(100));
            limiter.Update(3333.3, TimeSpan.FromMilliseconds(0.1));
            Assert.IsFalse(limiter.TryAcquire(0));
            var admitted = CountAcquired(limiter, 100_001, 2);
            Assert.AreEqual(33333.0, admitted, 1.0);
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
    [TestCategory(TestCategories.Mechanism)]
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
        public void Enqueue_RejectsNegativeProcessingTicks()
        {
            var queue = new BrokerProcessingQueue();
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => queue.Enqueue(CreatePacket(1), processingTicks: -5));
            Assert.AreEqual(0, queue.Count);
        }
    }

}
