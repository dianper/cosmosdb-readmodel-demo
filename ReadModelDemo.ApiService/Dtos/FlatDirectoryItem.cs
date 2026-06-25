namespace ReadModelDemo.ApiService.Dtos;

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

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize
);

public record BenchmarkExecutionSummary(
    string Strategy,
    double MinElapsedMs,
    double AvgElapsedMs,
    double P95ElapsedMs,
    double MinRequestUnits,
    double AvgRequestUnits,
    double P95RequestUnits,
    string Complexity,
    int ResultItems,
    int IdentityReads,
    int RelationshipReads,
    int OrgUnitReads
);

public record BenchmarkResponse(
    string TenantId,
    int Iterations,
    int Page,
    int PageSize,
    IReadOnlyList<BenchmarkExecutionSummary> Results,
    DateTimeOffset ExecutedAtUtc
);

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
