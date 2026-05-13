using FileBatcher.Contracts;
using FileBatcher.Domain;
using FileBatcher.Services;
using Microsoft.AspNetCore.Mvc;

namespace FileBatcher.Controllers;

[ApiController]
[Route("api/file-batches")]
[Produces("application/json")]
public sealed class FileBatchesController(IFileBatcherService svc) : ControllerBase
{
    /// <summary>Lista arquivos com filtros opcionais por intervalo de <c>updated_at</c>, status e ação.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<FileBatchResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<FileBatchResponse>>> List(
        [FromQuery] DateTime? fromUpdatedAt,
        [FromQuery] DateTime? toUpdatedAt,
        [FromQuery] FileBatchStatus? status,
        [FromQuery] FileBatchAction? action,
        CancellationToken ct)
    {
        var q = new FileBatchListQuery(fromUpdatedAt, toUpdatedAt, status, action);
        return Ok(await svc.ListFileBatchesAsync(q, ct));
    }

    /// <summary>Importa CSV para ativação (<c>TO_ACTIVE</c>) com status <c>IMPORTED</c>.</summary>
    [HttpPost("import/to-active")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(FileBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<FileBatchResponse>> ImportToActive(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest("Arquivo obrigatório.");
        await using var stream = file.OpenReadStream();
        try
        {
            var result = await svc.ImportCsvAsync(stream, file.FileName, FileBatchAction.TO_ACTIVE, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Importa CSV para inativação (<c>TO_INACTIVE</c>) com status <c>IMPORTED</c>.</summary>
    [HttpPost("import/to-inactive")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(FileBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<FileBatchResponse>> ImportToInactive(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest("Arquivo obrigatório.");
        await using var stream = file.OpenReadStream();
        try
        {
            var result = await svc.ImportCsvAsync(stream, file.FileName, FileBatchAction.TO_INACTIVE, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Marca o arquivo como <c>PROCESSED</c>.</summary>
    [HttpPut("{id:guid}/status/processed")]
    [ProducesResponseType(typeof(FileBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<FileBatchResponse>> MarkProcessed(Guid id, CancellationToken ct)
    {
        try
        {
            return Ok(await svc.SetFileStatusAsync(id, FileBatchStatus.PROCESSED, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Marca o arquivo como <c>CANCELLED</c>.</summary>
    [HttpPut("{id:guid}/status/cancelled")]
    [ProducesResponseType(typeof(FileBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FileBatchResponse>> MarkCancelled(Guid id, CancellationToken ct)
    {
        try
        {
            return Ok(await svc.SetFileStatusAsync(id, FileBatchStatus.CANCELLED, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>Reprocessa arquivo em <c>ERROR</c>: arquivo volta para <c>IMPORTED</c> e itens <c>ERROR</c> para <c>PENDING</c>.</summary>
    [HttpPut("{id:guid}/retry")]
    [ProducesResponseType(typeof(FileBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<FileBatchResponse>> Retry(Guid id, CancellationToken ct)
    {
        try
        {
            return Ok(await svc.RetryErrorFileAsync(id, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Inicia o processamento do arquivo importado mais antigo (FIFO por <c>updated_at</c>). Só um arquivo por vez.</summary>
    [HttpPost("start-processing")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartProcessing(CancellationToken ct)
    {
        try
        {
            await svc.StartProcessingAsync(ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
