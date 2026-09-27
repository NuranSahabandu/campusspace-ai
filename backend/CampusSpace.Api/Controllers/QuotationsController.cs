using CampusSpace.Api.Dtos.Quotations;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusSpace.Api.Controllers;

/// <summary>
/// Quotations (§9 Component D). Prices come only from IQuotationCalculator. Requesters read the quotes of their own
/// requests; Facilities Officers read all. No endpoint creates quotes yet (the Phase 3 poller saves the Draft).
/// </summary>
[ApiController]
[Route("api/quotations")]
[Authorize(Roles = $"{Roles.Student},{Roles.Lecturer},{Roles.FacilitiesOfficer}")]
public class QuotationsController(IQuotationService quotations) : ControllerBase
{
    /// <summary>
    /// Prices a room slot and equipment without saving anything. Students and Lecturers are priced as their own role;
    /// an Officer must give requesterRole (Student or Lecturer).
    /// </summary>
    [HttpPost("preview")]
    [ProducesResponseType<QuotationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuotationDto>> Preview(QuotePreviewRequest request, CancellationToken ct)
        => await quotations.PreviewAsync(request, ct) is { } quote ? Ok(quote) : NotFound();

    [HttpGet("{id:long}")]
    [ProducesResponseType<QuotationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuotationDto>> Get(long id, CancellationToken ct)
        => await quotations.GetAsync(id, ct) is { } quote ? Ok(quote) : NotFound();

    /// <summary>The request's live (Draft or Issued) quote; 404 when it has none.</summary>
    [HttpGet("~/api/booking-requests/{requestId:long}/quotation")]
    [ProducesResponseType<QuotationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuotationDto>> GetForRequest(long requestId, CancellationToken ct)
        => await quotations.GetForRequestAsync(requestId, ct) is { } quote ? Ok(quote) : NotFound();
}
