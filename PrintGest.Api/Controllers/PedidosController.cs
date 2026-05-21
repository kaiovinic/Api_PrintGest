using Microsoft.AspNetCore.Mvc;
using PrintGest.Application.Abstractions;
using System.ComponentModel.DataAnnotations;

namespace PrintGest.Api.Controllers;

[ApiController]
[Route("api/pedidos")]
public sealed class PedidosController(IPedidoRepository pedidos, IUnitOfWork unitOfWork) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery, Range(2000, 2100, ErrorMessage = "Informe um ano válido.")] int? ano, [FromQuery, Range(1, 12, ErrorMessage = "Informe um mês entre 1 e 12.")] int? mes, [FromQuery] DateOnly? inicio, [FromQuery] DateOnly? fim, [FromQuery] string? status, CancellationToken cancellationToken)
    {
        return Ok(await pedidos.ListAsync(new PedidoFiltro(ano, mes, inicio, fim, status), cancellationToken));
    }

    [HttpGet("recentes")]
    public async Task<IActionResult> ListarRecentes(CancellationToken cancellationToken)
    {
        return Ok(await pedidos.ListRecentAsync(cancellationToken));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> ObterPorId(long id, CancellationToken cancellationToken)
    {
        var detalhe = await pedidos.GetDetailsByIdAsync(id, cancellationToken);
        if (detalhe is null)
        {
            return NotFound();
        }
        return Ok(detalhe);
    }

    [HttpPost("orcamentos")]
    public async Task<IActionResult> CriarOrcamento([FromBody] PedidoRequest request, CancellationToken cancellationToken)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var dto = MapToDto(request);
            var id = await pedidos.CriarPedidoAsync(dto, "ORCAMENTO", "ORCADO", cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return CreatedAtAction(nameof(Listar), new { id }, new { id });
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    [HttpPost]
    public async Task<IActionResult> CriarPedido([FromBody] PedidoRequest request, CancellationToken cancellationToken)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var dto = MapToDto(request);
            var id = await pedidos.CriarPedidoAsync(dto, "PEDIDO", "ABERTO", cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return CreatedAtAction(nameof(Listar), new { id }, new { id });
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    [HttpPut("{id:long}/orcamento")]
    public async Task<IActionResult> EditarOrcamento(long id, [FromBody] PedidoRequest request, CancellationToken cancellationToken)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var dto = MapToDto(request);
            var sucesso = await pedidos.EditarOrcamentoAsync(id, dto, cancellationToken);
            if (!sucesso)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                
                var estado = await pedidos.ObterEstadoPedidoAsync(id, cancellationToken);
                if (estado is null)
                {
                    return NotFound(new { mensagem = "Pedido ou orçamento não encontrado." });
                }

                if (estado.Value.Tipo == "PEDIDO")
                {
                    return BadRequest(new
                    {
                        mensagem = "Não é permitido transformar um pedido em orçamento. Se o cliente desistiu, use a opção Cancelar."
                    });
                }

                return BadRequest(new { mensagem = $"Não foi possível editar este orçamento porque ele está com status {FormatarStatus(estado.Value.Status)}." });
            }

            await unitOfWork.CommitAsync(cancellationToken);
            return NoContent();
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> EditarPedido(long id, [FromBody] PedidoRequest request, CancellationToken cancellationToken)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var dto = MapToDto(request);
            var sucesso = await pedidos.EditarPedidoAsync(id, dto, cancellationToken);
            if (!sucesso)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return NotFound();
            }

            await unitOfWork.CommitAsync(cancellationToken);
            return NoContent();
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    [HttpPatch("{id:long}/converter-em-pedido")]
    public async Task<IActionResult> ConverterEmPedido(long id, [FromBody] ConverterPedidoRequest request, CancellationToken cancellationToken)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var dto = new ConverterPedidoDto(
                request.UsuarioId,
                request.FormaPagamento,
                request.CondicaoPagamento,
                request.ValorEntrada
            );

            var sucesso = await pedidos.ConverterEmPedidoAsync(id, dto, cancellationToken);
            if (!sucesso)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return NotFound();
            }

            await unitOfWork.CommitAsync(cancellationToken);
            return Ok(new { mensagem = "Orçamento convertido em pedido." });
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    [HttpPatch("{id:long}/cancelar")]
    public async Task<IActionResult> Cancelar(long id, [FromBody] AlterarStatusPedidoRequest request, CancellationToken cancellationToken)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var estado = await pedidos.ObterEstadoPedidoAsync(id, cancellationToken);
            if (estado is null)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return NotFound(new { mensagem = "Pedido ou orçamento não encontrado." });
            }

            var (_, statusAtual, valorPago, _) = estado.Value;

            if (string.IsNullOrWhiteSpace(request.Observacao) || request.Observacao.Trim().Length < 10)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return BadRequest(new { mensagem = "Informe o motivo do cancelamento com pelo menos 10 caracteres." });
            }

            if (statusAtual == "CANCELADO")
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return BadRequest(new { mensagem = "Este registro já está cancelado." });
            }

            if (statusAtual == "FINALIZADO")
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return BadRequest(new { mensagem = "Não é possível cancelar um pedido finalizado." });
            }

            if (request.ValorDevolvido < 0 || request.ValorDevolvido > valorPago)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return BadRequest(new { mensagem = "O valor devolvido deve estar entre zero e o valor pago pelo cliente." });
            }

            if (request.ValorDevolvido > 0 && string.IsNullOrWhiteSpace(request.FormaDevolucao))
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return BadRequest(new { mensagem = "Informe a forma da devolução ao cliente." });
            }

            if (request.ValorDevolvido > 0 && request.ValorDevolvido < valorPago && string.IsNullOrWhiteSpace(request.ObservacaoEstorno))
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return BadRequest(new { mensagem = "Informe uma observação explicando a devolução parcial." });
            }

            var dto = new AlterarStatusPedidoDto(
                request.UsuarioId,
                request.Observacao,
                request.ValorDevolvido,
                request.FormaDevolucao,
                request.ObservacaoEstorno
            );

            var sucesso = await pedidos.CancelarPedidoAsync(id, dto, cancellationToken);
            if (!sucesso)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return NotFound();
            }

            await unitOfWork.CommitAsync(cancellationToken);
            return NoContent();
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    [HttpPatch("{id:long}/estornar")]
    public async Task<IActionResult> Estornar(long id, [FromBody] EstornarPedidoRequest request, CancellationToken cancellationToken)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var detalhe = await pedidos.GetDetailsByIdAsync(id, cancellationToken);
            if (detalhe is null)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return NotFound(new { mensagem = "Pedido nao encontrado." });
            }

            if (detalhe.Tipo != "PEDIDO")
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return BadRequest(new { mensagem = "Somente pedidos cancelados podem receber devolucao complementar." });
            }

            if (detalhe.Status != "CANCELADO")
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return BadRequest(new { mensagem = "A devolucao complementar so pode ser registrada para pedido cancelado." });
            }

            var valorRetido = detalhe.ValorRetido;
            if (valorRetido <= 0)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return BadRequest(new { mensagem = "Este pedido nao possui valor retido para devolver." });
            }

            if (request.ValorDevolvido <= 0 || request.ValorDevolvido > valorRetido)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return BadRequest(new { mensagem = $"O valor devolvido deve ser maior que zero e no maximo {valorRetido:C}." });
            }

            if (string.IsNullOrWhiteSpace(request.FormaDevolucao))
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return BadRequest(new { mensagem = "Informe a forma da devolucao ao cliente." });
            }

            if (string.IsNullOrWhiteSpace(request.Observacao) || request.Observacao.Trim().Length < 10)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return BadRequest(new { mensagem = "Informe uma observacao da devolucao com pelo menos 10 caracteres." });
            }

            var dto = new EstornarPedidoDto(
                request.UsuarioId,
                request.ValorDevolvido,
                request.FormaDevolucao,
                request.Observacao
            );

            var sucesso = await pedidos.RegistrarEstornoAsync(id, dto, cancellationToken);
            if (!sucesso)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return NotFound();
            }

            await unitOfWork.CommitAsync(cancellationToken);
            return NoContent();
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    [HttpPatch("{id:long}/finalizar")]
    public async Task<IActionResult> Finalizar(long id, [FromBody] FinalizarPedidoRequest request, CancellationToken cancellationToken)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var estado = await pedidos.ObterEstadoPedidoAsync(id, cancellationToken);
            if (estado is null)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return NotFound(new { mensagem = "Pedido não encontrado." });
            }

            var (tipoAtual, statusAtual, _, saldoDevedor) = estado.Value;

            if (tipoAtual != "PEDIDO")
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return BadRequest(new { mensagem = "Somente pedidos podem ser finalizados. Orçamentos devem ser convertidos em pedido primeiro." });
            }

            if (statusAtual == "CANCELADO")
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return BadRequest(new { mensagem = "Não é possível finalizar um pedido cancelado." });
            }

            if (statusAtual == "FINALIZADO")
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return BadRequest(new { mensagem = "Este pedido já está finalizado." });
            }

            if (saldoDevedor > 0)
            {
                if (!request.ReceberSaldo)
                {
                    await unitOfWork.RollbackAsync(cancellationToken);
                    return BadRequest(new { mensagem = "Não é possível finalizar pedido com saldo devedor em aberto." });
                }

                if (string.IsNullOrWhiteSpace(request.FormaPagamento))
                {
                    await unitOfWork.RollbackAsync(cancellationToken);
                    return BadRequest(new { mensagem = "Informe a forma de pagamento para receber o saldo devedor." });
                }
            }

            var dto = new FinalizarPedidoDto(
                request.UsuarioId,
                request.Observacao,
                request.ReceberSaldo,
                request.FormaPagamento
            );

            var sucesso = await pedidos.FinalizarPedidoAsync(id, dto, cancellationToken);
            if (!sucesso)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return NotFound();
            }

            await unitOfWork.CommitAsync(cancellationToken);
            return NoContent();
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static string FormatarStatus(string status)
    {
        return status switch
        {
            "ORCADO" => "orçado",
            "ABERTO" => "aberto",
            "FINALIZADO" => "finalizado",
            "CANCELADO" => "cancelado",
            _ => status.ToLowerInvariant()
        };
    }

    private static PedidoCadastroDto MapToDto(PedidoRequest request)
    {
        return new PedidoCadastroDto(
            request.Numero,
            request.ClienteId,
            request.ClienteNome,
            request.Empresa,
            request.CpfCnpj,
            request.Telefone,
            request.Endereco,
            request.Cidade,
            request.UsuarioId,
            request.DataPedido,
            request.DataEntrega,
            request.Vendedor,
            request.FormaPagamento,
            request.CondicaoPagamento,
            request.Frente,
            request.Fundo,
            request.Observacao,
            request.OutrosItens,
            request.Total,
            request.ValorPago,
            request.Itens.Select(i => new ItemPedidoCadastroDto(
                i.Descricao,
                i.Tamanho,
                i.Quantidade,
                i.ValorUnitario,
                i.ValorTotal
            )).ToList()
        );
    }
}

