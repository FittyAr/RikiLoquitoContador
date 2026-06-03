using Microsoft.EntityFrameworkCore;
using RikiLoquitoContador.Core.Models;

namespace RikiLoquitoContador.Core.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Factura> Facturas => Set<Factura>();
        public DbSet<FacturaDetalle> FacturaDetalles => Set<FacturaDetalle>();
        public DbSet<TipoFactura> TipoFacturas => Set<TipoFactura>();
        public DbSet<TipoIva> TipoIvas => Set<TipoIva>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<TipoFactura>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Codigo).IsRequired().HasMaxLength(10);
                entity.Property(e => e.Descripcion).IsRequired().HasMaxLength(100);
            });

            modelBuilder.Entity<TipoIva>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Descripcion).IsRequired().HasMaxLength(100);
            });

            modelBuilder.Entity<FacturaDetalle>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Description).IsRequired().HasMaxLength(500);
                entity.Property(e => e.Quantity).HasPrecision(18, 4);
                entity.Property(e => e.UnitPrice).HasPrecision(18, 4);
                entity.Property(e => e.Subtotal).HasPrecision(18, 2);
                entity.Property(e => e.VatRate).HasPrecision(5, 2);
                
                entity.HasOne<Factura>()
                      .WithMany(f => f.Detalles)
                      .HasForeignKey(d => d.FacturaId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Factura>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.FileName).IsRequired().HasMaxLength(255);
                entity.Property(e => e.FilePath).IsRequired().HasMaxLength(500);
                entity.Property(e => e.FileExtension).IsRequired().HasMaxLength(10);
                entity.Property(e => e.TotalAmount).HasPrecision(18, 2);
                
                entity.Property(e => e.FileHash).IsRequired().HasMaxLength(64);
                entity.HasIndex(e => e.FileHash);
            });
        }
    }
}
