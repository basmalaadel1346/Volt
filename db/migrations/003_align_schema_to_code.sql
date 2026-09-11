/* ============================================================================
   Migration 003 — Align the schema with the application code
   ----------------------------------------------------------------------------
   Written against the LIVE schema you scripted on 2026-09-11, which had
   diverged from migration 002. This script closes only the gaps the running
   code actually requires.

   Every statement is idempotent and additive. No column is dropped. No row is
   deleted. No existing value is overwritten.

   Two steps are GATED — they check for offending data and refuse to proceed
   rather than corrupting or discarding it. Those are §5 and §7.

   Companion code changes are mandatory — see docs/FULL_SYSTEM_AUDIT.md §14.
   Database-First: run this FIRST, then update the EF entities/configurations.
   ========================================================================== */

USE VoltDB;
GO

/* ============================================================================
   §1 — Questions.ImageUrl  and  QuestionOptions.ImageUrl
   Problem : EF selects ImageUrl on every question and option read; the columns
             do not exist → "Invalid column name 'ImageUrl'".
   Why     : optional question/option images are a required product feature.
   Impact  : none. Nullable additions.
   ========================================================================== */

IF COL_LENGTH('Assessment.Questions', 'ImageUrl') IS NULL
    ALTER TABLE Assessment.Questions ADD ImageUrl NVARCHAR(500) NULL;
GO

IF COL_LENGTH('Assessment.QuestionOptions', 'ImageUrl') IS NULL
    ALTER TABLE Assessment.QuestionOptions ADD ImageUrl NVARCHAR(500) NULL;
GO


/* ============================================================================
   §2 — QuestionOptions.OptionText must accept NULL
   Problem : NOT NULL in the database, nullable in EF. An image-only option
             cannot be inserted.
   Why     : an option may be text-only, image-only, or both — never neither.
             The CHECK added below is what makes the widening safe.
   Impact  : widening only. Every existing row has text, so the CHECK passes on
             all of them.
   ========================================================================== */

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('Assessment.QuestionOptions')
             AND name = 'OptionText' AND is_nullable = 0)
    ALTER TABLE Assessment.QuestionOptions ALTER COLUMN OptionText NVARCHAR(MAX) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_QuestionOptions_TextOrImage')
    ALTER TABLE Assessment.QuestionOptions
        ADD CONSTRAINT CK_QuestionOptions_TextOrImage
            CHECK (OptionText IS NOT NULL OR ImageUrl IS NOT NULL);
GO


/* ============================================================================
   §3 — Assessment.QuizAttemptEssayAnswers
   Problem : the table does not exist; essay submission throws SqlException 208.
   Why     : QuizAttemptMistakes.SelectedOptionId carries a composite FK to
             QuestionOptions and cannot hold free text. QuizAttemptQuestions is
             written once at attempt start and must stay immutable.
   Impact  : new table.
   ========================================================================== */

