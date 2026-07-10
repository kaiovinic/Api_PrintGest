using Microsoft.EntityFrameworkCore;
using PrintGest.Application.Abstractions;
using PrintGest.Domain.Entities;
using PrintGest.Infrastructure.Data;

namespace PrintGest.Infrastructure.Repositories;

public sealed class CaixaRepository(PrintGestDbContext context) : ICaixaRepository
{
    public async Task<CaixaResumoDto> ObterResumoAsync(DateOnly? inicio, DateOnly? fim, CancellationToken cancellationToken = default)
    {
        var (dtInicio, dtFim) = ResolverPeriodo(inicio, fim);

        var pagamentosQuery = context.Pagamentos
            .Where(p => p.RegistradoEm >= dtInicio && p.RegistradoEm <= dtFim)
            .Select(p => new { Tipo = "ENTRADA", FormaPagamento = p.FormaPagamento, Valor = p.ValorTotal });

        var manualQuery = context.CaixaMovimentacoes
            .Where(m => m.MovimentadoEm >= dtInicio && m.MovimentadoEm <= dtFim)
            .Select(m => new { Tipo = m.Tipo, FormaPagamento = m.FormaPagamento, Valor = m.Valor });

        var combined = await pagamentosQuery.Union(manualQuery).ToListAsync(cancellationToken);

        decimal entradas = combined.Where(x => x.Tipo == "ENTRADA").Sum(x => x.Valor);
        decimal saidas = combined.Where(x => x.Tipo == "SAIDA").Sum(x => x.Valor);
        decimal saldo = entradas - saidas;

        decimal dinheiro = combined.Where(x => x.Tipo == "ENTRADA" && x.FormaPagamento == "DINHEIRO").Sum(x => x.Valor);
        decimal pix = combined.Where(x => x.Tipo == "ENTRADA" && x.FormaPagamento == "PIX").Sum(x => x.Valor);
        decimal cartaoCredito = combined.Where(x => x.Tipo == "ENTRADA" && x.FormaPagamento == "CARTAO_CREDITO").Sum(x => x.Valor);
        decimal cartaoDebito = combined.Where(x => x.Tipo == "ENTRADA" && x.FormaPagamento == "CARTAO_DEBITO").Sum(x => x.Valor);

        return new CaixaResumoDto(entradas, saidas, saldo, dinheiro, pix, cartaoCredito, cartaoDebito);
    }

    public async Task<ResultadoPaginado<CaixaMovimentacaoDto>> ListarMovimentacoesAsync(DateOnly? inicio, DateOnly? fim, int pagina, int tamanhoPagina, CancellationToken cancellationToken = default)
    {
        var (dtInicio, dtFim) = ResolverPeriodo(inicio, fim);
        var page = Math.Max(pagina, 1);
        var size = Math.Clamp(tamanhoPagina, 5, 100);
        var offset = (page - 1) * size;

        var pagamentosListQuery = from p in context.Pagamentos
                                  join ped in context.Pedidos on p.PedidoId equals ped.Id
                                  join c in context.Clientes on ped.ClienteId equals c.Id
                                  join u in context.Usuarios on p.RegistradoPorUsuarioId equals u.Id
                                  where p.RegistradoEm >= dtInicio && p.RegistradoEm <= dtFim
                                  select new {
                                      Id = "PAG-" + p.Id,
                                      PedidoId = (long?)p.PedidoId,
                                      Tipo = "ENTRADA",
                                      FormaPagamento = p.FormaPagamento,
                                      Categoria = "Pedido",
                                      Descricao = "Pagamento do pedido " + ped.Numero + " - " + c.Nome,
                                      Valor = p.ValorTotal,
                                      MovimentadoEm = p.RegistradoEm,
                                      Usuario = u.Nome,
                                      Observacao = p.Observacao,
                                      Origem = "PEDIDO"
                                  };

        var manualListQuery = from m in context.CaixaMovimentacoes
                              join u in context.Usuarios on m.UsuarioId equals u.Id
                              where m.MovimentadoEm >= dtInicio && m.MovimentadoEm <= dtFim
                              select new {
                                  Id = "CX-" + m.Id,
                                  PedidoId = (long?)null,
                                  Tipo = m.Tipo,
                                  FormaPagamento = m.FormaPagamento,
                                  Categoria = m.Categoria,
                                  Descricao = m.Descricao,
                                  Valor = m.Valor,
                                  MovimentadoEm = m.MovimentadoEm,
                                  Usuario = u.Nome,
                                  Observacao = m.Observacao,
                                  Origem = "MANUAL"
                              };

        var combinedQuery = pagamentosListQuery.Union(manualListQuery);

        var total = await combinedQuery.CountAsync(cancellationToken);
        var totalPaginas = total == 0 ? 1 : (int)Math.Ceiling(total / (double)size);

        var items = await combinedQuery
            .OrderByDescending(x => x.MovimentadoEm)
            .Skip(offset)
            .Take(size)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(x => new CaixaMovimentacaoDto(
            x.Id,
            x.PedidoId,
            x.Tipo,
            x.FormaPagamento,
            x.Categoria,
            x.Descricao,
            x.Valor,
            x.MovimentadoEm,
            x.Usuario,
            x.Observacao,
            x.Origem
        )).ToList();

        return new ResultadoPaginado<CaixaMovimentacaoDto>(dtos, total, page, size, totalPaginas);
    }

