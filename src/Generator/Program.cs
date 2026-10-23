// Writes one synthetic graph as CSV, so MySQL and NebulaGraph load exactly the same data.
//
//   dotnet run -c Release --project src/Generator -- --nodes 50000000 --degree 9 --out data
//
// data/nodes.csv          id,name
// data/edges/part-NNNN.csv src,dst   (split so MySQL can load it in separate transactions)
//
// Shape: every node gets a geometric number of edges with mean --degree. Half of the targets are
// picked uniformly, half from a skewed distribution, so a few nodes become hubs with very many
// relationships, as in most real data (customers, accounts, devices, people).
// The run is deterministic for a given --seed.
//
// For a hub-heavy graph, shaped like an identity graph where a few placeholder values link to
// almost everything:
//
//   dotnet run -c Release --project src/Generator -- --nodes 1000000 --degree 50 --hubs 20 --hub-degree 500000
//
// --hubs plants that many extra nodes with --hub-degree uniform relationships each.
// data/hubs.csv lists every node whose degree (both directions) is above --hub-threshold, so both
// databases can be told which nodes a hub-avoiding traversal must not enter.

using System.Text;

var options = Options.Parse(args);
var outDir = Path.GetFullPath(options.Out);
var edgeDir = Path.Combine(outDir, "edges");
Directory.CreateDirectory(outDir);
if (Directory.Exists(edgeDir)) Directory.Delete(edgeDir, recursive: true);
Directory.CreateDirectory(edgeDir);

long n = options.Nodes;
var rng = new Random(options.Seed);
var started = DateTime.UtcNow;

using (var nodes = Open(Path.Combine(outDir, "nodes.csv")))
{
    for (long id = 0; id < n; id++)
        nodes.WriteLine($"{id},n{id}");
}

// A fixed permutation scatters the hubs across the id range instead of putting them at 0, 1, 2...
const long Multiplier = 2_654_435_761; // prime, so it is coprime with any node count below it
long Scatter(long rank) => rank * Multiplier % n;

double p = 1.0 / options.Degree;
double logQ = Math.Log(1 - p);
var planted = Enumerable.Range(1, options.Hubs).Select(r => Scatter(r * 7919L)).ToHashSet();
var degrees = new int[n];
var targets = new SortedSet<long>();
long edges = 0, part = 0, inPart = 0;
var writer = Open(PartPath(part));

for (long src = 0; src < n; src++)
{
    bool hub = planted.Contains(src);
    int degree = hub ? options.HubDegree : 1 + (int)Math.Floor(Math.Log(1 - rng.NextDouble()) / logQ);
    targets.Clear();
    for (int i = 0; i < degree; i++)
    {
        long dst = hub || rng.NextDouble() < 0.5
            ? rng.NextInt64(n)
            : Scatter((long)(n * Math.Pow(rng.NextDouble(), 3)));
        if (dst != src) targets.Add(dst);
    }
    foreach (var dst in targets)
    {
        writer.Write(src);
        writer.Write(',');
        writer.WriteLine(dst);
        degrees[src]++;
        degrees[dst]++;
        edges++;
        if (++inPart == options.EdgesPerFile)
        {
            writer.Dispose();
            writer = Open(PartPath(++part));
            inPart = 0;
        }
    }
    if (src % 5_000_000 == 0 && src > 0)
        Console.WriteLine($"{src:N0} nodes, {edges:N0} edges, {DateTime.UtcNow - started:hh\\:mm\\:ss}");
}
writer.Dispose();
if (inPart == 0) File.Delete(PartPath(part));

long hubCount = 0;
using (var hubs = Open(Path.Combine(outDir, "hubs.csv")))
{
    for (long id = 0; id < n; id++)
        if (degrees[id] > options.HubThreshold) { hubs.WriteLine(id); hubCount++; }
}
Console.WriteLine($"hubs: {hubCount:N0} nodes with degree above {options.HubThreshold:N0}, max degree {degrees.Max():N0}");

Console.WriteLine($"done: {n:N0} nodes + {edges:N0} edges = {n + edges:N0} rows in {DateTime.UtcNow - started:hh\\:mm\\:ss}");
File.WriteAllText(Path.Combine(outDir, "manifest.txt"),
    $"nodes={n}\nedges={edges}\nrows={n + edges}\ndegree={options.Degree}\nseed={options.Seed}\nplanted_hubs={options.Hubs}\nhub_degree={options.HubDegree}\nhub_threshold={options.HubThreshold}\nhubs={hubCount}\n");

string PartPath(long i) => Path.Combine(edgeDir, $"part-{i:D4}.csv");

static StreamWriter Open(string path) =>
    new(path, false, new UTF8Encoding(false), bufferSize: 1 << 20) { NewLine = "\n" };

record Options(long Nodes, double Degree, int Seed, string Out, long EdgesPerFile, int Hubs, int HubDegree, int HubThreshold)
{
    public static Options Parse(string[] args)
    {
        string Get(string name, string fallback)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }
        return new Options(
            long.Parse(Get("--nodes", "100000")),
            double.Parse(Get("--degree", "9"), System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(Get("--seed", "42")),
            Get("--out", "data"),
            long.Parse(Get("--edges-per-file", "10000000")),
            int.Parse(Get("--hubs", "0")),
            int.Parse(Get("--hub-degree", "0")),
            int.Parse(Get("--hub-threshold", "1000")));
    }
}
