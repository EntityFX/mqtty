using System.Security.Cryptography;
using System.Text.Json;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Engines.Contracts;
using EntityFX.MqttY.Engines.Execution;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;
using EntityFX.MqttY.Plugin.Mqtt.Experiments;

namespace EntityFX.MqttY.Engines.TwinValidation;

public static class TwinValidationAdapter
{
    public static ExperimentResult Run(SimulationRunOptions run, string? output, bool validateOnly,
        CancellationToken cancellation, IProgress<EngineProgress>? progress)
    {
        var configuration=JsonSerializer.Deserialize<TwinValidationOptions>(run.NativeConfiguration!.Value.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive=true }) ?? throw new InvalidDataException("Empty twin configuration.");
        configuration.Validate();
        var profileBytes=File.ReadAllBytes(configuration.ProfilePath);
        var sha=Convert.ToHexString(SHA256.HashData(profileBytes)).ToLowerInvariant();
        if(!string.Equals(sha,configuration.ProfileSha256,StringComparison.OrdinalIgnoreCase)||
            !string.Equals(sha,run.ExpectedProfileSha256,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Frozen profile hash mismatch.");
        var profiles=new BrokerProfileSet(new[]{configuration.ProfilePath});
        NativeControlReference? nativeControl=null;byte[]? nativeControlBytes=null;
        if(configuration.NativeControlReferencePath!=null || configuration.NativeControlReferenceSha256!=null)
        {
            if(string.IsNullOrWhiteSpace(configuration.NativeControlReferencePath) || configuration.NativeControlReferenceSha256?.Length!=64)
                throw new InvalidDataException("Неполная привязка натурного контроля.");
            nativeControlBytes=File.ReadAllBytes(configuration.NativeControlReferencePath);
            if(!string.Equals(NativeControlReference.Sha(nativeControlBytes),configuration.NativeControlReferenceSha256,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Frozen native control hash mismatch.");
            nativeControl=NativeControlReference.Parse(nativeControlBytes);nativeControl.ValidateFor(configuration,profileBytes);
        }
        var profile=profiles.Get(configuration.Broker);
        var point=profile.For(configuration.PayloadBytes,(MqttQos)configuration.Qos,configuration.Publishers);
        if(!profile.RequireObservedPublisherCount || point.RttBaselineMs is not double rtt || !double.IsFinite(rtt) || rtt<=0)
            throw new InvalidDataException("Для этого опыта нужен измеренный профиль с RTT и точным числом отправителей.");
        var ticks=configuration.TickOptions();ticks.Validate();
        if(ticks.TickPeriod<=TimeSpan.Zero || Math.Abs(ticks.TickPeriod.TotalSeconds-configuration.TickSeconds)>1e-12)
            throw new InvalidDataException("Шаг времени не представим без округления.");
        if(validateOnly)return new ExperimentResult { Success=true };
        long totalTicks=0,steps=0,errors=0;
        void Report(INetworkSimulator graph,string phase)
        {
            cancellation.ThrowIfCancellationRequested();
            totalTicks=graph.TotalTicks;steps=graph.TotalSteps;errors=graph.Errors;
            progress?.Report(new EngineProgress { TotalTicks=totalTicks,Step=steps,Errors=checked((int)errors),Phase=phase,
                Nodes=NodeSnapshotBuilder.Build(graph),Links=graph.Networks.Values.SelectMany(n=>
                    n.LinkedNearestNetworks.Values.Select(other=>new NetworkLinkSnapshot(n.Name,other.Name))).ToArray() });
        }
        var result=new MqttRelayExperimentRunner(ticks,configuration.NetworkOptions(),profiles).Run(
            configuration.ExperimentOptions(),output!,new(configuration.RuntimeIdentity,configuration.RuntimeIdentity,sha)
                { BrokerProfiles=profiles.Artifacts },onProgress:Report);
        cancellation.ThrowIfCancellationRequested();
        // Keep the runner's original directory; expose byte-identical files at the Web result root.
        foreach(var file in Directory.GetFiles(result.RunDirectory))
            File.Copy(file,Path.Combine(output!,Path.GetFileName(file)),overwrite:false);
        File.Copy(configuration.ProfilePath,Path.Combine(output!,"profile-snapshot.json"),overwrite:false);
        if(nativeControl!=null)
        {
            File.WriteAllBytes(Path.Combine(output!,"native-control-snapshot.json"),nativeControlBytes!);
            File.WriteAllText(Path.Combine(output!,"native-control-comparison.json"),nativeControl.Compare(result.RunDirectory,configuration.NativeControlReferenceSha256!).GetRawText());
        }
        File.WriteAllText(Path.Combine(output!,"twin-validation.json"),JsonSerializer.Serialize(new {
            kind="matched-single-broker",configuration,profileSha256=sha,originalRunDirectory=result.RunDirectory,
            referenceScope=nativeControl==null?"calibration profile; not new independent native validation":NativeControlReference.Scope,
            nativeControlReferenceSha256=configuration.NativeControlReferenceSha256,totalTicks,steps,errors
        },new JsonSerializerOptions { PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true }));
        var hashes=Directory.GetFiles(output!).OrderBy(p=>p,StringComparer.Ordinal).ToDictionary(path=>Path.GetFileName(path)!,
            path=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
        File.WriteAllText(Path.Combine(output!,"sha256.json"),JsonSerializer.Serialize(hashes,new JsonSerializerOptions { WriteIndented=true }));
        var completed=result.Summary.Status==ExperimentRunStatus.Completed;
        return new ExperimentResult { Success=completed,ExitCode=completed?0:1,Error=result.Summary.Error,
            OutputDirectory=output,TotalTicks=totalTicks,TotalSteps=steps,Errors=errors };
    }
}
