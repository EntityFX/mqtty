using System.Security.Cryptography;
using System.Text.Json;
using EntityFX.MqttY.Engines.Execution;
using EntityFX.MqttY.Engines.TwinValidation;
using EntityFX.MqttY.Web.App.Models;
using EntityFX.MqttY.Web.App.Services;
using Microsoft.AspNetCore.Mvc;

namespace EntityFX.MqttY.Web.App.Controllers;

[ApiController]
[Route("api/twin-validations")]
public sealed class TwinValidationsController : ControllerBase
{
    private readonly IPresetCatalogService catalog;
    private readonly JsonFileStore store;
    private readonly ISimulationJobRunner runner;
    private readonly DataDirectoryOptions storage;
    public TwinValidationsController(IPresetCatalogService catalog,JsonFileStore store,ISimulationJobRunner runner,DataDirectoryOptions storage)
    {this.catalog=catalog;this.store=store;this.runner=runner;this.storage=storage;}

    [HttpPost]
    public async Task<IActionResult> Create(TwinValidationCreateDto request)
    {
        try
        {
            SimulationStorageGuard.Check(storage,store.Root);
            if(request.TimeLimitSeconds is <1 or >21600 || string.IsNullOrWhiteSpace(request.Name)||request.Name.Length>200)
                throw new InvalidDataException("Укажите имя и ограничение времени от 1 до 21600 секунд.");
            var preset=await catalog.GetBrokerPresetAsync(request.BrokerPresetId);
            if(preset==null || string.IsNullOrWhiteSpace(preset.ProfilePath))
                throw new InvalidDataException("Выберите пресет с измеренным профилем.");
            var bytes=await System.IO.File.ReadAllBytesAsync(store.Resolve(preset.ProfilePath));
            var sha=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var snapshot=store.Resolve($"profile-snapshots/{sha}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
            if(!System.IO.File.Exists(snapshot))
            {
                try { await using var stream=new FileStream(snapshot,FileMode.CreateNew,FileAccess.Write,FileShare.None);
                    await stream.WriteAsync(bytes); }
                catch(IOException) when(System.IO.File.Exists(snapshot)) { }
            }
            var config=new TwinValidationOptions { Broker=preset.BrokerType,Publishers=request.Publishers,Qos=request.Qos,
                Seed=request.Seed,WarmupSeconds=request.WarmupSeconds,MeasurementSeconds=request.MeasurementSeconds,
                DrainSeconds=request.DrainSeconds,TickSeconds=request.TickSeconds,ProfilePath=snapshot,ProfileSha256=sha,
                RuntimeIdentity="sha256:"+Convert.ToHexString(SHA256.HashData(await System.IO.File.ReadAllBytesAsync(typeof(TwinValidationAdapter).Assembly.Location))).ToLowerInvariant() };
            var options=new SimulationRunOptions { Engine="twinValidation",ExpectedProfileSha256=sha,
                NativeConfiguration=JsonSerializer.SerializeToElement(config) };
            NativeExperimentAdapter.Run(options,null,validateOnly:true);
            options.Graph.Ticks=config.TickOptions();
            if(request.ValidateOnly)return Ok(new { valid=true,profileSha256=sha,referenceScope="calibration only",
                warmupTicks=config.Ticks(config.WarmupSeconds),measurementTicks=config.Ticks(config.MeasurementSeconds),
                drainTicks=config.Ticks(config.DrainSeconds) });
            var job=new SimulationJob { Name=request.Name,Engine="twinValidation",RunOptions=options,
                ExecutionMode="OutOfProcess",Parallel=false,TimeLimit=TimeSpan.FromSeconds(request.TimeLimitSeconds) };
            await runner.EnqueueAsync(job);
            return Accepted(new {id=job.Id,engine=job.Engine,state=job.State});
        }
        catch(Exception error) when(error is ArgumentException or IOException or InvalidDataException or InvalidOperationException or KeyNotFoundException)
        {return BadRequest(new {error=error.Message});}
    }
}
