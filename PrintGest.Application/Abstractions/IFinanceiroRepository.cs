using System.ComponentModel.DataAnnotations;

namespace PrintGest.Application.Abstractions;

public interface IFinanceiroRepository
{
    Task<FinanceiroVendasResult> ObterVendasAsync(FinanceiroFiltro filtro, CancellationToken cancellationToken = default);
    Task<FinanceiroEntradasResult> ObterEntradasAsync(FinanceiroFiltro filtro, CancellationToken cancellationToken = default);
    Task<FinanceiroDespesasResult> ListarDespesasAsync(FinanceiroFiltro filtro, CancellationToken cancellationToken = default);
    Task<long> CriarDespesaAsync(FinanceiroDespesaRequest request, CancellationToken cancellationToken = default);
    Task<bool> PagarDespesaAsync(long id, CancellationToken cancellationToken = default);
    Task<FinanceiroGraficosResult> ObterGraficosAsync(int? ano, int? mes, CancellationToken cancellationToken = default);
}

public sealed record FinanceiroFiltro(int? Ano, int? Mes, DateOnly? Inicio, DateOnly? Fim, string? Status = null);

public sealed record FinanceiroPeriodo(DateOnly Inicio, DateOnly Fim);

public sealed record FinanceiroVendasResult(
    FinanceiroPeriodo Periodo,
    FinanceiroVendasResumo Resumo,
    IReadOnlyList<FinanceiroPedido> Pedidos);

public sealed record FinanceiroVendasResumo(
    decimal TotalVendas,
    decimal ValorRecebido,
    decimal ValorPendente,
    int QuantidadePedidos,
    int QuantidadeDevolucoes,
    decimal ValorDevolvido,
    int PedidosEmAndamento,
    decimal ValorEntrouHoje);

public sealed record FinanceiroPedido(
    long Id,
    string Numero,
    string Cliente,
    string Tipo,
    string Status,
    DateOnly DataPedido,
    DateOnly? DataEntrega,
    decimal Total,
    decimal ValorPago,
    decimal ValorEstornado,
    decimal SaldoDevedor,
    string CriadoPor,
    string? MotivoCancelamento);

public sealed record FinanceiroEntradasResult(
    FinanceiroEntradasResumo Resumo,
    IReadOnlyList<FinanceiroEntrada> Entradas);

public sealed record FinanceiroEntradasResumo(
    decimal Total,
    decimal Dinheiro,
    decimal Pix,
    decimal CartaoCredito,
    decimal CartaoDebito,
    decimal EntrouHoje);

public sealed record FinanceiroEntrada(
    string Origem,
    string FormaPagamento,
    decimal Valor,
    DateTime Data,
    string Descricao,
    string Usuario);

public sealed record FinanceiroDespesasResult(
    FinanceiroDespesasResumo Resumo,
    IReadOnlyCollection<string> Categorias,
    IReadOnlyList<FinanceiroDespesa> Despesas);

public sealed record FinanceiroDespesasResumo(
    int TotalDespesas,
    int VencimentoHoje,
    decimal ValorVencimentoHoje,
    decimal TotalMes,
    decimal TotalNaoPagoMes,
    decimal TotalPagoMes);

public sealed record FinanceiroDespesa(
    long Id,
    string GrupoDespesaId,
    int NumeroParcela,
    int TotalParcelas,
    string Categoria,
    string Descricao,
    decimal Valor,
    decimal ValorTotal,
    DateOnly Vencimento,
    string Status,
    DateOnly? DataPagamento,
    string? Observacao);

public sealed record FinanceiroDespesaRequest(
    [Range(1, long.MaxValue, ErrorMessage = "Informe o usuÃƒÂ¡rio responsÃƒÂ¡vel pelo lanÃƒÂ§amento.")]
    long UsuarioId,
    [Required(ErrorMessage = "Informe a categoria da despesa.")]
    [StringLength(80, ErrorMessage = "A categoria deve ter no mÃƒÂ¡ximo 80 caracteres.")]
    string Categoria,
    [Required(ErrorMessage = "Informe a descriÃƒÂ§ÃƒÂ£o da despesa.")]
    [StringLength(200, ErrorMessage = "A descriÃƒÂ§ÃƒÂ£o deve ter no mÃƒÂ¡ximo 200 caracteres.")]
    string Descricao,
    [Range(0.01, double.MaxValue, ErrorMessage = "Informe um valor maior que zero.")]
    decimal Valor,
    DateOnly Vencimento,
    [Required(ErrorMessage = "Informe se a despesa ÃƒÂ© ÃƒÂ  vista ou parcelada.")]
    string CondicaoPagamento,
    [Range(1, 120, ErrorMessage = "Informe uma quantidade de parcelas entre 1 e 120.")]
    int QuantidadeParcelas,
    [StringLength(300, ErrorMessage = "A observaÃƒÂ§ÃƒÂ£o deve ter no mÃƒÂ¡ximo 300 caracteres.")]
    string? Observacao);

public sealed record FinanceiroGraficosResult(
    int Ano,
    int Mes,
    IReadOnlyList<FinanceiroGraficoMensal> ReceitaAnual,
    IReadOnlyList<FinanceiroGraficoMensal> DespesaAnual,
    IReadOnlyList<FinanceiroDespesaCategoria> DespesasMes,
    IReadOnlyList<FinanceiroClienteValor> ClientesMes);

public sealed record FinanceiroGraficoMensal(int Mes, decimal Valor);

public sealed record FinanceiroDespesaCategoria(string Categoria, decimal Valor);

public sealed record FinanceiroClienteValor(string Cliente, decimal Valor);
