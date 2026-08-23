namespace SimpleShare.IntegrationTests;

/// <summary>
/// Binds <see cref="ApiFixture"/> to every test class marked
/// <c>[Collection(nameof(ApiCollection))]</c>, so one container and one host are
/// shared across the assembly instead of one per test class.
/// </summary>
[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
