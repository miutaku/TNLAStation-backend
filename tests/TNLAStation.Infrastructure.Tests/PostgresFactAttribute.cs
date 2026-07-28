namespace TNLAStation.Infrastructure.Tests;

/// <summary>
/// PostgreSQL を実際に使う試験。接続先が与えられていない環境では飛ばし、通常の単体試験が
/// 外部サービスの有無で不安定にならないようにする。
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string ConnectionStringVariable = "TNLA_TEST_POSTGRES";

    public PostgresFactAttribute()
    {
        if (ConnectionString is null)
        {
            Skip = $"{ConnectionStringVariable} が設定されていないため PostgreSQL 統合試験を飛ばします。";
        }
    }

    public static string? ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringVariable) is { Length: > 0 } value ? value : null;
}
