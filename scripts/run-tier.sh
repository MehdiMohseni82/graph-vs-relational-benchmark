#!/usr/bin/env bash
# One size, end to end, from empty databases:
#   scripts/run-tier.sh <tier name> <node count> [extra Bench arguments]
#   scripts/run-tier.sh 500M 50000000 --timeout 900
# rows = nodes + edges, and the generator gives each node 9 edges on average,
# so 50,000,000 nodes is about 500,000,000 rows.
set -euo pipefail
cd "$(dirname "$0")/.."
tier=$1; nodes=$2; shift 2
log() { echo "$(date '+%F %T') [$tier] $*" | tee -a "results/$tier-run.log"; }
mkdir -p results; : > "results/$tier-run.log"; : > results/load-mysql.txt; : > results/load-nebula.txt

log "generate $nodes nodes"
dotnet run -c Release --project src/Generator -- --nodes "$nodes" --degree 9 --out data | tail -1 | tee -a "results/$tier-run.log"

log "reset databases"
docker compose down -v --remove-orphans >/dev/null 2>&1
docker compose up -d mysql metad storaged graphd >/dev/null 2>&1
dotnet build -c Release src/Bench >/dev/null
bench() { dotnet src/Bench/bin/Release/net10.0/Bench.dll "$@"; }
until docker compose exec -T mysql mysqladmin -uroot -pbench ping >/dev/null 2>&1; do sleep 3; done
until bench nebula "SHOW HOSTS" >/dev/null 2>&1; do sleep 3; done

log "load MySQL"
scripts/load-mysql.sh >/dev/null
tail -1 results/load-mysql.txt | tee -a "results/$tier-run.log"

log "load NebulaGraph"
docker compose run --rm importer --config /config/importer.yaml 2>&1 | grep -o 'Records{[^}]*}' | tail -1 | tee -a "results/$tier-run.log"
bench nebula "SUBMIT JOB COMPACT" >/dev/null
until bench nebula "SHOW JOBS" | head -1 | grep -q FINISHED; do sleep 10; done
bench nebula "SUBMIT JOB STATS" >/dev/null; sleep 5
until bench nebula "SHOW JOBS" | head -1 | grep -q FINISHED; do sleep 5; done
bench nebula "SHOW STATS" | tee -a "results/$tier-run.log"

log "sizes on disk"
docker compose exec -T mysql du -sh /var/lib/mysql/graph | tee -a "results/$tier-run.log"
docker compose exec -T storaged du -sh /data/storage | tee -a "results/$tier-run.log"

log "benchmark"
bench run --tier "$tier" "$@" | tail -15 | tee -a "results/$tier-run.log"
log "done"
