# Desafio frontend — FileBatcher

Repositório da API: [github.com/israelburigo/filebatcher](https://github.com/israelburigo/filebatcher)  
Swagger em produção: [filebatcher.onrender.com/swagger](https://filebatcher.onrender.com/swagger/index.html) (o primeiro pedido pode demorar se o serviço estiver inativo no Render).

---

## 1. Contexto (história curta)

Equipes enviam ficheiros **CSV** com parceiros em massa. Cada ficheiro corresponde a uma **ação**: **ativar** (`TO_ACTIVE`) ou **inativar** (`TO_INACTIVE`) quem está descrito nas linhas. O **FileBatcher** guarda cada ficheiro como um **lote** (`file batch`), cada linha como um **item**, e mantém um cadastro de **parceiros**. O processamento automático corre **um lote de cada vez**, por ordem de prioridade, para não disparar tudo em paralelo. O teu trabalho é construir o **frontend** que torna estes fluxos óbvios para o utilizador.

---

## 2. Como a API funciona (modelo mental para o front)

### 2.1 Três “camadas” de dados

| Conceito | O que é | Para o UI |
|----------|---------|-----------|
| **Lote** (`file batch`) | Um CSV importado: nome, ação (`TO_ACTIVE` / `TO_INACTIVE`), estado geral, datas. | Lista de “trabalhos”; detalhe de um trabalho. |
| **Item** | Uma linha do CSV (dados em JSON no campo `data`). | Tabela de linhas dentro do lote; estados por linha. |
| **Parceiro** | Registo no cadastro (nome, CPF, email, telefone, `ACTIVE` / `INACTIVE`). | Lista pesquisável; ativar/inativar fora do CSV. |

Relação: **um lote tem muitos itens**. Os itens **alteram parceiros** quando o processamento (automático ou manual) corre com sucesso. O CPF (`document` no parceiro) é a chave de negócio para encontrar o parceiro.

### 2.2 Ciclo de vida do **lote** (`status`)

```
IMPORTED  →  PROCESSING  →  PROCESSED   (sucesso: nenhum item ficou em ERROR)
          ↘  ERROR       (se pelo menos um item terminar em ERROR)
CANCELLED / PROCESSED também podem ser definidos por ações explícitas (ver abaixo).
```

- **`IMPORTED`** — CSV aceite; à espera de processamento. É o estado inicial após import.
- **`PROCESSING`** — O lote **selecionado** está a ser processado agora. **Só pode existir um lote neste estado** em todo o sistema.
- **`PROCESSED`** — Processamento automático terminou **sem** itens em `ERROR`, **ou** o utilizador marcou manualmente como processado (`PUT .../status/processed`).
- **`ERROR`** — Após o processamento automático, **se existir pelo menos um item com `ERROR`**, o lote inteiro fica `ERROR`.
- **`CANCELLED`** — Marcação explícita de cancelamento (`PUT .../status/cancelled`).

**FIFO:** ao chamar “iniciar processamento”, a API escolhe o lote mais antigo entre os que estão **`IMPORTED`**, usando o campo **`updatedAt`** como critério de ordenação (o mais antigo primeiro).

**Importante para o UI:** não existe endpoint “estado do job” em tempo real; durante `PROCESSING` podes fazer **polling** em `GET /api/file-batches/{id}` ou na lista de itens até o estado deixar de ser `PROCESSING`. Entre itens a API espera **~200 ms** de propósito (processamento “lento”).

### 2.3 Ciclo de vida do **item** (`status`)

- **`PENDING`** — Linha ainda não processada (ou reposta após *retry*).
- **`PROCESSED`** — Linha tratada com sucesso (criação/ativação ou inativação conforme a ação do lote).
- **`ERROR`** — Validação falhou, regra de negócio falhou, ou inativação sem parceiro existente (ver abaixo).
- **`IGNORED`** — Utilizador (ou operador) marcou a linha para ignorar (`PUT .../ignore`); não entra no fluxo automático como sucesso/erro da mesma forma que os outros.

**Regra que liga item ao lote:** se **qualquer** item ficar `ERROR` após o processamento automático do lote, o **lote** passa a **`ERROR`**.

### 2.4 Ação do lote (`action`) e o que a API faz por linha

Cada lote tem **`TO_ACTIVE`** ou **`TO_INACTIVE`**. Isso define o comportamento **por item** quando corre o processamento (automático ou manual no item):

| Ação | Comportamento esperado (resumo) |
|------|----------------------------------|
| **`TO_ACTIVE`** | Se o CPF **não** existir no cadastro, **cria** parceiro ativo. Se já existir (ativo ou inativo), **ativa** e **atualiza** nome, email e telefone. |
| **`TO_INACTIVE`** | Se o CPF **existir**, **inativa** o parceiro. Se **não** existir, o **item** fica **`ERROR`** (não há quem inativar). |

Em ambos os casos, antes disso a API valida os campos da linha (ver secção 2.5). Falha de validação ⇒ item **`ERROR`**.

### 2.5 Validações por linha (útil para mensagens no front)

Para uma linha ser aceite no processamento (automático ou manual), os dados têm de cumprir:

- Todas as colunas preenchidas (nome, email, CPF, telefone).
- **Nome:** pelo menos **duas palavras**, só letras (Unicode permitido).
- **Email:** formato simples válido.
- **CPF:** 11 dígitos com **dígitos verificadores** válidos.
- **Telefone:** exatamente o padrão **`(##)#####-####`** (parênteses, DDD, 5 dígitos, hífen, 4 dígitos).

O CSV deve ter cabeçalho **`NOME;EMAIL;CPF;TELEFONE`** e separador **`;`**.

### 2.6 Fluxos que o utilizador dispara (mapeamento API)

1. **Importar** — `POST` multipart com campo de ficheiro **`file`**:  
   - `/api/file-batches/import/to-active` → lote com `action: TO_ACTIVE`, `status: IMPORTED`  
   - `/api/file-batches/import/to-inactive` → idem com `TO_INACTIVE`  
   Resposta: objeto do lote (JSON), não só o `id`.

2. **Ver fila / histórico** — `GET /api/file-batches` com query opcional: `fromUpdatedAt`, `toUpdatedAt`, `status`, `action`.

3. **Iniciar processamento** — `POST /api/file-batches/start-processing`  
   - Processa **um** lote `IMPORTED` (o mais antigo por `updatedAt`).  
   - Resposta **`204 No Content`** em sucesso (não há corpo).  
   - **`400`** se já houver um lote em `PROCESSING` ou regra de negócio violada.

4. **Ver linhas de um lote** — `GET /api/file-batches/{fileBatchId}/items` — cada item inclui `data` (JSON da linha).

5. **Ignorar linha** — `PUT .../items/{itemId}/ignore`.

6. **Corrigir linha manualmente** — `PUT .../items/{itemId}` com JSON no corpo: **`nome`**, **`email`**, **`cpf`**, **`telefone`** (nomes em minúsculas no JSON). A API aplica as mesmas validações e a **ação do lote** (`TO_ACTIVE` / `TO_INACTIVE`). O item passa a `PROCESSED` ou `ERROR`.

7. **Marcar lote como processado / cancelado** — `PUT .../status/processed` e `PUT .../status/cancelled`.  
   Não é permitido marcar `PROCESSED` enquanto o lote está `PROCESSING` (a API devolve erro).

8. **Reprocessar lote com erro** — `PUT .../retry` só se o lote estiver **`ERROR`**: lote volta a **`IMPORTED`**, itens em **`ERROR`** voltam a **`PENDING`**.

9. **Parceiros** — `GET /api/partners` com paginação (`page`, `pageSize`) e filtros `nameContains`, `documentEquals` (CPF normalizado).  
   Ativar / inativar: `PUT /api/partners/{id}/activate` e `.../deactivate`.

### 2.7 Formato JSON (enums e paginação)

- Campos como `status` e `action` vêm como **strings** (`"IMPORTED"`, `"TO_ACTIVE"`, etc.).
- Lista de parceiros devolve um objeto **`PagedResult`**: em JSON costuma aparecer em **camelCase** (`items`, `page`, `pageSize`, `totalCount`). Confirma no Swagger o contrato exato.

### 2.8 CORS e produção

Em produção, só origens configuradas na API são aceites. Para desenvolver contra o deploy Render, alinha com a equipe ou usa **proxy** no teu bundler apontando para a API. O Swagger em PRD pode demorar no primeiro pedido (**cold start**).

---

## 3. O desafio (entrega)

Implementa uma aplicação frontend que permita, no mínimo: importar os dois tipos de CSV, listar e filtrar lotes, ver itens de um lote, disparar processamento, lidar com estados `PROCESSING` / `ERROR` / *retry*, ignorar e editar manualmente itens, e listar/filtrar/paginar parceiros com ativar/inativar.

Inclui **README** no teu repositório com instruções de execução e URL base da API. Opcional: deploy público.

Boa sorte.
