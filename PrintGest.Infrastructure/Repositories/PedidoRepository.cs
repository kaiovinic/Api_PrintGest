using Microsoft.EntityFrameworkCore;
using PrintGest.Application.Abstractions;
using PrintGest.Domain.Entities;
using PrintGest.Infrastructure.Data;

namespace PrintGest.Infrastructure.Repositories;

public sealed class PedidoRepository(PrintGestDbContext context) : IPedidoRepository
{
    public async Task<ResultadoPaginado<PedidoResumo>> ListAsync(PedidoFiltro filtro, CancellationToken cancellationToken = default)
    {
        var (inicioDate, fimDate) = ResolverPeriodo(filtro.Inicio, filtro.Fim, filtro.Ano, filtro.Mes);
        var page = Math.Max(filtro.Pagina, 1);
        var size = Math.Clamp(filtro.TamanhoPagina, 5, 100);
        var offset = (page - 1) * size;

        var query = from p in context.Pedidos
                    join c in context.Clientes on p.ClienteId equals c.Id
                    join u in context.Usuarios on p.CriadoPorUsuarioId equals u.Id
                    where p.DataPedido >= inicioDate && p.DataPedido <= fimDate
                    select new { p, ClienteNome = c.Nome, CriadoPorNome = u.Nome };

        if (!string.IsNullOrWhiteSpace(filtro.Status))
        {
            query = query.Where(q => q.p.Status == filtro.Status);
        }

        if (!string.IsNullOrWhiteSpace(filtro.Atendente))
        {
            query = query.Where(q => q.CriadoPorNome == filtro.Atendente);
        }

        if (!string.IsNullOrWhiteSpace(filtro.Cliente))
        {
            var cleanCliente = filtro.Cliente.Trim();
            query = query.Where(q => q.ClienteNome.Contains(cleanCliente));
        }

        var total = await query.CountAsync(cancellationToken);
        var totalPaginas = total == 0 ? 1 : (int)Math.Ceiling(total / (double)size);

        var items = await query
            .OrderByDescending(q => q.p.DataPedido)
            .ThenByDescending(q => q.p.Id)
            .Skip(offset)
            .Take(size)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(x => new PedidoResumo(
            x.p.Id,
            x.p.Numero,
            x.ClienteNome,
            Mapping.TipoPedido(x.p.Tipo),
            Mapping.StatusPedido(x.p.Status),
            x.p.DataPedido,
            x.p.DataEntrega,
            x.p.Total,
            x.p.ValorPago,
            x.p.ValorEstornado,
            x.p.SaldoDevedor,
            x.CriadoPorNome,
            x.p.MotivoCancelamento
        )).ToList();

        return new ResultadoPaginado<PedidoResumo>(dtos, total, page, size, totalPaginas);
    }

    public async Task<IReadOnlyList<PedidoResumo>> ListRecentAsync(CancellationToken cancellationToken = default)
    {
        var query = from p in context.Pedidos
                    join c in context.Clientes on p.ClienteId equals c.Id
                    join u in context.Usuarios on p.CriadoPorUsuarioId equals u.Id
                    where p.Status == "ABERTO" || p.Status == "ORCADO"
                    orderby p.DataPedido descending, p.Id descending
                    select new { p, ClienteNome = c.Nome, CriadoPorNome = u.Nome };

        var items = await query.Take(20).ToListAsync(cancellationToken);

        return items.Select(x => new PedidoResumo(
            x.p.Id,
            x.p.Numero,
            x.ClienteNome,
            Mapping.TipoPedido(x.p.Tipo),
            Mapping.StatusPedido(x.p.Status),
            x.p.DataPedido,
            x.p.DataEntrega,
            x.p.Total,
            x.p.ValorPago,
            x.p.ValorEstornado,
            x.p.SaldoDevedor,
            x.CriadoPorNome,
            x.p.MotivoCancelamento
        )).ToList();
    }

