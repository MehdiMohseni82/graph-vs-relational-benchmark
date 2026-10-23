# Results: 500M (50,000,000 nodes)

Median of the per-case medians (warm-up excluded), 10 cases per row, timeout 900 s.
All methods returned the same answers.

| Query | MySQL recursive CTE | MySQL + BFS in C# | NebulaGraph nGQL |
|---|---:|---:|---:|
| neighbourhood, 1 hops | 0.7 ms | 0.3 ms | 1.3 ms |
| neighbourhood, 2 hops | 0.8 ms | 0.8 ms | 1.9 ms |
| neighbourhood, 3 hops | 10.7 ms | 8.2 ms | 9.1 ms |
| neighbourhood, 4 hops | 3.18 s | 579.6 ms | 529.1 ms |
| shortest path (up to 6 hops) | timeout (10/10) | 2.7 ms | 23.6 ms |
