# Results: 1M (100,000 nodes)

Median of the per-case medians (warm-up excluded), 10 cases per row, timeout 900 s.
All methods returned the same answers.

| Query | MySQL recursive CTE | MySQL + BFS in C# | NebulaGraph nGQL |
|---|---:|---:|---:|
| neighbourhood, 1 hops | 0.7 ms | 0.2 ms | 1.0 ms |
| neighbourhood, 2 hops | 0.7 ms | 0.9 ms | 1.9 ms |
| neighbourhood, 3 hops | 11.5 ms | 8.7 ms | 8.9 ms |
| neighbourhood, 4 hops | 180.0 ms | 177.0 ms | 72.4 ms |
| shortest path (up to 6 hops) | 5.68 s | 1.1 ms | 2.1 ms |
