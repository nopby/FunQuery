-- Idempotent development seed for hosts/FunQuery.PostgreSql.Server.
-- Run with: scripts/dev-postgres.ps1 seed  (Windows)
--           scripts/dev-postgres.sh seed    (Unix)
--
-- The host schema is: id integer, customer text, total numeric, ordered_on date.

BEGIN;

DROP TABLE IF EXISTS orders;

CREATE TABLE orders (
    id integer PRIMARY KEY,
    customer text NOT NULL,
    total numeric(12, 2) NOT NULL,
    ordered_on date NOT NULL
);

INSERT INTO orders (id, customer, total, ordered_on) VALUES
    (1, 'Ana',   120.50, DATE '2024-01-15'),
    (2, 'Budi',   89.90, DATE '2024-02-03'),
    (3, 'Citra', 250.00, DATE '2024-03-21'),
    (4, 'Dewi',   15.75, DATE '2024-04-02'),
    (5, 'Eko',   320.10, DATE '2024-05-18');

COMMIT;
