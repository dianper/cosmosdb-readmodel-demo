var builder = DistributedApplication.CreateBuilder(args);

#pragma warning disable ASPIRECOSMOSDB001
var cosmos = builder.AddAzureCosmosDB("cosmos")
    .RunAsPreviewEmulator(e => e
        .WithLifetime(ContainerLifetime.Persistent)
        .WithDataExplorer());
#pragma warning restore ASPIRECOSMOSDB001

var directoryDb = cosmos.AddCosmosDatabase("directory-db");
directoryDb.AddContainer("identity", "/tenantId");
directoryDb.AddContainer("relationship", "/tenantId");
directoryDb.AddContainer("orgUnit", "/tenantId");

var apiService = builder.AddProject<Projects.ReadModelDemo_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithReference(cosmos)
    .WaitFor(cosmos);

builder.AddProject<Projects.ReadModelDemo_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
