#!/usr/bin/env bash
# The 100M results in the article: a Docker VM with only 8 GB, so the engines run one at a time
# with 1 GB of cache each, 3 cases per question, 1 measured run, 5 minute timeout.
# In this setup the 4-hop NebulaGraph query runs storaged out of memory (it restarts) and the run
# stops, so after storaged is back the NebulaGraph run is repeated without 4 hops.
set -euo pipefail
cd "$(dirname "$0")/.."
L=results/100M-rough-run.log; : > $L
log() { echo "$(date '+%F %T') $*" | tee -a $L; }
bench() { dotnet src/Bench/bin/Release/net10.0/Bench.dll "$@"; }
ARGS="--seeds 3 --runs 1 --timeout 300"

log "generate 10M nodes"
dotnet run -c Release --project src/Generator -- --nodes 10000000 --degree 9 --out data | tail -1 | tee -a $L
dotnet build -c Release src/Bench >/dev/null

log "phase 1: MySQL only (2 GB container, 1 GB buffer pool)"
docker compose down -v --remove-orphans >/dev/null 2>&1
export MYSQL_MEM_LIMIT=2g MYSQL_BUFFER_POOL=1G
docker compose up -d mysql >/dev/null 2>&1
until docker compose exec -T mysql mysqladmin -uroot -pbench ping >/dev/null 2>&1; do sleep 3; done
scripts/load-mysql.sh >/dev/null; tail -1 results/load-mysql.txt | tee -a $L
docker compose exec -T mysql du -sh /var/lib/mysql/graph | tee -a $L
log "bench cte,bfs"
bench run --tier 100M-rough-mysql --methods cte,bfs $ARGS 2>&1 | tail -15 | tee -a $L || log "mysql bench exited non-zero"
docker inspect -f 'mysql OOMKilled={{.State.OOMKilled}} status={{.State.Status}}' graph-vs-relational-benchmark-mysql-1 | tee -a $L

log "phase 2: NebulaGraph (storaged 2.5 GB, 1 GB block cache); MySQL shrunk to 256 MB pool for the node count only"
docker compose stop mysql >/dev/null 2>&1
export MYSQL_MEM_LIMIT=768m MYSQL_BUFFER_POOL=256M NEBULA_STORAGE_MEM_LIMIT=2560m NEBULA_BLOCK_CACHE_MB=1024 NEBULA_GRAPH_MEM_LIMIT=1g
docker compose up -d metad storaged graphd >/dev/null 2>&1
until bench nebula "SHOW HOSTS" >/dev/null 2>&1; do sleep 3; done
docker compose run --rm importer --config /config/importer.yaml 2>&1 | grep -o 'Records{[^}]*}' | tail -1 | tee -a $L
bench nebula "SUBMIT JOB COMPACT" >/dev/null
until bench nebula "SHOW JOBS" | head -1 | grep -q FINISHED; do sleep 10; done
docker compose exec -T storaged du -sh /data/storage | tee -a $L
docker compose up -d mysql >/dev/null 2>&1
until docker compose exec -T mysql mysqladmin -uroot -pbench ping >/dev/null 2>&1; do sleep 3; done
log "bench nebula"
bench run --tier 100M-rough-nebula --methods nebula $ARGS 2>&1 | tail -15 | tee -a $L || log "nebula bench exited non-zero"
log "wait for storaged, then NebulaGraph again without 4 hops"
until bench nebula 'GO 1 STEPS FROM 1 OVER related YIELD id($$)' >/dev/null 2>&1; do sleep 5; done
bench run --tier 100M-rough-nebula --methods nebula $ARGS --hops 1,2,3 2>&1 | tail -15 | tee -a $L
log "done"
