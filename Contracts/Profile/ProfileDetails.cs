namespace simpleShare_backend.Contracts.Profile;

public sealed record ProfileResponse(string FirstName, string LastName, string DisplayName, bool HasPassword);

public sealed record ProfileDetails(string FirstName, string LastName, string DisplayName);
