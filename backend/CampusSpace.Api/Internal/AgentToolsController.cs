using CampusSpace.Api.Auth;
using CampusSpace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusSpace.Api.Internal;

/// <summary>
/// Read-only tool routes for the agent service (§9 "Internal read-only tool endpoints", §7.1 rule 3). X-Agent-Key only
/// (policy AgentTools): a user JWT gets a 401 here, and the agent key gets a 401 on /api. Hidden from Swagger. Every
/// route is a GET except POST quote, which calculates and never saves. Errors are Problem Details (400/404), which the
/// Python tools turn into TOOL_ERROR observations.
/// </summary>
[ApiController]
[Route("internal/agent-tools")]
[Authorize(Policy = AgentKeyDefaults.Policy)]
public class AgentToolsController(IPolicySettingsService policy) : ControllerBase
{
    /// <summary>
    /// The full policy snapshot (addendum A.1/A.2): every key with its typed value, the same as
    /// GET /api/policy-settings/public. The agent service reads it once at the start of a run.
    /// </summary>
    [HttpGet("policy")]
    [ProducesResponseType<IReadOnlyDictionary<string, object>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyDictionary<string, object?>>> Policy(CancellationToken ct)
        => Ok((await policy.GetAsync(ct)).ToPublicValues());
}
