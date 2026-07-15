using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FleetTelemetry.Infrastructure.Persistence.Entities;

[Table("fleet_devices")]
public class FleetDeviceRecord
{
    [Key]
    [Column("device_id")]
    public Guid DeviceId { get; set; }

    [Required]
    [MaxLength(100)]
    [Column("vehicle_name")]
    public string VehicleName { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
