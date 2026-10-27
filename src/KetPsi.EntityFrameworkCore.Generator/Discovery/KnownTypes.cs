namespace KetPsi.EntityFrameworkCore.Generator.Discovery;

/// <summary>Metadata names of the types the generator looks for.</summary>
internal static class KnownTypes
{
    public const string DbContext = "Microsoft.EntityFrameworkCore.DbContext";
    public const string DbSet = "Microsoft.EntityFrameworkCore.DbSet`1";
    public const string ModelBuilder = "Microsoft.EntityFrameworkCore.ModelBuilder";
    public const string EntityTypeConfiguration = "Microsoft.EntityFrameworkCore.IEntityTypeConfiguration`1";
    public const string Queryable = "System.Linq.Queryable";
    public const string Enumerable = "System.Linq.Enumerable";
    public const string QueryableInterface = "System.Linq.IQueryable`1";
    public const string EntityFrameworkQueryableExtensions = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    public const string RelationalQueryableExtensions = "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions";
    public const string SqlServerQueryableExtensions = "Microsoft.EntityFrameworkCore.SqlServerQueryableExtensions";
    public const string SqlServerDbSetExtensions = "Microsoft.EntityFrameworkCore.SqlServerDbSetExtensions";
    public const string DbSetDefinition = "Microsoft.EntityFrameworkCore.DbSet<TEntity>";
    public const string KetPsiQueryableExtensions = "KetPsi.EntityFrameworkCore.KetPsiQueryableExtensions";

    public const string EntityFrameworkCoreAssembly = "Microsoft.EntityFrameworkCore";
    public const string KetPsiAssemblyPrefix = "KetPsi.EntityFrameworkCore";
}
