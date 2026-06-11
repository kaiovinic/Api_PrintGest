using Microsoft.EntityFrameworkCore;
using PrintGest.Application.Abstractions;
using PrintGest.Domain.Entities;
using PrintGest.Infrastructure.Data;

namespace PrintGest.Infrastructure.Repositories;

public sealed class EstoqueRepository(PrintGestDbContext context) : IEstoqueRepository
{
    public async Task<ResultadoPaginado<ProdutoEstoqueDto>> ListarProdutosAsync(int pagina, int tamanhoPagina, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(pagina, 1);
        var size = Math.Clamp(tamanhoPagina, 5, 100);
        var offset = (page - 1) * size;

        var query = context.ProdutosEstoque.AsNoTracking();
        var total = await query.CountAsync(cancellationToken);

        var items = await query.OrderBy(p => p.Nome)
                               .Skip(offset)
                               .Take(size)
                               .ToListAsync(cancellationToken);

        var dtos = items.Select(p => new ProdutoEstoqueDto(
            p.Id,
            p.Nome,
            p.Categoria,
            p.Tamanho,
            p.Unidade,
            p.QuantidadeAtual,
            p.EstoqueMinimo,
            p.CustoUnitario,
            p.QuantidadeAtual * p.CustoUnitario,
            p.Fornecedor,
            p.Observacao,
            p.Ativo
        )).ToList();

        var totalPaginas = total == 0 ? 1 : (int)Math.Ceiling(total / (double)size);
        return new ResultadoPaginado<ProdutoEstoqueDto>(dtos, total, page, size, totalPaginas);
    }

    public async Task<long> CriarProdutoAsync(ProdutoEstoqueRequest request, CancellationToken cancellationToken = default)
    {
        var produto = new ProdutoEstoque(
            0,
            request.Nome.Trim(),
            request.Categoria.Trim(),
            request.Tamanho,
            request.Unidade.Trim(),
            0,
            request.EstoqueMinimo,
            0,
            request.Fornecedor,
            request.Observacao,
            true
        );

        context.ProdutosEstoque.Add(produto);
        await context.SaveChangesAsync(cancellationToken);
        return produto.Id;
    }

    public async Task<bool> EditarProdutoAsync(long id, ProdutoEstoqueRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await context.ProdutosEstoque.FindAsync(new object[] { id }, cancellationToken);
        if (existing == null)
        {
            return false;
        }

        var updated = existing with
        {
            Nome = request.Nome.Trim(),
            Categoria = request.Categoria.Trim(),
            Tamanho = request.Tamanho,
            Unidade = request.Unidade.Trim(),
            EstoqueMinimo = request.EstoqueMinimo,
            Fornecedor = request.Fornecedor,
            Observacao = request.Observacao
        };

        context.Entry(existing).CurrentValues.SetValues(updated);
        return await context.SaveChangesAsync(cancellationToken) > 0;
    }

    public async Task<IReadOnlyList<CategoriaEstoqueDto>> ListarCategoriasAsync(CancellationToken cancellationToken = default)
    {
        var catList = await context.CategoriasEstoque
            .Where(c => c.Ativo)
            .Select(c => c.Nome)
            .Union(context.ProdutosEstoque.Where(p => p.Categoria != null && p.Categoria != "").Select(p => p.Categoria))
            .OrderBy(c => c)
            .ToListAsync(cancellationToken);

        return catList.Select(c => new CategoriaEstoqueDto(c)).ToList();
    }

