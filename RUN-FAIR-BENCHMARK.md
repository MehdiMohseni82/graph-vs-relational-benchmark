# Fair benchmark run: MySQL vs NebulaGraph, 1M to 500M rows

This file is a complete runbook. Follow it top to bottom on the Mac Studio. It needs no other
context.

## Why this run exists

The first results were measured inside a Docker VM with 8 GB of memory. That memory was shared
with about 30 other containers, and only 1.7 GB was free. During the 4-hop query, Linux ran out
of memory for the whole VM and killed NebulaGraph's storage process. The kernel log said
`global_oom ... Killed process (nebula-storaged)`. That was the machine's failure, not
NebulaGraph's, so those results were withdrawn.

This run has one goal: **the same test, with enough memory, the same budget for both databases,
and nothing else running.** Every result must be checked with the steps in "Validity checks"
before anyone uses it.

## Rules for whoever runs this

1. Do not change the queries, the schema or the benchmark code. If something looks wrong,
   stop and report it.
2. Both databases get the same memory budget, from `.env`. Never tune one engine alone.
3. A crash, an OOM kill, an `ERROR` line or a `WARNING` in a summary makes that size invalid.
   Find the cause in the logs, fix the environment, and rerun that size from scratch. Never
   report a number from an invalid run.
4. Nothing else runs in Docker during the benchmark.

## 1. Requirements

| Item | Needed |
|---|---|
| Docker Desktop | recent version, Apple silicon |
| .NET SDK | 10.0 (`dotnet --version` shows 10.x) |
| git | any |
| Free disk | at least **250 GB** (500M rows: ~10 GB CSV, ~30 GB MySQL, ~60-80 GB NebulaGraph, plus compaction headroom) |
| RAM | 128 GB or more is ideal; see the table in step 2 |

Install .NET 10 if it's missing: `brew install --cask dotnet-sdk`, or use the installer from
dot.net.

## 2. Docker Desktop settings

Open Docker Desktop, go to **Settings > Resources**, and set:

- **CPUs:** all of them.
- **Memory:** the value from the table below.
- **Swap:** 4 GB.
- **Disk usage limit:** at least 300 GB.

Then click **Apply & Restart**.

| Mac Studio RAM | Docker memory | `.env` to use |
|---|---|---|
| 192 GB or more | 160 GB | `.env.example` as it is (40 GB per engine) |
| 128 GB | 100 GB | `.env.example` as it is (40 GB per engine) |
| 96 GB | 80 GB | the "96 GB" block below |
| 64 GB | 52 GB | the "64 GB" block below (500M will be slow but is still valid) |

```bash
# 96 GB Mac Studio: .env
MYSQL_MEM_LIMIT=30g
MYSQL_BUFFER_POOL=24G
NEBULA_STORAGE_MEM_LIMIT=24g
NEBULA_BLOCK_CACHE_MB=16384
NEBULA_GRAPH_MEM_LIMIT=6g

# 64 GB Mac Studio: .env
MYSQL_MEM_LIMIT=20g
MYSQL_BUFFER_POOL=14G
NEBULA_STORAGE_MEM_LIMIT=16g
NEBULA_BLOCK_CACHE_MB=10240
NEBULA_GRAPH_MEM_LIMIT=4g
```

Each pair is equal: the MySQL container limit equals NebulaGraph's storage plus graph limits,
give or take a few GB of process overhead.

## 3. Get the code

```bash
mkdir -p ~/bench && cd ~/bench
git clone https://github.com/MehdiMohseni82/graph-vs-relational-benchmark.git
cd graph-vs-relational-benchmark
git log --oneline -1          # must be 01047dd or newer
cp .env.example .env          # or paste the block for your RAM from step 2
```

## 4. Check the machine before starting

```bash
# Nothing else may run in Docker:
docker ps --format '{{.Names}}'          # must print nothing
# If it prints anything, stop it: docker stop $(docker ps -q)

# The VM really has the memory you set:
docker run --rm alpine free -g           # "total" ~ the Docker memory from step 2, "available" close to it

# Record the hardware for the article:
mkdir -p results
{ system_profiler SPHardwareDataType | grep -E "Model Name|Chip|Total Number of Cores|Memory"
  docker info --format 'Docker: {{.ServerVersion}}, CPUs {{.NCPU}}, memory {{.MemTotal}}'
  dotnet --version; git log --oneline -1; cat .env; df -h ~ | tail -1
} | tee results/machine.txt

# Baseline of kernel OOM kills (should be 0 after the restart):
docker run --rm --privileged alpine dmesg | grep -ci "killed process"
```

