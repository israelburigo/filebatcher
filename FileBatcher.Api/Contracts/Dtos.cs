using System.Text.Json.Serialization;
using FileBatcher.Domain;

namespace FileBatcher.Contracts;

public sealed record FileBatchResponse(
    Guid Id,
    string Name,
    FileBatchAction Action,
    FileBatchStatus Status,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record FileBatchListQuery(
    DateTime? FromUpdatedAt,
    DateTime? ToUpdatedAt,
    FileBatchStatus? Status,
    FileBatchAction? Action);

public sealed record FileBatchItemResponse(
    Guid Id,
    Guid FileBatchId,
    string Data,
    FileBatchItemStatus Status,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record PartnerResponse(
    Guid Id,
    PartnerStatus Status,
    string Name,
    string Document,
    string Email,
    string Phone,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record PartnerListQuery(string? NameContains, string? DocumentEquals, int Page, int PageSize);

public sealed record ManualItemSaveRequest(
    [property: JsonPropertyName("nome")] string Nome,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("cpf")] string Cpf,
    [property: JsonPropertyName("telefone")] string Telefone);
