namespace EntityFX.MqttY.Contracts.Scenarios
{
    public interface IExecutable
    {
        public string Name { get; init; }

        Task ExecuteAsync();

        public object? Context { get; }
    }
}
