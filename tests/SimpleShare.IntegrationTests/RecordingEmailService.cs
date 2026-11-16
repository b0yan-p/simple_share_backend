using System.Collections.Concurrent;
using simpleShare_backend.Services.Interfaces;

namespace SimpleShare.IntegrationTests;

/// <summary>Test-only transport: never creates a network client.</summary>
public sealed class RecordingEmailService : IEmailService
{
    public sealed record Message(string Email, string FirstName, string VerificationUrl);
    private readonly ConcurrentQueue<Message> messages = new();
    private readonly ConcurrentDictionary<string, byte> failures = new(StringComparer.OrdinalIgnoreCase);

    public Message[] For(string email) => messages.Where(x => string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase)).ToArray();
    public void FailFor(string email) => failures[email] = 0;
    public void RecoverFor(string email) => failures.TryRemove(email, out _);

    public Task SendVerificationEmailAsync(string email, string firstName, string verificationUrl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (failures.ContainsKey(email)) throw new InvalidOperationException("Simulated email transport failure.");
        messages.Enqueue(new(email, firstName, verificationUrl));
        return Task.CompletedTask;
    }
}
