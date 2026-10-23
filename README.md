# Graph vs relational: MySQL and NebulaGraph on the same graph

A reproducible benchmark for one question: what happens when you store a graph as a
many-to-many table in a relational database and ask it graph questions, compared with a
graph database, from 1 million to 500 million rows, and on a second graph with large hubs.

It is the code behind the article
[Relationships in a Relational Database: MySQL vs NebulaGraph from 1 Million to 500 Million Rows](https://dotnet-talk.com/blog/graph-vs-relational-mysql-nebulagraph-benchmark)
on dotnet-talk.com.

## What it measures

Three questions, asked of the same data in several ways:

| Question | MySQL recursive CTE | MySQL + BFS in C# | NebulaGraph |
|---|---|---|---|
| How many nodes are within 1, 2, 3, 4 hops of this one? | `WITH RECURSIVE` | level-by-level BFS with a visited set | `GO 1 TO n STEPS ... BIDIRECT` |
| Are A and B related, and how many hops apart (up to 6)? | `WITH RECURSIVE` + `MIN(depth)` | bidirectional BFS | `FIND SINGLE SHORTEST PATH` |
| The neighbourhood again, but never entering a hub (`--avoid-hubs`) | the CTE with `NOT EXISTS` on a `hubs` table | the BFS with the same condition | one `GO` step per level from C# (`nebula-steps`, `nebula-steps-client`) |

A relationship counts in both directions. All queries are in
[`src/Bench/Queries.cs`](src/Bench/Queries.cs), and the methods are in
[`src/Bench/Methods.cs`](src/Bench/Methods.cs). For the third question NebulaGraph has no
single statement that is both correct and usable (see the comments in `Queries.cs`), so it is
driven level by level from C#.

Before any timing is trusted, the runner compares the answers of all methods for every case
and reports any mismatch in the summary.

## The data

[`src/Generator`](src/Generator/Program.cs) writes one deterministic graph as CSV, which both
databases load:

- every node gets a geometric number of relationships with a given mean (`--degree`, 9 by default);
- half of the targets are uniform, half come from a skewed distribution, so some nodes become hubs;
- `--hubs` and `--hub-degree` plant extra hubs, and `data/hubs.csv` lists every node above
  `--hub-threshold` relationships;
- rows = nodes + relationships, so 10,000,000 nodes at degree 9 is about 100,000,000 rows.

## Running it

[`RUN-FAIR-BENCHMARK.md`](RUN-FAIR-BENCHMARK.md) is the full runbook for the four sizes, with
the Docker settings and the validity checks, and [`RUN-HUB-BENCHMARK.md`](RUN-HUB-BENCHMARK.md)
adds the hub graph. In short:

```bash
cp .env.example .env      # memory per engine, equal for both
scripts/run-tier.sh 1M   100000   --timeout 900
scripts/run-tier.sh 500M 50000000 --timeout 900
GEN_ARGS="--degree 50 --hubs 20 --hub-degree 1000000 --hub-threshold 1000" \
  scripts/run-tier.sh hubs 1000000 --timeout 300 --avoid-hubs 1,2,3,4 \
  --methods cte,bfs,nebula,nebula-steps
```

Each run starts from empty databases: it generates the data, loads MySQL (`LOAD DATA INFILE`,
reverse index built after the load), loads NebulaGraph (nebula-importer, then compaction), and
runs the benchmark. Results go to `results/<tier>.csv` (every single run) and
`results/<tier>-summary.md` (the table).

## Setup used for the published results

- Mac Studio, Apple M3 Ultra, 96 GB RAM. Docker Desktop with 80 GB of memory and 24 CPUs,
  nothing else running ([`results/machine.txt`](results/machine.txt), [`results/notes.txt`](results/notes.txt)).
- MySQL 8.4 in a 30 GB container with a 24 GB buffer pool. NebulaGraph 3.8 with 30 GB in total:
  storaged 24 GB with a 16 GB block cache, graphd 6 GB. One storage node, 36 partitions.
- 10 cases per question, one warm-up and 3 measured runs, median of the per-case medians.
  Timeout 15 minutes for the four sizes and 5 minutes for the hub graph.
- Every size passed the validity checks: no container killed or restarted, no kernel OOM kill,
  no engine error, and all methods returned the same answers (`results/*-checks.txt`).

## Results

[`results/`](results/) has the summary table, the CSV of every run and the checks for each size
(`1M`, `10M`, `100M`, `500M`, `hubs`).

One note on `hubs-summary.md`: its "NebulaGraph, steps from C#" column filtered hubs with
`WHERE NOT "hub" IN tags($$)` in every step, which makes NebulaGraph read the tags of every
destination node. A variant that keeps the hub ids in C# (`nebula-steps-client`) was about twice
as fast in a small test but was not measured at full size, so the article leaves NebulaGraph out
of that table. Measurements of `nebula-steps-client` at full size are very welcome.

Earlier laptop results from 21-22 October 2026 were withdrawn because Docker's VM ran out of
memory; they are kept in [`results/withdrawn-2026-10-22-laptop/`](results/withdrawn-2026-10-22-laptop/)
with the reason.

## Limits, honestly

- One machine, local disk, no other traffic. This shows how each approach scales with the size
  and depth of the question, not what a production cluster will do.
- NebulaGraph runs with one storage node here; it is built to spread partitions over many.
- The data is generated: it has a realistic shape, but it is not production data.
- The MySQL schema and queries are the common approach, not the only one. If you can make the
  relational side faster, a pull request with the change and its numbers is very welcome.

## License

MIT
