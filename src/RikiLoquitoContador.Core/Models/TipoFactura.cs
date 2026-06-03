using System;

namespace RikiLoquitoContador.Core.Models
{
    public class TipoFactura
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public bool IsCustom { get; set; }
    }
}
