#pragma warning disable ASPIRECOSMOSDB001

var builder = DistributedApplication.CreateBuilder(args);

var cosmos = builder.AddAzureCosmosDB("cosmos")
    .WithHttpEndpoint(port: 8081, targetPort: 8081, name: "cosmos-http", isProxied: false)
    .RunAsPreviewEmulator(emulator =>
    {
        emulator.WithContainerName("cosmos-readmodel");
        emulator.WithDataExplorer();
        emulator.WithLifetime(ContainerLifetime.Persistent);
        emulator.WithArgs("--port", "8081");
    });

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