public sealed record PedidoRequest(
    [Required(ErrorMessage = "Informe o número do pedido ou orçamento.")]
    [StringLength(30, ErrorMessage = "O número deve ter no máximo 30 caracteres.")]
    string Numero,
    [Range(0, long.MaxValue, ErrorMessage = "Informe um cliente válido.")]
    long ClienteId,
    [Required(ErrorMessage = "Informe o nome do cliente.")]
    [StringLength(120, ErrorMessage = "O nome do cliente deve ter no máximo 120 caracteres.")]
    string ClienteNome,
    [StringLength(120, ErrorMessage = "A empresa deve ter no máximo 120 caracteres.")]
    string? Empresa,
    [StringLength(20, ErrorMessage = "O CPF/CNPJ deve ter no máximo 20 caracteres.")]
    string? CpfCnpj,
    [StringLength(20, ErrorMessage = "O telefone deve ter no máximo 20 caracteres.")]
    string? Telefone,
    [StringLength(200, ErrorMessage = "O endereço deve ter no máximo 200 caracteres.")]
    string? Endereco,
    [StringLength(80, ErrorMessage = "A cidade deve ter no máximo 80 caracteres.")]
    string? Cidade,
    [Range(1, long.MaxValue, ErrorMessage = "Informe o usuário responsável pelo pedido.")]
    long UsuarioId,
    DateOnly DataPedido,
    DateOnly? DataEntrega,
    [StringLength(120, ErrorMessage = "O vendedor deve ter no máximo 120 caracteres.")]
    string? Vendedor,
    [StringLength(30, ErrorMessage = "A forma de pagamento deve ter no máximo 30 caracteres.")]
    string? FormaPagamento,
    [StringLength(30, ErrorMessage = "A condição de pagamento deve ter no máximo 30 caracteres.")]
    string? CondicaoPagamento,
    [StringLength(500, ErrorMessage = "A descrição da frente deve ter no máximo 500 caracteres.")]
    string? Frente,
    [StringLength(500, ErrorMessage = "A descrição do fundo deve ter no máximo 500 caracteres.")]
    string? Fundo,
    [StringLength(300, ErrorMessage = "A observação deve ter no máximo 300 caracteres.")]
    string? Observacao,
    [StringLength(300, ErrorMessage = "Outros itens deve ter no máximo 300 caracteres.")]
    string? OutrosItens,
    [Range(0.01, double.MaxValue, ErrorMessage = "O total do pedido deve ser maior que zero.")]
    decimal Total,
    [Range(0, double.MaxValue, ErrorMessage = "O valor pago não pode ser negativo.")]
    decimal ValorPago,
    [Required(ErrorMessage = "Informe ao menos um item do pedido.")]
    [MinLength(1, ErrorMessage = "Informe ao menos um item do pedido.")]
    IReadOnlyList<ItemPedidoRequest> Itens);

