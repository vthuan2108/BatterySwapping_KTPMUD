SET XACT_ABORT ON;
BEGIN TRANSACTION;
-- Promote existing active third-or-later episodes without restarting isolation
-- or changing the battery's accumulated incident history.
UPDATE dbo.BssSlotCooldowns
 SET RequiresAdmin=1
 WHERE ClearedAt IS NULL AND IncidentCount>=3 AND RequiresAdmin=0;
COMMIT;
