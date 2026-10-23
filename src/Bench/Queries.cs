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

    // Question 3, the one identity graphs need: the neighbourhood, but never entering a hub
    // (a node listed in the hubs table). The filter sits inside the recursion, so a hub is
    // neither counted nor expanded. The start node itself is never counted here.
    public const string MySqlNeighbourhoodAvoidingHubs = """
        WITH RECURSIVE reach (id, depth) AS (
            SELECT @start, 0
            UNION
            SELECT e.dst, r.depth + 1 FROM reach r JOIN edges e ON e.src = r.id
              WHERE r.depth < @hops AND NOT EXISTS (SELECT 1 FROM hubs h WHERE h.id = e.dst)
            UNION
            SELECT e.src, r.depth + 1 FROM reach r JOIN edges e ON e.dst = r.id
              WHERE r.depth < @hops AND NOT EXISTS (SELECT 1 FROM hubs h WHERE h.id = e.src)
        )
        SELECT COUNT(DISTINCT id) FROM reach WHERE depth > 0 AND id <> @start;
        """;

    // One BFS step that skips hubs, so the application never receives their ids.
    public const string MySqlNeighboursOfAvoidingHubs = """
        SELECT e.dst FROM edges e WHERE e.src IN ({0}) AND NOT EXISTS (SELECT 1 FROM hubs h WHERE h.id = e.dst)
        UNION ALL
        SELECT e.src FROM edges e WHERE e.dst IN ({0}) AND NOT EXISTS (SELECT 1 FROM hubs h WHERE h.id = e.src);
        """;

    // NebulaGraph: the same questions in nGQL.
    public static string NebulaNeighbourhood(long start, int hops) =>
        $"GO 1 TO {hops} STEPS FROM {start} OVER related BIDIRECT YIELD DISTINCT id($$) AS v | YIELD COUNT(*) AS n;";

    // Question 3 has no single nGQL statement that is both right and usable:
    // - GO 1 TO n STEPS ... WHERE NOT "hub" IN tags($$) still walks through hubs. On the 20k-node
    //   test graph it returned 5,821 nodes at 2 hops where the CTE and the BFS returned 463.
    // - MATCH p = (s)-[:related*1..n]-(v) WHERE none(x IN nodes(p) WHERE "hub" IN tags(x)) is right,
    //   but it enumerates paths: 1.4 s at 3 hops on 20k nodes, and at 4 hops graphd ran out of
    //   its 1 GB. So NebulaGraph answers question 3 one GO step per level, driven from C#.

    // One level of the same search, driven from C# (what an application does when the
    // database cannot filter during the search).
    public static string NebulaNeighboursOfAvoidingHubs(IEnumerable<long> ids) =>
        $"GO FROM {string.Join(',', ids)} OVER related BIDIRECT WHERE NOT \"hub\" IN tags($$) YIELD DISTINCT id($$) AS v;";

    // The same level without any filter: the caller drops hubs from a set it loaded once.
    public static string NebulaNeighboursOf(IEnumerable<long> ids) =>
        $"GO FROM {string.Join(',', ids)} OVER related BIDIRECT YIELD DISTINCT id($$) AS v;";

    public static string NebulaShortestPath(long a, long b, int maxHops) =>
        $"FIND SINGLE SHORTEST PATH FROM {a} TO {b} OVER related BIDIRECT UPTO {maxHops} STEPS YIELD path AS p | YIELD length($-.p) AS len;";
}