public sealed record ItemPedidoRequest(
    [Required(ErrorMessage = "Informe a descrição do item.")]
    [StringLength(200, ErrorMessage = "A descrição do item deve ter no máximo 200 caracteres.")]
    string Descricao,
    [StringLength(20, ErrorMessage = "O tamanho deve ter no máximo 20 caracteres.")]
    string? Tamanho,
    [Range(1, int.MaxValue, ErrorMessage = "A quantidade do item deve ser maior que zero.")]
    int Quantidade,
    [Range(0.01, double.MaxValue, ErrorMessage = "O valor unitário deve ser maior que zero.")]
    decimal ValorUnitario,
    [Range(0.01, double.MaxValue, ErrorMessage = "O valor total do item deve ser maior que zero.")]
    decimal ValorTotal);

public sealed record ConverterPedidoRequest(
    [Range(1, long.MaxValue, ErrorMessage = "Informe o usuário responsável pela conversão.")]
    long UsuarioId,
    [Required(ErrorMessage = "Informe a forma de pagamento.")]
    string FormaPagamento,
    [Required(ErrorMessage = "Informe a condição de pagamento.")]
    string CondicaoPagamento,
    [Range(0.01, double.MaxValue, ErrorMessage = "Informe um valor de entrada maior que zero.")]
    decimal ValorEntrada);

