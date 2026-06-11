using Microsoft.EntityFrameworkCore;
using PrintGest.Application.Abstractions;
using PrintGest.Domain.Entities;
using PrintGest.Infrastructure.Data;

namespace PrintGest.Infrastructure.Repositories;

public sealed class FinanceiroRepository(PrintGestDbContext context) : IFinanceiroRepository
{
    public async Task<FinanceiroVendasResult> ObterVendasAsync(FinanceiroFiltro filtro, CancellationToken cancellationToken = default)
    {
        var (inicioDate, fimDate) = ResolverPeriodo(filtro.Inicio, filtro.Fim, filtro.Ano, filtro.Mes);
        var page = Math.Max(filtro.Pagina, 1);
        var size = Math.Clamp(filtro.TamanhoPagina, 5, 100);
        var offset = (page - 1) * size;

        var salesQuery = context.Pedidos
            .Where(p => p.Tipo == "PEDIDO" && p.DataPedido >= inicioDate && p.DataPedido <= fimDate);

        var salesStats = await salesQuery
            .GroupBy(p => 1)
            .Select(g => new {
                TotalVendas = g.Sum(p => p.Total),
                ValorRecebido = g.Sum(p => p.ValorPago),
                ValorPendente = g.Sum(p => p.SaldoDevedor),
                QuantidadePedidos = g.Count(),
                QuantidadeDevolucoes = g.Count(p => p.Status == "CANCELADO" && p.ValorEstornado > 0),
                ValorDevolvido = g.Sum(p => p.ValorEstornado),
                PedidosEmAndamento = g.Count(p => p.Status == "ABERTO")
            })
            .FirstOrDefaultAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var todayStart = today.ToDateTime(TimeOnly.MinValue);
        var todayEnd = today.ToDateTime(TimeOnly.MaxValue);
        
        var todayPayments = await context.Pagamentos
            .Where(p => p.RegistradoEm >= todayStart && p.RegistradoEm <= todayEnd)
            .SumAsync(p => p.ValorTotal, cancellationToken);
            
        var todayManual = await context.CaixaMovimentacoes
            .Where(m => m.Tipo == "ENTRADA" && m.MovimentadoEm >= todayStart && m.MovimentadoEm <= todayEnd)
            .SumAsync(m => m.Valor, cancellationToken);
            
        var valorEntrouHoje = todayPayments + todayManual;

        var resumen = salesStats != null 
            ? new FinanceiroVendasResumo(
                salesStats.TotalVendas,
                salesStats.ValorRecebido,
                salesStats.ValorPendente,
                salesStats.QuantidadePedidos,
                salesStats.QuantidadeDevolucoes,
                salesStats.ValorDevolvido,
                salesStats.PedidosEmAndamento,
                valorEntrouHoje)
            : new FinanceiroVendasResumo(0, 0, 0, 0, 0, 0, 0, valorEntrouHoje);

        var listQuery = from p in context.Pedidos
                        join c in context.Clientes on p.ClienteId equals c.Id
                        join u in context.Usuarios on p.CriadoPorUsuarioId equals u.Id
                        where p.Tipo == "PEDIDO" && p.DataPedido >= inicioDate && p.DataPedido <= fimDate
                        select new { p, ClienteNome = c.Nome, CriadoPorNome = u.Nome };

        if (!string.IsNullOrWhiteSpace(filtro.Status))
        {
            listQuery = listQuery.Where(q => q.p.Status == filtro.Status);
        }

        var total = await listQuery.CountAsync(cancellationToken);
        var totalPaginas = total == 0 ? 1 : (int)Math.Ceiling(total / (double)size);

        var listItems = await listQuery
            .OrderByDescending(q => q.p.DataPedido)
            .ThenByDescending(q => q.p.Id)
            .Skip(offset)
            .Take(size)
            .ToListAsync(cancellationToken);

        var financeiroPedidos = listItems.Select(x => new FinanceiroPedido(
            x.p.Id,
            x.p.Numero,
            x.ClienteNome,
            x.p.Tipo,
            x.p.Status,
            x.p.DataPedido,
            x.p.DataEntrega,
            x.p.Total,
            x.p.ValorPago,
            x.p.ValorEstornado,
            x.p.SaldoDevedor,
            x.CriadoPorNome,
            x.p.MotivoCancelamento
        )).ToList();

        return new FinanceiroVendasResult(
            new FinanceiroPeriodo(inicioDate, fimDate),
            resumen,
            new ResultadoPaginado<FinanceiroPedido>(financeiroPedidos, total, page, size, totalPaginas)
        );
    }

