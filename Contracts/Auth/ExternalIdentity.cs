namespace simpleShare_backend.Contracts.Auth;

/// <summary>
/// A verified identity from an external provider (Google today). Not an HTTP
/// contract. Names are already resolved to values that fit User and Member.
/// </summary>
/// <param name="Provider">Stored as UserLogins.LoginProvider, e.g. "Google".</param>
/// <param name="ProviderKey">The provider's stable user id (Google `sub`) - unlike the email, it never changes.</param>
public sealed record ExternalIdentity(
    string Provider,
    string ProviderKey,
    string Email,
    string FirstName,
    string LastName);
