namespace simpleShare_backend.Contracts.Auth
{
    /// <summary>
    /// What registration answers with instead of a session: the account exists
    /// but cannot be used until the address is confirmed, so there is no token
    /// to hand out. The client shows a "check your email" screen off this.
    /// </summary>
    public class RegisterResponse
    {
        public required string Email { get; set; }

        /// <summary>Always true today; kept explicit so the client never infers it from a missing token.</summary>
        public bool EmailVerificationRequired { get; set; } = true;
    }
}