    public async Task<FinanceiroEntradasResult> ObterEntradasAsync(FinanceiroFiltro filtro, CancellationToken cancellationToken = default)
    {
        var (inicioDate, fimDate) = ResolverPeriodo(filtro.Inicio, filtro.Fim, filtro.Ano, filtro.Mes);
        var page = Math.Max(filtro.Pagina, 1);
        var size = Math.Clamp(filtro.TamanhoPagina, 5, 100);
        var offset = (page - 1) * size;

        var startDateTime = inicioDate.ToDateTime(TimeOnly.MinValue);
        var endDateTime = fimDate.ToDateTime(TimeOnly.MaxValue);

        var pagQuery = from p in context.Pagamentos
                       join ped in context.Pedidos on p.PedidoId equals ped.Id
                       join c in context.Clientes on ped.ClienteId equals c.Id
                       join u in context.Usuarios on p.RegistradoPorUsuarioId equals u.Id
                       where p.RegistradoEm >= startDateTime && p.RegistradoEm <= endDateTime
                       select new {
                           Origem = "PAGAMENTO",
                           FormaPagamento = p.FormaPagamento,
                           Valor = p.ValorTotal,
                           Data = p.RegistradoEm,
                           Descricao = "Pagamento do pedido " + ped.Numero + " - " + c.Nome,
                           Usuario = u.Nome
                       };

        var manQuery = from m in context.CaixaMovimentacoes
                       join u in context.Usuarios on m.UsuarioId equals u.Id
                       where m.Tipo == "ENTRADA" && m.MovimentadoEm >= startDateTime && m.MovimentadoEm <= endDateTime
                       select new {
                           Origem = "CAIXA",
                           FormaPagamento = m.FormaPagamento,
                           Valor = m.Valor,
                           Data = m.MovimentadoEm,
                           Descricao = m.Descricao,
                           Usuario = u.Nome
                       };

        var combinedQuery = pagQuery.Union(manQuery);
        var combinedList = await combinedQuery.ToListAsync(cancellationToken);

        decimal totalSum = combinedList.Sum(x => x.Valor);
        decimal dinheiro = combinedList.Where(x => x.FormaPagamento == "DINHEIRO").Sum(x => x.Valor);
        decimal pix = combinedList.Where(x => x.FormaPagamento == "PIX").Sum(x => x.Valor);
        decimal cartaoCredito = combinedList.Where(x => x.FormaPagamento == "CARTAO_CREDITO").Sum(x => x.Valor);
        decimal cartaoDebito = combinedList.Where(x => x.FormaPagamento == "CARTAO_DEBITO").Sum(x => x.Valor);

        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);
        decimal entrouHoje = combinedList.Where(x => x.Data >= today && x.Data < tomorrow).Sum(x => x.Valor);

        var resumen = new FinanceiroEntradasResumo(totalSum, dinheiro, pix, cartaoCredito, cartaoDebito, entrouHoje);

        var total = combinedList.Count;
        var totalPaginas = total == 0 ? 1 : (int)Math.Ceiling(total / (double)size);

        var items = combinedList
            .OrderByDescending(x => x.Data)
            .Skip(offset)
            .Take(size)
            .Select(x => new FinanceiroEntrada(x.Origem, x.FormaPagamento, x.Valor, x.Data, x.Descricao, x.Usuario))
            .ToList();

