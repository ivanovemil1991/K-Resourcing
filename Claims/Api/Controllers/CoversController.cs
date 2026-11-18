using System.ComponentModel.DataAnnotations;
using Claims.Application.Abstractions;
using Claims.Application.Covers;
using Claims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Claims.Api.Controllers;

/// <summary>HTTP endpoints for managing covers and quoting premiums.</summary>
[ApiController]
[Route("[controller]")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.Read)]
[EnableRateLimiting(RateLimitingOptions.PolicyName)]
public class CoversController : ControllerBase
{
    /// <summary>Quotes a premium without creating a cover.</summary>
    /// <remarks>Uses the same validation as cover creation.</remarks>
    /// <response code="200">The total premium for the period.</response>
    /// <response code="400">The request is invalid.</response>
    [HttpPost("compute")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<decimal>> ComputePremium(
        DateTime startDate,
        DateTime endDate,
        CoverType coverType,
        [FromServices] IQueryHandler<ComputePremiumQuery, decimal> handler,
        CancellationToken cancellationToken) =>
        await handler.HandleAsync(new ComputePremiumQuery(startDate, endDate, coverType), cancellationToken);

    /// <summary>Returns one page of covers, ordered by id. Use <c>skip</c> and <c>take</c> (at most 100, default 50) to page.</summary>
    [HttpGet]
    public Task<IReadOnlyList<Cover>> GetAll(
        [FromServices] IQueryHandler<GetCoversQuery, IReadOnlyList<Cover>> handler,
        CancellationToken cancellationToken,
        [FromQuery, Range(0, int.MaxValue)] int skip,
        [FromQuery, Range(1, Page.MaxTake)] int take = Page.DefaultTake) =>
        handler.HandleAsync(new GetCoversQuery(new Page(skip, take)), cancellationToken);

    /// <summary>Returns a single cover.</summary>
    /// <response code="200">The cover.</response>
    /// <response code="404">No cover with this id exists.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Cover>> GetById(
        Guid id,
        [FromServices] IQueryHandler<GetCoverByIdQuery, Cover?> handler,
        CancellationToken cancellationToken)
    {
        var cover = await handler.HandleAsync(new GetCoverByIdQuery(id), cancellationToken);
        return cover is null ? NotFound() : cover;
    }

    /// <summary>Creates a cover. The server calculates the premium.</summary>
    /// <response code="201">The cover was created.</response>
    /// <response code="400">The request is invalid; the body lists the errors.</response>
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.Write)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Cover>> Create(
        CreateCoverCommand command,
        [FromServices] ICommandHandler<CreateCoverCommand, Cover> handler,
        CancellationToken cancellationToken)
    {
        var cover = await handler.HandleAsync(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = cover.Id }, cover);
    }

    /// <summary>Deletes a cover.</summary>
    /// <response code="204">The cover was deleted.</response>
    /// <response code="404">No cover with this id exists.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromServices] ICommandHandler<DeleteCoverCommand, bool> handler,
        CancellationToken cancellationToken) =>
        await handler.HandleAsync(new DeleteCoverCommand(id), cancellationToken) ? NoContent() : NotFound();
}
