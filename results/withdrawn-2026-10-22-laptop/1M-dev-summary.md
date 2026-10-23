# Results: 1M-dev (100,000 nodes)

Median of the per-case medians (warm-up excluded), 4 cases per row, timeout 120 s.
All methods returned the same answers.

| Query | MySQL recursive CTE | MySQL + BFS in C# | NebulaGraph nGQL |
|---|---:|---:|---:|
| neighbourhood, 1 hops | 0.6 ms | 0.3 ms | 1.0 ms |
| neighbourhood, 2 hops | 0.9 ms | 0.9 ms | 1.6 ms |
| neighbourhood, 3 hops | 9.3 ms | 9.0 ms | 8.1 ms |
| neighbourhood, 4 hops | 194.2 ms | 147.8 ms | 70.1 ms |
| shortest path (up to 6 hops) | 6.21 s | 1.5 ms | 2.0 ms |
