namespace Fratak_Shop.Data.Auditing;

/// <summary>Staff member performing the current admin-panel request.</summary>
public sealed record AuditActor(Guid? UserId, string? UserName, string? IpAddress, string? Path);

/// <summary>
/// Ambient (async-local) actor of the current request. The web layer sets it for admin-panel requests;
/// <see cref="AuditSaveChangesInterceptor"/> then records every change saved during that request.
/// Storefront requests and background jobs have no actor and are not audited.
/// </summary>
public static class AuditContext
{
    private static readonly AsyncLocal<AuditActor?> CurrentActor = new();

    public static AuditActor? Current => CurrentActor.Value;

    /// <summary>Sets the actor until the returned scope is disposed.</summary>
    public static IDisposable Begin(AuditActor actor)
    {
        var previous = CurrentActor.Value;
        CurrentActor.Value = actor;
        return new Scope(previous);
    }

    private sealed class Scope(AuditActor? previous) : IDisposable
    {
        public void Dispose() => CurrentActor.Value = previous;
    }
}
