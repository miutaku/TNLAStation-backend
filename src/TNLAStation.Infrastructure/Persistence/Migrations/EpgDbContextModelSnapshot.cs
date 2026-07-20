using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace TNLAStation.Infrastructure.Persistence.Migrations;

[DbContext(typeof(EpgDbContext))]
public sealed class EpgDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.10")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        EpgDbContext.ConfigureModel(modelBuilder);
    }
}
