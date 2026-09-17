using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sense.Modules.Announcements.Infrastructure.Persistence;

public class AnnouncementsDbContextFactory : IDesignTimeDbContextFactory<AnnouncementsDbContext>
{
    public AnnouncementsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AnnouncementsDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=(local);Database=SenseIK_Announcements;Trusted_Connection=true;TrustServerCertificate=true;",
            x => x.MigrationsHistoryTable("__EFMigrationsHistory", "announcements"));

        return new AnnouncementsDbContext(optionsBuilder.Options);
    }
}
