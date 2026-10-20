namespace ProjectTracker.Web.Services.Email;

/// <summary>
/// Builds the link that opens a project inside the system from the system's public address (PublicUrlResolver:
/// Application:BaseUrl when filled in, otherwise the address the system is actually opened with) and the project
/// route (Application:ProjectPath). No address is ever written in code; without a known address there is no link.
/// </summary>
public sealed class ProjectLinkBuilder(AppConfig config, PublicUrlResolver publicUrl)
{
    /// <summary>The public address (no trailing slash), or null when none is known.</summary>
    public string? BaseUrl() => publicUrl.BaseUrl();

    public string? Build(string projectId)
    {
        var baseUrl = BaseUrl();
        return baseUrl is null ? null : baseUrl + config.ProjectPathTemplate.Replace("{id}", Uri.EscapeDataString(projectId.ToLowerInvariant()));
    }

    /// <summary>A link of the right shape for the preview; null when no address is known (the catalogue sample is used).</summary>
    public string? SampleProjectUrl() => Build(Guid.Empty.ToString());
}
