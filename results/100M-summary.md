# Results: 100M (10,000,000 nodes)

Median of the per-case medians (warm-up excluded), 10 cases per row, timeout 900 s.
All methods returned the same answers.

| Query | MySQL recursive CTE | MySQL + BFS in C# | NebulaGraph nGQL |
|---|---:|---:|---:|
| neighbourhood, 1 hops | 0.7 ms | 0.3 ms | 1.1 ms |
| neighbourhood, 2 hops | 1.0 ms | 1.7 ms | 2.3 ms |
| neighbourhood, 3 hops | 129.3 ms | 36.0 ms | 44.3 ms |
| neighbourhood, 4 hops | 10.61 s | 1.70 s | 1.13 s |
| shortest path (up to 6 hops) | 9.7 min, 2/10 timed out | 1.5 ms | 8.6 ms |
