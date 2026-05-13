using System.Text;
using System.Text.Json;
using FileBatcher.Contracts;
using FileBatcher.Domain;
using FileBatcher.Infrastructure;
using FileBatcher.Models;
using Microsoft.EntityFrameworkCore;

namespace FileBatcher.Services;

public interface IFileBatcherService
{
    Task<IReadOnlyList<FileBatchResponse>> ListFileBatchesAsync(FileBatchListQuery query, CancellationToken ct);
    Task<FileBatchResponse> ImportCsvAsync(Stream csvStream, string fileName, FileBatchAction action, CancellationToken ct);
    Task<FileBatchResponse> SetFileStatusAsync(Guid id, FileBatchStatus status, CancellationToken ct);
    Task<FileBatchResponse> RetryErrorFileAsync(Guid id, CancellationToken ct);
    Task StartProcessingAsync(CancellationToken ct);
    Task<IReadOnlyList<FileBatchItemResponse>> ListItemsAsync(Guid fileBatchId, CancellationToken ct);
    Task<FileBatchItemResponse> IgnoreItemAsync(Guid fileBatchId, Guid itemId, CancellationToken ct);
    Task<FileBatchItemResponse> ManualSaveItemAsync(Guid fileBatchId, Guid itemId, ManualItemSaveRequest body, CancellationToken ct);
    Task<PagedResult<PartnerResponse>> ListPartnersAsync(PartnerListQuery query, CancellationToken ct);
    Task<PartnerResponse> SetPartnerStatusAsync(Guid id, PartnerStatus status, CancellationToken ct);
}

public sealed class FileBatcherService(AppDbContext db) : IFileBatcherService
{
    private const int ItemProcessingDelayMs = 200;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly SemaphoreSlim ProcessingGate = new(1, 1);

    public async Task<IReadOnlyList<FileBatchResponse>> ListFileBatchesAsync(FileBatchListQuery query, CancellationToken ct)
    {
        var q = db.FileBatches.AsNoTracking().AsQueryable();
        if (query.FromUpdatedAt is { } from) q = q.Where(x => x.UpdatedAt >= from);
        if (query.ToUpdatedAt is { } to) q = q.Where(x => x.UpdatedAt <= to);
        if (query.Status is { } st) q = q.Where(x => x.Status == st);
        if (query.Action is { } ac) q = q.Where(x => x.Action == ac);
        var list = await q.OrderByDescending(x => x.UpdatedAt).ToListAsync(ct);
        return list.Select(MapFile).ToList();
    }

    public async Task<FileBatchResponse> ImportCsvAsync(Stream csvStream, string fileName, FileBatchAction action, CancellationToken ct)
    {
        using var reader = new StreamReader(csvStream, Encoding.UTF8, leaveOpen: true);
        var lines = await ReadAllNonEmptyLinesAsync(reader, ct);
        if (lines.Count < 2)
            throw new InvalidOperationException("CSV deve conter cabeçalho e ao menos uma linha de dados.");

        var header = SplitCsvLine(lines[0]);
        ValidateHeader(header);

        var now = UtcNow();
        var batch = new FileBatch
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(fileName) ? "import.csv" : fileName.Trim(),
            Action = action,
            Status = FileBatchStatus.IMPORTED,
            CreatedAt = now,
            UpdatedAt = now
        };

