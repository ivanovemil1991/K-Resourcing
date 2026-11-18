using Claims.Application.Abstractions;
using Claims.Application.Claims;
using Claims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Claims.Api.Controllers;

/// <summary>HTTP endpoints for managing insurance claims.</summary>
[ApiController]
[Route("[controller]")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.Read)]
public class ClaimsController : ControllerBase
{
    /// <summary>Returns all claims.</summary>
    [HttpGet]
    public Task<IReadOnlyList<Claim>> GetAll(
        [FromServices] IQueryHandler<GetClaimsQuery, IReadOnlyList<Claim>> handler,
        CancellationToken cancellationToken) =>
        handler.HandleAsync(new GetClaimsQuery(), cancellationToken);

    /// <summary>Returns a single claim.</summary>
    /// <response code="200">The claim.</response>
    /// <response code="404">No claim with this id exists.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Claim>> GetById(
        string id,
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
    [HttpDelete("{id}")]
    [Authorize(Policy = AuthorizationPolicies.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        string id,
        [FromServices] ICommandHandler<DeleteClaimCommand, bool> handler,
        CancellationToken cancellationToken) =>
        await handler.HandleAsync(new DeleteClaimCommand(id), cancellationToken) ? NoContent() : NotFound();
}
