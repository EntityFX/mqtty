namespace EntityFX.MqttY.Contracts.Network
{
    /// <summary>
    /// Network admission policy for the rolling byte budget. The policy is
    /// deliberately split into reserve/commit/rollback because the network
    /// performs a second queue-capacity check after the byte claim; a failed
    /// second check must release the claimed bytes without enqueueing a window
    /// entry that would later be pruned twice.
    /// </summary>
    public interface IAdmissionPolicy
    {
        long WindowBytes { get; }

        bool TryReserve(long tick, int bytes);

        void Commit(long tick, int bytes);

        void Rollback(int bytes);

        void Prune(long tick);

        void Reset();
    }
}
