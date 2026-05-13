using FileBatcher.Contracts;
using FileBatcher.Domain;
using FileBatcher.Services;
using Microsoft.AspNetCore.Mvc;

namespace FileBatcher.Controllers;

[ApiController]
[Route("api/partners")]
[Produces("application/json")]
public sealed class PartnersController(IFileBatcherService svc) : ControllerBase
{
    /// <summary>Lista parceiros paginado; filtros opcionais por nome (contém) e CPF (igual, 11 dígitos).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<PartnerResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<PartnerResponse>>> List(
        [FromQuery] string? nameContains,
        [FromQuery] string? documentEquals,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var q = new PartnerListQuery(nameContains, documentEquals, page, pageSize);
        return Ok(await svc.ListPartnersAsync(q, ct));
    }

    /// <summary>Ativa parceiro (<c>ACTIVE</c>).</summary>
    [HttpPut("{id:guid}/activate")]
    [ProducesResponseType(typeof(PartnerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PartnerResponse>> Activate(Guid id, CancellationToken ct)
    {
        try
        {
            return Ok(await svc.SetPartnerStatusAsync(id, PartnerStatus.ACTIVE, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>Inativa parceiro (<c>INACTIVE</c>).</summary>
    [HttpPut("{id:guid}/deactivate")]
    [ProducesResponseType(typeof(PartnerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PartnerResponse>> Deactivate(Guid id, CancellationToken ct)
    {
        try
        {
            return Ok(await svc.SetPartnerStatusAsync(id, PartnerStatus.INACTIVE, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
