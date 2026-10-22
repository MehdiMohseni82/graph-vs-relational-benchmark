// Runs the same questions against MySQL (two ways) and NebulaGraph, checks the answers agree,
// and writes every timing to results/<tier>.csv plus a summary table.
//
//   dotnet run -c Release --project src/Bench -- run --tier 1M
//   dotnet run -c Release --project src/Bench -- nebula "SUBMIT JOB COMPACT"

using System.Diagnostics;
using System.Globalization;
using MySqlConnector;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

if (args.Length >= 2 && args[0] == "nebula")
{
    await using var nebula = await NebulaGql.OpenAsync(requireSpace: false);
    Console.WriteLine(await nebula.ExecuteAsync(args[1]));
    return;
}

var options = RunOptions.Parse(args);
Directory.CreateDirectory("results");
var csvPath = Path.Combine("results", $"{options.Tier}.csv");
await using var csv = new StreamWriter(csvPath, append: false);
await csv.WriteLineAsync("tier,method,query,hops,case,a,b,run,ms,answer");

await using var setup = await MySql.OpenAsync();
long nodeCount = Convert.ToInt64(await new MySqlCommand("SELECT MAX(id) + 1 FROM nodes", setup).ExecuteScalarAsync());
Console.WriteLine($"tier {options.Tier}: {nodeCount:N0} nodes");

var rng = new Random(7);
var starts = Enumerable.Range(0, options.Seeds).Select(_ => rng.NextInt64(nodeCount)).ToList();
var pairs = new List<(long A, long B, string Kind)>();
for (int i = 0; i < options.Seeds; i++)
{
    long a = rng.NextInt64(nodeCount);
    if (i % 2 == 0)
        pairs.Add((a, await RandomWalkAsync(setup, a, 2 + i / 2 % 3, rng), "walk"));
    else
        pairs.Add((a, rng.NextInt64(nodeCount), "random"));
}

var methods = new List<IMethod>();
foreach (var m in options.Methods)
    methods.Add(m switch
    {
        "cte" => new MySqlCte(await MySql.OpenAsync()),
        "bfs" => new MySqlBfs(await MySql.OpenAsync()),
        "nebula" => await NebulaGql.OpenAsync(),
        _ => throw new ArgumentException($"unknown method {m}")
    });

var timings = new List<Timing>();
int errors = 0;
var answers = new Dictionary<string, Dictionary<string, long?>>();

// Question 1: how many nodes are within N hops of this one?
foreach (var method in methods)
    for (int s = 0; s < starts.Count; s++)
    {
        long start = starts[s];
        foreach (int hops in options.Hops)
        {
            var key = $"neighbourhood {hops} hops from {start}";
            bool timedOut = await MeasureAsync(method, "neighbourhood", hops, $"s{s}", start, -1, key,
                () => method.NeighbourhoodAsync(start, hops, options.Timeout));
            if (timedOut) break; // a deeper search from the same node will not be faster
        }
    }

// Question 2: are A and B related, and how many hops apart?
foreach (var method in methods)
    for (int p = 0; p < pairs.Count; p++)
    {
        var (a, b, kind) = pairs[p];
        var key = $"path {a} to {b}";
        await MeasureAsync(method, "shortest path", options.MaxPathHops, $"{kind}{p}", a, b, key,
            () => method.ShortestPathAsync(a, b, options.MaxPathHops, options.Timeout));
    }

foreach (var method in methods) await method.DisposeAsync();

// Every method has to give the same answer, or its timing means nothing.
int mismatches = 0;
foreach (var (key, byMethod) in answers)
{
    var distinct = byMethod.Where(kv => kv.Value is not null || !timedOutKeys.Contains((kv.Key, key)))
        .Select(kv => kv.Value).Distinct().ToList();
    if (distinct.Count > 1)
    {
        mismatches++;
        Console.WriteLine($"MISMATCH {key}: " + string.Join(", ", byMethod.Select(kv => $"{kv.Key}={kv.Value?.ToString() ?? "none"}")));
    }
}
Console.WriteLine(mismatches == 0 ? "answers: all methods agree" : $"answers: {mismatches} mismatches");

var summary = Summary.Render(options, nodeCount, timings, methods.Select(m => m.Name).ToList(), mismatches);
if (errors > 0)
{
    summary = $"WARNING: {errors} queries failed with an engine error (see the CSV). Check the logs; these results are not valid.\n\n" + summary;
    Console.WriteLine($"errors: {errors}");
}
await File.WriteAllTextAsync(Path.Combine("results", $"{options.Tier}-summary.md"), summary);
Console.WriteLine(summary);
return;

