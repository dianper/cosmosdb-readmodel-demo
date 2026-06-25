using Microsoft.Azure.Cosmos;
using ReadModelDemo.ApiService.Models;

namespace ReadModelDemo.ApiService.Services;

public static class SeedData
{
    private const string DatabaseName = "directory-db";
    private const string TenantId = "tenant-demo";
    private const int TargetPerContainer = 5000;
    private const int RelatedPoolSize = 4000;

    public static async Task EnsureSeededAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var cosmosClient = scope.ServiceProvider.GetRequiredService<CosmosClient>();

        var database = await cosmosClient.CreateDatabaseIfNotExistsAsync(DatabaseName);

        var identityContainer = (await database.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties("identity", "/tenantId"))).Container;

        var relationshipContainer = (await database.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties("relationship", "/tenantId"))).Container;

        var orgUnitContainer = (await database.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties("orgUnit", "/tenantId"))).Container;

        var checkOptions = new QueryRequestOptions { PartitionKey = new PartitionKey(TenantId) };
        var identityCount = await CountByTenantAsync(identityContainer, TenantId, checkOptions);
        var relationshipCount = await CountByTenantAsync(relationshipContainer, TenantId, checkOptions);
        var orgUnitCount = await CountByTenantAsync(orgUnitContainer, TenantId, checkOptions);

        if (identityCount >= TargetPerContainer
            && relationshipCount >= TargetPerContainer
            && orgUnitCount >= TargetPerContainer)
        {
            return;
        }

        var random = new Random(42);
        var identities = new List<Identity>(TargetPerContainer);
        for (var i = 1; i <= TargetPerContainer; i++)
        {
            identities.Add(new Identity
            {
                Id = $"i-{i:D5}",
                TenantId = TenantId,
                DisplayName = $"Identity {i:D5}"
            });
        }

        var orgUnits = new List<OrgUnit>(TargetPerContainer);
        for (var i = 1; i <= TargetPerContainer; i++)
        {
            var managerIndex = random.Next(1, RelatedPoolSize + 1);
            var parentOrgUnitId = i == 1
                ? null
                : $"ou-{random.Next(1, i):D5}";

            orgUnits.Add(new OrgUnit
            {
                Id = $"ou-{i:D5}",
                TenantId = TenantId,
                DisplayName = $"OrgUnit {i:D5}",
                ManagerId = $"i-{managerIndex:D5}",
                ParentOrgUnitId = parentOrgUnitId
            });
        }

        // 80% of identities/orgUnits are in the related pool; 20% remain orphaned.
        var relationships = new List<Relationship>(TargetPerContainer);
        for (var i = 1; i <= TargetPerContainer; i++)
        {
            var identityIndex = random.Next(1, RelatedPoolSize + 1);
            var orgUnitIndex = random.Next(1, RelatedPoolSize + 1);
            relationships.Add(new Relationship
            {
                Id = $"r-{i:D5}",
                TenantId = TenantId,
                DisplayName = $"Relationship {i:D5}",
                IdentityId = $"i-{identityIndex:D5}",
                OrgUnitId = $"ou-{orgUnitIndex:D5}"
            });
        }

        var pk = new PartitionKey(TenantId);
        await UpsertInBatchesAsync(identityContainer, identities, pk);
        await UpsertInBatchesAsync(orgUnitContainer, orgUnits, pk);
        await UpsertInBatchesAsync(relationshipContainer, relationships, pk);
    }

    private static async Task<int> CountByTenantAsync(Container container, string tenantId, QueryRequestOptions options)
    {
        var countQuery = new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.tenantId = @tenantId")
            .WithParameter("@tenantId", tenantId);

        using var iterator = container.GetItemQueryIterator<int>(countQuery, requestOptions: options);
        var response = await iterator.ReadNextAsync();
        return response.FirstOrDefault();
    }

    private static async Task UpsertInBatchesAsync<T>(Container container, List<T> items, PartitionKey partitionKey)
    {
        const int batchSize = 150;
        for (var i = 0; i < items.Count; i += batchSize)
        {
            var batch = items.Skip(i).Take(batchSize)
                .Select(item => container.UpsertItemAsync(item, partitionKey));

            await Task.WhenAll(batch);
        }
    }
}
