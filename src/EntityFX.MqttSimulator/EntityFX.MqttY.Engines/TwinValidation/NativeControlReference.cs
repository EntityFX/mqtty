using System.Security.Cryptography;
using System.Text.Json;
using EntityFX.MqttY.Plugin.Mqtt.Experiments;

namespace EntityFX.MqttY.Engines.TwinValidation;

/// <summary>Imported control metadata; does not certify the truth of uploaded primary observations.</summary>
public sealed class NativeControlReference
{
    private readonly JsonElement data;
    private NativeControlReference(JsonElement data) => this.data=data;
    public string Name => Text(data,"name");
    public string Broker => Text(data,"broker");
    public string ProfileSha256 => Text(data,"profileSha256");
    public JsonElement Workload => data.GetProperty("workload");
    public const string Scope="native control after profile freeze";

    public static NativeControlReference Parse(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var document=JsonDocument.Parse(bytes);var d=document.RootElement;
            Fields(d,"schemaVersion","artifactType","name","broker","profileSha256","calibrationCampaignId","controlCampaignId","profileFrozenUtc","programSha256","auditSha256","workload","runs");
            Require(d.GetProperty("schemaVersion").GetInt32()==1 && Text(d,"artifactType")=="mqtt-native-control-v1","Неизвестный формат натурного контроля.");
            Text(d,"name");Require(Text(d,"broker") is "Mosquitto" or "Aedes" or "ActiveMQ" or "EMQX","Неизвестный брокер.");
            foreach(var key in new[]{"profileSha256","programSha256","auditSha256"}) Hash(Text(d,key));
            Require(Text(d,"calibrationCampaignId")!=Text(d,"controlCampaignId"),"Контроль и калибровка должны быть разными сериями.");
            var frozen=d.GetProperty("profileFrozenUtc").GetDateTimeOffset();var w=d.GetProperty("workload");
            Fields(w,"payloadBytes","qos","publishers","subscribers","warmupSeconds","measurementSeconds","drainSeconds","loadPolicy");
            Require(w.GetProperty("payloadBytes").GetInt32()==16 && w.GetProperty("qos").GetInt32() is 1 or 2 && w.GetProperty("publishers").GetInt32()>0 && w.GetProperty("subscribers").GetInt32()==1 && Text(w,"loadPolicy")=="closedLoopPerPublisher","Неподдерживаемая нагрузка натурного контроля.");
            Require(Number(w,"warmupSeconds")>=0 && Number(w,"measurementSeconds")>0 && Number(w,"drainSeconds")>0,"Некорректные окна контроля.");
            var runs=d.GetProperty("runs");Require(runs.GetArrayLength()==3,"Нужны три отдельных натурных повтора.");
            var ids=new HashSet<string>();var publications=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var r in runs.EnumerateArray())
            {
                Fields(r,"id","stage","measurementStartedUtc","publicationsSha256","deliveriesSha256","phasesSha256","attempted","completed","failed","completedIdsDelivered","pending","measurementSeconds","p99PublishMs");
                Require(ids.Add(Text(r,"id")) && Text(r,"stage")=="control","Повторы должны быть уникальными контрольными измерениями.");
                Require(r.GetProperty("measurementStartedUtc").GetDateTimeOffset()>frozen,"Контроль должен начинаться после фиксации профиля.");
                foreach(var key in new[]{"publicationsSha256","deliveriesSha256","phasesSha256"}) Hash(Text(r,key));
                Require(publications.Add(Text(r,"publicationsSha256")),"Повторно использован один журнал публикаций.");
                var a=r.GetProperty("attempted").GetInt64();var c=r.GetProperty("completed").GetInt64();var f=r.GetProperty("failed").GetInt64();var delivered=r.GetProperty("completedIdsDelivered").GetInt64();
                Require(a>0 && c>0 && f>=0 && checked(c+f)==a && delivered>=0 && delivered<=c && r.GetProperty("pending").GetInt64()==0,"Не разрешены исходы или неверны счётчики контроля.");
                Require(Math.Abs(Number(r,"measurementSeconds")-Number(w,"measurementSeconds"))<=1e-6 && Number(r,"p99PublishMs")>=0,"Неверный знаменатель времени или p99.");
            }
            return new NativeControlReference(d.Clone());
        }
        catch(Exception e) when(e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new InvalidDataException("Некорректный пакет натурного контроля: "+e.Message,e); }
    }

    public void ValidateFor(TwinValidationOptions options,byte[] profileBytes)
    {
        Require(string.Equals(ProfileSha256,options.ProfileSha256,StringComparison.OrdinalIgnoreCase),"Контроль относится к другому SHA профиля.");
        Require(Broker==options.Broker && Workload.GetProperty("payloadBytes").GetInt32()==options.PayloadBytes && Workload.GetProperty("qos").GetInt32()==options.Qos && Workload.GetProperty("publishers").GetInt32()==options.Publishers,"Брокер, QoS, payload или число отправителей не совпадают с контролем.");
        foreach(var pair in new[]{("warmupSeconds",options.WarmupSeconds),("measurementSeconds",options.MeasurementSeconds),("drainSeconds",options.DrainSeconds)})
            Require(Math.Abs(Number(Workload,pair.Item1)-pair.Item2)<=1e-12,"Окна модели и натурного контроля должны совпадать. Примените условия контроля.");
        try
        {
            using var doc=JsonDocument.Parse(profileBytes);var profile=doc.RootElement;
            if(profile.GetProperty("schemaVersion").GetInt32()==4) profile=profile.GetProperty("calibration");
            var observations=profile.GetProperty("provenance").GetProperty("observations");
            Require(Text(observations.GetProperty("identity"),"campaignId")==Text(data,"calibrationCampaignId"),"Происхождение контроля не соответствует калибровочной серии профиля.");
            var calibrationHashes=observations.GetProperty("inputSha256").EnumerateObject().Select(p=>p.Value.GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Require(!data.GetProperty("runs").EnumerateArray().Any(r=>calibrationHashes.Contains(Text(r,"publicationsSha256"))),"Журнал калибровки нельзя использовать как независимый контроль.");
        }
        catch(Exception e) when(e is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new InvalidDataException("Профиль не содержит проверяемого происхождения калибровки.",e); }
    }

    public object Describe(string id) => new { id,sha256=id,name=Name,broker=Broker,profileSha256=ProfileSha256,workload=Workload,referenceRuns=3,referenceScope=Scope,controlCampaignId=Text(data,"controlCampaignId") };

    public JsonElement Compare(string directory,string referenceSha256)
    {
        var cohort=NativeControlCohort.Read(directory);var runs=data.GetProperty("runs").EnumerateArray().ToArray();
        var means=new Dictionary<string,double> {
            ["rps"]=runs.Average(r=>r.GetProperty("completed").GetInt64()/Number(r,"measurementSeconds")),
            ["p99"]=runs.Average(r=>Number(r,"p99PublishMs")),
            ["failure"]=runs.Average(r=>(double)r.GetProperty("failed").GetInt64()/r.GetProperty("attempted").GetInt64()),
            ["loss"]=runs.Average(r=>1-(double)r.GetProperty("completedIdsDelivered").GetInt64()/r.GetProperty("completed").GetInt64()) };
        var metrics=new Dictionary<string,AcceptanceCriterionResult>();
        foreach(var (key,limit) in new[]{("rps",.15),("p99",.20),("failure",.05),("loss",.05)})
        {
            var v=cohort.GetProperty("metrics").GetProperty(key);double? model=v.ValueKind==JsonValueKind.Null?null:v.GetDouble();var reference=means[key];
            double? error=model is null || (key is "rps" or "p99" && reference==0)?null:key is "rps" or "p99"?Math.Abs(model.Value-reference)/reference:Math.Abs(model.Value-reference);
            metrics[key]=new AcceptanceCriterionResult(key,reference,model,error,limit,error is null?"notApplicable":error<=limit?"passed":"failed");
        }
        return JsonSerializer.SerializeToElement(new {referenceKind="nativeControl",referenceName=Name,referenceSha256,profileSha256=ProfileSha256,referenceRuns=3,
            calibrationCampaignId=Text(data,"calibrationCampaignId"),controlCampaignId=Text(data,"controlCampaignId"),metrics,cohort,
            allCriteriaPassed=cohort.GetProperty("outcomesSettled").GetBoolean() && metrics.Values.All(m=>m.Status=="passed"),
            scope="Отдельный натурный контроль после фиксации профиля; не доказательство слепой независимости всей разработки.",
            sourceVerification="Проверены формат, SHA и связность метаданных; первичные натурные журналы пересчитываются вне Web." },new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true});
    }

    public static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static double Number(JsonElement d,string key) {var n=d.GetProperty(key).GetDouble();Require(double.IsFinite(n),"Число должно быть конечным.");return n;}
    private static string Text(JsonElement d,string key) {var v=d.GetProperty(key).GetString();Require(!string.IsNullOrWhiteSpace(v)&&v.Length<=250,"Пустая или слишком длинная строка: "+key);return v!;}
    private static void Hash(string h) => Require(h.Length==64 && h.All(Uri.IsHexDigit),"Некорректный SHA-256.");
    private static void Require(bool ok,string error) {if(!ok)throw new InvalidDataException(error);}
    private static void Fields(JsonElement d,params string[] keys) => Require(d.ValueKind==JsonValueKind.Object && d.EnumerateObject().Select(p=>p.Name).OrderBy(x=>x).SequenceEqual(keys.OrderBy(x=>x)),"Отсутствуют или неизвестны поля пакета.");
}
