﻿namespace EntityFX.MqttY.Mqtt
{
    internal interface IRepository<T>
        where T : IStorageObject
    {
        IEnumerable<T> ReadAll();

        T? Read(string id);

        void Create(T element);

        void Update(T element);

        void Delete(string id);
    }
}
