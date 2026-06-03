using System;

namespace RikiLoquitoContador.Core.Models
{
    public class TipoIva
    {
        public int Id { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public bool IsCustom { get; set; }
    }
}
