using EntityFX.MqttY.Engines.TwinValidation;
using EntityFX.MqttY.Web.App.Services;
using Microsoft.AspNetCore.Mvc;

namespace EntityFX.MqttY.Web.App.Controllers;

[ApiController]
[Route("api/twin-validations/references")]
public sealed class NativeControlReferencesController : ControllerBase
{
    private readonly JsonFileStore store;
    private readonly DataDirectoryOptions storage;
    public NativeControlReferencesController(JsonFileStore store,DataDirectoryOptions storage) {this.store=store;this.storage=storage;}

    internal static byte[] Load(JsonFileStore store,string id)
    {
        if(id.Length!=64 || !id.All(Uri.IsHexDigit))throw new InvalidDataException("Некорректный идентификатор контроля.");
        var bytes=System.IO.File.ReadAllBytes(store.Resolve("native-controls/"+id.ToLowerInvariant()+".json"));
        if(!string.Equals(NativeControlReference.Sha(bytes),id,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("SHA натурного контроля изменился.");
        return bytes;
    }

    [HttpGet]
    public IActionResult List()
    {
        var entries=new List<object>();
        foreach(var id in store.ListFiles("native-controls","*.json").OrderBy(x=>x))
        {
            try {var bytes=Load(store,id);entries.Add(NativeControlReference.Parse(bytes).Describe(id));}
            catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException)
            { entries.Add(new {id,name="Недоступный контроль",error=e.Message}); }
        }
        return Ok(entries);
    }

    [HttpPost]
    [RequestSizeLimit(1_000_000)]
    public async Task<IActionResult> Import()
    {
        try
        {
            SimulationStorageGuard.Check(storage,store.Root);
            using var memory=new MemoryStream();await Request.Body.CopyToAsync(memory);var bytes=memory.ToArray();
            if(bytes.Length==0 || bytes.Length>1_000_000)throw new InvalidDataException("Пакет контроля должен быть непустым JSON до 1 МБ.");
            var reference=NativeControlReference.Parse(bytes);var id=NativeControlReference.Sha(bytes);
            var path=store.Resolve("native-controls/"+id+".json");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            try {await using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);await stream.WriteAsync(bytes);}
            catch(IOException) when(System.IO.File.Exists(path)) { }
            Load(store,id);return Ok(reference.Describe(id));
        }
        catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException)
        {return BadRequest(new {error=e.Message});}
    }
}
