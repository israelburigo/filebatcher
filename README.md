# FileBatcher

API em ASP.NET Core para importar arquivos CSV de parceiros, enfileirar processamento (um arquivo por vez) e manter cadastro de parceiros com validações de nome, e-mail, CPF e telefone. Documentação interativa em **Swagger** (`/swagger`).

## Requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Executar localmente

```bash
cd FileBatcher.Api
dotnet run
```

Por padrão a URL de desenvolvimento está em `Properties/launchSettings.json` (HTTP em `http://localhost:5288`).

- **Swagger UI:** `http://localhost:5288/swagger` (ou a URL base do seu perfil + `/swagger`)
- O banco é **SQLite**, arquivo `filebatcher.db` no diretório de trabalho da API (criado na primeira execução com `EnsureCreated`).

## CSV de importação

- Separador: **`;`**
- Cabeçalho obrigatório: **`NOME;EMAIL;CPF;TELEFONE`**
- CPF: 11 dígitos com dígitos verificadores válidos
- Telefone: formato **`(##)#####-####`**
- Nome: ao menos duas palavras, apenas letras (Unicode permitido)

Há um exemplo em `exemplo-parceiros.csv` na raiz do repositório.

## Principais endpoints

| Método | Caminho | Descrição |
|--------|---------|-----------|
| `GET` | `/api/file-batches` | Lista lotes (filtros: `fromUpdatedAt`, `toUpdatedAt`, `status`, `action`) |
| `POST` | `/api/file-batches/import/to-active` | Upload CSV (`multipart/form-data`, campo `file`) — ação `TO_ACTIVE` |
| `POST` | `/api/file-batches/import/to-inactive` | Upload CSV — ação `TO_INACTIVE` |
| `PUT` | `/api/file-batches/{id}/status/processed` | Marca lote como `PROCESSED` |
| `PUT` | `/api/file-batches/{id}/status/cancelled` | Marca lote como `CANCELLED` |
| `PUT` | `/api/file-batches/{id}/retry` | Lote em `ERROR` volta para `IMPORTED`; itens `ERROR` → `PENDING` |
| `POST` | `/api/file-batches/start-processing` | Processa o lote `IMPORTED` mais antigo (FIFO por `updated_at`), um lote por vez |
| `GET` | `/api/file-batches/{fileBatchId}/items` | Lista itens do lote |
| `PUT` | `/api/file-batches/{fileBatchId}/items/{itemId}/ignore` | Item `IGNORED` |
| `PUT` | `/api/file-batches/{fileBatchId}/items/{itemId}` | Corpo JSON manual (`nome`, `email`, `cpf`, `telefone`) |
| `GET` | `/api/partners` | Lista parceiros paginada (`nameContains`, `documentEquals`, `page`, `pageSize`) |
| `PUT` | `/api/partners/{id}/activate` | `ACTIVE` |
| `PUT` | `/api/partners/{id}/deactivate` | `INACTIVE` |

Enums (`status`, `action`, etc.) são serializados como **strings** no JSON (ex.: `IMPORTED`, `TO_ACTIVE`).

Entre o processamento de cada item pendente há um atraso configurável de **200 ms** para simular processamento mais lento.

## Configuração (`appsettings`)

- **`ConnectionStrings:Default`:** connection string SQLite (padrão `Data Source=filebatcher.db`).
- **`Cors:Origins`:** lista de origens do frontend permitidas. Em **Development**, se a lista estiver vazia, origens `localhost` / `127.0.0.1` são aceitas por porta. Em **Production**, defina origens explicitamente.
- **`Swagger:Enabled`:** quando `true`, expõe Swagger fora de Development (útil em ambiente de homologação).

### Variáveis de ambiente (ex.: Render)

- **`PORT`:** a API escuta em `http://0.0.0.0:{PORT}` quando definida (Render injeta automaticamente).
- **`ConnectionStrings__Default`:** ex. `Data Source=/data/filebatcher.db` se usar disco persistente montado.
- **`Cors__Origins__0`**, **`Cors__Origins__1`**, … URLs do frontend (HTTPS).

## Docker

Na raiz do repositório:

```bash
docker build -t filebatcher-api .
docker run --rm -p 8080:8080 -e Swagger__Enabled=true filebatcher-api
```

A aplicação escuta na porta **8080** dentro do container (`ASPNETCORE_URLS`).

## Solução

O arquivo `FileBatcher.slnx` na raiz agrupa o projeto `FileBatcher.Api`.
