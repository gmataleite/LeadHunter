using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LeadHunter.Infrastructure.Persistence;

public sealed class LeadHunterDbContextFactory : IDesignTimeDbContextFactory<LeadHunterDbContext>
{
    public LeadHunterDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<LeadHunterDbContext>();
        optionsBuilder.UseSqlite("Data Source=leadhunter.db");
        return new LeadHunterDbContext(optionsBuilder.Options);
    }
}
