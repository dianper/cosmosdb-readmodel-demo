using Newtonsoft.Json;

namespace ReadModelDemo.ApiService.Models;

public class Identity
{
    [JsonProperty("id")]
    public string Id { get; set; } = default!;

    [JsonProperty("tenantId")]
    public string TenantId { get; set; } = default!;

    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = default!;
}
