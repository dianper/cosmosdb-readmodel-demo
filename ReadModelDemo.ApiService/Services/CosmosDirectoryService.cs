using Microsoft.Azure.Cosmos;
using ReadModelDemo.ApiService.Dtos;
using ReadModelDemo.ApiService.Models;

namespace ReadModelDemo.ApiService.Services;

public class CosmosDirectoryService(CosmosClient cosmosClient)
{
    private const string DatabaseName = "directory-db";
    private const string IdentityContainerName = "identity";
    private const string RelationshipContainerName = "relationship";
    private const string OrgUnitContainerName = "orgUnit";

    private const string StrategyFanOut = "fanout";
    private const string StrategyTwoPhase = "two-phase";

    public async Task<DirectoryQueryResult> GetFlatDirectoryAsync(
        string tenantId,
        int page,
        int pageSize,
        string strategy = StrategyFanOut,
        CancellationToken cancellationToken = default)
    {
        var normalized = strategy.Trim().ToLowerInvariant();
        return normalized switch
        {
            StrategyTwoPhase => await ExecuteTwoPhaseAsync(tenantId, page, pageSize, cancellationToken),
            _ => await ExecuteFanOutAsync(tenantId, page, pageSize, cancellationToken)
        };
    }

    public async Task<BenchmarkResponse> RunBenchmarkAsync(
        string tenantId,
        int page,
        int pageSize,
        int iterations,
        CancellationToken cancellationToken = default)
    {
        var totalIterations = Math.Max(1, iterations);
        var strategyResults = new List<BenchmarkExecutionSummary>(2);

        var fanOutSamples = new List<DirectoryQueryResult>(totalIterations);
        for (var i = 0; i < totalIterations; i++)
        {
            fanOutSamples.Add(await ExecuteFanOutAsync(tenantId, page, pageSize, cancellationToken));
        }

        var twoPhaseSamples = new List<DirectoryQueryResult>(totalIterations);
        for (var i = 0; i < totalIterations; i++)
        {
            twoPhaseSamples.Add(await ExecuteTwoPhaseAsync(tenantId, page, pageSize, cancellationToken));
        }

        strategyResults.Add(Summarize(StrategyFanOut, fanOutSamples));
        strategyResults.Add(Summarize(StrategyTwoPhase, twoPhaseSamples));

        return new BenchmarkResponse(
            TenantId: tenantId,
            Iterations: totalIterations,
            Page: page,
            PageSize: pageSize,
            Results: strategyResults,
            ExecutedAtUtc: DateTimeOffset.UtcNow);
    }

    private async Task<DirectoryQueryResult> ExecuteFanOutAsync(
        string tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var identityContainer = cosmosClient.GetContainer(DatabaseName, IdentityContainerName);
        var relationshipContainer = cosmosClient.GetContainer(DatabaseName, RelationshipContainerName);
        var orgUnitContainer = cosmosClient.GetContainer(DatabaseName, OrgUnitContainerName);

        var queryOptions = new QueryRequestOptions { PartitionKey = new PartitionKey(tenantId) };
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var identitiesTask = FetchAllByTenantAsync<Identity>(identityContainer, tenantId, queryOptions, cancellationToken);
        var relationshipsTask = FetchAllByTenantAsync<Relationship>(relationshipContainer, tenantId, queryOptions, cancellationToken);
        var orgUnitsTask = FetchAllByTenantAsync<OrgUnit>(orgUnitContainer, tenantId, queryOptions, cancellationToken);

        await Task.WhenAll(identitiesTask, relationshipsTask, orgUnitsTask);

        var identities = await identitiesTask;
        var relationships = await relationshipsTask;
        var orgUnits = await orgUnitsTask;

        var allRows = ComposeFlattenedRows(identities.Items, relationships.Items, orgUnits.Items);
        var paged = ApplyPaging(allRows, page, pageSize);

        stopwatch.Stop();

        return new DirectoryQueryResult(
            Data: paged,
            Strategy: StrategyFanOut,
            Complexity: "O(I + R + O)",
            ElapsedMs: stopwatch.Elapsed.TotalMilliseconds,
            RequestUnits: identities.RequestUnits + relationships.RequestUnits + orgUnits.RequestUnits,
            IdentityReads: identities.DocsRead,
            RelationshipReads: relationships.DocsRead,
            OrgUnitReads: orgUnits.DocsRead);
    }

