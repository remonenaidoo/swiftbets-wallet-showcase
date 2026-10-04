-- Bonus wallet (D166): a promotions account per currency pays bonuses and free bet stakes; forfeits return to it.
INSERT INTO wallet.Accounts (AccountId, Kind, Currency, CreatedAt)
SELECT v.AccountId, 4, v.Currency, SYSUTCDATETIME()
FROM (VALUES ('00000000-0000-0000-0000-00000000c001', 'ZAR'),
             ('00000000-0000-0000-0000-00000000c840', 'USD')) v (AccountId, Currency)
WHERE NOT EXISTS (SELECT 1 FROM wallet.Accounts a WHERE a.AccountId = v.AccountId);

-- How a held stake was paid, so a release gives each part back where it came from.
ALTER TABLE wallet.Reservations ADD BonusAmount bigint NOT NULL CONSTRAINT DF_Reservations_BonusAmount DEFAULT (0),
                                    FreeBetId uniqueidentifier NULL;
GO

-- Bonus money from an offer and its wagering; one active grant per account owns the account's bonus balance.
CREATE TABLE wallet.BonusGrants
(
    GrantId           uniqueidentifier  NOT NULL CONSTRAINT PK_BonusGrants PRIMARY KEY,
    AccountId         uniqueidentifier  NOT NULL CONSTRAINT FK_BonusGrants_Accounts REFERENCES wallet.Accounts (AccountId),
    PromotionId       uniqueidentifier  NULL,
    Amount            bigint            NOT NULL CONSTRAINT CK_BonusGrants_Amount CHECK (Amount > 0),
    WageringTarget    bigint            NOT NULL,
    Wagered           bigint            NOT NULL,
    MinOddsHundredths int               NOT NULL,
    SportsPercent     tinyint           NOT NULL,
    CasinoPercent     tinyint           NOT NULL,
    ExpiresAt         datetimeoffset(3) NOT NULL,
    State             tinyint           NOT NULL,
    CreatedAt         datetimeoffset(3) NOT NULL,
    ClosedAt          datetimeoffset(3) NULL
);
CREATE UNIQUE INDEX UX_BonusGrants_ActivePerAccount ON wallet.BonusGrants (AccountId) WHERE State = 1;
CREATE INDEX IX_BonusGrants_Active ON wallet.BonusGrants (ExpiresAt) WHERE State = 1;

-- Free bet tokens: no money moves until one is staked.
CREATE TABLE wallet.FreeBets
(
    FreeBetId         uniqueidentifier  NOT NULL CONSTRAINT PK_FreeBets PRIMARY KEY,
    AccountId         uniqueidentifier  NOT NULL CONSTRAINT FK_FreeBets_Accounts REFERENCES wallet.Accounts (AccountId),
    PromotionId       uniqueidentifier  NULL,
    Amount            bigint            NOT NULL CONSTRAINT CK_FreeBets_Amount CHECK (Amount > 0),
    MinOddsHundredths int               NOT NULL,
    ExpiresAt         datetimeoffset(3) NOT NULL,
    State             tinyint           NOT NULL,
    CreatedAt         datetimeoffset(3) NOT NULL,
    UpdatedAt         datetimeoffset(3) NOT NULL
);
CREATE INDEX IX_FreeBets_Account ON wallet.FreeBets (AccountId, State);

-- Stakes placed while an offer was involved, keyed by the stake's idempotency key; credits are split by them.
CREATE TABLE wallet.BonusStakes
(
    StakeKey        nvarchar(200)     NOT NULL CONSTRAINT PK_BonusStakes PRIMARY KEY,
    AccountId       uniqueidentifier  NOT NULL,
    Reference       nvarchar(200)     NOT NULL,
    Product         tinyint           NOT NULL,
    OddsHundredths  int               NOT NULL,
    CashAmount      bigint            NOT NULL,
    BonusAmount     bigint            NOT NULL,
    GrantId         uniqueidentifier  NULL,
    FreeBetId       uniqueidentifier  NULL,
    FreeBetAmount   bigint            NOT NULL,
    FreeBetRetained bigint            NOT NULL,
    PostedAt        datetimeoffset(3) NOT NULL
);
CREATE INDEX IX_BonusStakes_Reference ON wallet.BonusStakes (AccountId, Reference);
CREATE INDEX IX_BonusStakes_Casino ON wallet.BonusStakes (AccountId, Product, PostedAt);

