# Results: 100M-rough-mysql (10,000,000 nodes)

Median of the per-case medians (warm-up excluded), 3 cases per row, timeout 300 s.
All methods returned the same answers.

| Query | MySQL recursive CTE | MySQL + BFS in C# |
|---|---:|---:|
| neighbourhood, 1 hops | 0.7 ms | 0.7 ms |
| neighbourhood, 2 hops | 1.2 ms | 1.6 ms |
| neighbourhood, 3 hops | 63.2 ms | 51.0 ms |
| neighbourhood, 4 hops | 20.48 s | 12.44 s |
| shortest path (up to 6 hops) | timeout (3/3) | 0.7 ms |
