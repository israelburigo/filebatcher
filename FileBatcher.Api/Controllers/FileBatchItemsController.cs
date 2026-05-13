using FileBatcher.Contracts;
using FileBatcher.Services;
using Microsoft.AspNetCore.Mvc;

namespace FileBatcher.Controllers;

[ApiController]
[Route("api/file-batches/{fileBatchId:guid}/items")]
[Produces("application/json")]
public sealed class FileBatchItemsController(IFileBatcherService svc) : ControllerBase
{
    /// <summary>Lista itens de um arquivo.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<FileBatchItemResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<FileBatchItemResponse>>> List(Guid fileBatchId, CancellationToken ct)
    {
        try
        {
            return Ok(await svc.ListItemsAsync(fileBatchId, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>Marca um item como <c>IGNORED</c>.</summary>
    [HttpPut("{itemId:guid}/ignore")]
    [ProducesResponseType(typeof(FileBatchItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FileBatchItemResponse>> Ignore(Guid fileBatchId, Guid itemId, CancellationToken ct)
    {
        try
        {
            return Ok(await svc.IgnoreItemAsync(fileBatchId, itemId, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>Processamento manual: valida regras, cria/atualiza parceiro conforme ação do arquivo e marca item <c>PROCESSED</c> ou <c>ERROR</c>.</summary>
    [HttpPut("{itemId:guid}")]
    [ProducesResponseType(typeof(FileBatchItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FileBatchItemResponse>> Save(
        Guid fileBatchId,
        Guid itemId,
        [FromBody] ManualItemSaveRequest body,
        CancellationToken ct)
    {
        try
        {
            return Ok(await svc.ManualSaveItemAsync(fileBatchId, itemId, body, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
