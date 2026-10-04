-- Demo punters (ids match the identity seed), each funded with R1 000.00 once through a balanced top-up posting.
DECLARE @now datetimeoffset(3) = SYSUTCDATETIME();
DECLARE @punters TABLE (AccountId uniqueidentifier PRIMARY KEY);
INSERT INTO @punters VALUES
    ('10000000-0000-0000-0000-000000000001'),
    ('10000000-0000-0000-0000-000000000002'),
    ('10000000-0000-0000-0000-000000000003'),
    ('10000000-0000-0000-0000-000000000004'),
    ('10000000-0000-0000-0000-000000000005');
DELETE p FROM @punters p WHERE EXISTS (SELECT 1 FROM wallet.Accounts a WHERE a.AccountId = p.AccountId);

DECLARE @postings TABLE (PostingId uniqueidentifier PRIMARY KEY, AccountId uniqueidentifier NOT NULL);
INSERT INTO @postings (PostingId, AccountId) SELECT NEWID(), AccountId FROM @punters;

BEGIN TRANSACTION;

INSERT INTO wallet.Accounts (AccountId, Kind, Currency, Available, UserId, CreatedAt)
SELECT AccountId, 1, 'ZAR', 100000, AccountId, @now FROM @punters;

INSERT INTO wallet.Postings (PostingId, Kind, IdempotencyKey, AccountId, Amount, Reference, PostedAt)
SELECT PostingId, 1, CONCAT('seed_', AccountId), AccountId, 100000, 'demo seed', @now FROM @postings;

-- The punter's entry carries the balance after (D196); the funding account's is not a statement account.
INSERT INTO wallet.LedgerEntries (PostingId, AccountId, Bucket, Amount, Sequence, PostedAt, AvailableAfter, BonusAfter)
SELECT d.PostingId, '00000000-0000-0000-0000-00000000f001', 1, -100000, p.Sequence, p.PostedAt, NULL, NULL FROM @postings d JOIN wallet.Postings p ON p.PostingId = d.PostingId
UNION ALL
SELECT d.PostingId, d.AccountId, 1, 100000, p.Sequence, p.PostedAt, 100000, 0 FROM @postings d JOIN wallet.Postings p ON p.PostingId = d.PostingId;

COMMIT TRANSACTION;
