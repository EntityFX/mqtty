namespace EntityFX.MqttY.Contracts.Network
{
    public interface IApplication : INode, ILeafNode
    {
        bool IsStarted { get; }

        void Start();

        void Stop();
    }
}
