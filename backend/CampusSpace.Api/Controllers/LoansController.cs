using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Loans;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusSpace.Api.Controllers;

/// <summary>
/// Equipment loans (§9 Component B, UC09–UC12). Lab Technicians hand equipment over and take it back; Facilities
/// Officers can also read. Damage photos are served only through GET {id}/photo, never as a static URL.
/// </summary>
[ApiController]
[Route("api/loans")]
[Authorize(Roles = $"{Roles.LabTechnician},{Roles.FacilitiesOfficer}")]
public class LoansController(ILoanService loans) : ControllerBase
{
    /// <summary>The largest check-in body accepted: the 5 MB photo plus the form fields. Larger bodies get 413.</summary>
    private const long MaxCheckInBodyBytes = 6 * 1024 * 1024;

    [HttpGet("today")]
    [ProducesResponseType<IReadOnlyList<HandoverDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<HandoverDto>>> Today(CancellationToken ct) => Ok(await loans.TodayAsync(ct));

    /// <summary>?overdue=true lists open loans past their due time, newest due first.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<LoanDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<LoanDto>>> List([FromQuery] LoansQuery query, CancellationToken ct)
        => Ok(await loans.ListAsync(query, ct));

    [HttpGet("{id:long}")]
    [ProducesResponseType<LoanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LoanDto>> Get(long id, CancellationToken ct)
        => await loans.GetAsync(id, ct) is { } loan ? Ok(loan) : NotFound();

    [HttpPost("checkout")]
    [Authorize(Roles = Roles.LabTechnician)]
    [ProducesResponseType<LoanDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LoanDto>> Checkout(CheckoutRequest request, CancellationToken ct)
    {
        var loan = await loans.CheckoutAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = loan.Id }, loan);
    }

    /// <summary>Multipart: condition (Good|MinorWear|Damaged), note, photo (JPEG/PNG ≤ 5 MB). Damaged needs a note and a photo.</summary>
    [HttpPost("{id:long}/checkin")]
    [Authorize(Roles = Roles.LabTechnician)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxCheckInBodyBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxCheckInBodyBytes)]
    [ProducesResponseType<LoanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LoanDto>> CheckIn(long id, [FromForm] CheckInRequest request, CancellationToken ct)
        => await loans.CheckInAsync(id, request, ct) is { } loan ? Ok(loan) : NotFound();

    [HttpGet("{id:long}/photo")]
    [ProducesResponseType<FileStreamResult>(StatusCodes.Status200OK, "image/jpeg", "image/png")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Photo(long id, CancellationToken ct)
    {
        if (await loans.GetPhotoAsync(id, ct) is not { } photo)
            return NotFound();
        // The content type comes from the verified magic bytes; nosniff stops a browser from guessing another one.
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(photo.Content, photo.ContentType);
    }
}
