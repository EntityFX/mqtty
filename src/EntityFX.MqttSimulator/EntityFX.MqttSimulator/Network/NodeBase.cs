﻿using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using System.Collections.Concurrent;
using System.Collections.Generic;

public abstract class NodeBase : ISender
{
    protected readonly INetworkSimulator NetworkGraph;

    public Guid Id { get; private set; }

    public int Index { get; private set; }

    public string Address { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Group { get; set; }

    public abstract NodeType NodeType { get; }
    public int? GroupAmount { get; set; }
    public NetworkLoggerScope? Scope { get; set; }

    public abstract CounterGroup Counters { get; set;  }


    protected abstract bool ReceiveImplementation(NetworkPacket packet);

    protected abstract bool SendImplementation(NetworkPacket packet);


    protected abstract void BeforeReceive(NetworkPacket packet);
    protected abstract void AfterReceive(NetworkPacket packet);

    protected abstract void BeforeSend(NetworkPacket packet);
    protected abstract void AfterSend(NetworkPacket packet);


    public NodeBase(int index, string name, string address, INetworkSimulator networkGraph)
    {
        Address = address;
        Name = name;
        Id = Guid.NewGuid();
        Index = index;
        this.NetworkGraph = networkGraph;
    }


    //Создаём ManualResetEventSlim 
    public bool Send(NetworkPacket packet)
    {
        BeforeSend(packet);

        var result = SendImplementation(packet);

        AfterSend(packet);

        return result;
    }



    //Добавляем и Снимаем ManualResetEventSlim 
    public bool Receive(NetworkPacket packet)
    {
        BeforeReceive(packet);

        var result = ReceiveImplementation(packet);

        AfterReceive(packet);

        return result;
    }

    protected NetworkPacket GetPacket(Guid guid, string to, NodeType toType, byte[] payload,
        string protocol, string? category = null, Guid? requestId = null, bool willWait = false)
        => new NetworkPacket(Name, to, NodeType, toType, payload, protocol, category)
        {
            Id = guid,
            RequestId = requestId,
            WillWait = willWait
        };


    //Здесь обновляем время ождидания и триггерим ManualResetEventSlim
    public virtual void Refresh()
    {
        Counters.Refresh(NetworkGraph.TotalTicks);
    }

    public abstract void Reset();
}
