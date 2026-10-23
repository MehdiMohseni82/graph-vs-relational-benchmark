using System.Text;
using MySqlConnector;
using NebulaNet;

// One way of answering the two questions. Each returns the answer so the runner can check
// that all three methods agree before it trusts any timing.
interface IMethod : IAsyncDisposable
{
    string Name { get; }
    Task<long?> NeighbourhoodAsync(long start, int hops, TimeSpan timeout);
    Task<long?> ShortestPathAsync(long a, long b, int maxHops, TimeSpan timeout);
    Task<long?> NeighbourhoodAvoidingHubsAsync(long start, int hops, TimeSpan timeout);
}

sealed class QueryTimeout : Exception;

static class MySql
{
    public const string ConnectionString =
        "Server=127.0.0.1;Port=3306;User ID=root;Password=bench;Database=graph;Pooling=false;Default Command Timeout=0";

    public static async Task<MySqlConnection> OpenAsync()
    {
        var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}

// The recursive CTE, sent as one statement. MySQL itself stops it at the timeout.
sealed class MySqlCte(MySqlConnection connection) : IMethod
{
    public string Name => "MySQL recursive CTE";

    public Task<long?> NeighbourhoodAsync(long start, int hops, TimeSpan timeout) =>
        ScalarAsync(Queries.MySqlNeighbourhood, timeout, ("@start", start), ("@hops", hops));

    public Task<long?> ShortestPathAsync(long a, long b, int maxHops, TimeSpan timeout) =>
        ScalarAsync(Queries.MySqlShortestPath, timeout, ("@a", a), ("@b", b), ("@maxHops", maxHops));

    public Task<long?> NeighbourhoodAvoidingHubsAsync(long start, int hops, TimeSpan timeout) =>
        ScalarAsync(Queries.MySqlNeighbourhoodAvoidingHubs, timeout, ("@start", start), ("@hops", hops));

    async Task<long?> ScalarAsync(string sql, TimeSpan timeout, params (string Name, long Value)[] parameters)
    {
        await using (var limit = new MySqlCommand($"SET SESSION max_execution_time = {(long)timeout.TotalMilliseconds}", connection))
            await limit.ExecuteNonQueryAsync();

        await using var command = new MySqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        try
        {
            var result = await command.ExecuteScalarAsync();
            return result is null or DBNull ? null : Convert.ToInt64(result);
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.QueryTimeout || e.Number == 3024)
        {
            throw new QueryTimeout();
        }
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();
}

// The fix most teams reach for next: keep the edges table, but walk it from C#, one level at a
// time, remembering what was already visited. This is a small graph engine in the application.
sealed class MySqlBfs(MySqlConnection connection) : IMethod
{
    const int BatchSize = 2_000;

    public string Name => "MySQL + BFS in C#";

    public Task<long?> NeighbourhoodAsync(long start, int hops, TimeSpan timeout) =>
        ReachAsync(start, hops, timeout, Queries.MySqlNeighboursOf);

    // Question 3 never counts the start node.
    public async Task<long?> NeighbourhoodAvoidingHubsAsync(long start, int hops, TimeSpan timeout) =>
        await ReachAsync(start, hops, timeout, Queries.MySqlNeighboursOfAvoidingHubs, countStart: false);

    async Task<long?> ReachAsync(long start, int hops, TimeSpan timeout, string step, bool countStart = true)
    {
        var deadline = DateTime.UtcNow + timeout;
        var visited = new HashSet<long> { start };
        var frontier = new List<long> { start };
        bool startHasNeighbours = false;

        for (int depth = 1; depth <= hops && frontier.Count > 0; depth++)
        {
            var next = new List<long>();
            foreach (var neighbour in await NeighboursAsync(frontier, deadline, step))
            {
                if (depth == 1) startHasNeighbours = true;
                if (visited.Add(neighbour)) next.Add(neighbour);
            }
            frontier = next;
        }
        // Same answer as the CTE: every node reachable in 1..hops steps. The start node is one of
        // them from two steps on, because you can go to a neighbour and come straight back.
        bool startCounts = countStart && hops >= 2 && startHasNeighbours;
        return visited.Count - (startCounts ? 0 : 1);
    }

    // Bidirectional BFS: grow a frontier from each end, always the smaller one, stop when they meet.
    public async Task<long?> ShortestPathAsync(long a, long b, int maxHops, TimeSpan timeout)
    {
        if (a == b) return 0;
        var deadline = DateTime.UtcNow + timeout;
        var seenFromA = new HashSet<long> { a };
        var seenFromB = new HashSet<long> { b };
        var frontierA = new List<long> { a };
        var frontierB = new List<long> { b };

        for (int length = 1; length <= maxHops; length++)
        {
            bool expandA = frontierA.Count <= frontierB.Count;
            var (frontier, seen, other) = expandA
                ? (frontierA, seenFromA, seenFromB)
                : (frontierB, seenFromB, seenFromA);
            if (frontier.Count == 0) return null;

            var next = new List<long>();
            foreach (var neighbour in await NeighboursAsync(frontier, deadline, Queries.MySqlNeighboursOf))
            {
                if (other.Contains(neighbour)) return length;
                if (seen.Add(neighbour)) next.Add(neighbour);
            }
            if (expandA) frontierA = next; else frontierB = next;
        }
        return null;
    }

