namespace EntityFX.Tests.Integration;

[TestClass]
[TestCategory(TestCategories.Mechanism)]
public class FederationDeterminismTests
{
    [TestMethod]
    public void Publications_AreSortedByOriginAndSequence()
    {
        var options = new EntityFX.MqttY.Federation.FederationOptions
        {
            Brokers = 3,
            Publishers = 3,
            PayloadBytes = 16
        };
        var ledger = new EntityFX.MqttY.Federation.FederationDeliveryLedger(options);

        // Create out of origin order: (origin 1, seq 1), (origin 0, seq 1),
        // (origin 1, seq 2). The export must be stable regardless of insertion.
        ledger.Create(1, 0, measurement: true);
        ledger.Create(0, 0, measurement: true);
        ledger.Create(1, 0, measurement: true);

        var publications = ledger.Publications();

        Assert.AreEqual(3, publications.Length);
        Assert.AreEqual(0, publications[0].Origin);
        Assert.AreEqual(1, publications[0].Sequence);
        Assert.AreEqual(1, publications[1].Origin);
        Assert.AreEqual(1, publications[1].Sequence);
        Assert.AreEqual(1, publications[2].Origin);
        Assert.AreEqual(2, publications[2].Sequence);
    }
}
