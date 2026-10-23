# Hub-heavy run: one extra size on the Mac Studio

This adds one graph shaped like a real identity graph: mean degree 50, plus 20 hubs with about
630,000 relationships each, and about 900 nodes with more than 1,000. It also adds a third
question, "the neighbourhood, but never through a hub", for every method.

Same machine, same `.env` and same rules as `RUN-FAIR-BENCHMARK.md`:
- Nothing else runs in Docker.
- Don't change the code.
- Any OOM, restart, `error` row or `WARNING` makes the run invalid.

Expected time: about 10 minutes to load, then 2-4 hours of benchmark (most of it the CTE timing out).

## 1. Check the code

```bash
cd ~/bench/graph-vs-relational-benchmark
git pull
grep -c "avoid-hubs" src/Bench/Program.cs   # must print 1 or more
cat .env                                    # the same .env as the fair run
docker ps --format '{{.Names}}'             # must print nothing
```

## 2. Run

Save this as `run-hubs.sh` in the repo folder:

```bash
#!/usr/bin/env bash
cd ~/bench/graph-vs-relational-benchmark
t=hubs
GEN_ARGS="--degree 50 --hubs 20 --hub-degree 1000000 --hub-threshold 1000" \
  scripts/run-tier.sh $t 1000000 --timeout 300 --avoid-hubs 1,2,3,4 \
  --methods cte,bfs,nebula,nebula-steps 2>&1 | tee "results/$t-console.log"
cp data/manifest.txt "results/$t-manifest.txt"

bad=0
{
  for c in mysql metad storaged graphd; do
    s=$(docker inspect -f '{{.State.OOMKilled}} {{.RestartCount}} {{.State.Status}}' graph-vs-relational-benchmark-$c-1)
    echo "$c: $s"; [ "$s" = "false 0 running" ] || bad=1
  done
  k=$(docker run --rm --privileged alpine dmesg | grep -ci "killed process"); echo "kernel OOM kills: $k"; [ "$k" = 0 ] || bad=1
  e=$(grep -c ",error," "results/$t.csv"); echo "engine errors: $e"; [ "$e" = 0 ] || bad=1
  grep -q "WARNING" "results/$t-summary.md" && { echo "summary has a WARNING"; bad=1; }
  grep -q "All methods returned the same answers" "results/$t-summary.md" || { echo "answers differ"; bad=1; }
  docker stats --no-stream --format '{{.Name}} {{.MemUsage}}' | grep graph-vs
  [ $bad = 0 ] && echo "== $t VALID" || echo "== $t INVALID"
} 2>&1 | tee "results/$t-checks.txt"
```

Restart Docker Desktop first, so the kernel OOM counter starts at 0. Then:

```bash
caffeinate -dims &          # keep the Mac awake
nohup bash run-hubs.sh > results/run-hubs.out 2>&1 &
tail -f results/run-hubs.out
```

If it ends `INVALID`, follow step 6 of `RUN-FAIR-BENCHMARK.md` and rerun `run-hubs.sh`.

## 3. Send back

```bash
zip -r ~/Desktop/benchmark-hubs.zip results/hubs* .env
```

Don't commit or push from the Mac Studio.
