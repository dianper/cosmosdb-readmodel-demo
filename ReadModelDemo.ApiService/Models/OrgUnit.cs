using Newtonsoft.Json;

namespace ReadModelDemo.ApiService.Models;

public class OrgUnit
{
    [JsonProperty("id")]
    public string Id { get; set; } = default!;

    [JsonProperty("tenantId")]
    public string TenantId { get; set; } = default!;

    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = default!;

    [JsonProperty("managerId")]
    public string? ManagerId { get; set; }

    [JsonProperty("parentOrgUnitId")]
    public string? ParentOrgUnitId { get; set; }
}