    public async Task<IReadOnlyList<PedidoResumo>> ListPendingDeliveriesAsync(string? atendente, CancellationToken cancellationToken = default)
    {
        var query = from p in context.Pedidos
                    join c in context.Clientes on p.ClienteId equals c.Id
                    join u in context.Usuarios on p.CriadoPorUsuarioId equals u.Id
                    where p.Status == "ABERTO" && p.DataEntrega != null
                    select new { p, ClienteNome = c.Nome, CriadoPorNome = u.Nome };

        if (!string.IsNullOrWhiteSpace(atendente))
        {
            query = query.Where(q => q.CriadoPorNome == atendente);
        }

        var items = await query
            .OrderBy(q => q.p.DataEntrega)
            .ThenBy(q => q.p.Id)
            .ToListAsync(cancellationToken);

        return items.Select(x => new PedidoResumo(
            x.p.Id,
            x.p.Numero,
            x.ClienteNome,
            Mapping.TipoPedido(x.p.Tipo),
            Mapping.StatusPedido(x.p.Status),
            x.p.DataPedido,
            x.p.DataEntrega,
            x.p.Total,
            x.p.ValorPago,
            x.p.ValorEstornado,
            x.p.SaldoDevedor,
            x.CriadoPorNome,
            x.p.MotivoCancelamento
        )).ToList();
    }

    public async Task<PedidoDetalhe?> GetDetailsByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var pedido = await context.Pedidos
            .Include(p => p.Itens)
            .Include(p => p.Pagamentos)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (pedido == null)
        {
            return null;
        }

        var cliente = await context.Clientes.FindAsync(new object[] { pedido.ClienteId }, cancellationToken);
        var criadoPor = await context.Usuarios.FindAsync(new object[] { pedido.CriadoPorUsuarioId }, cancellationToken);

        var itens = pedido.Itens.Select(i => new ItemPedidoDetalhe(
            i.Id,
            i.Descricao,
            i.Tamanho,
            i.Quantidade,
            i.ValorUnitario,
            i.ValorTotal
        )).ToList();

        var pagamentosIds = pedido.Pagamentos.Select(pg => pg.RegistradoPorUsuarioId).Distinct().ToList();
        var usuariosPagamento = await context.Usuarios
            .Where(u => pagamentosIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Nome, cancellationToken);

        var pagamentos = pedido.Pagamentos.Select(pg => new PagamentoDetalhe(
            pg.Id,
            pg.FormaPagamento,
            pg.CondicaoPagamento,
            pg.ValorTotal,
            pg.Observacao,
            pg.RegistradoEm,
            usuariosPagamento.TryGetValue(pg.RegistradoPorUsuarioId, out var nome) ? nome : "Desconhecido"
        )).OrderByDescending(pg => pg.RegistradoEm).ThenByDescending(pg => pg.Id).ToList();

        string? formaPagamentoExibicao = pedido.FormaPagamento;
        if (pedido.Pagamentos != null && pedido.Pagamentos.Any())
        {
            var formasUnicas = pedido.Pagamentos
                .Select(p => p.FormaPagamento)
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct()
                .ToList();
            if (formasUnicas.Count > 1)
            {
                formaPagamentoExibicao = string.Join(" e ", formasUnicas);
            }
            else if (formasUnicas.Count == 1)
            {
                formaPagamentoExibicao = formasUnicas[0];
            }
        }

        decimal valorRetido = Math.Max(pedido.ValorPago - pedido.ValorEstornado, 0);