IF OBJECT_ID('Assessment.QuizAttemptEssayAnswers', 'U') IS NULL
BEGIN
    CREATE TABLE Assessment.QuizAttemptEssayAnswers
    (
        Id              BIGINT IDENTITY(1,1)    NOT NULL,
        QuizAttemptId   BIGINT                  NOT NULL,
        QuestionId      INT                     NOT NULL,
        AnswerText      NVARCHAR(MAX)           NOT NULL,

        Status          NVARCHAR(20)            NOT NULL
            CONSTRAINT DF_QuizAttemptEssayAnswers_Status DEFAULT ('Pending'),
        AwardedPoints   TINYINT                 NULL,
        Feedback        NVARCHAR(MAX)           NULL,
        GradedBy        NVARCHAR(20)            NULL,
        GradedAt        DATETIME2(3)            NULL,

        CreatedAt       DATETIME2(3)            NOT NULL
            CONSTRAINT DF_QuizAttemptEssayAnswers_CreatedAt DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_QuizAttemptEssayAnswers PRIMARY KEY CLUSTERED (Id),

        CONSTRAINT UQ_QuizAttemptEssayAnswers_AttemptId_QuestionId
            UNIQUE (QuizAttemptId, QuestionId),

        CONSTRAINT FK_QuizAttemptEssayAnswers_QuizAttempts FOREIGN KEY (QuizAttemptId)
            REFERENCES Assessment.QuizAttempts (Id) ON DELETE CASCADE,

        CONSTRAINT FK_QuizAttemptEssayAnswers_Questions FOREIGN KEY (QuestionId)
            REFERENCES Assessment.Questions (Id),

        CONSTRAINT FK_QuizAttemptEssayAnswers_QuizAttemptQuestions
            FOREIGN KEY (QuizAttemptId, QuestionId)
            REFERENCES Assessment.QuizAttemptQuestions (QuizAttemptId, QuestionId),

        CONSTRAINT CK_QuizAttemptEssayAnswers_Status
            CHECK (Status IN ('Pending', 'Graded', 'Skipped')),

        CONSTRAINT CK_QuizAttemptEssayAnswers_GradedBy
            CHECK (GradedBy IS NULL OR GradedBy IN ('Human', 'Ai')),

        CONSTRAINT CK_QuizAttemptEssayAnswers_GradedIsComplete
            CHECK (
                (Status =  'Graded' AND AwardedPoints IS NOT NULL AND GradedAt IS NOT NULL AND GradedBy IS NOT NULL)
             OR (Status <> 'Graded' AND AwardedPoints IS NULL     AND GradedAt IS NULL     AND GradedBy IS NULL)
            )
    );

    CREATE INDEX IX_QuizAttemptEssayAnswers_QuestionId
        ON Assessment.QuizAttemptEssayAnswers (QuestionId);

    CREATE INDEX IX_QuizAttemptEssayAnswers_Status
        ON Assessment.QuizAttemptEssayAnswers (Status)
        WHERE Status = 'Pending';
END
GO


/* ============================================================================
   §4 — The four missing translation tables
   ----------------------------------------------------------------------------
   DESIGN DECISION, stated explicitly:

   The live Assessment.QuestionTranslations uses a SCOPED EAV shape:
       (QuestionId, LanguageCode, Field, Value)   PK on all three
   The application code expected typed columns. The DATABASE WINS — it is the
   source of truth, it already holds data, and the EAV shape generalizes to
   several fields per entity with one table each.

   So the four missing sibling tables are created in the SAME shape, and the
   EF entities are rewritten to match (audit §14, C-1).

   The one real weakness of EAV is that a typo in Field silently produces no
   translation and a silent fallback. A CHECK constraint on the permitted field
   names removes that risk, so every table below carries one.

   Impact  : new tables. Existing QuestionTranslations rows are untouched.
   ========================================================================== */

-- Guard the EXISTING table the same way (it currently has no Field whitelist).
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_QuestionTranslations_Field')
    ALTER TABLE Assessment.QuestionTranslations
        ADD CONSTRAINT CK_QuestionTranslations_Field
            CHECK (Field IN (N'QuestionText'));
GO

IF OBJECT_ID('Assessment.QuestionOptionTranslations', 'U') IS NULL
BEGIN
    CREATE TABLE Assessment.QuestionOptionTranslations
    (
        QuestionOptionId INT           NOT NULL,
        LanguageCode     VARCHAR(5)    NOT NULL,
        Field            NVARCHAR(50)  NOT NULL,
        Value            NVARCHAR(MAX) NOT NULL,

        CONSTRAINT PK_QuestionOptionTranslations
            PRIMARY KEY CLUSTERED (QuestionOptionId, LanguageCode, Field),
        CONSTRAINT FK_QuestionOptionTranslations_QuestionOptions
            FOREIGN KEY (QuestionOptionId)
            REFERENCES Assessment.QuestionOptions (Id) ON DELETE CASCADE,
        CONSTRAINT FK_QuestionOptionTranslations_Languages
            FOREIGN KEY (LanguageCode) REFERENCES dbo.Languages (Code),
        CONSTRAINT CK_QuestionOptionTranslations_Field
            CHECK (Field IN (N'OptionText'))
    );
END
GO

IF OBJECT_ID('Assessment.QuizTranslations', 'U') IS NULL
BEGIN
    CREATE TABLE Assessment.QuizTranslations
    (
        QuizId       INT           NOT NULL,
        LanguageCode VARCHAR(5)    NOT NULL,
        Field        NVARCHAR(50)  NOT NULL,
        Value        NVARCHAR(MAX) NOT NULL,

        CONSTRAINT PK_QuizTranslations
            PRIMARY KEY CLUSTERED (QuizId, LanguageCode, Field),
        CONSTRAINT FK_QuizTranslations_Quizzes FOREIGN KEY (QuizId)
            REFERENCES Assessment.Quizzes (Id) ON DELETE CASCADE,
        CONSTRAINT FK_QuizTranslations_Languages
            FOREIGN KEY (LanguageCode) REFERENCES dbo.Languages (Code),
        CONSTRAINT CK_QuizTranslations_Field
            CHECK (Field IN (N'Title', N'Description'))
    );
