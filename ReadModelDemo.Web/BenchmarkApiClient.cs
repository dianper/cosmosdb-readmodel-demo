namespace ReadModelDemo.Web;

public class BenchmarkApiClient(HttpClient httpClient)
{
    public async Task<BenchmarkResponse> RunBenchmarkAsync(
        string tenantId,
        int page,
        int pageSize,
        int iterations,
        CancellationToken cancellationToken = default)
    {
        var url = $"/benchmark?tenantId={Uri.EscapeDataString(tenantId)}&page={page}&pageSize={pageSize}&iterations={iterations}";
        var response = await httpClient.GetFromJsonAsync<BenchmarkResponse>(url, cancellationToken);
        return response ?? new BenchmarkResponse(tenantId, iterations, page, pageSize, [], DateTimeOffset.UtcNow);
    }
}

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