        return new PedidoDetalhe(
            pedido.Id,
            pedido.Numero,
            pedido.ClienteId,
            cliente?.Nome ?? "Desconhecido",
            cliente?.Empresa,
            cliente?.CpfCnpj,
            cliente?.Telefone,
            cliente?.Endereco,
            cliente?.Cidade,
            pedido.CriadoPorUsuarioId,
            pedido.Tipo,
            pedido.Status,
            pedido.DataPedido,
            pedido.DataEntrega,
            pedido.Vendedor,
            formaPagamentoExibicao,
            pedido.CondicaoPagamento,
            pedido.Frente,
            pedido.Fundo,
            pedido.Observacao,
            pedido.MotivoCancelamento,
            pedido.ValorEstornado,
            valorRetido,
            pedido.ObservacaoEstorno,
            null, // outros_itens
            pedido.Total,
            pedido.ValorPago,
            pedido.SaldoDevedor,
            itens,
            pagamentos
        );
    }

    public async Task<long> CriarPedidoAsync(PedidoCadastroDto request, string tipo, string status, CancellationToken cancellationToken = default)
    {
        var total = request.Total;
        var valorPago1 = request.ValorPago;
        var valorPago2 = request.ValorPago2 ?? 0;
        var totalValorPago = valorPago1 + valorPago2;
        var saldoDevedor = total - totalValorPago;

        var clienteId = await SalvarOuAtualizarClienteAsync(request, cancellationToken);

        string formaPagamentoSalvar = NormalizarFormaPagamento(request.FormaPagamento);

        var pedido = new Pedido(
            0,
            request.Numero.Trim(),
            clienteId,
            request.UsuarioId,
            tipo,
            status,
            request.DataPedido,
            request.DataEntrega,
            request.Vendedor?.Trim(),
            formaPagamentoSalvar,
            NormalizarCondicaoPagamento(request.CondicaoPagamento),
            request.Frente?.Trim(),
            request.Fundo?.Trim(),
            null,
            null,
            request.Observacao?.Trim(),
            null,
            0,
            total,
            total,
            totalValorPago,
            saldoDevedor,
            null,
            null, null, null, null
        );

        foreach (var item in request.Itens)
        {
            pedido.Itens.Add(new ItemPedido(
                0,
                0,
                item.Descricao.Trim(),
                item.Tamanho?.Trim(),
                item.Quantidade,
                item.ValorUnitario,
                item.ValorTotal
            ));
        }

        if (tipo == "PEDIDO")
        {
            if (valorPago1 > 0)
            {
                pedido.Pagamentos.Add(new Pagamento(
                    0,
                    0,
                    request.UsuarioId,
                    NormalizarFormaPagamento(request.FormaPagamento),
                    NormalizarCondicaoPagamento(request.CondicaoPagamento),
                    valorPago1,
                    "Entrada registrada na criação do pedido",
                    DateTime.UtcNow
                ));
            }

            if (valorPago2 > 0 && !string.IsNullOrWhiteSpace(request.FormaPagamento2))
            {
                pedido.Pagamentos.Add(new Pagamento(
                    0,
                    0,
                    request.UsuarioId,
                    NormalizarFormaPagamento(request.FormaPagamento2),
                    NormalizarCondicaoPagamento(request.CondicaoPagamento),
                    valorPago2,
                    "Entrada registrada na criação do pedido (segunda forma)",
                    DateTime.UtcNow
                ));
            }
        }

        context.Pedidos.Add(pedido);
        await context.SaveChangesAsync(cancellationToken);
        return pedido.Id;
    }

    public async Task<bool> EditarOrcamentoAsync(long id, PedidoCadastroDto request, CancellationToken cancellationToken = default)
    {
        var existing = await context.Pedidos
            .Include(p => p.Itens)
            .FirstOrDefaultAsync(p => p.Id == id && p.Tipo == "ORCAMENTO", cancellationToken);

        if (existing == null)
        {
            return false;
        }

        if (existing.Status is "CANCELADO" or "FINALIZADO")
        {
            return false;
        }

        var total = request.Total;
        var saldoDevedor = total - existing.ValorPago;

        var clienteId = await SalvarOuAtualizarClienteAsync(request, cancellationToken);

        var updated = existing with
        {
            Numero = request.Numero.Trim(),
            ClienteId = clienteId,
            DataPedido = request.DataPedido,
            DataEntrega = request.DataEntrega,
            Vendedor = request.Vendedor?.Trim(),
            FormaPagamento = NormalizarFormaPagamento(request.FormaPagamento),
            CondicaoPagamento = NormalizarCondicaoPagamento(request.CondicaoPagamento),
            Frente = request.Frente?.Trim(),
            Fundo = request.Fundo?.Trim(),
            Observacao = request.Observacao?.Trim(),
            Subtotal = total,
            Total = total,
            SaldoDevedor = saldoDevedor
        };

        context.Entry(existing).CurrentValues.SetValues(updated);

        context.ItensPedido.RemoveRange(existing.Itens);
        existing.Itens.Clear();

        foreach (var item in request.Itens)
        {
            existing.Itens.Add(new ItemPedido(
                0,
                id,
                item.Descricao.Trim(),
                item.Tamanho?.Trim(),
                item.Quantidade,
                item.ValorUnitario,
                item.ValorTotal
            ));
        }

        return await context.SaveChangesAsync(cancellationToken) > 0;
    }

    public async Task<bool> EditarPedidoAsync(long id, PedidoCadastroDto request, CancellationToken cancellationToken = default)
    {
        var existing = await context.Pedidos
            .Include(p => p.Itens)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (existing == null)
        {
            return false;
        }

        if (existing.Status is "CANCELADO" or "FINALIZADO")
        {
            return false;
        }

        var eraOrcamento = existing.Tipo == "ORCAMENTO";

        var total = request.Total;
        var valorPago1 = request.ValorPago;
        var valorPago2 = request.ValorPago2 ?? 0;
        var totalValorPago = valorPago1 + valorPago2;
        var saldoDevedor = total - totalValorPago;

        var clienteId = await SalvarOuAtualizarClienteAsync(request, cancellationToken);

        string formaPagamentoSalvar = NormalizarFormaPagamento(request.FormaPagamento);

        var updated = existing with
        {
            Tipo = "PEDIDO",
            Status = existing.Status == "ORCADO" ? "ABERTO" : existing.Status,
            Numero = request.Numero.Trim(),
            ClienteId = clienteId,
            DataPedido = request.DataPedido,
            DataEntrega = request.DataEntrega,
            Vendedor = request.Vendedor?.Trim(),
            FormaPagamento = formaPagamentoSalvar,
            CondicaoPagamento = NormalizarCondicaoPagamento(request.CondicaoPagamento),
            Frente = request.Frente?.Trim(),
            Fundo = request.Fundo?.Trim(),
            Observacao = request.Observacao?.Trim(),
            Subtotal = total,
            Total = total,
            ValorPago = totalValorPago,
            SaldoDevedor = saldoDevedor
        };

        context.Entry(existing).CurrentValues.SetValues(updated);

        context.ItensPedido.RemoveRange(existing.Itens);
        existing.Itens.Clear();

        foreach (var item in request.Itens)
        {
            existing.Itens.Add(new ItemPedido(
                0,
                id,
                item.Descricao.Trim(),
                item.Tamanho?.Trim(),
                item.Quantidade,
                item.ValorUnitario,
                item.ValorTotal
            ));
        }

        if (eraOrcamento)
        {
            if (valorPago1 > 0)
            {
                context.Pagamentos.Add(new Pagamento(
                    0,
                    id,
                    request.UsuarioId,
                    NormalizarFormaPagamento(request.FormaPagamento),
                    NormalizarCondicaoPagamento(request.CondicaoPagamento),
                    valorPago1,
                    "Entrada registrada na criação do pedido",
                    DateTime.UtcNow
                ));
            }

            if (valorPago2 > 0 && !string.IsNullOrWhiteSpace(request.FormaPagamento2))
            {
                context.Pagamentos.Add(new Pagamento(
                    0,
                    id,
                    request.UsuarioId,
                    NormalizarFormaPagamento(request.FormaPagamento2),
                    NormalizarCondicaoPagamento(request.CondicaoPagamento),
                    valorPago2,
                    "Entrada registrada na criação do pedido (segunda forma)",
                    DateTime.UtcNow
                ));
            }
        }

        return await context.SaveChangesAsync(cancellationToken) > 0;
    }

    public async Task<bool> ConverterEmPedidoAsync(long id, ConverterPedidoDto request, CancellationToken cancellationToken = default)
    {
        var existing = await context.Pedidos.FirstOrDefaultAsync(p => p.Id == id && p.Tipo == "ORCAMENTO", cancellationToken);
        if (existing == null)
        {
            return false;
        }

        var novaForma = request.FormaPagamento;
        var novaCondicao = request.CondicaoPagamento;
        var entrada = request.ValorEntrada;
        var entrada2 = request.ValorEntrada2 ?? 0;
        var totalEntrada = entrada + entrada2;

        string formaPagamentoSalvar = NormalizarFormaPagamento(novaForma);

        var updated = existing with
        {
            Tipo = "PEDIDO",
            Status = "ABERTO",
            FormaPagamento = formaPagamentoSalvar,
            CondicaoPagamento = NormalizarCondicaoPagamento(novaCondicao),
            ValorPago = totalEntrada,
            SaldoDevedor = existing.Total - totalEntrada
        };

        context.Entry(existing).CurrentValues.SetValues(updated);

        if (entrada > 0)
        {
            var pagamento = new Pagamento(
                0,
                id,
                request.UsuarioId,
                NormalizarFormaPagamento(novaForma),
                NormalizarCondicaoPagamento(novaCondicao),
                entrada,
                "Conversão de orçamento em pedido",
                DateTime.UtcNow
            );
            context.Pagamentos.Add(pagamento);
        }

        if (entrada2 > 0 && !string.IsNullOrWhiteSpace(request.FormaPagamento2))
        {
            var pagamento2 = new Pagamento(
                0,
                id,
                request.UsuarioId,
                NormalizarFormaPagamento(request.FormaPagamento2),
                NormalizarCondicaoPagamento(novaCondicao),
                entrada2,
                "Conversão de orçamento em pedido (segunda forma)",
                DateTime.UtcNow
            );
            context.Pagamentos.Add(pagamento2);
        }

        return await context.SaveChangesAsync(cancellationToken) > 0;
    }

    public async Task<bool> CancelarPedidoAsync(long id, AlterarStatusPedidoDto request, CancellationToken cancellationToken = default)
    {
        var existing = await context.Pedidos.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (existing == null)
        {
            return false;
        }

        var updated = existing with
        {
            Status = "CANCELADO",
            CanceladoPorUsuarioId = request.UsuarioId,
            CanceladoEm = DateTime.UtcNow,
            MotivoCancelamento = request.Observacao,
            ValorEstornado = request.ValorDevolvido,
            ObservacaoEstorno = request.ObservacaoEstorno,
            SaldoDevedor = 0
        };

        context.Entry(existing).CurrentValues.SetValues(updated);

        if (request.ValorDevolvido > 0)
        {
            var mov = new CaixaMovimentacao(
                0,
                id,
                "SAIDA",
                request.FormaDevolucao ?? "DINHEIRO",
                "Estorno de pedido",
                $"Devolucao referente ao cancelamento do pedido #{existing.Numero}",
                request.ValorDevolvido,
                request.UsuarioId,
                request.ObservacaoEstorno,
                DateTime.UtcNow
            );
            context.CaixaMovimentacoes.Add(mov);
        }

        return await context.SaveChangesAsync(cancellationToken) > 0;
    }

    public async Task<bool> RegistrarEstornoAsync(long id, EstornarPedidoDto request, CancellationToken cancellationToken = default)
    {
        var existing = await context.Pedidos.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (existing == null)
        {
            return false;
        }

        var updated = existing with
        {
            ValorEstornado = existing.ValorEstornado + request.ValorDevolvido,
            ObservacaoEstorno = string.IsNullOrWhiteSpace(existing.ObservacaoEstorno) 
                ? request.Observacao 
                : existing.ObservacaoEstorno + "\n" + request.Observacao
        };

        context.Entry(existing).CurrentValues.SetValues(updated);

        var mov = new CaixaMovimentacao(
            0,
            id,
            "SAIDA",
            request.FormaDevolucao,
            "Complemento de estorno",
            "Complemento de devolucao do pedido cancelado",
            request.ValorDevolvido,
            request.UsuarioId,
            request.Observacao,
            DateTime.UtcNow
        );
        context.CaixaMovimentacoes.Add(mov);

        return await context.SaveChangesAsync(cancellationToken) > 0;
    }

    public async Task<bool> FinalizarPedidoAsync(long id, FinalizarPedidoDto request, CancellationToken cancellationToken = default)
    {
        var existing = await context.Pedidos.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (existing == null)
        {
            return false;
        }

        var valorPago = existing.ValorPago;
        var saldoDevedor = existing.SaldoDevedor;

        if (request.ReceberSaldo && saldoDevedor > 0)
        {
            var pagamento = new Pagamento(
                0,
                id,
                request.UsuarioId,
                NormalizarFormaPagamento(request.FormaPagamento),
                "PAGAMENTO_NO_PEDIDO",
                saldoDevedor,
                request.Observacao,
                DateTime.UtcNow
            );
            context.Pagamentos.Add(pagamento);

            valorPago += saldoDevedor;
            saldoDevedor = 0;
        }

        var updated = existing with
        {
            Status = "FINALIZADO",
            FinalizadoPorUsuarioId = request.UsuarioId,
            FinalizadoEm = DateTime.UtcNow,
            ValorPago = valorPago,
            SaldoDevedor = saldoDevedor
        };

        context.Entry(existing).CurrentValues.SetValues(updated);

        return await context.SaveChangesAsync(cancellationToken) > 0;
    }

    public async Task<(string Tipo, string Status, decimal ValorPago, decimal SaldoDevedor, DateOnly? DataEntrega)?> ObterEstadoPedidoAsync(long id, CancellationToken cancellationToken = default)
    {
        var pedido = await context.Pedidos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (pedido == null)
        {
            return null;
        }

        return (pedido.Tipo, pedido.Status, pedido.ValorPago, pedido.SaldoDevedor, pedido.DataEntrega);
    }

    private async Task<long> SalvarOuAtualizarClienteAsync(PedidoCadastroDto request, CancellationToken cancellationToken)
    {
        if (request.ClienteId > 0)
        {
            var clienteExistente = await context.Clientes.FindAsync(new object[] { request.ClienteId }, cancellationToken);
            if (clienteExistente != null)
            {
                var updated = clienteExistente with
                {
                    Nome = request.ClienteNome.Trim(),
                    Empresa = request.Empresa?.Trim(),
                    CpfCnpj = request.CpfCnpj?.Trim(),
                    Telefone = request.Telefone?.Trim(),
                    Endereco = request.Endereco?.Trim(),
                    Cidade = request.Cidade?.Trim()
                };
                context.Entry(clienteExistente).CurrentValues.SetValues(updated);
            }
            return request.ClienteId;
        }
        else
        {
            var novoCliente = new Cliente(
                0,
                request.ClienteNome.Trim(),
                request.CpfCnpj?.Trim(),
                request.Empresa?.Trim(),
                request.Telefone?.Trim(),
                null,
                request.Endereco?.Trim(),
                request.Cidade?.Trim()
            );
            context.Clientes.Add(novoCliente);
            await context.SaveChangesAsync(cancellationToken);
            return novoCliente.Id;
        }
    }

    private static string NormalizarFormaPagamento(string? formaPagamento)
    {
        return formaPagamento?.Trim().ToUpperInvariant() switch
        {
            null or "" => "DINHEIRO",
            "PIX" => "PIX",
            "DINHEIRO" => "DINHEIRO",
            "CRÉDITO" or "CREDITO" or "CARTÃO DE CRÉDITO" or "CARTAO DE CREDITO" or "CARTAO_CREDITO" => "CARTAO_CREDITO",
            "DÉBITO" or "DEBITO" or "CARTÃO DE DÉBITO" or "CARTAO DE DEBITO" or "CARTAO_DEBITO" => "CARTAO_DEBITO",
            _ => formaPagamento
        };
    }

    private static string NormalizarCondicaoPagamento(string? condicaoPagamento)
    {
        return condicaoPagamento?.Trim().ToUpperInvariant() switch
        {
            null or "" => "A_VISTA",
            "PAGO" or "À VISTA" or "A VISTA" or "A_VISTA" => "A_VISTA",
            "PAGAMENTO NO PEDIDO" or "PAGAMENTO_NO_PEDIDO" => "PAGAMENTO_NO_PEDIDO",
            "PARCELADO" or "ADIANTAMENTO" => "ADIANTAMENTO",
            "PAGAR NA ENTREGA" => "ADIANTAMENTO",
            _ => condicaoPagamento
        };
    }

    private static (DateOnly Inicio, DateOnly Fim) ResolverPeriodo(DateOnly? inicio, DateOnly? fim, int? ano, int? mes)
    {
        if (inicio.HasValue && fim.HasValue)
        {
            return (inicio.Value, fim.Value);
        }

        var y = ano ?? DateTime.Today.Year;
        var m = mes ?? DateTime.Today.Month;
        var firstDay = new DateOnly(y, m, 1);
        var lastDay = firstDay.AddMonths(1).AddDays(-1);
        return (firstDay, lastDay);
    }
}
