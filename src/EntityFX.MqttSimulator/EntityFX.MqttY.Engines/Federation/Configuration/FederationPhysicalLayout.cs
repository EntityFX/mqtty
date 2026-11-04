namespace EntityFX.MqttY.Federation;

/// <summary>Физические сети редактора, включая промежуточные сети без брокера.</summary>
public sealed record FederationPhysicalLayout(string[] NetworkNames, int[] BrokerNetworks, string[]? BrokerNames = null, string[]? PublisherNames = null);
