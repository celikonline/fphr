using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sense.Modules.Training.Infrastructure.Persistence;

public class TrainingDbContextFactory : IDesignTimeDbContextFactory<TrainingDbContext>
{
    public TrainingDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TrainingDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=(local);Database=SenseIK_Training;Trusted_Connection=true;TrustServerCertificate=true;",
            x => x.MigrationsHistoryTable("__EFMigrationsHistory", "training"));

        return new TrainingDbContext(optionsBuilder.Options);
    }
}
