// Opciones de conexión a TimescaleDB/PostgreSQL.
namespace FleetTelemetry.Infrastructure.Configuration;

// Cadena de conexión a la base de datos.
public class TimescaleDbOptions
{
    public const string SectionName = "TimescaleDb";

    public string ConnectionString { get; set; } =
        "Host=localhost;Port=5432;Database=fleet;Username=fleet;Password=fleet";
}
