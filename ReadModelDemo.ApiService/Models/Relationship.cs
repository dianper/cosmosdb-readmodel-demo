using Newtonsoft.Json;

namespace ReadModelDemo.ApiService.Models;

public class Relationship
{
    [JsonProperty("id")]
    public string Id { get; set; } = default!;

    [JsonProperty("tenantId")]
    public string TenantId { get; set; } = default!;

    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = default!;

    [JsonProperty("identityId")]
    public string IdentityId { get; set; } = default!;

    [JsonProperty("orgUnitId")]
    public string OrgUnitId { get; set; } = default!;
}
