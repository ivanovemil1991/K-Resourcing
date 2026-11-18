using Microsoft.Extensions.Options;

namespace Claims.Api;

/// <summary>Microsoft Entra ID token validation settings.</summary>
public sealed class AuthenticationOptions
{
    public bool Enabled { get; set; } = true;
    public string Authority { get; set; } = "";
    public string Audience { get; set; } = "";
}

public sealed class AuthenticationOptionsValidator(IHostEnvironment environment) : IValidateOptions<AuthenticationOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthenticationOptions options)
    {
        if (!options.Enabled)
            return environment.IsDevelopment() || environment.IsEnvironment("Test")
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail("Authentication can be disabled only in Development or Test.");

        var errors = new List<string>();
        if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out var authority) ||
            authority.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(authority.UserInfo) ||
            !string.IsNullOrEmpty(authority.Query) || !string.IsNullOrEmpty(authority.Fragment))
            errors.Add("Authentication:Authority must be an HTTPS authority URL.");
        if (string.IsNullOrWhiteSpace(options.Audience))
            errors.Add("Authentication:Audience is required.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
