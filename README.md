# PrintGest — API

API REST do sistema de gestão para gráficas **PrintGest**. Construída com ASP.NET Core 10 seguindo Clean Architecture, autenticação JWT e acesso a banco MySQL via ADO.NET puro.

---

## Tecnologias

| | |
|---|---|
| **Runtime** | .NET 10 / ASP.NET Core 10 |
| **Banco de dados** | MySQL 8.0+ via MySqlConnector 2.5.0 |
| **Autenticação** | JWT Bearer 9.0.4 |
| **Documentação** | Swagger / Swashbuckle 10.1.7 |

---

## Estrutura da solução

```
Api_PrintGest/
├── PrintGest.Domain/         # Entidades e Enums — sem dependências externas
├── PrintGest.Application/    # Services, interfaces de repositório, DTOs
├── PrintGest.Infrastructure/ # Repositórios (SQL puro), UnitOfWork, Mapping.cs
├── PrintGest.Api/            # Controllers, Program.cs, configuração do host
└── PrintGest.Tests/          # Testes unitários
```

O fluxo de dependência segue a regra de Clean Architecture:

```
Api → Application → Domain
Infrastructure → Application → Domain
```

---

## Pré-requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- MySQL 8.0+

---

## Configuração

### Banco de dados

```sql
CREATE DATABASE printgest CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
```

### Connection string

Edite `PrintGest.Api/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "PrintGest": "Server=localhost;Port=3306;Database=printgest;User ID=root;Password=sua_senha"
  }
}
```

Em produção, injete via variável de ambiente `ConnectionStrings__PrintGest`.

### Chave JWT

Em `PrintGest.Api/appsettings.json`:

```json
{
  "Jwt": {
    "Secret": "<mínimo 32 caracteres>",
    "Issuer": "PrintGest",
    "Audience": "PrintGest",
    "ExpiryHours": 5
  }
}
```

Em produção, injete via variável de ambiente `Jwt__Secret`.

---

## Como executar

```bash
dotnet restore
dotnet run --project PrintGest.Api
```

- API: `https://localhost:7131`
- Swagger: `https://localhost:7131/swagger` *(disponível apenas em Development)*

---

## Endpoints

Todas as rotas exigem o header `Authorization: Bearer <token>`, exceto as de autenticação.

### Auth

| Método | Rota | Descrição |
|---|---|---|
| `POST` | `/api/auth/login` | Autentica e retorna JWT |
| `PATCH` | `/api/auth/trocar-senha` | Troca a senha (exige senha atual) |

**Login — request / response:**

```json
// POST /api/auth/login
{ "email": "admin@printgest.com", "senha": "Senha@2024" }

// 200 OK
{
  "token": "eyJhbGci...",
  "expiresAt": "2024-01-01T15:00:00Z",
  "usuarioId": 1,
  "nome": "Admin",
  "perfil": "ADMIN"
}
```

Requisitos da nova senha em `trocar-senha`: mínimo 8 caracteres com letra maiúscula, minúscula, número e caractere especial.

---

### Usuários

| Método | Rota | Perfil mínimo | Descrição |
|---|---|---|---|
| `GET` | `/api/usuarios` | GERENTE | Lista com filtros: `nome`, `email`, `perfil`, `status` |
| `POST` | `/api/usuarios` | ADMIN | Cria usuário |
| `PUT` | `/api/usuarios/{id}` | ADMIN | Edita usuário |
| `PATCH` | `/api/usuarios/{id}/bloquear` | ADMIN | Bloqueia / desbloqueia |

---

### Clientes

| Método | Rota | Descrição |
|---|---|---|
| `GET` | `/api/clientes` | Lista com filtro `nome` |
| `POST` | `/api/clientes` | Cria cliente |
| `PUT` | `/api/clientes/{id}` | Edita cliente |

---

### Pedidos

