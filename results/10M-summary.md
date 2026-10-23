# Results: 10M (1,000,000 nodes)

Median of the per-case medians (warm-up excluded), 10 cases per row, timeout 900 s.
All methods returned the same answers.

| Query | MySQL recursive CTE | MySQL + BFS in C# | NebulaGraph nGQL |
|---|---:|---:|---:|
| neighbourhood, 1 hops | 0.7 ms | 0.3 ms | 1.1 ms |
| neighbourhood, 2 hops | 0.7 ms | 0.8 ms | 1.7 ms |
| neighbourhood, 3 hops | 22.4 ms | 13.4 ms | 16.8 ms |
| neighbourhood, 4 hops | 2.65 s | 508.5 ms | 288.1 ms |
| shortest path (up to 6 hops) | 1.2 min | 1.6 ms | 18.7 ms |
