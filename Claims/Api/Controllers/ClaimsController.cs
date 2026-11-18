using System.ComponentModel.DataAnnotations;
using Claims.Application.Abstractions;
using Claims.Application.Claims;
using Claims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Claims.Api.Controllers;

/// <summary>HTTP endpoints for managing insurance claims.</summary>
[ApiController]
[Route("[controller]")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.Read)]
[EnableRateLimiting(RateLimitingOptions.PolicyName)]
public class ClaimsController : ControllerBase
{
    /// <summary>Returns one page of claims, ordered by id. Use <c>skip</c> and <c>take</c> (at most 100, default 50) to page.</summary>
    [HttpGet]
    public Task<IReadOnlyList<Claim>> GetAll(
        [FromServices] IQueryHandler<GetClaimsQuery, IReadOnlyList<Claim>> handler,
        CancellationToken cancellationToken,
        [FromQuery, Range(0, int.MaxValue)] int skip,
        [FromQuery, Range(1, Page.MaxTake)] int take = Page.DefaultTake) =>
        handler.HandleAsync(new GetClaimsQuery(new Page(skip, take)), cancellationToken);

    /// <summary>Returns a single claim.</summary>
    /// <response code="200">The claim.</response>
    /// <response code="404">No claim with this id exists.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Claim>> GetById(
        Guid id,
        [FromServices] IQueryHandler<GetClaimByIdQuery, Claim?> handler,
        CancellationToken cancellationToken)
    {
        var claim = await handler.HandleAsync(new GetClaimByIdQuery(id), cancellationToken);
        return claim is null ? NotFound() : claim;
    }

    /// <summary>Files a new claim against an existing cover.</summary>
    /// <response code="201">The claim was created.</response>
    /// <response code="400">The request is invalid; the body lists the errors.</response>
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.Write)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Claim>> Create(
        CreateClaimCommand command,
        [FromServices] ICommandHandler<CreateClaimCommand, Claim> handler,
        CancellationToken cancellationToken)
    {
        var claim = await handler.HandleAsync(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = claim.Id }, claim);
    }

    /// <summary>Deletes a claim.</summary>
    /// <response code="204">The claim was deleted.</response>
    /// <response code="404">No claim with this id exists.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromServices] ICommandHandler<DeleteClaimCommand, bool> handler,
        CancellationToken cancellationToken) =>
        await handler.HandleAsync(new DeleteClaimCommand(id), cancellationToken) ? NoContent() : NotFound();
}
