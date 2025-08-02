namespace Wolverine.Nats.Internal;

/// <summary>
/// Represents a tenant configuration for NATS transport
/// </summary>
public class NatsTenant
{
    public NatsTenant(string tenantId)
    {
        TenantId = tenantId ?? throw new ArgumentNullException(nameof(tenantId));
    }
    
    /// <summary>
    /// The unique tenant identifier
    /// </summary>
    public string TenantId { get; }
    
    /// <summary>
    /// Optional custom subject mapper for this tenant.
    /// If null, the transport's default mapper will be used.
    /// </summary>
    public ITenantSubjectMapper? SubjectMapper { get; set; }
    
    /// <summary>
    /// Optional connection string specific to this tenant.
    /// If null, the transport's default connection will be used.
    /// </summary>
    public string? ConnectionString { get; set; }
    
    /// <summary>
    /// Optional credentials file path for this tenant (for JWT/NKey auth).
    /// </summary>
    public string? CredentialsFile { get; set; }
    
    /// <summary>
    /// Optional username for this tenant.
    /// </summary>
    public string? Username { get; set; }
    
    /// <summary>
    /// Optional password for this tenant.
    /// </summary>
    public string? Password { get; set; }
    
    /// <summary>
    /// Optional token for this tenant.
    /// </summary>
    public string? Token { get; set; }
}