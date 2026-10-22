#!/usr/bin/env bash
# Loads data/nodes.csv and data/edges/*.csv into MySQL, one transaction per edge file,
# then builds the reverse index. Timings go to results/load-mysql.txt.
set -euo pipefail
cd "$(dirname "$0")/.."
mysql() { docker compose exec -T mysql mysql -uroot -pbench --local-infile=0 graph "$@" 2>/dev/null; }
log() { echo "$(date '+%F %T') $*" | tee -a results/load-mysql.txt; }

log "schema"
mysql < mysql/schema.sql
log "nodes"
mysql -e "LOAD DATA INFILE '/bench-data/nodes.csv' INTO TABLE nodes FIELDS TERMINATED BY ',' (id, name);"
for f in data/edges/part-*.csv; do
  log "edges $(basename "$f")"
  mysql -e "SET unique_checks=0; LOAD DATA INFILE '/bench-data/edges/$(basename "$f")' INTO TABLE edges FIELDS TERMINATED BY ',' (src, dst);"
done
log "reverse index (dst, src)"
mysql -e "ALTER TABLE edges ADD INDEX ix_edges_dst_src (dst, src);"
mysql -e "ANALYZE TABLE nodes, edges;" > /dev/null
log "done: $(mysql -N -e 'SELECT (SELECT COUNT(*) FROM nodes) + (SELECT COUNT(*) FROM edges);') rows"
