# Results: hubs (1,000,000 nodes)

Median of the per-case medians (warm-up excluded), 10 cases per row, timeout 300 s.
All methods returned the same answers.

| Query | MySQL recursive CTE | MySQL + BFS in C# | NebulaGraph nGQL | NebulaGraph, steps from C# |
|---|---:|---:|---:|---:|
| neighbourhood avoiding hubs, 1 hops | 0.9 ms | 1.0 ms | skipped | 3.9 ms |
| neighbourhood avoiding hubs, 2 hops | 6.8 ms | 5.5 ms | skipped | 81.6 ms |
| neighbourhood avoiding hubs, 3 hops | 2.55 s | 479.6 ms | skipped | 8.37 s |
| neighbourhood avoiding hubs, 4 hops | 2.6 min | 22.91 s | skipped | 5.0 min, 9/10 timed out |
| neighbourhood, 1 hops | 0.6 ms | 0.6 ms | 1.7 ms | skipped |
| neighbourhood, 2 hops | 8.49 s | 1.07 s | 575.6 ms | skipped |
| neighbourhood, 3 hops | 3.6 min | 20.58 s | 5.95 s | skipped |
| neighbourhood, 4 hops | timeout (10/10) | 20.59 s | 11.27 s | skipped |
| shortest path (up to 6 hops) | timeout (10/10) | 0.5 ms | 1.1 ms | skipped |
