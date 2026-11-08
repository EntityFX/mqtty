using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EntityFX.MqttY.Results
{
    /// <summary>
    /// Versioned result schema identifiers. Writers may keep their legacy file
    /// names, but every manifest carries the same schema version.
    /// </summary>
    public static class ResultSchema
    {
        public const int Version = 1;
        public const string LoadBalancingExperiment = "connection-balancing-v1";
        public const string FederationExperiment = "static-federation-v1";
        public const string RelayExperiment = "relay-experiment-v1";
    }

    /// <summary>Minimal provenance envelope shared by all experiment writers.</summary>
    public sealed record ExperimentManifestV1(
        int SchemaVersion,
        string Experiment,
        object ResolvedOptions,
        object? Configuration,
        object? Runtime);

    /// <summary>Artifact entry in a sealed result directory.</summary>
    public sealed record ExperimentArtifactV1(string Path, string Sha256);

    /// <summary>
    /// Single result writer used by all engines: deterministic JSON, atomic
    /// replacement, sorted artifact hashing. Engine adapters keep their current
    /// file names and serializer options.
    /// </summary>
    public interface IResultWriter
    {
        void CreateOutput(string directory);

        void WriteJson<T>(string path, T value, JsonSerializerOptions options);

        void WriteText(string path, string value);

        void WriteBytes(string path, byte[] value);

        void Seal(string directory, JsonSerializerOptions options);
    }

    public sealed class JsonResultWriter : IResultWriter
    {
        public static JsonResultWriter Default { get; } = new();

        public void CreateOutput(string directory)
        {
            directory = Path.GetFullPath(directory);
            if (Directory.Exists(directory) || File.Exists(directory))
            {
                throw new IOException("Choose a new output directory: " + directory);
            }

            Directory.CreateDirectory(directory);
            // CreateNew protects the ownership marker from being overwritten by a
            // competing invocation.
            using var marker = new FileStream(Path.Combine(directory, ".experiment"),
                FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }

        public void WriteJson<T>(string path, T value, JsonSerializerOptions options)
        {
            var temporary = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, value, options);
                }

                File.Move(temporary, path, overwrite: true);
            }
            catch
            {
                TryDelete(temporary);
                throw;
            }
        }

        public void WriteText(string path, string value) =>
            WriteBytes(path, new UTF8Encoding(false).GetBytes(value));

        public void WriteBytes(string path, byte[] value)
        {
            var temporary = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(value);
                }

                File.Move(temporary, path, overwrite: true);
            }
            catch
            {
                TryDelete(temporary);
                throw;
            }
        }

        public void Seal(string directory, JsonSerializerOptions options)
        {
            directory = Path.GetFullPath(directory);
            var sealPath = Path.GetFullPath(Path.Combine(directory, "sha256.json"));
            var artifactPath = Path.GetFullPath(Path.Combine(directory, ".experiment"));

            var hashes = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .Where(path => path != sealPath && path != artifactPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToDictionary(
                    path => Path.GetRelativePath(directory, path).Replace('\\', '/'),
                    HashFile);

            WriteJson(Path.Combine(directory, "sha256.json"), hashes, options);
        }

        private static string HashFile(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha256 = SHA256.Create();
            return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // The original exception is more informative than a cleanup failure.
            }
        }
    }
}
