-- What the running apps need in the marketplace database, granted after every migration so new tables are covered.
-- The migration role owns the schema; the apps only read and write rows. They never own objects or change the
-- schema, so a compromised app can't alter tables or drop data wholesale.
GRANT CONNECT ON DATABASE marketplace TO :"api", :"worker";
GRANT USAGE ON SCHEMA public TO :"api", :"worker";
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO :"api", :"worker";
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO :"api", :"worker";
GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA public TO :"api", :"worker";

-- The Worker refreshes the seller revenue view (R-10) with REFRESH MATERIALIZED VIEW CONCURRENTLY. In PostgreSQL 17
-- the MAINTAIN privilege allows that without owning the view.
GRANT MAINTAIN ON mv_seller_monthly_revenue TO :"worker";

-- Objects a later migration creates get the same row privileges straight away.
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO :"api", :"worker";
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO :"api", :"worker";
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT EXECUTE ON FUNCTIONS TO :"api", :"worker";
