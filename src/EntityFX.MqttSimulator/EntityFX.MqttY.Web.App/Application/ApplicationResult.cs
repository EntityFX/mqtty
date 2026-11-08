namespace EntityFX.MqttY.Web.App.Application;

public enum ApplicationStatus
{
    Ok,
    Accepted,
    NoContent,
    BadRequest,
    NotFound,
    Conflict
}

/// <summary>Typed result of an application handler; controllers only map it to HTTP.</summary>
public readonly record struct ApplicationResult<T>(ApplicationStatus Status, T? Value, string? Error)
{
    public static ApplicationResult<T> Ok(T value) => new(ApplicationStatus.Ok, value, null);
    public static ApplicationResult<T> Accepted(T value) => new(ApplicationStatus.Accepted, value, null);
    public static ApplicationResult<T> NoContent() => new(ApplicationStatus.NoContent, default, null);
    public static ApplicationResult<T> BadRequest(string error) => new(ApplicationStatus.BadRequest, default, error);
    public static ApplicationResult<T> NotFound(string? error = null) => new(ApplicationStatus.NotFound, default, error);
    public static ApplicationResult<T> Conflict(string error) => new(ApplicationStatus.Conflict, default, error);
}
