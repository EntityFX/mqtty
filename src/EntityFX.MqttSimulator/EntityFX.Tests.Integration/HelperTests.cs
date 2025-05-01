using EntityFX.MqttY.Helper;

namespace EntityFX.Tests.Integration
{
    [TestClass]
    public class HelperTests
    {
        [TestMethod]
        public void HumanBytesTest()
        {
            var val1 = 12345678.0;
            var val1BytesH = val1.ToHumanBytes();
            var val1BitsH = val1.ToHumanBits();
        }
    }
}