public sealed record AlterarStatusPedidoRequest(
    [Range(1, long.MaxValue, ErrorMessage = "Informe o usuário responsável pela alteração.")]
    long UsuarioId,
    [Required(ErrorMessage = "Informe o motivo do cancelamento.")]
    [MinLength(10, ErrorMessage = "Informe o motivo do cancelamento com pelo menos 10 caracteres.")]
    [StringLength(300, ErrorMessage = "O motivo do cancelamento deve ter no máximo 300 caracteres.")]
    string? Observacao,
    [Range(0, double.MaxValue, ErrorMessage = "O valor devolvido não pode ser negativo.")]
    decimal ValorDevolvido,
    string? FormaDevolucao,
    [StringLength(300, ErrorMessage = "A observação do estorno deve ter no máximo 300 caracteres.")]
    string? ObservacaoEstorno);

public sealed record EstornarPedidoRequest(
    [Range(1, long.MaxValue, ErrorMessage = "Informe o usuário responsável pela devolução.")]
    long UsuarioId,
    [Range(0.01, double.MaxValue, ErrorMessage = "Informe um valor de devolução maior que zero.")]
    decimal ValorDevolvido,
    [Required(ErrorMessage = "Informe a forma da devolução.")]
    string FormaDevolucao,
    [Required(ErrorMessage = "Informe uma observação da devolução.")]
    [MinLength(10, ErrorMessage = "Informe uma observação da devolução com pelo menos 10 caracteres.")]
    [StringLength(300, ErrorMessage = "A observação da devolução deve ter no máximo 300 caracteres.")]
    string Observacao);

public sealed record FinalizarPedidoRequest(
    [Range(1, long.MaxValue, ErrorMessage = "Informe o usuário responsável pela finalização.")]
    long UsuarioId,
    [StringLength(300, ErrorMessage = "A observação deve ter no máximo 300 caracteres.")]
    string? Observacao,
    bool ReceberSaldo,
    string? FormaPagamento);
