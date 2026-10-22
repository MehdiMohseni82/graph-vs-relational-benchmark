# Graph vs relational: MySQL and NebulaGraph on the same graph

A reproducible benchmark for one question: what happens when you store a graph as a
many-to-many table in a relational database and ask it graph questions, compared with a
graph database, from 1 million to 100 million rows.

It is the code behind the article
[Relationships in a Relational Database: MySQL vs NebulaGraph from 1 Million to 100 Million Rows](https://dotnet-talk.com/blog/)
on dotnet-talk.com.

## What it measures

Two questions, asked of the same data in three ways:

| Question | MySQL recursive CTE | MySQL + BFS in C# | NebulaGraph nGQL |
|---|---|---|---|
| How many nodes are within 1, 2, 3, 4 hops of this one? | `WITH RECURSIVE` | level-by-level BFS with a visited set | `GO 1 TO n STEPS ... BIDIRECT` |
| Are A and B related, and how many hops apart (up to 6)? | `WITH RECURSIVE` + `MIN(depth)` | bidirectional BFS | `FIND SINGLE SHORTEST PATH` |

A relationship counts in both directions. All queries are in
[`src/Bench/Queries.cs`](src/Bench/Queries.cs), and the BFS is in
[`src/Bench/Methods.cs`](src/Bench/Methods.cs).

Before any timing is trusted, the runner compares the answers of all methods for every case
(node counts and path lengths) and reports any mismatch in the summary.

## The data

[`src/Generator`](src/Generator/Program.cs) writes one deterministic graph as CSV, which both
databases load:

- every node gets a geometric number of relationships with mean 9;
- half of the targets are uniform, half come from a skewed distribution, so some nodes become
  hubs with very many relationships, as in most real data;
- rows = nodes + relationships, so 10,000,000 nodes is about 100,000,000 rows.

## Running it

Requirements: Docker, .NET 10 SDK, about 30 GB of free disk for 100M, and a Docker memory limit
large enough for the values in `.env`.

```bash
cp .env.example .env      # memory per engine, equal for both
scripts/run-tier.sh 1M   100000
scripts/run-tier.sh 10M  1000000
scripts/run-tier.sh 100M 10000000  --timeout 900
```

Each run starts from empty databases: it generates the data, loads MySQL (`LOAD DATA INFILE`,
reverse index built after the load), loads NebulaGraph (nebula-importer, then compaction), and
runs the benchmark. Results go to `results/<tier>.csv` (every single run) and
`results/<tier>-summary.md` (the table).

Useful options for `Bench run`: `--seeds` (cases per question, default 10), `--runs` (measured
runs after one warm-up, default 3), `--timeout` (seconds per query, default 900), `--hops`
(default `1,2,3,4`), `--max-path` (default 6), `--methods` (default `cte,bfs,nebula`).

## Setup used for the published results

- Apple M5 Max, 18 cores, 128 GB RAM, local NVMe disk, but Docker Desktop's VM had only 8 GB,
  shared with other containers. These are rough numbers on a small memory budget.
- MySQL 8.4 (InnoDB), NebulaGraph 3.8 (one metad, one storaged, one graphd, 36 partitions)
- 1M and 10M: `scripts/run-tier.sh` with `MYSQL_BUFFER_POOL=2G` and `NEBULA_BLOCK_CACHE_MB=2048`,
  both engines running together.
- 100M: [`scripts/run-100m-small-memory.sh`](scripts/run-100m-small-memory.sh), one engine at a
  time with 1 GB of cache each (MySQL in a 2 GB container, storaged in a 2.5 GB container),
  3 cases per question, 1 measured run, 5 minute timeout.
- The values in [`.env.example`](.env.example) (40 GB per engine) are for a fair run with
  enough memory, which has not been done yet. Results from such a run are very welcome.

## Results

See [`results/`](results/). The summary tables are copied into the article.

## Limits, honestly

- One machine, local disk, no other traffic. This shows how each approach scales with the size
  and depth of the question, not what a production cluster will do.
- NebulaGraph runs with one storage node here; it is built to spread partitions over many.
- The MySQL schema and queries are the common approach, not the only one. If you can make the
  relational side faster, a pull request with the change and its numbers is very welcome.

## License

MIT
