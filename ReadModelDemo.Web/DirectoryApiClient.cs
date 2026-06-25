namespace ReadModelDemo.Web;

public class DirectoryApiClient(HttpClient httpClient)
{
    public async Task<DirectoryQueryResult?> GetDirectoryAsync(
        string tenantId,
        int page,
        int pageSize,
        string strategy,
        CancellationToken cancellationToken = default)
    {
        var url = $"/readmodel?tenantId={Uri.EscapeDataString(tenantId)}&page={page}&pageSize={pageSize}&strategy={Uri.EscapeDataString(strategy)}";
        return await httpClient.GetFromJsonAsync<DirectoryQueryResult>(url, cancellationToken);
    }
}

public record DirectoryQueryResult(
    PagedResult<FlatDirectoryItem> Data,
    string Strategy,
    string Complexity,
    double ElapsedMs,
    double RequestUnits,
    int IdentityReads,
    int RelationshipReads,
    int OrgUnitReads
);

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize
);

public record FlatDirectoryItem(
    string RowType,
    string? IdentityId,
    string? IdentityDisplayName,
    string? RelationshipId,
    string? RelationshipDisplayName,
    string? OrgUnitId,
    string? OrgUnitDisplayName,
    string? ManagerId,
    string? ManagerDisplayName
);
