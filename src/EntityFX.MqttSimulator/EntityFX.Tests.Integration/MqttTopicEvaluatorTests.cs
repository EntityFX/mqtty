using EntityFX.MqttY.Plugin.Mqtt.Internals;

namespace EntityFX.Tests.Integration
{
    [TestClass]
    public class MqttTopicEvaluatorTests
    {
        private readonly MqttTopicEvaluator _evaluator = new(allowWildcardsInTopicFilters: true);

        [DataTestMethod]
        [DataRow("sensors/room1/temp", "sensors/+/temp", true)]
        [DataRow("sensors//temp", "sensors/+/temp", true)]
        [DataRow("sensors", "sensors/#", true)]
        [DataRow("sensors/room1/temp", "sensors/#", true)]
        [DataRow("$SYS/broker/uptime", "#", false)]
        [DataRow("$SYS/broker/uptime", "+/broker/#", false)]
        [DataRow("$SYS/broker/uptime", "$SYS/#", true)]
        public void Matches_ImplementsMqttWildcardAndSystemTopicRules(
            string topic, string filter, bool expected)
        {
            Assert.AreEqual(expected, _evaluator.Matches(topic, filter));
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("sport/#/ranking")]
        [DataRow("sport/tennis#")]
        [DataRow("sport+")]
        [DataRow("sport/#/")]
        public void IsValidTopicFilter_RejectsInvalidFilters(string filter)
        {
            Assert.IsFalse(_evaluator.IsValidTopicFilter(filter));
        }

        [TestMethod]
        public void IsValidTopicFilter_RejectsNullAndOversizedFilter()
        {
            Assert.IsFalse(_evaluator.IsValidTopicFilter(null!));
            Assert.IsFalse(_evaluator.IsValidTopicFilter(new string('a', 65_536)));
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("sport/+")]
        [DataRow("sport/#")]
        public void IsValidTopicName_RejectsInvalidNames(string topic)
        {
            Assert.IsFalse(_evaluator.IsValidTopicName(topic));
        }
    }
}
