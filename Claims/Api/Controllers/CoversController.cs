using Claims.Application.Covers;
using Claims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Claims.Api.Controllers;

/// <summary>HTTP endpoints for managing covers and quoting premiums.</summary>
[ApiController]
[Route("[controller]")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.Read)]
public class CoversController(ICoverService coverService) : ControllerBase
{
    /// <summary>Quotes a premium without creating a cover.</summary>
    /// <remarks>Uses the same validation as cover creation.</remarks>
    /// <response code="200">The total premium for the period.</response>
    /// <response code="400">The request is invalid.</response>
    [HttpPost("compute")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<decimal> ComputePremium(DateTime startDate, DateTime endDate, CoverType coverType) =>
        coverService.ComputePremium(new CreateCoverRequest(startDate, endDate, coverType));

    /// <summary>Returns all covers.</summary>
    [HttpGet]
    public Task<IReadOnlyList<Cover>> GetAll(CancellationToken cancellationToken) =>
        coverService.GetAllAsync(cancellationToken);

    /// <summary>Returns a single cover.</summary>
    /// <response code="200">The cover.</response>
    /// <response code="404">No cover with this id exists.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Cover>> GetById(string id, CancellationToken cancellationToken)
    {
        var cover = await coverService.GetByIdAsync(id, cancellationToken);
        return cover is null ? NotFound() : cover;
    }

    /// <summary>Creates a cover. The server calculates the premium.</summary>
    /// <response code="201">The cover was created.</response>
    /// <response code="400">The request is invalid; the body lists the errors.</response>
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.Write)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Cover>> Create(CreateCoverRequest request, CancellationToken cancellationToken)
    {
        var cover = await coverService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = cover.Id }, cover);
    }

    /// <summary>Deletes a cover.</summary>
    /// <response code="204">The cover was deleted.</response>
    /// <response code="404">No cover with this id exists.</response>
    [HttpDelete("{id}")]
    [Authorize(Policy = AuthorizationPolicies.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken) =>
        await coverService.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
}
