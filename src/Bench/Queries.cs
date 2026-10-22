// The queries under test. The article quotes these, so they stay in one place.

static class Queries
{
    // MySQL, the way it is usually written: a recursive CTE over the many-to-many table.
    // A relationship counts in both directions, so each step joins edges twice:
    // once on src (who X points to) and once on dst (who points to X).
    // UNION (not UNION ALL) drops a (node, depth) pair it has already produced.
    public const string MySqlNeighbourhood = """
        WITH RECURSIVE reach (id, depth) AS (
            SELECT @start, 0
            UNION
            SELECT e.dst, r.depth + 1 FROM reach r JOIN edges e ON e.src = r.id WHERE r.depth < @hops
            UNION
            SELECT e.src, r.depth + 1 FROM reach r JOIN edges e ON e.dst = r.id WHERE r.depth < @hops
        )
        SELECT COUNT(DISTINCT id) FROM reach WHERE depth > 0;
        """;

    // "Are A and B related, and how closely?" The CTE cannot stop when it reaches B:
    // it builds every node within @maxHops of A first, then looks for B.
    public const string MySqlShortestPath = """
        WITH RECURSIVE reach (id, depth) AS (
            SELECT @a, 0
            UNION
            SELECT e.dst, r.depth + 1 FROM reach r JOIN edges e ON e.src = r.id WHERE r.depth < @maxHops
            UNION
            SELECT e.src, r.depth + 1 FROM reach r JOIN edges e ON e.dst = r.id WHERE r.depth < @maxHops
        )
        SELECT MIN(depth) FROM reach WHERE id = @b;
        """;

    // One BFS step done by the application: all neighbours of a batch of nodes, both directions.
    public const string MySqlNeighboursOf = """
        SELECT dst FROM edges WHERE src IN ({0})
        UNION ALL
        SELECT src FROM edges WHERE dst IN ({0});
        """;

    // NebulaGraph: the same two questions in nGQL.
    public static string NebulaNeighbourhood(long start, int hops) =>
        $"GO 1 TO {hops} STEPS FROM {start} OVER related BIDIRECT YIELD DISTINCT id($$) AS v | YIELD COUNT(*) AS n;";

    public static string NebulaShortestPath(long a, long b, int maxHops) =>
        $"FIND SINGLE SHORTEST PATH FROM {a} TO {b} OVER related BIDIRECT UPTO {maxHops} STEPS YIELD path AS p | YIELD length($-.p) AS len;";
}
