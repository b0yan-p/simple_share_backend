namespace simpleShare_backend.Contracts.Auth
{
    public class ConfirmEmailResponse
    {
        public required bool Confirmed { get; set; }

        /// <summary>
        /// True when the address was already confirmed before this call. The
        /// endpoint is idempotent on purpose - a second click on the same link
        /// is a success, not an error.
        /// </summary>
        public bool AlreadyConfirmed { get; set; }
    }
}
