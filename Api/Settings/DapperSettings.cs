using AlfaHackaton.DapperContext.Common.Interfaces.Dapper.Settings;

namespace ContextService.Settings;

public sealed class DapperSettings : IDapperSettings
{
    public string ConnectionString { get; set; } = string.Empty;
}
