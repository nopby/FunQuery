-- Named query catalog for FunQuery.
-- Run once against the application database:
--   psql "$FUNQUERY_POSTGRES" -f db/named_queries.sql

CREATE SCHEMA IF NOT EXISTS funquery;

CREATE TABLE IF NOT EXISTS funquery.named_queries (
    app_id          text        NOT NULL DEFAULT 'global',
    tenant_id       text        NOT NULL DEFAULT 'global',
    query_key       text        NOT NULL,
    query_text      text        NOT NULL,
    source_name     text        NULL,
    enabled         boolean     NOT NULL DEFAULT true,
    cache_mode      text        NOT NULL DEFAULT 'default'
                    CHECK (cache_mode IN ('none', 'default', 'custom')),
    l1_ttl_seconds  integer     NULL CHECK (l1_ttl_seconds IS NULL OR l1_ttl_seconds > 0),
    l2_ttl_seconds  integer     NULL CHECK (l2_ttl_seconds IS NULL OR l2_ttl_seconds > 0),
    version         integer     NOT NULL DEFAULT 1,
    parameters      jsonb       NULL,
    updated_at      timestamptz NOT NULL DEFAULT now(),
    updated_by      text        NULL,

    PRIMARY KEY (app_id, tenant_id, query_key)
);

CREATE INDEX IF NOT EXISTS ix_named_queries_enabled
    ON funquery.named_queries (app_id, tenant_id, enabled);

CREATE OR REPLACE FUNCTION funquery.notify_named_query_change()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    PERFORM pg_notify(
        'funquery_named_queries',
        json_build_object(
            'operation', TG_OP,
            'app_id', COALESCE(NEW.app_id, OLD.app_id),
            'tenant_id', COALESCE(NEW.tenant_id, OLD.tenant_id),
            'query_key', COALESCE(NEW.query_key, OLD.query_key),
            'version', COALESCE(NEW.version, OLD.version)
        )::text
    );

    RETURN COALESCE(NEW, OLD);
END;
$$;

DROP TRIGGER IF EXISTS named_queries_notify ON funquery.named_queries;

CREATE TRIGGER named_queries_notify
AFTER INSERT OR UPDATE OR DELETE ON funquery.named_queries
FOR EACH ROW EXECUTE FUNCTION funquery.notify_named_query_change();
