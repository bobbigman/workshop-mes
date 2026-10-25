using Microsoft.Data.SqlClient;

namespace ahu.MicrosoftMes.Common;

/// <summary>从有效配置新建连接，不能复用已打开的EF连接串（默认会移除密码）。</summary>
public sealed class ConfiguredSqlConnection(IConfiguration config)
{
    public string ConnectionString => config.GetConnectionString("Default")
        is { Length: > 0 } value ? value : throw ThrowHelper.General(nameof(ConfiguredSqlConnection), "缺少ConnectionStrings:Default配置");

    public SqlConnection Create() => new(ConnectionString);
}
