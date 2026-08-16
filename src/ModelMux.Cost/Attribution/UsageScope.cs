namespace ModelMux.Cost.Attribution;

/// <summary>
/// Attribution values applied to usage records produced inside a scope.
/// </summary>
public sealed class UsageAttribution
{
    /// <summary>Tenant to bill calls in this scope to.</summary>
    public string? TenantId { get; init; }

    /// <summary>Feature or route name for calls in this scope.</summary>
    public string? Feature { get; init; }

    /// <summary>End user who triggered the calls in this scope.</summary>
    public string? UserId { get; init; }
}

/// <summary>
/// Supplies the attribution applied to each recorded call.
/// </summary>
/// <remarks>
/// The default implementation reads an ambient <see cref="AsyncLocal{T}"/> scope. Replace it
/// in DI when your app already resolves the current tenant some other way â€” for example a
/// multi-tenant SaaS host that has an <c>ITenantContext</c> â€” so ModelMux.Cost attributes usage
/// without callers having to open a scope by hand.
/// </remarks>
public interface IUsageAttributionAccessor
{
    /// <summary>Attribution to apply to calls happening right now, or null when there is none.</summary>
    UsageAttribution? Current { get; }
}

/// <summary>
/// Ambient attribution scope. Values flow to every LLM call made inside the scope,
/// including across <c>await</c> boundaries, and are restored when the scope is disposed.
/// </summary>
/// <example>
/// <code>
/// using (UsageScope.Begin(tenantId: "acme", feature: "invoice-extraction"))
/// {
///     await _chatClient.GetResponseAsync(messages);
/// }
/// </code>
/// </example>
public static class UsageScope
{
    private static readonly AsyncLocal<UsageAttribution?> Ambient = new();

    /// <summary>The attribution currently in effect, or null when no scope is open.</summary>
    public static UsageAttribution? Current => Ambient.Value;

    /// <summary>
    /// Opens a scope. Values left null inherit from the enclosing scope, so an inner scope
    /// can set a feature without restating the tenant.
    /// </summary>
    public static IDisposable Begin(string? tenantId = null, string? feature = null, string? userId = null)
    {
        var previous = Ambient.Value;

        Ambient.Value = new UsageAttribution
        {
            TenantId = tenantId ?? previous?.TenantId,
            Feature = feature ?? previous?.Feature,
            UserId = userId ?? previous?.UserId,
        };

        return new Restore(previous);
    }

    private sealed class Restore(UsageAttribution? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Ambient.Value = previous;
        }
    }
}

/// <summary>Default accessor, backed by <see cref="UsageScope"/>.</summary>
public sealed class AmbientUsageAttributionAccessor : IUsageAttributionAccessor
{
    /// <inheritdoc />
    public UsageAttribution? Current => UsageScope.Current;
}
