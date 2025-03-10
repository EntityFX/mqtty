﻿namespace EntityFX.MqttY.Contracts.Network
{
    public interface IClient : ISender, ILeafNode
    {
        bool IsConnected { get; }

        Task<bool> ConnectAsync(string server);

        bool Disconnect();


        event EventHandler<Packet>? PacketReceived;

        Task<byte[]> SendWithResponseAsync(byte[] packet, string? category = null);

        Task SendAsync(byte[] packet, string? category = null);

        byte[] SendWithResponse(byte[] packet, string? category = null);

        void Send(byte[] packet, string? category = null);
    }
}
