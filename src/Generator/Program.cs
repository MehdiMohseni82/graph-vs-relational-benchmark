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
var targets = new SortedSet<long>();
long edges = 0, part = 0, inPart = 0;
var writer = Open(PartPath(part));

for (long src = 0; src < n; src++)
{
    int degree = 1 + (int)Math.Floor(Math.Log(1 - rng.NextDouble()) / logQ);
    targets.Clear();
    for (int i = 0; i < degree; i++)
    {
        long dst = rng.NextDouble() < 0.5
            ? rng.NextInt64(n)
            : Scatter((long)(n * Math.Pow(rng.NextDouble(), 3)));
        if (dst != src) targets.Add(dst);
    }
    foreach (var dst in targets)
    {
        writer.Write(src);
        writer.Write(',');
        writer.WriteLine(dst);
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

Console.WriteLine($"done: {n:N0} nodes + {edges:N0} edges = {n + edges:N0} rows in {DateTime.UtcNow - started:hh\\:mm\\:ss}");
File.WriteAllText(Path.Combine(outDir, "manifest.txt"),
    $"nodes={n}\nedges={edges}\nrows={n + edges}\ndegree={options.Degree}\nseed={options.Seed}\n");

string PartPath(long i) => Path.Combine(edgeDir, $"part-{i:D4}.csv");

static StreamWriter Open(string path) =>
    new(path, false, new UTF8Encoding(false), bufferSize: 1 << 20) { NewLine = "\n" };

record Options(long Nodes, double Degree, int Seed, string Out, long EdgesPerFile)
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
            long.Parse(Get("--edges-per-file", "10000000")));
    }
}