    public async Task CriarCategoriaAsync(CategoriaEstoqueRequest request, CancellationToken cancellationToken = default)
    {
        var nome = request.Nome.Trim();
        var existing = await context.CategoriasEstoque.FirstOrDefaultAsync(c => c.Nome == nome, cancellationToken);
        if (existing != null)
        {
            if (!existing.Ativo)
            {
                var updated = existing with { Ativo = true };
                context.Entry(existing).CurrentValues.SetValues(updated);
                await context.SaveChangesAsync(cancellationToken);
            }
        }
        else
        {
            var categoria = new CategoriaEstoque(0, nome, true, DateTime.UtcNow);
            context.CategoriasEstoque.Add(categoria);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RegistrarMovimentacaoAsync(MovimentacaoEstoqueRequest request, CancellationToken cancellationToken = default)
    {
        if (request.PedidoId.HasValue)
        {
            var pedidoExiste = await context.Pedidos.AnyAsync(p => p.Id == request.PedidoId.Value, cancellationToken);
            if (!pedidoExiste)
            {
                throw new InvalidOperationException("Pedido vinculado nao encontrado. Informe um pedido existente ou deixe o campo vazio.");
            }
        }

        var produto = await context.ProdutosEstoque.FindAsync(new object[] { request.ProdutoId }, cancellationToken);
        if (produto == null)
        {
            throw new InvalidOperationException("Produto nao encontrado.");
        }

        var movimentacao = new MovimentacaoEstoque(
            0,
            request.ProdutoId,
            request.PedidoId,
            request.UsuarioId,
            request.Tipo,
            request.Quantidade,
            request.CustoUnitario,
            request.Observacao,
            DateTime.UtcNow
        );
        context.MovimentacoesEstoque.Add(movimentacao);

        var isEntrada = request.Tipo.Equals("ENTRADA", StringComparison.OrdinalIgnoreCase);
        ProdutoEstoque updatedProduto;
        if (isEntrada)
        {
            var custo = request.CustoUnitario ?? 0m;
            var novaQuantidade = produto.QuantidadeAtual + request.Quantidade;
            var novoCusto = novaQuantidade <= 0 ? custo 
                : ((produto.QuantidadeAtual * produto.CustoUnitario) + (request.Quantidade * custo)) / novaQuantidade;

            updatedProduto = produto with
            {
                QuantidadeAtual = novaQuantidade,
                CustoUnitario = novoCusto
            };
        }
        else
        {
            updatedProduto = produto with
            {
                QuantidadeAtual = produto.QuantidadeAtual - request.Quantidade
            };
        }

        context.Entry(produto).CurrentValues.SetValues(updatedProduto);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ResultadoPaginado<MovimentacaoEstoqueDto>> ListarMovimentacoesAsync(int pagina, int tamanhoPagina, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(pagina, 1);
        var size = Math.Clamp(tamanhoPagina, 5, 100);
        var offset = (page - 1) * size;

        var total = await context.MovimentacoesEstoque.CountAsync(cancellationToken);
        var totalPaginas = total == 0 ? 1 : (int)Math.Ceiling(total / (double)size);

        var query = from m in context.MovimentacoesEstoque
                    join p in context.ProdutosEstoque on m.ProdutoId equals p.Id
                    join u in context.Usuarios on m.UsuarioId equals u.Id
                    orderby m.MovimentadoEm descending
                    select new { m, ProdutoNome = p.Nome, ProdutoTamanho = p.Tamanho, UsuarioNome = u.Nome };

        var items = await query.Skip(offset).Take(size).ToListAsync(cancellationToken);

        var dtos = items.Select(i => {
            decimal? totalVal = i.m.CustoUnitario.HasValue ? i.m.Quantidade * i.m.CustoUnitario.Value : null;
            return new MovimentacaoEstoqueDto(
                i.m.Id,
                i.m.Tipo,
                i.m.Quantidade,
                i.m.CustoUnitario,
                totalVal,
                i.ProdutoNome,
                i.ProdutoTamanho,
                i.m.ProdutoId,
                i.UsuarioNome,
                i.m.PedidoId,
                i.m.MovimentadoEm,
                i.m.Observacao
            );
        }).ToList();

        return new ResultadoPaginado<MovimentacaoEstoqueDto>(dtos, total, page, size, totalPaginas);
    }

    public async Task EditarMovimentacaoAsync(long id, EditarMovimentacaoRequest request, CancellationToken cancellationToken = default)
    {
        var movimentacao = await context.MovimentacoesEstoque.FindAsync(new object[] { id }, cancellationToken);
        if (movimentacao == null)
        {
            throw new InvalidOperationException("Movimentacao nao encontrada.");
        }

        var produto = await context.ProdutosEstoque.FindAsync(new object[] { movimentacao.ProdutoId }, cancellationToken);
        if (produto == null)
        {
            throw new InvalidOperationException("Produto nao encontrado.");
        }

        var wasEntrada = movimentacao.Tipo.Equals("ENTRADA", StringComparison.OrdinalIgnoreCase);
        int qtdAtual = produto.QuantidadeAtual;
        if (wasEntrada)
        {
            qtdAtual -= movimentacao.Quantidade;
        }
        else
        {
            qtdAtual += movimentacao.Quantidade;
        }

        var isNewEntrada = request.Tipo.Equals("ENTRADA", StringComparison.OrdinalIgnoreCase);
        decimal custoAtual = produto.CustoUnitario;
        if (isNewEntrada)
        {
            qtdAtual += request.Quantidade;
            custoAtual = request.CustoUnitario ?? 0m;
        }
        else
        {
            qtdAtual -= request.Quantidade;
        }

        var updatedProduto = produto with
        {
            QuantidadeAtual = qtdAtual,
            CustoUnitario = custoAtual
        };
        context.Entry(produto).CurrentValues.SetValues(updatedProduto);

        var updatedMovimentacao = movimentacao with
        {
            Tipo = request.Tipo,
            Quantidade = request.Quantidade,
            CustoUnitario = request.CustoUnitario,
            PedidoId = request.PedidoId,
            Observacao = request.Observacao
        };
        context.Entry(movimentacao).CurrentValues.SetValues(updatedMovimentacao);

        await context.SaveChangesAsync(cancellationToken);
    }
}
