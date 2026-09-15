using System.Net.Sockets;
using Npgsql;

namespace OpenGISToolbox.TestKit;

/// <summary>
/// Locates a usable PostgreSQL/PostGIS endpoint for round-trip tests.
/// Connection parameters come from OGT_TEST_PG_* environment variables,
/// defaulting to the local development container. When the endpoint is not
/// reachable, callers must skip (Warn) rather than fail.
/// </summary>
public sealed class PgProbe
{
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 5432;
    public string Database { get; init; } = "postgres";
    public string User { get; init; } = "postgres";
    public string Password { get; init; } = "postgres";

    /// <summary>GDAL-style connection string used by the PostGIS tools. No quoting (GDAL takes quotes literally).</summary>
    public string GdalConnectionString =>
        $"PG:host={Host} port={Port} dbname={Database} user={User} password={Password}";

    public string NpgsqlConnectionString =>
        $"Host={Host};Port={Port};Database={Database};Username={User};Password={Password};Timeout=10;Command Timeout=60";

    /// <summary>
    /// Returns a reachable probe, or null when PG is not installed/running.
    /// Performs a TCP connect first so a missing server costs ~1s, not a driver timeout.
    /// </summary>
    public static PgProbe? TryResolve(IProgress<string>? log = null)
    {
        var probe = new PgProbe
        {
            Host = Environment.GetEnvironmentVariable("OGT_TEST_PG_HOST") is { Length: > 0 } h ? h : "127.0.0.1",
            Port = int.TryParse(Environment.GetEnvironmentVariable("OGT_TEST_PG_PORT"), out var p) ? p : 5432,
            Database = Environment.GetEnvironmentVariable("OGT_TEST_PG_DB") is { Length: > 0 } db ? db : "postgres",
            User = Environment.GetEnvironmentVariable("OGT_TEST_PG_USER") is { Length: > 0 } u ? u : "postgres",
            Password = Environment.GetEnvironmentVariable("OGT_TEST_PG_PASSWORD") is { Length: > 0 } pw ? pw : "postgres",
        };

        try
        {
            using var tcp = new TcpClient();
            var ok = tcp.ConnectAsync(probe.Host, probe.Port).Wait(2000);
            if (!ok) { log?.Report($"PG {probe.Host}:{probe.Port} not reachable (tcp timeout)"); return null; }
        }
        catch (Exception ex)
        {
            log?.Report($"PG {probe.Host}:{probe.Port} not reachable: {ex.Message}");
            return null;
        }

        try
        {
            using var conn = new NpgsqlConnection(probe.NpgsqlConnectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand("SELECT postgis_version()", conn);
            var v = cmd.ExecuteScalar()?.ToString();
            log?.Report($"PostGIS at {probe.Host}:{probe.Port} version {v}");
            return probe;
        }
        catch (Exception ex)
        {
            log?.Report($"PG reachable but query failed: {ex.Message}");
            return null;
        }
    }

    public async Task<int> CountAsync(string table)
    {
        await using var conn = new NpgsqlConnection(NpgsqlConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM \"{table}\"", conn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<List<long>> FidsAsync(string table)
    {
        await using var conn = new NpgsqlConnection(NpgsqlConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"SELECT ogc_fid FROM \"{table}\" ORDER BY ogc_fid", conn);
        await using var rdr = await cmd.ExecuteReaderAsync();
        var result = new List<long>();
        while (await rdr.ReadAsync()) result.Add(rdr.GetInt64(0));
        return result;
    }

    public async Task<List<string>> ColumnNamesAsync(string table)
    {
        await using var conn = new NpgsqlConnection(NpgsqlConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_schema='public' AND lower(table_name)=lower(@t) ORDER BY ordinal_position", conn);
        cmd.Parameters.AddWithValue("t", table.ToLowerInvariant());
        await using var rdr = await cmd.ExecuteReaderAsync();
        var result = new List<string>();
        while (await rdr.ReadAsync()) result.Add(rdr.GetString(0));
        return result;
    }

    public async Task<string> GeomSummaryAsync(string table)
    {
        await using var conn = new NpgsqlConnection(NpgsqlConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"""
            SELECT coalesce(string_agg(geom_type || '@' || srid, ','), 'none') FROM (
              SELECT DISTINCT ST_GeometryType(wkb_geometry) AS geom_type, ST_SRID(wkb_geometry) AS srid
              FROM "{table}") s
            """, conn);
        return (await cmd.ExecuteScalarAsync())?.ToString() ?? "none";
    }

    public async Task DropAsync(string table)
    {
        await using var conn = new NpgsqlConnection(NpgsqlConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP TABLE IF EXISTS \"{table}\"", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