    async Task<List<long>> NeighboursAsync(List<long> nodes, DateTime deadline, string step)
    {
        var result = new List<long>();
        for (int i = 0; i < nodes.Count; i += BatchSize)
        {
            if (DateTime.UtcNow > deadline) throw new QueryTimeout();
            var ids = string.Join(',', nodes.Skip(i).Take(BatchSize));
            await using var command = new MySqlCommand(string.Format(step, ids), connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result.Add(reader.GetInt64(0));
        }
        return result;
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();
}

sealed class NebulaGql : IMethod
{
    NebulaConnection connection = null!;
    long session;
    bool requireSpace = true;
    bool levelByLevel;
    public bool LevelByLevel => levelByLevel;
    HashSet<long>? clientHubs; // set: hubs are filtered in C#, not by a WHERE on tags($$)

    // levelByLevel: question 3 is walked from C#, one GO step per level, as an application does
    // when it wants the filter applied during the search. Questions 1 and 2 are unchanged.
    public string Name => !levelByLevel ? "NebulaGraph nGQL"
        : clientHubs is null ? "NebulaGraph steps (WHERE tags)" : "NebulaGraph steps (hub set in C#)";

    public static async Task<NebulaGql> OpenAsync(bool requireSpace = true, bool levelByLevel = false, HashSet<long>? clientHubs = null)
    {
        var nebula = new NebulaGql { requireSpace = requireSpace, levelByLevel = levelByLevel, clientHubs = clientHubs };
        await nebula.ConnectAsync();
        return nebula;
    }

    async Task ConnectAsync()
    {
        connection = new NebulaConnection();
        // A host name, not an IP: for an IP this client sets a 10 ms socket timeout.
        await connection.OpenAsync("localhost", 9669);
        session = (await connection.AuthenticateAsync("root", "nebula")).Session_id;
        try { await connection.ExecuteAsync(session, "USE graph;"); }
        catch (Exception) when (!requireSpace) { } // admin commands also run before the space exists
    }

    public Task<long?> NeighbourhoodAsync(long start, int hops, TimeSpan timeout) =>
        ScalarAsync(Queries.NebulaNeighbourhood(start, hops), timeout);

    public Task<long?> ShortestPathAsync(long a, long b, int maxHops, TimeSpan timeout) =>
        ScalarAsync(Queries.NebulaShortestPath(a, b, maxHops), timeout);

    public Task<long?> NeighbourhoodAvoidingHubsAsync(long start, int hops, TimeSpan timeout) =>
        levelByLevel
            ? StepsAsync(start, hops, timeout)
            : throw new NotSupportedException("run question 3 with nebula-steps");

    async Task<long?> StepsAsync(long start, int hops, TimeSpan timeout)
    {
        const int BatchSize = 2_000;
        var deadline = DateTime.UtcNow + timeout;
        var visited = new HashSet<long> { start };
        var frontier = new List<long> { start };
        for (int depth = 1; depth <= hops && frontier.Count > 0; depth++)
        {
            var next = new List<long>();
            for (int i = 0; i < frontier.Count; i += BatchSize)
            {
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) throw new QueryTimeout();
                var batch = frontier.Skip(i).Take(BatchSize);
                var statement = clientHubs is null
                    ? Queries.NebulaNeighboursOfAvoidingHubs(batch)
                    : Queries.NebulaNeighboursOf(batch);
                foreach (var neighbour in await ColumnAsync(statement, left))
                    if ((clientHubs is null || !clientHubs.Contains(neighbour)) && visited.Add(neighbour)) next.Add(neighbour);
            }
            frontier = next;
        }
        return visited.Count - 1; // question 3 never counts the start node
    }

    async Task<List<long>> ColumnAsync(string statement, TimeSpan timeout)
    {
        var call = connection.ExecuteAsync(session, statement);
        if (await Task.WhenAny(call, Task.Delay(timeout)) != call)
        {
            connection.Dispose();
            await ConnectAsync();
            throw new QueryTimeout();
        }
        var response = await call;
        if (response.Error_code != 0)
            throw new InvalidOperationException(Encoding.UTF8.GetString(response.Error_msg ?? []));
        return response.Data?.Rows?.Select(r => r.Values[0].IVal).ToList() ?? [];
    }

    public async Task<string> ExecuteAsync(string statement)
    {
        var response = await connection.ExecuteAsync(session, statement);
        if (response.Data?.Rows is not { Count: > 0 } rows) return "(no rows)";
        return string.Join('\n', rows.Select(r => string.Join(" | ", r.Values.Select(Show))));
    }

    async Task<long?> ScalarAsync(string statement, TimeSpan timeout)
    {
        var call = connection.ExecuteAsync(session, statement);
        if (await Task.WhenAny(call, Task.Delay(timeout)) != call)
        {
            // The client cannot cancel a running query; drop the connection and start a new one.
            connection.Dispose();
            await ConnectAsync();
            throw new QueryTimeout();
        }
        var response = await call;
        var rows = response.Data?.Rows;
        return rows is { Count: > 0 } ? rows[0].Values[0].IVal : null;
    }

    static string Show(Nebula.Common.Value v) =>
        v.__isset.sVal ? Encoding.UTF8.GetString(v.SVal) : v.__isset.iVal ? v.IVal.ToString() : v.ToString();

    public async ValueTask DisposeAsync()
    {
        try { await connection.SignOutAsync(session); } catch { }
        connection.Dispose();
    }
}