END
GO

IF OBJECT_ID('Assessment.TopicTranslations', 'U') IS NULL
BEGIN
    CREATE TABLE Assessment.TopicTranslations
    (
        TopicId      INT           NOT NULL,
        LanguageCode VARCHAR(5)    NOT NULL,
        Field        NVARCHAR(50)  NOT NULL,
        Value        NVARCHAR(MAX) NOT NULL,

        CONSTRAINT PK_TopicTranslations
            PRIMARY KEY CLUSTERED (TopicId, LanguageCode, Field),
        CONSTRAINT FK_TopicTranslations_Topics FOREIGN KEY (TopicId)
            REFERENCES Assessment.Topics (Id) ON DELETE CASCADE,
        CONSTRAINT FK_TopicTranslations_Languages
            FOREIGN KEY (LanguageCode) REFERENCES dbo.Languages (Code),
        CONSTRAINT CK_TopicTranslations_Field
            CHECK (Field IN (N'Name', N'Description'))
    );
END
GO

IF OBJECT_ID('Assessment.CategoryTranslations', 'U') IS NULL
BEGIN
    CREATE TABLE Assessment.CategoryTranslations
    (
        CategoryId   TINYINT       NOT NULL,
        LanguageCode VARCHAR(5)    NOT NULL,
        Field        NVARCHAR(50)  NOT NULL,
        Value        NVARCHAR(MAX) NOT NULL,

        CONSTRAINT PK_CategoryTranslations
            PRIMARY KEY CLUSTERED (CategoryId, LanguageCode, Field),
        CONSTRAINT FK_CategoryTranslations_Categories FOREIGN KEY (CategoryId)
            REFERENCES Assessment.Categories (Id) ON DELETE CASCADE,
        CONSTRAINT FK_CategoryTranslations_Languages
            FOREIGN KEY (LanguageCode) REFERENCES dbo.Languages (Code),
        CONSTRAINT CK_CategoryTranslations_Field
            CHECK (Field IN (N'Name'))
    );
END
GO

-- The Languages lookup must actually contain the codes the API accepts.
IF NOT EXISTS (SELECT 1 FROM dbo.Languages WHERE Code = 'en')
    INSERT INTO dbo.Languages (Code, Name) VALUES ('en', N'English');
IF NOT EXISTS (SELECT 1 FROM dbo.Languages WHERE Code = 'ar')
    INSERT INTO dbo.Languages (Code, Name) VALUES ('ar', N'Arabic');
GO


/* ============================================================================
   §5 — GATED: restore uniqueness on QuestionHints
   ----------------------------------------------------------------------------
   Problem : the live table has ONLY a primary key. The original
             UQ_QuestionHints_MistakeId_Sequence was dropped and the intended
             per-language replacement was never created. Nothing prevents two
             hints claiming sequence 1 for the same mistake, which makes
             "latest hint per question" non-deterministic.
   Why     : this is a data-integrity REGRESSION, not a missing feature.
   Impact  : ⚠ if duplicates already exist, the CREATE fails and this script
             stops. That is deliberate — resolve them by hand; there is no safe
             automatic answer to "which duplicate hint should survive".
   ========================================================================== */

-- Run this FIRST. It must return 0 rows.
SELECT QuizAttemptMistakeId, LanguageCode, HintSequence, COUNT(*) AS Duplicates
FROM Assessment.QuestionHints
GROUP BY QuizAttemptMistakeId, LanguageCode, HintSequence
HAVING COUNT(*) > 1;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UQ_QuestionHints_MistakeId_Language_Sequence')
    CREATE UNIQUE INDEX UQ_QuestionHints_MistakeId_Language_Sequence
        ON Assessment.QuestionHints (QuizAttemptMistakeId, LanguageCode, HintSequence);
GO

