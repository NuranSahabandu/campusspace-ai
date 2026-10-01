-- CampusSpace AI on Neon (Task 6.D3): databases, roles and grants. Idempotent: safe to run again.
-- Run it as the project's owner role (Neon's default, e.g. neondb_owner) on the DIRECT endpoint:
--     ./scripts/neon-db.sh roles
-- It contains NO password. Set each role's password afterwards (hidden prompt, hashed by psql before it is sent):
--     ./scripts/neon-db.sh password campusspace_app
--     ./scripts/neon-db.sh password campusspace_agent
--
-- Why SQL and not the Neon console: roles created in the console (or CLI/API) become members of neon_superuser, which
-- has CREATEDB, CREATEROLE, BYPASSRLS, pg_read_all_data and pg_write_all_data. Roles created with SQL get only what is
-- granted here (https://neon.com/docs/manage/roles).
--
--   campusspace        business database, owned by the owner role, which runs the EF migrations (DDL).
--   campusspace_app    the API: CONNECT on campusspace only; DML on its tables, USAGE/SELECT on its sequences; no DDL.
--   campusspace_agent  the agent service's checkpointer (D2): owns ONLY the database campusspace_agent (schema
--                      agent_checkpoints); it cannot connect to campusspace, so it can't read any business table.

\set ON_ERROR_STOP on
\set VERBOSITY terse

-- Roles. LOGIN without a password until neon-db.sh sets one (a role without a password cannot sign in). Every
-- attribute is off. (A re-run can't ALTER SUPERUSER/REPLICATION/BYPASSRLS: PostgreSQL 16 lets only a role that has an
-- attribute change it, even to turn it off. neon-check.sql reports them instead.)
SELECT 'CREATE ROLE campusspace_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS'
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'campusspace_app') \gexec
SELECT 'CREATE ROLE campusspace_agent LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS'
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'campusspace_agent') \gexec
ALTER ROLE campusspace_app LOGIN NOCREATEDB NOCREATEROLE;
ALTER ROLE campusspace_agent LOGIN NOCREATEDB NOCREATEROLE;

-- PostgreSQL 16: the role that created campusspace_agent holds ADMIN on it but may not act as it. SET (no INHERIT, so
-- the owner gains none of its rights implicitly) lets the owner make it the owner of its database and create its schema.
GRANT campusspace_agent TO CURRENT_USER WITH INHERIT FALSE, SET TRUE;

-- Databases.
SELECT 'CREATE DATABASE campusspace'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'campusspace') \gexec
SELECT 'CREATE DATABASE campusspace_agent OWNER campusspace_agent'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'campusspace_agent') \gexec

-- CONNECT (and TEMP) come from PUBLIC by default: take them away and grant CONNECT explicitly to who needs it. Only a
-- database's owner can revoke on it, so the checkpoint database's grants run as its owner, campusspace_agent.
REVOKE ALL ON DATABASE campusspace FROM PUBLIC;
GRANT CONNECT ON DATABASE campusspace TO campusspace_app;
SET ROLE campusspace_agent;
REVOKE ALL ON DATABASE campusspace_agent FROM PUBLIC;
-- SESSION_USER is the owner role running this file (it administers the database and re-runs this script).
GRANT CONNECT ON DATABASE campusspace_agent TO campusspace_agent, SESSION_USER;
RESET ROLE;
ALTER ROLE campusspace_agent IN DATABASE campusspace_agent SET search_path = agent_checkpoints;

-- Business database: DML only for the app role. Run again after new migrations is harmless; the default privileges
-- below already cover tables and sequences the owner creates later.
\connect campusspace
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO campusspace_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO campusspace_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO campusspace_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO campusspace_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO campusspace_app;

-- Checkpoint database: the schema belongs to the agent role (LangGraph's setup() creates its tables there).
\connect campusspace_agent
SET ROLE campusspace_agent;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
CREATE SCHEMA IF NOT EXISTS agent_checkpoints;
RESET ROLE;

-- neon-db.sh runs docs/deploy/neon-check.sql (the read-only self-report) right after this file.
