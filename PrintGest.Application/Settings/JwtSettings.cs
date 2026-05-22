namespace PrintGest.Application.Settings;

public sealed class JwtSettings
{
    public string Secret { get; init; } = string.Empty;
    public string Issuer { get; init; } = "PrintGest";
    public string Audience { get; init; } = "PrintGest";
    public int ExpiryHours { get; init; } = 5;
}