        for (var i = 1; i < lines.Count; i++)
        {
            var cols = SplitCsvLine(lines[i]);
            if (cols.Count < 4) throw new InvalidOperationException($"Linha {i + 1}: número de colunas inválido.");
            var payload = new PartnerRowPayload
            {
                Nome = cols[0]?.Trim(),
                Email = cols[1]?.Trim(),
                Cpf = cols[2]?.Trim(),
                Telefone = cols[3]?.Trim()
            };
            var json = JsonSerializer.Serialize(payload, JsonOpts);
            batch.Items.Add(new FileBatchItem
            {
                Id = Guid.NewGuid(),
                FileBatchId = batch.Id,
                Data = json,
                Status = FileBatchItemStatus.PENDING,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        db.FileBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        return MapFile(batch);
    }

    public async Task<FileBatchResponse> SetFileStatusAsync(Guid id, FileBatchStatus status, CancellationToken ct)
    {
        var batch = await db.FileBatches.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Arquivo não encontrado.");

        if (status == FileBatchStatus.PROCESSED && batch.Status == FileBatchStatus.PROCESSING)
            throw new InvalidOperationException("Não é possível marcar como PROCESSED enquanto o arquivo está em PROCESSING.");

        batch.Status = status;
        Touch(batch);
        await db.SaveChangesAsync(ct);
        return MapFile(batch);
    }

    public async Task<FileBatchResponse> RetryErrorFileAsync(Guid id, CancellationToken ct)
    {
        var batch = await db.FileBatches.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Arquivo não encontrado.");

        if (batch.Status != FileBatchStatus.ERROR)
            throw new InvalidOperationException("Somente arquivos com status ERROR podem ser reprocessados.");

        batch.Status = FileBatchStatus.IMPORTED;
        Touch(batch);
        foreach (var item in batch.Items.Where(i => i.Status == FileBatchItemStatus.ERROR))
        {
            item.Status = FileBatchItemStatus.PENDING;
            Touch(item);
        }

        await db.SaveChangesAsync(ct);
        return MapFile(batch);
    }

    public async Task StartProcessingAsync(CancellationToken ct)
    {
        await ProcessingGate.WaitAsync(ct);
        try
        {
            var anyProcessing = await db.FileBatches.AnyAsync(f => f.Status == FileBatchStatus.PROCESSING, ct);
            if (anyProcessing)
                throw new InvalidOperationException("Já existe um arquivo em processamento.");

            var next = await db.FileBatches
                .Include(x => x.Items)
                .Where(f => f.Status == FileBatchStatus.IMPORTED)
                .OrderBy(f => f.UpdatedAt)
                .FirstOrDefaultAsync(ct);

            if (next is null)
                return;

            next.Status = FileBatchStatus.PROCESSING;
            Touch(next);
            await db.SaveChangesAsync(ct);

            try
            {
                var pendingItems = next.Items
                    .Where(i => i.Status == FileBatchItemStatus.PENDING)
                    .OrderBy(i => i.CreatedAt)
                    .ToList();
                for (var i = 0; i < pendingItems.Count; i++)
                {
                    await ProcessItemAsync(next, pendingItems[i], ct);
                    await Task.Delay(ItemProcessingDelayMs, ct);
                }

                var hasError = await db.FileBatchItems.AnyAsync(
                    i => i.FileBatchId == next.Id && i.Status == FileBatchItemStatus.ERROR, ct);

                next.Status = hasError ? FileBatchStatus.ERROR : FileBatchStatus.PROCESSED;
                Touch(next);
                await db.SaveChangesAsync(ct);
            }
            catch
            {
                next.Status = FileBatchStatus.ERROR;
                Touch(next);
                await db.SaveChangesAsync(ct);
                throw;
            }
        }
        finally
        {
            ProcessingGate.Release();
        }
    }

    public async Task<IReadOnlyList<FileBatchItemResponse>> ListItemsAsync(Guid fileBatchId, CancellationToken ct)
    {
        var exists = await db.FileBatches.AnyAsync(x => x.Id == fileBatchId, ct);
        if (!exists) throw new KeyNotFoundException("Arquivo não encontrado.");

        var items = await db.FileBatchItems.AsNoTracking()
            .Where(x => x.FileBatchId == fileBatchId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
        return items.Select(MapItem).ToList();
    }

    public async Task<FileBatchItemResponse> IgnoreItemAsync(Guid fileBatchId, Guid itemId, CancellationToken ct)
    {
        var item = await db.FileBatchItems.FirstOrDefaultAsync(x => x.Id == itemId && x.FileBatchId == fileBatchId, ct)
            ?? throw new KeyNotFoundException("Item não encontrado.");

        item.Status = FileBatchItemStatus.IGNORED;
        Touch(item);
        var batch = await db.FileBatches.FirstAsync(x => x.Id == fileBatchId, ct);
        Touch(batch);
        await db.SaveChangesAsync(ct);
        return MapItem(item);
    }

    public async Task<FileBatchItemResponse> ManualSaveItemAsync(Guid fileBatchId, Guid itemId, ManualItemSaveRequest body, CancellationToken ct)
    {
        var item = await db.FileBatchItems.Include(i => i.FileBatch)
            .FirstOrDefaultAsync(x => x.Id == itemId && x.FileBatchId == fileBatchId, ct)
            ?? throw new KeyNotFoundException("Item não encontrado.");

        var batch = item.FileBatch!;
        var row = new PartnerRowPayload
        {
            Nome = body.Nome,
            Email = body.Email,
            Cpf = body.Cpf,
            Telefone = body.Telefone
        };

        if (!PartnerRowValidation.RowPassesFieldRules(row, out var cpf))
        {
            item.Status = FileBatchItemStatus.ERROR;
            item.Data = JsonSerializer.Serialize(row, JsonOpts);
            Touch(item);
            Touch(batch);
            await db.SaveChangesAsync(ct);
            return MapItem(item);
        }

        var ok = batch.Action switch
        {
            FileBatchAction.TO_ACTIVE => await ApplyActivateAsync(row, cpf!, ct),
            FileBatchAction.TO_INACTIVE => await ApplyInactivateAsync(cpf!, ct),
            _ => false
        };

        if (!ok)
        {
            item.Status = FileBatchItemStatus.ERROR;
            item.Data = JsonSerializer.Serialize(row, JsonOpts);
            Touch(item);
            Touch(batch);
            await db.SaveChangesAsync(ct);
            return MapItem(item);
        }

        item.Status = FileBatchItemStatus.PROCESSED;
        item.Data = JsonSerializer.Serialize(row, JsonOpts);
        Touch(item);
        Touch(batch);
        await db.SaveChangesAsync(ct);
        return MapItem(item);
    }

    public async Task<PagedResult<PartnerResponse>> ListPartnersAsync(PartnerListQuery query, CancellationToken ct)
    {
        var q = db.Partners.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.NameContains))
        {
            var n = query.NameContains.Trim();
            q = q.Where(x => x.Name.Contains(n));
        }

        if (!string.IsNullOrWhiteSpace(query.DocumentEquals))
        {
            var d = PartnerRowValidation.NormalizeCpfDigits(query.DocumentEquals);
            if (d is null) return new PagedResult<PartnerResponse>([], query.Page, query.PageSize, 0);
            q = q.Where(x => x.Document == d);
        }

        var total = await q.CountAsync(ct);
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 200);
        var items = await q.OrderBy(x => x.Name)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);
        return new PagedResult<PartnerResponse>(items.Select(MapPartner).ToList(), page, size, total);
    }

    public async Task<PartnerResponse> SetPartnerStatusAsync(Guid id, PartnerStatus status, CancellationToken ct)
    {
        var p = await db.Partners.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Parceiro não encontrado.");
        p.Status = status;
        Touch(p);
        await db.SaveChangesAsync(ct);
        return MapPartner(p);
    }

    private async Task ProcessItemAsync(FileBatch batch, FileBatchItem item, CancellationToken ct)
    {
        var row = JsonSerializer.Deserialize<PartnerRowPayload>(item.Data, JsonOpts) ?? new PartnerRowPayload();
        if (!PartnerRowValidation.RowPassesFieldRules(row, out var cpf))
        {
            item.Status = FileBatchItemStatus.ERROR;
            Touch(item);
            await db.SaveChangesAsync(ct);
            return;
        }

        var ok = batch.Action switch
        {
            FileBatchAction.TO_ACTIVE => await ApplyActivateAsync(row, cpf!, ct),
            FileBatchAction.TO_INACTIVE => await ApplyInactivateAsync(cpf!, ct),
            _ => false
        };

        item.Status = ok ? FileBatchItemStatus.PROCESSED : FileBatchItemStatus.ERROR;
        Touch(item);
        await db.SaveChangesAsync(ct);
    }

    private async Task<bool> ApplyActivateAsync(PartnerRowPayload row, string cpf11, CancellationToken ct)
    {
        var now = UtcNow();
        var existing = await db.Partners.FirstOrDefaultAsync(x => x.Document == cpf11, ct);
        if (existing is null)
        {
            db.Partners.Add(new Partner
            {
                Id = Guid.NewGuid(),
                Status = PartnerStatus.ACTIVE,
                Name = row.Nome!.Trim(),
                Document = cpf11,
                Email = row.Email!.Trim(),
                Phone = row.Telefone!.Trim(),
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(ct);
            return true;
        }

        existing.Status = PartnerStatus.ACTIVE;
        existing.Name = row.Nome!.Trim();
        existing.Email = row.Email!.Trim();
        existing.Phone = row.Telefone!.Trim();
        Touch(existing);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool> ApplyInactivateAsync(string cpf11, CancellationToken ct)
    {
        var existing = await db.Partners.FirstOrDefaultAsync(x => x.Document == cpf11, ct);
        if (existing is null) return false;
        existing.Status = PartnerStatus.INACTIVE;
        Touch(existing);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static void Touch(FileBatch b) => b.UpdatedAt = UtcNow();
    private static void Touch(FileBatchItem i) => i.UpdatedAt = UtcNow();
    private static void Touch(Partner p) => p.UpdatedAt = UtcNow();

    private static DateTime UtcNow() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

    private static FileBatchResponse MapFile(FileBatch x) => new(x.Id, x.Name, x.Action, x.Status, x.CreatedAt, x.UpdatedAt);

    private static FileBatchItemResponse MapItem(FileBatchItem x) =>
        new(x.Id, x.FileBatchId, x.Data, x.Status, x.CreatedAt, x.UpdatedAt);

    private static PartnerResponse MapPartner(Partner x) =>
        new(x.Id, x.Status, x.Name, x.Document, x.Email, x.Phone, x.CreatedAt, x.UpdatedAt);

    private static void ValidateHeader(IReadOnlyList<string> header)
    {
        var expected = new[] { "NOME", "EMAIL", "CPF", "TELEFONE" };
        if (header.Count < 4) throw new InvalidOperationException("Cabeçalho inválido.");
        for (var i = 0; i < 4; i++)
        {
            if (!string.Equals(header[i]?.Trim(), expected[i], StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Cabeçalho esperado: NOME;EMAIL;CPF;TELEFONE (coluna {i + 1}).");
        }
    }

    private static List<string> SplitCsvLine(string line) =>
        line.Split(';', StringSplitOptions.None).Select(c => c.Trim()).ToList();

    private static async Task<List<string>> ReadAllNonEmptyLinesAsync(TextReader reader, CancellationToken ct)
    {
        var lines = new List<string>();
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            lines.Add(line);
        }

        return lines;
    }
}
