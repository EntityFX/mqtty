using System.Security.Cryptography;

namespace EntityFX.MqttY.LoadBalancing;

public sealed record InputSnapshot(string Path, byte[] Bytes)
{
    public string Sha256 { get; } = Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant();
    public static InputSnapshot Read(string path) => new(System.IO.Path.GetFullPath(path), File.ReadAllBytes(path));
}
