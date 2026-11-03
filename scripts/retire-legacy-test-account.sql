\set ON_ERROR_STOP on

BEGIN;

DO $$
DECLARE
    legacy_user_id uuid;
BEGIN
    SELECT "Id" INTO legacy_user_id
    FROM users
    WHERE "Username" = 'codex-debug-ane';

    IF legacy_user_id IS NULL THEN
        RAISE EXCEPTION 'Legacy test account codex-debug-ane was not found; no data was changed.';
    END IF;

    DELETE FROM password_reset_tokens WHERE "UserId" = legacy_user_id;
    DELETE FROM email_verification_tokens WHERE "UserId" = legacy_user_id;
    DELETE FROM web_push_subscriptions WHERE "UserId" = legacy_user_id;
    DELETE FROM notifications WHERE "UserId" = legacy_user_id;
    DELETE FROM user_manga_entries WHERE "UserId" = legacy_user_id;
    DELETE FROM reading_progress WHERE "UserId" = legacy_user_id;
    DELETE FROM follows WHERE "UserId" = legacy_user_id;
    DELETE FROM usage_events WHERE "UserId" = legacy_user_id;
    DELETE FROM usage_daily_summaries WHERE "UserId" = legacy_user_id;
    DELETE FROM admin_issue_reports WHERE "ReporterUserId" = legacy_user_id;
    DELETE FROM maintenance_jobs WHERE "RequestedByUserId" = legacy_user_id;

    UPDATE users
    SET "TemporaryAccountCreatedByUserId" = NULL
    WHERE "TemporaryAccountCreatedByUserId" = legacy_user_id;

    UPDATE manga_entries SET "CreatedByUserId" = NULL WHERE "CreatedByUserId" = legacy_user_id;
    UPDATE admin_issues SET "ResolvedByUserId" = NULL WHERE "ResolvedByUserId" = legacy_user_id;
    DELETE FROM users WHERE "Id" = legacy_user_id;
END $$;

COMMIT;