-- Validate the language code, as the other translation tables already do.
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_QuestionHints_Languages')
    ALTER TABLE Assessment.QuestionHints
        ADD CONSTRAINT FK_QuestionHints_Languages
            FOREIGN KEY (LanguageCode) REFERENCES dbo.Languages (Code);
GO


/* ============================================================================
   §6 — Confirm the "at most one correct option" guard actually exists
   ----------------------------------------------------------------------------
   The scripts supplied contained no CREATE INDEX statements, so I could not
   verify this filtered unique index. It is the ONLY thing enforcing "at most
   one correct option per question" at the database level, and the grading
   snapshot depends on it. Creating it is a no-op if it is already there.
   ⚠ If it fails, a question already has two correct options — fix that first.
   ========================================================================== */

SELECT QuestionId, COUNT(*) AS CorrectOptions
FROM Assessment.QuestionOptions
WHERE IsCorrect = 1
GROUP BY QuestionId
HAVING COUNT(*) > 1;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UQ_QuestionOptions_OneCorrectPerQuestion')
    CREATE UNIQUE INDEX UQ_QuestionOptions_OneCorrectPerQuestion
        ON Assessment.QuestionOptions (QuestionId)
        WHERE IsCorrect = 1;
GO


/* ============================================================================
   §7 — GATED: restore QuizAttemptMistakes.SelectedOptionId NOT NULL
   ----------------------------------------------------------------------------
   Problem : nullable in the database, non-nullable in EF. A NULL row would
             throw on materialization, and the composite FK to QuestionOptions
             is NOT enforced for NULL values — losing the guarantee that the
             selected option belongs to the mistake's question.
   Why     : no code path writes NULL. Tightening restores the FK guarantee and
             removes the type mismatch. (The alternative — making the entity
             int? — would ripple through grading for no benefit.)
   Impact  : ⚠ fails safely if any NULL exists. Check first.
   ========================================================================== */

SELECT Id, QuizAttemptId, QuestionId
FROM Assessment.QuizAttemptMistakes
WHERE SelectedOptionId IS NULL;
GO

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('Assessment.QuizAttemptMistakes')
             AND name = 'SelectedOptionId' AND is_nullable = 1)
   AND NOT EXISTS (SELECT 1 FROM Assessment.QuizAttemptMistakes WHERE SelectedOptionId IS NULL)
BEGIN
    ALTER TABLE Assessment.QuizAttemptMistakes
        ALTER COLUMN SelectedOptionId INT NOT NULL;
END
GO


/* ============================================================================
   §8 — Users.RefreshTokens.TokenHash index
   Problem : unindexed. Every login, refresh and logout table-scans a table
             that gains a row per authentication and retains 30 days.
   Why     : authentication latency degrades permanently with usage.
   Impact  : ⚠ UNIQUE fails if duplicate hashes exist (they should not — the
             token is 64 random bytes). Check first; fall back to a
             non-unique index if the check returns rows.
   ========================================================================== */

SELECT TokenHash, COUNT(*) AS Duplicates
FROM Users.RefreshTokens
GROUP BY TokenHash
HAVING COUNT(*) > 1;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_RefreshTokens_TokenHash')
    CREATE UNIQUE INDEX UQ_RefreshTokens_TokenHash
        ON Users.RefreshTokens (TokenHash);
GO


/* ============================================================================
   §9 — Final verification. Every query must return 0 rows.
   ========================================================================== */

-- Options with neither text nor image:
SELECT Id FROM Assessment.QuestionOptions
WHERE OptionText IS NULL AND ImageUrl IS NULL;

-- Auto-graded snapshot rows with no answer key:
SELECT Id FROM Assessment.QuizAttemptQuestions
WHERE CorrectOptionId IS NULL AND QuestionType <> 'Essay';

-- Translation rows pointing at an unknown language:
SELECT QuestionId, LanguageCode FROM Assessment.QuestionTranslations
WHERE LanguageCode NOT IN (SELECT Code FROM dbo.Languages);

-- Tables the application requires (expect exactly 6 rows named below):
SELECT name FROM sys.tables
WHERE SCHEMA_NAME(schema_id) = 'Assessment'
  AND name IN ('QuizAttemptEssayAnswers','QuestionTranslations',
               'QuestionOptionTranslations','QuizTranslations',
               'TopicTranslations','CategoryTranslations')
ORDER BY name;
GO
