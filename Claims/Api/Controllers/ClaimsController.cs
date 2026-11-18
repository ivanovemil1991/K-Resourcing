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
public class ClaimsController(IClaimService claimService) : ControllerBase
{
    /// <summary>Returns all claims.</summary>
    [HttpGet]
    public Task<IReadOnlyList<Claim>> GetAll(CancellationToken cancellationToken) =>
        claimService.GetAllAsync(cancellationToken);

    /// <summary>Returns a single claim.</summary>
    /// <response code="200">The claim.</response>
    /// <response code="404">No claim with this id exists.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Claim>> GetById(string id, CancellationToken cancellationToken)
    {
        var claim = await claimService.GetByIdAsync(id, cancellationToken);
        return claim is null ? NotFound() : claim;
    }

    /// <summary>Files a new claim against an existing cover.</summary>
    /// <response code="201">The claim was created.</response>
    /// <response code="400">The request is invalid; the body lists the errors.</response>
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.Write)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Claim>> Create(CreateClaimRequest request, CancellationToken cancellationToken)
    {
        var claim = await claimService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = claim.Id }, claim);
    }

    /// <summary>Deletes a claim.</summary>
    /// <response code="204">The claim was deleted.</response>
    /// <response code="404">No claim with this id exists.</response>
    [HttpDelete("{id}")]
    [Authorize(Policy = AuthorizationPolicies.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken) =>
        await claimService.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
}
