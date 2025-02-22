﻿using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Monitoring
{
    public interface IMonitoring
    {
        public event EventHandler<MonitoringItem> Added;

        public event EventHandler<MonitoringScope> ScopeStarted;

        public event EventHandler<MonitoringScope> ScopeEnded;

        public IEnumerable<MonitoringItem> Items { get; }

        public void Push(Packet packet, MonitoringType type, string? category, MonitoringScope? scope = null);

        void Push(INode from, INode to, byte[]? packet, MonitoringType type, string? category, MonitoringScope? scope = null);

        MonitoringScope BeginScope(string scopeMessage, MonitoringScope? parent);

        void TryBeginScope(ref Packet packet, string scope);

        void TryEndScope(ref Packet packet);

        void TryEndScope(MonitoringScope? scope);

        MonitoringScope? EndScope(Guid? scopeId);
    }
}
