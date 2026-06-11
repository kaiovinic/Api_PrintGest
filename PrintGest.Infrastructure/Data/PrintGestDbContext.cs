using Microsoft.EntityFrameworkCore;
using PrintGest.Domain.Entities;

namespace PrintGest.Infrastructure.Data;

public sealed class PrintGestDbContext : DbContext
{
    public PrintGestDbContext(DbContextOptions<PrintGestDbContext> options) : base(options)
    {
    }

    public DbSet<Usuario> Usuarios { get; set; } = null!;
    public DbSet<Cliente> Clientes { get; set; } = null!;
    public DbSet<LogSistema> LogsSistema { get; set; } = null!;
    public DbSet<ProdutoEstoque> ProdutosEstoque { get; set; } = null!;
    public DbSet<CategoriaEstoque> CategoriasEstoque { get; set; } = null!;
    public DbSet<MovimentacaoEstoque> MovimentacoesEstoque { get; set; } = null!;
    public DbSet<CaixaMovimentacao> CaixaMovimentacoes { get; set; } = null!;
    public DbSet<Despesa> Despesas { get; set; } = null!;
    public DbSet<Pedido> Pedidos { get; set; } = null!;
    public DbSet<ItemPedido> ItensPedido { get; set; } = null!;
    public DbSet<Pagamento> Pagamentos { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.ToTable("usuarios");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Nome).HasColumnName("nome");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.Telefone).HasColumnName("telefone");
            entity.Property(e => e.SenhaHash).HasColumnName("senha_hash");
            
            entity.Property(e => e.Perfil)
                .HasColumnName("perfil")
                .HasConversion<string>();

            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasConversion<string>();

            entity.Property(e => e.DeveTrocarSenha).HasColumnName("deve_trocar_senha");
        });

        modelBuilder.Entity<Cliente>(entity =>
        {
            entity.ToTable("clientes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Nome).HasColumnName("nome");
            entity.Property(e => e.CpfCnpj).HasColumnName("cpf_cnpj");
            entity.Property(e => e.Empresa).HasColumnName("empresa");
            entity.Property(e => e.Telefone).HasColumnName("telefone");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.Endereco).HasColumnName("endereco");
            entity.Property(e => e.Cidade).HasColumnName("cidade");
        });

        modelBuilder.Entity<LogSistema>(entity =>
        {
            entity.ToTable("logs_sistema");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");
            entity.Property(e => e.Entidade).HasColumnName("entidade");
            entity.Property(e => e.EntidadeId).HasColumnName("entidade_id");
            entity.Property(e => e.Acao).HasColumnName("acao");
            entity.Property(e => e.Descricao).HasColumnName("descricao");
            entity.Property(e => e.CriadoEm).HasColumnName("criado_em");

            entity.Ignore(e => e.Usuario);
        });

        modelBuilder.Entity<ProdutoEstoque>(entity =>
        {
            entity.ToTable("produtos_estoque");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Nome).HasColumnName("nome");
            entity.Property(e => e.Categoria).HasColumnName("categoria");
            entity.Property(e => e.Tamanho).HasColumnName("tamanho");
            entity.Property(e => e.Unidade).HasColumnName("unidade");
            entity.Property(e => e.QuantidadeAtual).HasColumnName("quantidade_atual");
            entity.Property(e => e.EstoqueMinimo).HasColumnName("estoque_minimo");
            entity.Property(e => e.CustoUnitario).HasColumnName("custo_unitario");
            entity.Property(e => e.Fornecedor).HasColumnName("fornecedor");
            entity.Property(e => e.Observacao).HasColumnName("observacao");
            entity.Property(e => e.Ativo).HasColumnName("ativo");
        });

        modelBuilder.Entity<CategoriaEstoque>(entity =>
        {
            entity.ToTable("categorias_estoque");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Nome).HasColumnName("nome");
            entity.Property(e => e.Ativo).HasColumnName("ativo");
            entity.Property(e => e.CriadoEm).HasColumnName("criado_em");
        });

        modelBuilder.Entity<MovimentacaoEstoque>(entity =>
        {
            entity.ToTable("movimentacoes_estoque");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ProdutoId).HasColumnName("produto_id");
            entity.Property(e => e.PedidoId).HasColumnName("pedido_id");
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");
            entity.Property(e => e.Tipo).HasColumnName("tipo");
            entity.Property(e => e.Quantidade).HasColumnName("quantidade");
            entity.Property(e => e.CustoUnitario).HasColumnName("custo_unitario");
            entity.Property(e => e.Observacao).HasColumnName("observacao");
            entity.Property(e => e.MovimentadoEm).HasColumnName("movimentado_em");
        });

        modelBuilder.Entity<CaixaMovimentacao>(entity =>
        {
            entity.ToTable("caixa_movimentacoes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.PedidoId).HasColumnName("pedido_id");
            entity.Property(e => e.Tipo).HasColumnName("tipo");
            entity.Property(e => e.FormaPagamento).HasColumnName("forma_pagamento");
            entity.Property(e => e.Categoria).HasColumnName("categoria");
            entity.Property(e => e.Descricao).HasColumnName("descricao");
            entity.Property(e => e.Valor).HasColumnName("valor");
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");
            entity.Property(e => e.Observacao).HasColumnName("observacao");
            entity.Property(e => e.MovimentadoEm).HasColumnName("movimentado_em");
        });

        modelBuilder.Entity<Despesa>(entity =>
        {
            entity.ToTable("despesas");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CadastradoPorUsuarioId).HasColumnName("cadastrado_por_usuario_id");
            entity.Property(e => e.GrupoDespesaId).HasColumnName("grupo_despesa_id");
            entity.Property(e => e.NumeroParcela).HasColumnName("numero_parcela");
            entity.Property(e => e.TotalParcelas).HasColumnName("total_parcelas");
            entity.Property(e => e.Categoria).HasColumnName("categoria");
            entity.Property(e => e.Descricao).HasColumnName("descricao");
            entity.Property(e => e.Valor).HasColumnName("valor");
            entity.Property(e => e.ValorTotal).HasColumnName("valor_total");
            entity.Property(e => e.Vencimento).HasColumnName("vencimento");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.DataPagamento).HasColumnName("data_pagamento");
            entity.Property(e => e.Observacao).HasColumnName("observacao");
        });

        modelBuilder.Entity<Pedido>(entity =>
        {
            entity.ToTable("pedidos");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Numero).HasColumnName("numero");
            entity.Property(e => e.ClienteId).HasColumnName("cliente_id");
            entity.Property(e => e.CriadoPorUsuarioId).HasColumnName("criado_por_usuario_id");
            entity.Property(e => e.Tipo).HasColumnName("tipo");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.DataPedido).HasColumnName("data_pedido");
            entity.Property(e => e.DataEntrega).HasColumnName("data_entrega");
            entity.Property(e => e.Vendedor).HasColumnName("vendedor");
            entity.Property(e => e.FormaPagamento).HasColumnName("forma_pagamento");
            entity.Property(e => e.CondicaoPagamento).HasColumnName("condicao_pagamento");
            entity.Property(e => e.Frente).HasColumnName("frente");
            entity.Property(e => e.Fundo).HasColumnName("fundo");
            entity.Property(e => e.TamanhosMasculinos).HasColumnName("tamanhos_masculinos");
            entity.Property(e => e.TamanhosFemininos).HasColumnName("tamanhos_femininos");
            entity.Property(e => e.Observacao).HasColumnName("observacao");
            entity.Property(e => e.MotivoCancelamento).HasColumnName("motivo_cancelamento");
            entity.Property(e => e.ValorEstornado).HasColumnName("valor_estornado");
            entity.Property(e => e.Subtotal).HasColumnName("subtotal");
            entity.Property(e => e.Total).HasColumnName("total");
            entity.Property(e => e.ValorPago).HasColumnName("valor_pago");
            entity.Property(e => e.SaldoDevedor).HasColumnName("saldo_devedor");
            entity.Property(e => e.ObservacaoEstorno).HasColumnName("observacao_estorno");
            entity.Property(e => e.CanceladoPorUsuarioId).HasColumnName("cancelado_por_usuario_id");
            entity.Property(e => e.CanceladoEm).HasColumnName("cancelado_em");
            entity.Property(e => e.FinalizadoPorUsuarioId).HasColumnName("finalizado_por_usuario_id");
            entity.Property(e => e.FinalizadoEm).HasColumnName("finalizado_em");

            entity.HasMany(e => e.Itens)
                .WithOne()
                .HasForeignKey(d => d.PedidoId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Pagamentos)
                .WithOne()
                .HasForeignKey(d => d.PedidoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ItemPedido>(entity =>
        {
            entity.ToTable("itens_pedido");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.PedidoId).HasColumnName("pedido_id");
            entity.Property(e => e.Descricao).HasColumnName("descricao");
            entity.Property(e => e.Tamanho).HasColumnName("tamanho");
            entity.Property(e => e.Quantidade).HasColumnName("quantidade");
            entity.Property(e => e.ValorUnitario).HasColumnName("valor_unitario");
            entity.Property(e => e.ValorTotal).HasColumnName("valor_total");
        });

        modelBuilder.Entity<Pagamento>(entity =>
        {
            entity.ToTable("pagamentos");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.PedidoId).HasColumnName("pedido_id");
            entity.Property(e => e.RegistradoPorUsuarioId).HasColumnName("registrado_por_usuario_id");
            entity.Property(e => e.FormaPagamento).HasColumnName("forma_pagamento");
            entity.Property(e => e.CondicaoPagamento).HasColumnName("condicao_pagamento");
            entity.Property(e => e.ValorTotal).HasColumnName("valor_total");
            entity.Property(e => e.Observacao).HasColumnName("observacao");
            entity.Property(e => e.RegistradoEm).HasColumnName("registrado_em");
        });
    }
}