| Método | Rota | Descrição |
|---|---|---|
| `GET` | `/api/pedidos` | Lista com filtros e paginação |
| `GET` | `/api/pedidos/recentes` | Pedidos recentes (dashboard) |
| `GET` | `/api/pedidos/{id}` | Detalhes |
| `POST` | `/api/pedidos` | Cria pedido (status `ABERTO`) |
| `POST` | `/api/pedidos/orcamentos` | Cria orçamento (status `ORCADO`) |
| `PUT` | `/api/pedidos/{id}` | Edita pedido |
| `PUT` | `/api/pedidos/{id}/orcamento` | Edita orçamento |
| `PATCH` | `/api/pedidos/{id}/converter-em-pedido` | Converte orçamento em pedido |
| `PATCH` | `/api/pedidos/{id}/cancelar` | Cancela (ADMIN / GERENTE) |
| `PATCH` | `/api/pedidos/{id}/finalizar` | Finaliza (ADMIN / GERENTE) |
| `PATCH` | `/api/pedidos/{id}/estornar` | Devolução complementar em pedido cancelado |

**Filtros de listagem:**

| Parâmetro | Tipo | Descrição |
|---|---|---|
| `ano` | int | 2000 – 2100 |
| `mes` | int | 1 – 12 |
| `inicio` / `fim` | DateOnly | Intervalo de datas (`YYYY-MM-DD`) |
| `status` | string | `ORCADO`, `ABERTO`, `FINALIZADO`, `CANCELADO` |
| `pagina` | int | Padrão: 1 |
| `tamanhoPagina` | int | Padrão: 10 |

**Ciclo de vida:**

```
ORCADO ──converter-em-pedido──► ABERTO ──finalizar──► FINALIZADO
                                        ──cancelar──► CANCELADO
```

---

### Estoque / Caixa / Financeiro

| Método | Rota | Descrição |
|---|---|---|
| `GET` | `/api/estoque` | Consulta estoque |
| `GET` | `/api/caixa` | Movimentações do caixa |
| `GET` | `/api/financeiro` | Visão financeira consolidada |

---

### Logs

| Método | Rota | Perfil mínimo | Descrição |
|---|---|---|---|
| `GET` | `/api/logs` | GERENTE | Auditoria de operações |

---

### Health

| Método | Rota | Descrição |
|---|---|---|
| `GET` | `/api/health` | Status da API |
| `GET` | `/api/health/database` | Conectividade com o banco |

---

## Perfis de acesso

| Perfil | Permissões |
|---|---|
| `ADMIN` | Acesso total — gestão de usuários, cancelar, finalizar, logs |
| `GERENTE` | Operações completas + logs; sem gestão de usuários |
| `OPERACIONAL` | Pedidos, clientes, estoque, caixa e financeiro |

---

## Padrão de erros

Erros de validação de modelo (400):

```json
{
  "mensagem": "Existem campos invalidos na requisicao.",
  "erros": { "campo": ["Mensagem de erro"] }
}
```

Erros de negócio (400 / 401 / 403 / 404):

```json
{ "mensagem": "Descrição do erro em português." }
```

Enums são serializados como strings (`"ADMIN"`, `"ABERTO"`, etc.).

---

## Decisões de arquitetura

- **ADO.NET puro** — sem EF Core ou Dapper; toda query SQL fica nos repositórios em `PrintGest.Infrastructure`.
- **Unit of Work** — toda operação de escrita usa `BeginTransaction` / `Commit` / `Rollback`.
- **Mapeamento centralizado** — `Mapping.cs` converte `IDataReader` → entidade de domínio.
- **Auditoria automática** — toda operação de escrita registra uma entrada em `logs_sistema`.
- **ClockSkew zero** — tokens JWT expiram exatamente no horário configurado, sem margem extra.

---

## Testes

```bash
dotnet test
```

---

## CORS

Em desenvolvimento, a API aceita requisições de `localhost:5173`, `localhost:5174` e variantes com `127.0.0.1`. Para produção, atualize a política `PrintGestWeb` em `Program.cs` com o domínio real do frontend.
