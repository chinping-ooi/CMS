namespace CMS.Services.Auth;

public sealed class JwtSettings
{
    public string Issuer { get; set; } = "CMS";
    public string Audience { get; set; } = "CMS";
    public string Key { get; set; } = string.Empty;
    public int ExpiresMinutes { get; set; } = 60;
}
