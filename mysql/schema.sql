-- The way most of us first model a graph in a relational database:
-- one table for the things, one many-to-many table for the relationships between them.

DROP TABLE IF EXISTS edges;
DROP TABLE IF EXISTS nodes;

CREATE TABLE nodes (
  id   BIGINT      NOT NULL PRIMARY KEY,
  name VARCHAR(32) NOT NULL
) ENGINE=InnoDB;

-- The clustered primary key answers "who does X point to?".
-- The reverse index (dst, src) is added after the bulk load and answers "who points to X?".
CREATE TABLE edges (
  src BIGINT NOT NULL,
  dst BIGINT NOT NULL,
  PRIMARY KEY (src, dst)
) ENGINE=InnoDB;
