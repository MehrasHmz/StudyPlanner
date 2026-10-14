-- DBA-reviewed template. Supply existing role names and expected database through psql -v.
\set ON_ERROR_STOP on
BEGIN;
SELECT 1 / (current_database() = :'expected_database')::integer AS verified_database;
GRANT USAGE ON SCHEMA public TO :"runtime_role", :"operator_role";
GRANT SELECT ON "Users", "Categories", "StudyItems", "StudySessions", "StudySessionItems", "ProgressRecords", "__EFMigrationsHistory" TO :"runtime_role";
GRANT INSERT ("Id", "FullName", "Email", "PasswordHash", "IsActive", "CreatedAt", "UpdatedAt") ON "Users" TO :"runtime_role";
GRANT UPDATE ("FullName", "Email", "PasswordHash", "CreatedAt", "UpdatedAt") ON "Users" TO :"runtime_role";
GRANT INSERT, UPDATE ON "Categories", "StudyItems", "StudySessions", "StudySessionItems", "ProgressRecords" TO :"runtime_role";
GRANT SELECT ON "Users", "RoleChangeAudits" TO :"operator_role";
GRANT UPDATE ("Role", "IsActive") ON "Users" TO :"operator_role";
GRANT INSERT ON "RoleChangeAudits" TO :"operator_role";
COMMIT;
-- No owner, CREATE, DELETE, role-column INSERT, runtime audit write or schema-migration grants.
-- Review inherited/PUBLIC privileges separately; this script grants, it does not revoke unknown existing rights.
