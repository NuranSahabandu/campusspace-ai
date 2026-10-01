-- CampusSpace AI on Neon: read-only self-report (./scripts/neon-db.sh check, and after `roles` and `migrate`).
-- Expected: neither role has an attribute or a role membership, each can CONNECT only to its own database, neither can
-- CREATE in public, and the app role can insert into every business table.

\set ON_ERROR_STOP on
\connect campusspace
\pset footer off
SELECT r.rolname AS role,
       r.rolsuper OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication OR r.rolbypassrls AS has_any_attribute,
       coalesce((SELECT string_agg(g.rolname, ',') FROM pg_auth_members m JOIN pg_roles g ON g.oid = m.roleid
                 WHERE m.member = r.oid), '-') AS member_of,
       has_database_privilege(r.rolname, 'campusspace', 'CONNECT') AS connect_campusspace,
       has_database_privilege(r.rolname, 'campusspace_agent', 'CONNECT') AS connect_agent_db,
       has_schema_privilege(r.rolname, 'public', 'CREATE') AS create_in_public
FROM pg_roles r
WHERE r.rolname IN ('campusspace_app', 'campusspace_agent')
ORDER BY r.rolname;
SELECT count(*) AS business_tables,
       count(*) FILTER (WHERE has_table_privilege('campusspace_app', c.oid, 'INSERT')) AS app_can_insert
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'public' AND c.relkind = 'r';
SELECT to_regclass('public."__EFMigrationsHistory"') IS NOT NULL AS has_history \gset
\if :has_history
SELECT count(*) AS migrations_applied, max("MigrationId") AS latest_migration FROM "__EFMigrationsHistory";
\else
\echo 'migrations applied: none yet (run: ./scripts/neon-db.sh migrate)'
\endif
