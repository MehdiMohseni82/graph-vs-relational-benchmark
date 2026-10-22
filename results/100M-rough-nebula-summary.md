# Results: 100M-rough-nebula (10,000,000 nodes)

Median of the per-case medians (warm-up excluded), 3 cases per row, timeout 300 s.
All methods returned the same answers.

| Query | NebulaGraph nGQL |
|---|---:|
| neighbourhood, 1 hops | 1.4 ms |
| neighbourhood, 2 hops | 4.5 ms |
| neighbourhood, 3 hops | 65.7 ms |
| shortest path (up to 6 hops) | 1.7 ms |