async Task<bool> MeasureAsync(IMethod method, string query, int hops, string caseId, long a, long b, string key, Func<Task<long?>> run)
{
    for (int r = 0; r <= options.Runs; r++) // run 0 is the warm-up and is recorded but not summarised
    {
        var watch = Stopwatch.StartNew();
        long? answer;
        try
        {
            answer = await run();
        }
        catch (QueryTimeout)
        {
            watch.Stop();
            timings.Add(new Timing(method.Name, query, hops, caseId, r, null));
            timedOutKeys.Add((method.Name, key));
            await csv.WriteLineAsync($"{options.Tier},{method.Name},{query},{hops},{caseId},{a},{b},{r},timeout,");
            await csv.FlushAsync();
            Console.WriteLine($"{method.Name,-22} {query,-14} {hops} hops {caseId,-8} run {r}: TIMEOUT after {options.Timeout.TotalSeconds:N0} s");
            return true;
        }
        catch (Exception e) when (e is not QueryTimeout)
        {
            // An engine error is not a result. Record it, keep going, and mark the whole run invalid.
            errors++;
            await csv.WriteLineAsync($"{options.Tier},{method.Name},{query},{hops},{caseId},{a},{b},{r},error,");
            await csv.FlushAsync();
            Console.WriteLine($"{method.Name,-22} {query,-14} {hops} hops {caseId,-8} run {r}: ERROR {e.Message.Split('\n')[0]}");
            return true;
        }
        watch.Stop();
        double ms = watch.Elapsed.TotalMilliseconds;
        timings.Add(new Timing(method.Name, query, hops, caseId, r, ms));
        answers.TryAdd(key, new());
        answers[key][method.Name] = answer;
        await csv.WriteLineAsync($"{options.Tier},{method.Name},{query},{hops},{caseId},{a},{b},{r},{ms:F1},{answer}");
        await csv.FlushAsync();
        Console.WriteLine($"{method.Name,-22} {query,-14} {hops} hops {caseId,-8} run {r}: {ms,10:N1} ms  answer {answer?.ToString() ?? "none"}");
        if (ms > options.SlowRunMs) break; // one measured run is enough when a run takes minutes
    }
    return false;
}

static async Task<long> RandomWalkAsync(MySqlConnection connection, long from, int steps, Random rng)
{
    long current = from;
    for (int i = 0; i < steps; i++)
    {
        var neighbours = new List<long>();
        await using var command = new MySqlCommand(string.Format(Queries.MySqlNeighboursOf, current), connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) neighbours.Add(reader.GetInt64(0));
        if (neighbours.Count == 0) break;
        current = neighbours[rng.Next(neighbours.Count)];
    }
    return current;
}

partial class Program
{
    static readonly HashSet<(string Method, string Key)> timedOutKeys = new();
}

record Timing(string Method, string Query, int Hops, string Case, int Run, double? Ms);

record RunOptions(string Tier, int Seeds, int Runs, TimeSpan Timeout, int[] Hops, int MaxPathHops, string[] Methods, double SlowRunMs)
{
    public static RunOptions Parse(string[] args)
    {
        string Get(string name, string fallback)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }
        return new RunOptions(
            Get("--tier", "dev"),
            int.Parse(Get("--seeds", "10")),
            int.Parse(Get("--runs", "3")),
            TimeSpan.FromSeconds(double.Parse(Get("--timeout", "900"))),
            Get("--hops", "1,2,3,4").Split(',').Select(int.Parse).ToArray(),
            int.Parse(Get("--max-path", "6")),
            Get("--methods", "cte,bfs,nebula").Split(','),
            double.Parse(Get("--slow-run-ms", "60000")));
    }
}

static class Summary
{
    public static string Render(RunOptions options, long nodes, List<Timing> timings, List<string> methods, int mismatches)
    {
        var lines = new List<string>
        {
            $"# Results: {options.Tier} ({nodes:N0} nodes)",
            "",
            $"Median of the per-case medians (warm-up excluded), {options.Seeds} cases per row, timeout {options.Timeout.TotalSeconds:N0} s.",
            mismatches == 0 ? "All methods returned the same answers." : $"WARNING: {mismatches} answers differ between methods.",
            "",
            "| Query | " + string.Join(" | ", methods) + " |",
            "|---|" + string.Concat(methods.Select(_ => "---:|")),
        };
        foreach (var group in timings.GroupBy(t => (t.Query, t.Hops)).OrderBy(g => g.Key.Query).ThenBy(g => g.Key.Hops))
        {
            var label = group.Key.Query == "shortest path" ? $"shortest path (up to {group.Key.Hops} hops)" : $"neighbourhood, {group.Key.Hops} hops";
            var cells = methods.Select(m => Cell(group.Where(t => t.Method == m).ToList()));
            lines.Add($"| {label} | " + string.Join(" | ", cells) + " |");
        }
        return string.Join('\n', lines) + "\n";
    }

    static string Cell(List<Timing> runs)
    {
        if (runs.Count == 0) return "skipped";
        var perCase = runs.GroupBy(t => t.Case).Select(g =>
        {
            var measured = g.Where(t => t.Run > 0 && t.Ms is not null).Select(t => t.Ms!.Value).ToList();
            if (measured.Count == 0) measured = g.Where(t => t.Ms is not null).Select(t => t.Ms!.Value).ToList();
            return g.Any(t => t.Ms is null) ? (double?)null : Median(measured);
        }).ToList();
        int timeouts = perCase.Count(x => x is null);
        var done = perCase.Where(x => x is not null).Select(x => x!.Value).ToList();
        if (done.Count == 0) return $"timeout ({timeouts}/{perCase.Count})";
        var text = Format(Median(done));
        return timeouts > 0 ? $"{text}, {timeouts}/{perCase.Count} timed out" : text;
    }

    static double Median(List<double> xs)
    {
        xs.Sort();
        return xs.Count % 2 == 1 ? xs[xs.Count / 2] : (xs[xs.Count / 2 - 1] + xs[xs.Count / 2]) / 2;
    }

    static string Format(double ms) => ms switch
    {
        < 1_000 => $"{ms:N1} ms",
        < 60_000 => $"{ms / 1000:N2} s",
        _ => $"{ms / 60_000:N1} min",
    };
}
