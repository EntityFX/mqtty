namespace EntityFX.MqttY.Contracts.Scenarios
{
    public interface IExecutable : IDisposable
    {
        public string Name { get; init; }

        Task ExecuteAsync();

        public object? Context { get; }
    }
}