-- Offers built in the console.
CREATE TABLE wallet.Promotions
(
    PromotionId        uniqueidentifier  NOT NULL CONSTRAINT PK_Promotions PRIMARY KEY,
    Name               nvarchar(100)     NOT NULL,
    Kind               tinyint           NOT NULL,
    Currency           char(3)           NOT NULL,
    MatchPercent       int               NOT NULL,
    MaxAward           bigint            NOT NULL,
    MinDeposit         bigint            NOT NULL,
    WageringMultiplier int               NOT NULL,
    MinOddsHundredths  int               NOT NULL,
    SportsPercent      tinyint           NOT NULL,
    CasinoPercent      tinyint           NOT NULL,
    ValidDays          int               NOT NULL,
    OptInRequired      bit               NOT NULL,
    StartsAt           datetimeoffset(3) NOT NULL,
    EndsAt             datetimeoffset(3) NOT NULL,
    Terms              nvarchar(4000)    NOT NULL,
    IsActive           bit               NOT NULL,
    CreatedAt          datetimeoffset(3) NOT NULL,
    UpdatedAt          datetimeoffset(3) NOT NULL
);

CREATE TABLE wallet.PromotionOptIns
(
    PromotionId uniqueidentifier  NOT NULL CONSTRAINT FK_PromotionOptIns_Promotions REFERENCES wallet.Promotions (PromotionId),
    UserId      uniqueidentifier  NOT NULL,
    OptedInAt   datetimeoffset(3) NOT NULL,
    CONSTRAINT PK_PromotionOptIns PRIMARY KEY (PromotionId, UserId)
);

-- Every award decision; at most one award per customer per offer, and one decision per triggering event.
CREATE TABLE wallet.PromotionAwards
(
    AwardId     uniqueidentifier  NOT NULL CONSTRAINT PK_PromotionAwards PRIMARY KEY,
    PromotionId uniqueidentifier  NOT NULL CONSTRAINT FK_PromotionAwards_Promotions REFERENCES wallet.Promotions (PromotionId),
    UserId      uniqueidentifier  NOT NULL,
    TriggerKey  nvarchar(200)     NOT NULL,
    Outcome     tinyint           NOT NULL,
    Reason      nvarchar(200)     NULL,
    Amount      bigint            NOT NULL,
    AwardedAt   datetimeoffset(3) NOT NULL,
    CONSTRAINT UQ_PromotionAwards_Trigger UNIQUE (PromotionId, UserId, TriggerKey)
);
CREATE UNIQUE INDEX UX_PromotionAwards_OnePerCustomer ON wallet.PromotionAwards (PromotionId, UserId) WHERE Outcome = 1;

CREATE TABLE wallet.PromotionAudit
(
    AuditId     bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_PromotionAudit PRIMARY KEY,
    PromotionId uniqueidentifier  NOT NULL,
    Actor       nvarchar(200)     NOT NULL,
    Action      nvarchar(50)      NOT NULL,
    Detail      nvarchar(max)     NOT NULL,
    At          datetimeoffset(3) NOT NULL
);
CREATE INDEX IX_PromotionAudit_Promotion ON wallet.PromotionAudit (PromotionId, AuditId);

-- A customer who turned offers off sees none and is awarded none.
CREATE TABLE wallet.MarketingPreferences
(
    UserId    uniqueidentifier  NOT NULL CONSTRAINT PK_MarketingPreferences PRIMARY KEY,
    OptedOut  bit               NOT NULL,
    UpdatedAt datetimeoffset(3) NOT NULL
);
