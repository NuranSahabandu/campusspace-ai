using CampusSpace.Api.Dtos.Clubs;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusSpace.Api.Controllers;

/// <summary>Clubs and representatives (§9 Shared). Any signed-in user reads active clubs; Admins manage them.</summary>
[ApiController]
[Route("api/clubs")]
[Authorize]
public class ClubsController(IClubService clubs) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<ClubDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<ClubDto>>> List([FromQuery] ClubsQuery query, CancellationToken ct)
        => Ok(await clubs.ListAsync(query, ct));

    [HttpGet("{id:long}")]
    [ProducesResponseType<ClubDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClubDetailDto>> Get(long id, CancellationToken ct)
        => await clubs.GetAsync(id, ct) is { } club ? Ok(club) : NotFound();

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ClubDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClubDetailDto>> Create(CreateClubRequest request, CancellationToken ct)
    {
        var club = await clubs.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = club.Id }, club);
    }

    [HttpPut("{id:long}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ClubDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClubDetailDto>> Update(long id, UpdateClubRequest request, CancellationToken ct)
        => await clubs.UpdateAsync(id, request, ct) is { } club ? Ok(club) : NotFound();

    [HttpPost("{id:long}/members")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ClubDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClubDetailDto>> AddMember(long id, AddClubMemberRequest request, CancellationToken ct)
        => await clubs.AddMemberAsync(id, request, ct) is { } club
            ? CreatedAtAction(nameof(Get), new { id }, club)
            : NotFound();

    [HttpDelete("{id:long}/members/{userId:long}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveMember(long id, long userId, CancellationToken ct)
        => await clubs.RemoveMemberAsync(id, userId, ct) ? NoContent() : NotFound();

    [HttpPut("{id:long}/representative")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ClubDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClubDetailDto>> SetRepresentative(long id, SetRepresentativeRequest request, CancellationToken ct)
        => await clubs.SetRepresentativeAsync(id, request, ct) is { } club ? Ok(club) : NotFound();
}
