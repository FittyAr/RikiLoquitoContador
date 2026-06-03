using System.Linq;
using RikiLoquitoContador.Core.Models;

namespace RikiLoquitoContador.Core.Data
{
    public static class DbInitializer
    {
        public static void Seed(AppDbContext db)
        {
            if (!db.TipoFacturas.Any())
            {
                db.TipoFacturas.AddRange(new[]
                {
                    new TipoFactura { Codigo = "001", Descripcion = "Factura A", IsCustom = false },
                    new TipoFactura { Codigo = "006", Descripcion = "Factura B", IsCustom = false },
                    new TipoFactura { Codigo = "011", Descripcion = "Factura C", IsCustom = false },
                    new TipoFactura { Codigo = "051", Descripcion = "Factura M", IsCustom = false },
                    new TipoFactura { Codigo = "002", Descripcion = "Nota de Débito A", IsCustom = false },
                    new TipoFactura { Codigo = "003", Descripcion = "Nota de Crédito A", IsCustom = false },
                    new TipoFactura { Codigo = "007", Descripcion = "Nota de Débito B", IsCustom = false },
                    new TipoFactura { Codigo = "008", Descripcion = "Nota de Crédito B", IsCustom = false },
                    new TipoFactura { Codigo = "012", Descripcion = "Nota de Débito C", IsCustom = false },
                    new TipoFactura { Codigo = "013", Descripcion = "Nota de Crédito C", IsCustom = false }
                });
                db.SaveChanges();
            }

            if (!db.TipoIvas.Any())
            {
                db.TipoIvas.AddRange(new[]
                {
                    new TipoIva { Descripcion = "Responsable Inscripto", IsCustom = false },
                    new TipoIva { Descripcion = "Monotributista", IsCustom = false },
                    new TipoIva { Descripcion = "Exento", IsCustom = false },
                    new TipoIva { Descripcion = "Consumidor Final", IsCustom = false },
                    new TipoIva { Descripcion = "Sujeto No Categorizado", IsCustom = false }
                });
                db.SaveChanges();
            }
        }
    }
}
