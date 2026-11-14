namespace ProductAuction.API.Services.Auth
{
    public interface IJwtTokenService
    {
        Task<AuthTokenResult> GenerateTokenAsync(Microsoft.AspNetCore.Identity.IdentityUser user);
    }

    public class AuthTokenResult
    {
        public string? Email { get; set; }
        public IList<string> Roles { get; set; } = new List<string>();
        public string Token { get; set; } = string.Empty;
        public int ExpiresInMinutes { get; set; }
    }
}
