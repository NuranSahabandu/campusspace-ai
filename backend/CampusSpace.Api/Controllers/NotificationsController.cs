using CampusSpace.Api.Dtos.Notifications;
using CampusSpace.Api.Models;
using CampusSpace.Api.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusSpace.Api.Controllers;

/// <summary>Email status per request (UC27, Task 5.2). Facilities Officers only; read-only (there is no resend).</summary>
[ApiController]
[Authorize(Roles = Roles.FacilitiesOfficer)]
public class NotificationsController(INotificationLogService notifications) : ControllerBase
{
    /// <summary>The request's emails, newest first.</summary>
    [HttpGet("api/booking-requests/{requestId:long}/notifications")]
    [ProducesResponseType<IReadOnlyList<NotificationLogDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<NotificationLogDto>>> ForRequest(long requestId, CancellationToken ct)
        => await notifications.ListForRequestAsync(requestId, ct) is { } list ? Ok(list) : NotFound();
}