    private async Task<DirectoryQueryResult> ExecuteTwoPhaseAsync(
        string tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var identityContainer = cosmosClient.GetContainer(DatabaseName, IdentityContainerName);
        var relationshipContainer = cosmosClient.GetContainer(DatabaseName, RelationshipContainerName);
        var orgUnitContainer = cosmosClient.GetContainer(DatabaseName, OrgUnitContainerName);

        var queryOptions = new QueryRequestOptions { PartitionKey = new PartitionKey(tenantId) };
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var relationshipPage = await FetchRelationshipsPageAsync(
            relationshipContainer,
            tenantId,
            page,
            pageSize,
            queryOptions,
            cancellationToken);

        var identityIds = relationshipPage.Items
            .Select(x => x.IdentityId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var orgUnitIds = relationshipPage.Items
            .Select(x => x.OrgUnitId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var identities = await FetchByIdsAsync<Identity>(identityContainer, tenantId, identityIds, queryOptions, cancellationToken);
        var orgUnits = await FetchByIdsAsync<OrgUnit>(orgUnitContainer, tenantId, orgUnitIds, queryOptions, cancellationToken);

        var managerIds = orgUnits.Items
            .Select(x => x.ManagerId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .Except(identityIds, StringComparer.Ordinal)
            .ToList();

        var managerIdentities = await FetchByIdsAsync<Identity>(identityContainer, tenantId, managerIds, queryOptions, cancellationToken);
        var allIdentities = identities.Items.Concat(managerIdentities.Items).DistinctBy(x => x.Id).ToList();

        var rows = ComposeRelationshipRows(allIdentities, relationshipPage.Items, orgUnits.Items);
        var paged = new PagedResult<FlatDirectoryItem>(
            Items: rows,
            TotalCount: relationshipPage.TotalCount,
            Page: page,
            PageSize: pageSize);

        stopwatch.Stop();

        return new DirectoryQueryResult(
            Data: paged,
            Strategy: StrategyTwoPhase,
            Complexity: "O(R_page + I_batch + O_batch)",
            ElapsedMs: stopwatch.Elapsed.TotalMilliseconds,
            RequestUnits: relationshipPage.RequestUnits + identities.RequestUnits + managerIdentities.RequestUnits + orgUnits.RequestUnits,
            IdentityReads: identities.DocsRead + managerIdentities.DocsRead,
            RelationshipReads: relationshipPage.DocsRead,
            OrgUnitReads: orgUnits.DocsRead);
    }

    private static BenchmarkExecutionSummary Summarize(string strategy, List<DirectoryQueryResult> samples)
    {
        var elapsed = samples.Select(x => x.ElapsedMs).OrderBy(x => x).ToList();
        var ru = samples.Select(x => x.RequestUnits).OrderBy(x => x).ToList();

        return new BenchmarkExecutionSummary(
            Strategy: strategy,
            MinElapsedMs: elapsed.FirstOrDefault(),
            AvgElapsedMs: elapsed.Average(),
            P95ElapsedMs: Percentile(elapsed, 95),
            MinRequestUnits: ru.FirstOrDefault(),
            AvgRequestUnits: ru.Average(),
            P95RequestUnits: Percentile(ru, 95),
            Complexity: samples.First().Complexity,
            ResultItems: samples.First().Data.Items.Count,
            IdentityReads: samples.Average(x => x.IdentityReads) switch { var x => (int)Math.Round(x) },
            RelationshipReads: samples.Average(x => x.RelationshipReads) switch { var x => (int)Math.Round(x) },
            OrgUnitReads: samples.Average(x => x.OrgUnitReads) switch { var x => (int)Math.Round(x) });
    }

    private static double Percentile(List<double> orderedValues, int percentile)
    {
        if (orderedValues.Count == 0)
            return 0;

        var index = (int)Math.Ceiling((percentile / 100d) * orderedValues.Count) - 1;
        index = Math.Clamp(index, 0, orderedValues.Count - 1);
        return orderedValues[index];
    }

    private static PagedResult<FlatDirectoryItem> ApplyPaging(List<FlatDirectoryItem> rows, int page, int pageSize)
    {
        var total = rows.Count;
        var items = rows.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new PagedResult<FlatDirectoryItem>(items, total, page, pageSize);
    }

    private static List<FlatDirectoryItem> ComposeFlattenedRows(
        List<Identity> identities,
        List<Relationship> relationships,
        List<OrgUnit> orgUnits)
    {
        var relationshipRows = ComposeRelationshipRows(identities, relationships, orgUnits);

        var relationshipIdentityIds = relationships
            .Select(x => x.IdentityId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);

        var relationshipOrgUnitIds = relationships
            .Select(x => x.OrgUnitId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);

        var identityOnlyRows = identities
            .Where(x => !relationshipIdentityIds.Contains(x.Id))
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .Select(x => new FlatDirectoryItem(
                RowType: "identity-only",
                IdentityId: x.Id,
                IdentityDisplayName: x.DisplayName,
                RelationshipId: null,
                RelationshipDisplayName: null,
                OrgUnitId: null,
                OrgUnitDisplayName: null,
                ManagerId: null,
                ManagerDisplayName: null,
                AncestorOrgUnitIds: []));

        var identityMap = identities.ToDictionary(x => x.Id);
        var orgUnitMap = orgUnits.ToDictionary(x => x.Id);

        var orgUnitOnlyRows = orgUnits
            .Where(x => !relationshipOrgUnitIds.Contains(x.Id))
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .Select(x =>
            {
                identityMap.TryGetValue(x.ManagerId ?? string.Empty, out var manager);
                return new FlatDirectoryItem(
                    RowType: "orgunit-only",
                    IdentityId: null,
                    IdentityDisplayName: null,
                    RelationshipId: null,
                    RelationshipDisplayName: null,
                    OrgUnitId: x.Id,
                    OrgUnitDisplayName: x.DisplayName,
                    ManagerId: manager?.Id,
                    ManagerDisplayName: manager?.DisplayName,
                    AncestorOrgUnitIds: ResolveAncestorOrgUnitIds(x.Id, orgUnitMap));
            });

        return relationshipRows
            .Concat(identityOnlyRows)
            .Concat(orgUnitOnlyRows)
            .OrderBy(x => x.RowType, StringComparer.Ordinal)
            .ThenBy(x => x.IdentityId, StringComparer.Ordinal)
            .ThenBy(x => x.RelationshipId, StringComparer.Ordinal)
            .ThenBy(x => x.OrgUnitId, StringComparer.Ordinal)
            .ToList();
    }

    private static List<FlatDirectoryItem> ComposeRelationshipRows(
        List<Identity> identities,
        List<Relationship> relationships,
        List<OrgUnit> orgUnits)
    {
        var identityMap = identities.ToDictionary(x => x.Id);
        var orgUnitMap = orgUnits.ToDictionary(x => x.Id);

        return relationships
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .Select(x =>
            {
                identityMap.TryGetValue(x.IdentityId, out var identity);
                orgUnitMap.TryGetValue(x.OrgUnitId, out var orgUnit);
                identityMap.TryGetValue(orgUnit?.ManagerId ?? string.Empty, out var manager);

                var rowType = identity is not null && orgUnit is not null
                    ? "relationship-linked"
                    : "relationship-only";

                return new FlatDirectoryItem(
                    RowType: rowType,
                    IdentityId: identity?.Id ?? x.IdentityId,
                    IdentityDisplayName: identity?.DisplayName,
                    RelationshipId: x.Id,
                    RelationshipDisplayName: x.DisplayName,
                    OrgUnitId: orgUnit?.Id ?? x.OrgUnitId,
                    OrgUnitDisplayName: orgUnit?.DisplayName,
                    ManagerId: manager?.Id,
                    ManagerDisplayName: manager?.DisplayName,
                    AncestorOrgUnitIds: ResolveAncestorOrgUnitIds(orgUnit?.Id ?? x.OrgUnitId, orgUnitMap));
            })
            .ToList();
    }

    private static IReadOnlyList<string> ResolveAncestorOrgUnitIds(
        string? orgUnitId,
        IReadOnlyDictionary<string, OrgUnit> orgUnitMap)
    {
        if (string.IsNullOrWhiteSpace(orgUnitId))
            return [];

        var ancestors = new List<string> { orgUnitId };
        var currentId = orgUnitId;
        var visited = new HashSet<string>(StringComparer.Ordinal) { currentId };

        const int maxDepth = 64;
        for (var depth = 0; depth < maxDepth; depth++)
        {
            if (!orgUnitMap.TryGetValue(currentId, out var currentOrgUnit))
                break;

            var parentId = currentOrgUnit.ParentOrgUnitId;
            if (string.IsNullOrWhiteSpace(parentId))
                break;

            if (!visited.Add(parentId))
                break;

            ancestors.Add(parentId);
            currentId = parentId;
        }

        return ancestors;
    }

    private static async Task<ContainerRead<T>> FetchAllByTenantAsync<T>(
        Container container,
        string tenantId,
        QueryRequestOptions options,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.tenantId = @tenantId")
            .WithParameter("@tenantId", tenantId);

        using var iterator = container.GetItemQueryIterator<T>(query, requestOptions: options);

        var results = new List<T>();
        var requestUnits = 0d;
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            requestUnits += response.RequestCharge;
            results.AddRange(response);
        }

        return new ContainerRead<T>(results, requestUnits, results.Count, results.Count);
    }

    private static async Task<ContainerRead<Relationship>> FetchRelationshipsPageAsync(
        Container container,
        string tenantId,
        int page,
        int pageSize,
        QueryRequestOptions options,
        CancellationToken cancellationToken)
    {
        var totalCountQuery = new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.tenantId = @tenantId")
            .WithParameter("@tenantId", tenantId);

        using var countIterator = container.GetItemQueryIterator<int>(totalCountQuery, requestOptions: options);
        var countResponse = await countIterator.ReadNextAsync(cancellationToken);
        var totalCount = countResponse.FirstOrDefault();

        var offset = (page - 1) * pageSize;
        var pageQuery = new QueryDefinition(@"
            SELECT * FROM c
            WHERE c.tenantId = @tenantId
            ORDER BY c.id
            OFFSET @offset LIMIT @pageSize")
            .WithParameter("@tenantId", tenantId)
            .WithParameter("@offset", offset)
            .WithParameter("@pageSize", pageSize);

        using var pageIterator = container.GetItemQueryIterator<Relationship>(pageQuery, requestOptions: options);
        var results = new List<Relationship>();
        var requestUnits = countResponse.RequestCharge;

        while (pageIterator.HasMoreResults)
        {
            var response = await pageIterator.ReadNextAsync(cancellationToken);
            requestUnits += response.RequestCharge;
            results.AddRange(response);
        }

        return new ContainerRead<Relationship>(results, requestUnits, results.Count, totalCount);
    }

    private static async Task<ContainerRead<T>> FetchByIdsAsync<T>(
        Container container,
        string tenantId,
        IReadOnlyList<string> ids,
        QueryRequestOptions options,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return new ContainerRead<T>([], 0, 0, 0);

        var query = new QueryDefinition("SELECT * FROM c WHERE c.tenantId = @tenantId AND ARRAY_CONTAINS(@ids, c.id)")
            .WithParameter("@tenantId", tenantId)
            .WithParameter("@ids", ids);

        using var iterator = container.GetItemQueryIterator<T>(query, requestOptions: options);
        var results = new List<T>();
        var requestUnits = 0d;

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            requestUnits += response.RequestCharge;
            results.AddRange(response);
        }

        return new ContainerRead<T>(results, requestUnits, results.Count, results.Count);
    }

    private sealed record ContainerRead<T>(List<T> Items, double RequestUnits, int DocsRead, int TotalCount);
}