        return new FinanceiroEntradasResult(
            resumen,
            new ResultadoPaginado<FinanceiroEntrada>(items, total, page, size, totalPaginas)
        );
    }

    public async Task<FinanceiroDespesasResult> ListarDespesasAsync(FinanceiroFiltro filtro, CancellationToken cancellationToken = default)
    {
        var (inicioDate, fimDate) = ResolverPeriodo(filtro.Inicio, filtro.Fim, filtro.Ano, filtro.Mes);

        var despesas = await context.Despesas
            .Where(d => d.Vencimento >= inicioDate && d.Vencimento <= fimDate)
            .ToListAsync(cancellationToken);

        int totalDespesas = despesas.Count;
        var today = DateOnly.FromDateTime(DateTime.Today);
        int vencimentoHoje = despesas.Count(d => d.Vencimento == today && d.Status != "PAGO");
        decimal valorVencimentoHoje = despesas.Where(d => d.Vencimento == today && d.Status != "PAGO").Sum(d => d.Valor);

        decimal totalMes = despesas.Sum(d => d.Valor);
        decimal totalNaoPagoMes = despesas.Where(d => d.Status != "PAGO").Sum(d => d.Valor);
        decimal totalPagoMes = despesas.Where(d => d.Status == "PAGO").Sum(d => d.Valor);

        var resumen = new FinanceiroDespesasResumo(
            totalDespesas,
            vencimentoHoje,
            valorVencimentoHoje,
            totalMes,
            totalNaoPagoMes,
            totalPagoMes
        );

        var categorias = await context.Despesas
            .Select(d => d.Categoria)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(cancellationToken);

        var orderedDespesas = despesas
            .OrderBy(d => d.Vencimento == today && d.Status != "PAGO" ? 0 : 1)
            .ThenBy(d => d.Vencimento)
            .ThenBy(d => d.GrupoDespesaId)
            .ThenBy(d => d.NumeroParcela)
            .Select(d => new FinanceiroDespesa(
                d.Id,
                d.GrupoDespesaId ?? "",
                d.NumeroParcela,
                d.TotalParcelas,
                d.Categoria,
                d.Descricao,
                d.Valor,
                d.ValorTotal,
                d.Vencimento,
                d.Status,
                d.DataPagamento,
                d.Observacao
            ))
            .ToList();

        return new FinanceiroDespesasResult(resumen, categorias, orderedDespesas);
    }

    public async Task<long> CriarDespesaAsync(FinanceiroDespesaRequest request, CancellationToken cancellationToken = default)
    {
        var isParcelado = request.CondicaoPagamento.Equals("PARCELADO", StringComparison.OrdinalIgnoreCase);
        var parcelas = isParcelado ? Math.Max(request.QuantidadeParcelas, 1) : 1;
        var valorParcela = isParcelado ? Math.Round(request.Valor / parcelas, 2) : request.Valor;
        var valorAjustado = isParcelado ? request.Valor - (valorParcela * (parcelas - 1)) : request.Valor;

        var grupoId = Guid.NewGuid().ToString();
        long firstId = 0;

        for (int i = 1; i <= parcelas; i++)
        {
            var valor = i == parcelas ? valorAjustado : valorParcela;
            var vencimento = request.Vencimento.AddMonths(i - 1);
            var status = request.JaPago && i == 1 ? "PAGO" : "PENDENTE";
            var dataPagamento = status == "PAGO" ? (DateOnly?)DateOnly.FromDateTime(DateTime.Today) : null;

            var despesa = new Despesa(
                0,
                request.UsuarioId,
                grupoId,
                i,
                parcelas,
                request.Categoria.Trim(),
                request.Descricao.Trim(),
                valor,
                request.Valor,
                vencimento,
                status,
                dataPagamento,
                request.Observacao
            );

            context.Despesas.Add(despesa);
            await context.SaveChangesAsync(cancellationToken);
            if (i == 1)
            {
                firstId = despesa.Id;
            }
        }

        return firstId;
    }

    public async Task<bool> PagarDespesaAsync(long id, CancellationToken cancellationToken = default)
    {
        var existing = await context.Despesas.FindAsync(new object[] { id }, cancellationToken);
        if (existing == null)
        {
            return false;
        }

        var updated = existing with
        {
            Status = "PAGO",
            DataPagamento = DateOnly.FromDateTime(DateTime.Today)
        };

        context.Entry(existing).CurrentValues.SetValues(updated);
        return await context.SaveChangesAsync(cancellationToken) > 0;
    }

    public async Task<bool> AtualizarDespesaAsync(string grupoDespesaId, FinanceiroDespesaAtualizarRequest request, CancellationToken cancellationToken = default)
    {
        var despesas = await context.Despesas
            .Where(d => d.GrupoDespesaId == grupoDespesaId)
            .OrderBy(d => d.NumeroParcela)
            .ToListAsync(cancellationToken);

        if (despesas.Count == 0)
        {
            return false;
        }

        var totalParcelas = despesas.Count;
        var valorParcela = Math.Round(request.Valor / totalParcelas, 2);
        var valorAjustado = request.Valor - (valorParcela * (totalParcelas - 1));

        for (int i = 0; i < despesas.Count; i++)
        {
            var despesa = despesas[i];
            var n = i + 1;
            var valor = n == totalParcelas ? valorAjustado : valorParcela;
            var vencimento = request.Vencimento.AddMonths(i);

            var updated = despesa with
            {
                Categoria = request.Categoria.Trim(),
                Descricao = request.Descricao.Trim(),
                Valor = valor,
                ValorTotal = request.Valor,
                Vencimento = vencimento,
                Observacao = request.Observacao
            };

            context.Entry(despesa).CurrentValues.SetValues(updated);
        }

        return await context.SaveChangesAsync(cancellationToken) > 0;
    }

    public async Task<FinanceiroGraficosResult> ObterGraficosAsync(int? ano, int? mes, CancellationToken cancellationToken = default)
    {
        var targetAno = ano ?? DateTime.Today.Year;
        var targetMes = mes ?? DateTime.Today.Month;

        var startOfYear = new DateOnly(targetAno, 1, 1);
        var endOfYear = new DateOnly(targetAno, 12, 31);
        var startOfMonth = new DateOnly(targetAno, targetMes, 1);
        var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);

        var receitasRaw = await context.Pedidos
            .Where(p => p.DataPedido >= startOfYear && p.DataPedido <= endOfYear)
            .Select(p => new { p.DataPedido, p.ValorPago })
            .ToListAsync(cancellationToken);

        var receitasGrouped = receitasRaw
            .GroupBy(p => p.DataPedido.Month)
            .Select(g => new { Mes = g.Key, Valor = g.Sum(p => p.ValorPago) })
            .ToList();

        var receitaAnual = Enumerable.Range(1, 12)
            .Select(m => new FinanceiroGraficoMensal(m, receitasGrouped.FirstOrDefault(r => r.Mes == m)?.Valor ?? 0m))
            .ToList();

        var despesasRaw = await context.Despesas
            .Where(d => d.Vencimento >= startOfYear && d.Vencimento <= endOfYear)
            .Select(d => new { d.Vencimento, d.Valor })
            .ToListAsync(cancellationToken);

        var despesasGrouped = despesasRaw
            .GroupBy(d => d.Vencimento.Month)
            .Select(g => new { Mes = g.Key, Valor = g.Sum(d => d.Valor) })
            .ToList();

        var despesaAnual = Enumerable.Range(1, 12)
            .Select(m => new FinanceiroGraficoMensal(m, despesasGrouped.FirstOrDefault(d => d.Mes == m)?.Valor ?? 0m))
            .ToList();

        var despesasMesRaw = await context.Despesas
            .Where(d => d.Vencimento >= startOfMonth && d.Vencimento <= endOfMonth)
            .GroupBy(d => d.Categoria)
            .Select(g => new { Categoria = g.Key, Valor = g.Sum(d => d.Valor) })
            .OrderByDescending(x => x.Valor)
            .ToListAsync(cancellationToken);

        var despesasMes = despesasMesRaw
            .Select(x => new FinanceiroDespesaCategoria(x.Categoria, x.Valor))
            .ToList();

        var clientesMesList = await (from p in context.Pedidos
                                     join c in context.Clientes on p.ClienteId equals c.Id
                                     where p.Tipo == "PEDIDO" && p.DataPedido >= startOfMonth && p.DataPedido <= endOfMonth
                                     group p by c.Nome into g
                                     select new { Cliente = g.Key, Valor = g.Sum(p => p.Total) })
                                    .OrderByDescending(x => x.Valor)
                                    .Take(10)
                                    .ToListAsync(cancellationToken);

        var clientesMesRaw = clientesMesList
            .Select(x => new FinanceiroClienteValor(x.Cliente, x.Valor))
            .ToList();

        var pedidosPorStatusRaw = await context.Pedidos
            .Where(p => p.Tipo == "PEDIDO" && p.DataPedido >= startOfMonth && p.DataPedido <= endOfMonth)
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Quantidade = g.Count() })
            .ToListAsync(cancellationToken);

        var pedidosPorStatus = pedidosPorStatusRaw
            .Select(x => new FinanceiroPedidoStatus(x.Status, x.Quantidade))
            .ToList();

        var usuariosRankingList = await (from p in context.Pedidos
                                         join u in context.Usuarios on p.CriadoPorUsuarioId equals u.Id
                                         where p.Tipo == "PEDIDO" && p.DataPedido >= startOfMonth && p.DataPedido <= endOfMonth
                                         group p by u.Nome into g
                                         select new { Usuario = g.Key, Quantidade = g.Count() })
                                        .OrderByDescending(x => x.Quantidade)
                                        .Take(10)
                                        .ToListAsync(cancellationToken);

        var usuariosRanking = usuariosRankingList
            .Select(x => new FinanceiroUsuarioRanking(x.Usuario, x.Quantidade))
            .ToList();

        return new FinanceiroGraficosResult(
            targetAno,
            targetMes,
            receitaAnual,
            despesaAnual,
            despesasMes,
            clientesMesRaw,
            pedidosPorStatus,
            usuariosRanking
        );
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