Keep the Mac awake for the whole run (this blocks while the run is going, so use a second
terminal):

```bash
caffeinate -dims
```

## 5. Run

Run the four sizes in order. Each size starts from empty databases: it generates the data, loads
both engines, compacts NebulaGraph, and benchmarks. After each size, the validity checks run and
the script stops if any of them fails.

Save this as `run-all.sh` in the repo folder and start it with `bash run-all.sh`. It must run
in bash, not zsh.

```bash
#!/usr/bin/env bash
cd ~/bench/graph-vs-relational-benchmark

check() {   # validity checks for one size; non-zero means invalid
  t=$1; bad=0
  for c in mysql metad storaged graphd; do
    s=$(docker inspect -f '{{.State.OOMKilled}} {{.RestartCount}} {{.State.Status}}' graph-vs-relational-benchmark-$c-1)
    echo "$c: $s"; [ "$s" = "false 0 running" ] || bad=1
  done
  k=$(docker run --rm --privileged alpine dmesg | grep -ci "killed process"); echo "kernel OOM kills: $k"; [ "$k" = 0 ] || bad=1
  e=$(grep -c ",error," "results/$t.csv"); echo "engine errors: $e"; [ "$e" = 0 ] || bad=1
  grep -q "WARNING" "results/$t-summary.md" && { echo "summary has a WARNING"; bad=1; }
  grep -q "All methods returned the same answers" "results/$t-summary.md" || { echo "answers differ"; bad=1; }
  docker stats --no-stream --format '{{.Name}} {{.MemUsage}}' | grep graph-vs
  return $bad
}

for spec in "1M 100000" "10M 1000000" "100M 10000000" "500M 50000000"; do
  read -r tier nodes <<< "$spec"
  scripts/run-tier.sh "$tier" "$nodes" --timeout 900 2>&1 | tee "results/$tier-console.log"
  if check "$tier" > "results/$tier-checks.txt" 2>&1; then
    echo "== $tier VALID" | tee -a "results/$tier-checks.txt"
  else
    echo "== $tier INVALID: stop, read the logs, fix the environment, rerun this size" | tee -a "results/$tier-checks.txt"
    cat "results/$tier-checks.txt"
    break
  fi
done
```

Start it like this, so it keeps running if the terminal closes:

```bash
nohup bash run-all.sh > results/run-all.out 2>&1 &
tail -f results/run-all.out
```

The benchmark defaults are 10 cases per question, 1 warm-up plus 3 measured runs, depths 1 to 4,
shortest path up to 6 hops, and a 15-minute timeout. A query that times out is recorded once as
a timeout, and deeper depths from the same start node are skipped.

### Expected duration (rough)

| Size | Load (MySQL + NebulaGraph + compaction) | Benchmark | Total |
|---|---|---|---|
| 1M | 1 min | 2-3 min | ~5 min |
| 10M | 3 min | 10-30 min | ~30 min |
| 100M | 15-20 min | 2-4 h (the CTE shortest path may time out at 15 min per case) | ~3-4 h |
| 500M | 1-2 h | 3-8 h | ~5-10 h |

The long part is MySQL's recursive CTE timing out. That is expected, and it is part of the
result.

## 6. If a size is invalid

1. Read `results/<size>-checks.txt` to see which check failed.
2. Find the cause:
   ```bash
   docker run --rm --privileged alpine dmesg | grep -iE "oom|killed process" | tail
   docker compose logs --tail 200 storaged graphd mysql
   ```
3. A **kernel OOM** (`global_oom`) means the VM is too small for the `.env` values. Lower both
   engines by the same amount, or raise Docker's memory.
   A **container OOM** (`OOMKilled true`) means one engine's limit is too small for that size.
   Raise both engines' limits by the same amount.
4. Rerun only that size, for example `scripts/run-tier.sh 500M 50000000 --timeout 900`, then
   run the check again.
5. Record what you changed, and why, in `results/notes.txt`. The article reports the setting
   that was actually used.

## 7. What to send back

```bash
cd ~/bench/graph-vs-relational-benchmark
zip -r ~/Desktop/benchmark-results.zip results .env
```

The zip must contain, for every size:

- `<size>-summary.md`: the table.
- `<size>.csv`: every single run.
- `<size>-run.log` and `<size>-console.log`: what happened, with load times and data sizes.
- `<size>-checks.txt`: must end with `VALID`.

It must also contain `machine.txt`, `notes.txt` (if any), and the `.env` that was used.

Don't commit or push anything from the Mac Studio. The results are reviewed first, and then go
into the repo and the article together.
