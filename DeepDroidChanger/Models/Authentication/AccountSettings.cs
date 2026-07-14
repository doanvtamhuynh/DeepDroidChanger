namespace DeepDroidChanger.Models
{
    public sealed class AccountSettings
    {
        public bool RememberAccount { get; set; }
        public string Username { get; set; } = string.Empty;
        public string ProtectedPassword { get; set; } = string.Empty;
        public string Entropy { get; set; } = string.Empty;
        public DateTimeOffset? LastLoginUtc { get; set; }
    }
}
