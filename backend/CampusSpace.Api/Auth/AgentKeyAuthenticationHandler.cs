using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using CampusSpace.Api.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CampusSpace.Api.Auth;

/// <summary>
/// Authenticates the agent service by its X-Agent-Key header. A missing or blank header is "no result"; a wrong key
/// fails. Either way the challenge is a bare 401, which UseStatusCodePages turns into the same Problem Details as a
/// JWT 401. The key is never logged.
/// </summary>
public sealed class AgentKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<AgentToolsOptions> agentTools) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = Request.Headers[AgentKeyDefaults.HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(presented))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!KeysMatch(presented, agentTools.Value.Key))
            return Task.FromResult(AuthenticateResult.Fail("Invalid agent key."));

        var identity = new ClaimsIdentity([new Claim(AgentKeyDefaults.ClaimType, AgentKeyDefaults.ClaimValue)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    /// <summary>Constant time: both sides are hashed first, so neither the content nor the length of the key leaks.</summary>
    public static bool KeysMatch(string presented, string expected) => CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(Encoding.UTF8.GetBytes(presented)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
