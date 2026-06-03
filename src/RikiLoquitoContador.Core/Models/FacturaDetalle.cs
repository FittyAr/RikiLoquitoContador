using System;

namespace RikiLoquitoContador.Core.Models
{
    public class FacturaDetalle
    {
        public int Id { get; set; }
        public int FacturaId { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Subtotal { get; set; }
        public decimal? VatRate { get; set; }
    }
}
