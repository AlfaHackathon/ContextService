using ContextService.Resources;
using FluentMigrator;

namespace ContextService.Migrations;

[Migration(2026102301)]
public sealed class InitialContext : Migration
{
    public override void Up()
    {
        Execute.Sql(ContextSql.MaterialSchema);
        Execute.Sql(ContextSql.KnowledgeSchema);
        Execute.Sql(ContextSql.ChangeSchema);
        Execute.Sql(ContextSql.ProjectionSchema);
    }
    public override void Down() => throw new NotSupportedException("Canonical context storage requires an explicit data-preserving migration.");
}