    public async Task<long> CriarMovimentacaoAsync(CaixaMovimentacaoRequest request, CancellationToken cancellationToken = default)
    {
        var tipo = request.Tipo.ToUpperInvariant();
        if (tipo == "ENTRADA" && request.PedidoId is not null)
        {
            return await RegistrarPagamentoPedido(request, cancellationToken);
        }

        var movimentacao = new CaixaMovimentacao(
            0,
            request.PedidoId,
            tipo,
            NormalizarFormaPagamento(request.FormaPagamento),
            request.Categoria.Trim(),
            request.Descricao.Trim(),
            request.Valor,
            request.UsuarioId,
            request.Observacao,
            DateTime.UtcNow
        );

        context.CaixaMovimentacoes.Add(movimentacao);
        await context.SaveChangesAsync(cancellationToken);
        return movimentacao.Id;
    }

    private async Task<long> RegistrarPagamentoPedido(CaixaMovimentacaoRequest request, CancellationToken cancellationToken)
    {
        var pedido = await context.Pedidos.FirstOrDefaultAsync(p => p.Id == request.PedidoId!.Value, cancellationToken);
        if (pedido == null)
        {
            throw new InvalidOperationException("Pedido nao encontrado.");
        }

        if (pedido.Tipo != "PEDIDO")
        {
            throw new InvalidOperationException("Somente pedidos podem receber pagamento no caixa.");
        }

        if (pedido.Status is "CANCELADO" or "FINALIZADO")
        {
            throw new InvalidOperationException("Nao e possivel registrar pagamento para pedido cancelado ou finalizado.");
        }

        if (request.Valor > pedido.SaldoDevedor)
        {
            throw new InvalidOperationException("O valor informado e maior que o saldo devedor do pedido.");
        }

        var pagamento = new Pagamento(
            0,
            request.PedidoId!.Value,
            request.UsuarioId,
            NormalizarFormaPagamento(request.FormaPagamento),
            "PAGAMENTO_NO_PEDIDO",
            request.Valor,
            request.Observacao ?? request.Descricao,
            DateTime.UtcNow
        );
        context.Pagamentos.Add(pagamento);

        var updatedPedido = pedido with
        {
            ValorPago = pedido.ValorPago + request.Valor,
            SaldoDevedor = pedido.SaldoDevedor - request.Valor
        };
        context.Entry(pedido).CurrentValues.SetValues(updatedPedido);

        await context.SaveChangesAsync(cancellationToken);
        return pagamento.Id;
    }

    public async Task<bool> DeletarMovimentacaoAsync(string id, long usuarioId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;

        if (id.StartsWith("PAG-", StringComparison.OrdinalIgnoreCase))
        {
            if (!long.TryParse(id.Substring(4), out var pagamentoId)) return false;

            var pagamento = await context.Pagamentos.FirstOrDefaultAsync(p => p.Id == pagamentoId, cancellationToken);
            if (pagamento == null) return false;

            var pedido = await context.Pedidos.FirstOrDefaultAsync(p => p.Id == pagamento.PedidoId, cancellationToken);
            if (pedido != null)
            {
                if (pedido.Status == "CANCELADO")
                {
                    throw new InvalidOperationException("Não é possível remover pagamento de um pedido cancelado.");
                }

                var updatedPedido = pedido with
                {
                    ValorPago = Math.Max(0, pedido.ValorPago - pagamento.ValorTotal),
                    SaldoDevedor = pedido.SaldoDevedor + pagamento.ValorTotal
                };
                context.Entry(pedido).CurrentValues.SetValues(updatedPedido);
            }

            context.Pagamentos.Remove(pagamento);

            context.LogsSistema.Add(new LogSistema(
                0,
                usuarioId,
                "Caixa",
                pagamento.Id,
                "EXCLUSAO",
                $"Estorno/Exclusão do pagamento PAG-{pagamento.Id} de {pagamento.ValorTotal:C} do pedido #{pedido?.Numero ?? pagamento.PedidoId.ToString()}.",
                DateTime.UtcNow
            ));

            return await context.SaveChangesAsync(cancellationToken) > 0;
        }
        else if (id.StartsWith("CX-", StringComparison.OrdinalIgnoreCase))
        {
            if (!long.TryParse(id.Substring(3), out var movimentacaoId)) return false;

            var movimentacao = await context.CaixaMovimentacoes.FirstOrDefaultAsync(m => m.Id == movimentacaoId, cancellationToken);
            if (movimentacao == null) return false;

            context.CaixaMovimentacoes.Remove(movimentacao);

            context.LogsSistema.Add(new LogSistema(
                0,
                usuarioId,
                "Caixa",
                movimentacao.Id,
                "EXCLUSAO",
                $"Cancelamento da movimentação manual CX-{movimentacao.Id} ({movimentacao.Tipo}) de {movimentacao.Valor:C} — {movimentacao.Descricao}.",
                DateTime.UtcNow
            ));

            return await context.SaveChangesAsync(cancellationToken) > 0;
        }

        return false;
    }

    private static (DateTime Inicio, DateTime Fim) ResolverPeriodo(DateOnly? inicio, DateOnly? fim)
    {
        return (
            inicio?.ToDateTime(TimeOnly.MinValue) ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1),
            fim?.ToDateTime(TimeOnly.MaxValue) ?? DateTime.Today.Date.AddDays(1).AddTicks(-1));
    }

    private static string NormalizarFormaPagamento(string formaPagamento)
    {
        return formaPagamento.Trim().ToUpperInvariant() switch
        {
            "DINHEIRO" => "DINHEIRO",
            "PIX" => "PIX",
            "CARTAO_CREDITO" or "CARTAO CREDITO" or "CREDITO" => "CARTAO_CREDITO",
            "CARTAO_DEBITO" or "CARTAO DEBITO" or "DEBITO" => "CARTAO_DEBITO",
            _ => formaPagamento.Trim().ToUpperInvariant()
        };
    }
}
