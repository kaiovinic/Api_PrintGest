using PrintGest.Domain.Entities;

namespace PrintGest.Application.Abstractions;

public interface IPedidoRepository
{
    Task<ResultadoPaginado<PedidoResumo>> ListAsync(PedidoFiltro filtro, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PedidoResumo>> ListRecentAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PedidoResumo>> ListPendingDeliveriesAsync(string? atendente, CancellationToken cancellationToken = default);
    Task<PedidoDetalhe?> GetDetailsByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<long> CriarPedidoAsync(PedidoCadastroDto request, string tipo, string status, CancellationToken cancellationToken = default);
    Task<bool> EditarOrcamentoAsync(long id, PedidoCadastroDto request, CancellationToken cancellationToken = default);
    Task<bool> EditarPedidoAsync(long id, PedidoCadastroDto request, CancellationToken cancellationToken = default);
    Task<bool> ConverterEmPedidoAsync(long id, ConverterPedidoDto request, CancellationToken cancellationToken = default);
    Task<bool> CancelarPedidoAsync(long id, AlterarStatusPedidoDto request, CancellationToken cancellationToken = default);
    Task<bool> RegistrarEstornoAsync(long id, EstornarPedidoDto request, CancellationToken cancellationToken = default);
    Task<bool> FinalizarPedidoAsync(long id, FinalizarPedidoDto request, CancellationToken cancellationToken = default);
    
    // Método auxiliar caso precise validar o estado atual
    Task<(string Tipo, string Status, decimal ValorPago, decimal SaldoDevedor)?> ObterEstadoPedidoAsync(long id, CancellationToken cancellationToken = default);
}

public sealed record PedidoFiltro(int? Ano, int? Mes, DateOnly? Inicio, DateOnly? Fim, string? Status, string? Atendente = null, int Pagina = 1, int TamanhoPagina = 10);

public sealed record ResultadoPaginado<T>(
    IReadOnlyList<T> Itens,
    int Total,
    int Pagina,
    int TamanhoPagina,
    int TotalPaginas);

public record PedidoDetalhe(
    long Id,
    string Numero,
    long ClienteId,
    string Cliente,
    string? Empresa,
    string? CpfCnpj,
    string? Telefone,
    string? Endereco,
    string? Cidade,
    long UsuarioId,
    string Tipo,
    string Status,
    DateOnly DataPedido,
    DateOnly? DataEntrega,
    string? Vendedor,
    string? FormaPagamento,
    string? CondicaoPagamento,
    string? Frente,
    string? Fundo,
    string? Observacao,
    string? MotivoCancelamento,
    decimal ValorEstornado,
    decimal ValorRetido,
    string? ObservacaoEstorno,
    string? OutrosItens,
    decimal Total,
    decimal ValorPago,
    decimal SaldoDevedor,
    IReadOnlyList<ItemPedidoDetalhe> Itens,
    IReadOnlyList<PagamentoDetalhe> Pagamentos);

public record ItemPedidoDetalhe(
    long Id,
    string Descricao,
    string? Tamanho,
    int Quantidade,
    decimal ValorUnitario,
    decimal ValorTotal);

public record PagamentoDetalhe(
    long Id,
    string FormaPagamento,
    string CondicaoPagamento,
    decimal Valor,
    string? Observacao,
    DateTime RegistradoEm,
    string Usuario);

public record PedidoCadastroDto(
    string Numero,
    long ClienteId,
    string ClienteNome,
    string? Empresa,
    string? CpfCnpj,
    string? Telefone,
    string? Endereco,
    string? Cidade,
    long UsuarioId,
    DateOnly DataPedido,
    DateOnly? DataEntrega,
    string? Vendedor,
    string? FormaPagamento,
    string? CondicaoPagamento,
    string? Frente,
    string? Fundo,
    string? Observacao,
    string? OutrosItens,
    decimal Total,
    decimal ValorPago,
    IReadOnlyList<ItemPedidoCadastroDto> Itens);

public record ItemPedidoCadastroDto(
    string Descricao,
    string? Tamanho,
    int Quantidade,
    decimal ValorUnitario,
    decimal ValorTotal);

public record ConverterPedidoDto(
    long UsuarioId,
    string FormaPagamento,
    string CondicaoPagamento,
    decimal ValorEntrada);

public record AlterarStatusPedidoDto(
    long UsuarioId,
    string? Observacao,
    decimal ValorDevolvido,
    string? FormaDevolucao,
    string? ObservacaoEstorno);

public record EstornarPedidoDto(
    long UsuarioId,
    decimal ValorDevolvido,
    string FormaDevolucao,
    string Observacao);

public record FinalizarPedidoDto(
    long UsuarioId,
    string? Observacao,
    bool ReceberSaldo,
    string? FormaPagamento);
