using ERP.Service.Services.Shared;
using Microsoft.EntityFrameworkCore.Design;

namespace ERP.Service.Data;

/// <summary>يُستخدم فقط بواسطة dotnet ef لإنشاء المهاجرات دون تشغيل التطبيق أو الاتصال بقاعدة بيانات.</summary>
public class ErpDbContextFactory : IDesignTimeDbContextFactory<ErpDbContext>
{
    public ErpDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ERP_CONNECTION")
            ?? @"Server=(localdb)\MSSQLLocalDB;Database=ErpDb;Trusted_Connection=True;TrustServerCertificate=True;";
        var options = new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connection).Options;
        return new ErpDbContext(options, new TenantContext());
    }
}
