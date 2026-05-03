namespace RfidSyncApi.Infrastructure.Configuration;

/// <summary>
/// Strongly typed configuration section bound from appsettings.json → ApiSettings.
/// Secrets (StaticToken) should be overridden via environment variables or
/// Azure App Service Application Settings in production.
/// </summary>
public class ApiSettings
{
    public const string SectionName = "ApiSettings";

    /// <summary>
    /// The pre-shared static token clients must supply in the X-API-TOKEN header.
    /// Override via environment variable: ApiSettings__StaticToken=your-secret-here
    /// </summary>
    public string StaticToken { get; set; } = string.Empty;

    /// <summary>Maximum records per sync batch (default 10 000).</summary>
    public int MaxBatchSize { get; set; } = 10_000;

    /// <summary>Maximum records per server_changes page in sync response (default 500).</summary>
    public int ServerChangesPageSize { get; set; } = 500;
}
