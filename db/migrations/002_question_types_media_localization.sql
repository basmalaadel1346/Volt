/* ============================================================================
   Migration 002 — Question types, media, and localization
   ----------------------------------------------------------------------------
   ⚠ DEPENDS ON MIGRATION 001
   (2026-09-10_assessment_attempt_question_snapshot.sql).
   001 adds QuizAttemptQuestions.TopicId / Difficulty / CorrectOptionId.
   Step 3 below ALTERs CorrectOptionId, so 001 MUST be applied first.
   Verify with:
       SELECT 1 FROM sys.columns
       WHERE object_id = OBJECT_ID('Assessment.QuizAttemptQuestions')
         AND name = 'CorrectOptionId';
   If that returns no row, run 001 first and re-run this script.

   ⚠ EXISTING DATA
   Every step is additive or widening. No column is dropped, no row is deleted,
   and no existing value is overwritten. Existing MultipleChoice questions and
   attempts keep working unchanged. Steps 6 and 7 INSERT backfill rows only
   where none exist.

   Run the whole script inside one transaction per section, in order.
   ========================================================================== */

USE VoltDB;
GO

/* ============================================================================
   SECTION 1 — Question types
   Current : Assessment.Questions has no type column; every question is
             implicitly multiple-choice.
   Change  : add a CHECK-constrained QuestionType, defaulting to the current
             implicit behaviour so existing rows are correct by definition.
   Why     : True/False and Essay must be distinguishable at delivery and at
             grading time. Backward compatible: DEFAULT covers all existing rows.
   ========================================================================== */

ALTER TABLE Assessment.Questions
    ADD QuestionType NVARCHAR(20) NOT NULL
        CONSTRAINT DF_Questions_QuestionType DEFAULT ('MultipleChoice');
GO

ALTER TABLE Assessment.Questions
    ADD CONSTRAINT CK_Questions_QuestionType
        CHECK (QuestionType IN ('MultipleChoice', 'TrueFalse', 'Essay'));
GO

CREATE INDEX IX_Questions_QuestionType ON Assessment.Questions (QuestionType);
GO


/* ============================================================================
   SECTION 2 — Question and option images
   Current : no image column anywhere in Assessment.
   Change  : nullable ImageUrl on Questions and QuestionOptions; OptionText
             widened to NULL, guarded by a CHECK so an option can never be
             both textless and imageless.
   Why     : an option may be text-only, image-only, or both. Making OptionText
             nullable WITHOUT the CHECK would allow an empty option — the CHECK
             is what makes the widening safe.
   Data    : all existing options have text, so the widening cannot invalidate
             a single existing row and the CHECK passes on all of them.
   ========================================================================== */

ALTER TABLE Assessment.Questions
    ADD ImageUrl NVARCHAR(500) NULL;
GO

ALTER TABLE Assessment.QuestionOptions
    ADD ImageUrl NVARCHAR(500) NULL;
GO

-- Widen OptionText to nullable. NVARCHAR(MAX) is its current type; repeat it
-- exactly, because ALTER COLUMN rewrites the full definition.
ALTER TABLE Assessment.QuestionOptions
    ALTER COLUMN OptionText NVARCHAR(MAX) NULL;
GO

ALTER TABLE Assessment.QuestionOptions
    ADD CONSTRAINT CK_QuestionOptions_TextOrImage
        CHECK (OptionText IS NOT NULL OR ImageUrl IS NOT NULL);
GO


/* ============================================================================
   SECTION 3 — Essay support on the attempt snapshot
   Current : QuizAttemptQuestions.CorrectOptionId is NOT NULL (migration 001).
   Change  : make it nullable, snapshot QuestionType, and add a CHECK that ties
             the two together.
   Why     : an Essay question has no answer key, so a NOT NULL CorrectOptionId
             makes essays impossible to include in an attempt. QuestionType is
             snapshotted for the same reason Difficulty is — it determines how
             the answer is graded, so it must be frozen at attempt start.
   Data    : every existing row is multiple-choice with a key, so the DEFAULT
             and the widening leave them valid. Backfill in step 3c is a no-op
             on a fresh install.
   ========================================================================== */

-- 3a. Drop the composite FK before altering a column it references.
ALTER TABLE Assessment.QuizAttemptQuestions
    DROP CONSTRAINT FK_QuizAttemptQuestions_QuestionId_CorrectOptionId;
GO

ALTER TABLE Assessment.QuizAttemptQuestions
    ALTER COLUMN CorrectOptionId INT NULL;
GO

-- 3b. Recreate it. A NULL CorrectOptionId is exempt from FK checking in
--     SQL Server, which is exactly the behaviour Essay needs.
ALTER TABLE Assessment.QuizAttemptQuestions
    ADD CONSTRAINT FK_QuizAttemptQuestions_QuestionId_CorrectOptionId
        FOREIGN KEY (QuestionId, CorrectOptionId)
        REFERENCES Assessment.QuestionOptions (QuestionId, Id);
GO

-- 3c. Snapshot the question type alongside topic and difficulty.
ALTER TABLE Assessment.QuizAttemptQuestions
    ADD QuestionType NVARCHAR(20) NOT NULL
        CONSTRAINT DF_QuizAttemptQuestions_QuestionType DEFAULT ('MultipleChoice');
GO

UPDATE aq
SET aq.QuestionType = q.QuestionType
FROM Assessment.QuizAttemptQuestions AS aq
JOIN Assessment.Questions AS q ON q.Id = aq.QuestionId;
GO

ALTER TABLE Assessment.QuizAttemptQuestions
    ADD CONSTRAINT CK_QuizAttemptQuestions_QuestionType
        CHECK (QuestionType IN ('MultipleChoice', 'TrueFalse', 'Essay'));
GO

-- 3d. The invariant that makes the nullable key safe: only an Essay may lack
--     an answer key. Any auto-graded question without one is now rejected by
--     the database, not just by the service.
ALTER TABLE Assessment.QuizAttemptQuestions
    ADD CONSTRAINT CK_QuizAttemptQuestions_EssayHasNoKey
        CHECK (CorrectOptionId IS NOT NULL OR QuestionType = 'Essay');
GO


/* ============================================================================
   SECTION 4 — Essay answers
   Current : no table can hold free text. QuizAttemptMistakes.SelectedOptionId
             is NOT NULL and carries a composite FK to QuestionOptions, so it
             cannot represent an essay answer.
   Change  : one new table, one row per (attempt, question).
   Why     : an essay answer has a grading lifecycle (pending → graded) that a
             mistake row does not. Putting nullable text and grading columns on
             QuizAttemptMistakes would overload that table and break its
             "a row here means a wrong answer" meaning. Putting them on
             QuizAttemptQuestions would break its "written once at start,
             never modified" invariant.
   Data    : new table, nothing to migrate.
   ========================================================================== */

CREATE TABLE Assessment.QuizAttemptEssayAnswers
(
    Id              BIGINT IDENTITY(1,1)    NOT NULL,
    QuizAttemptId   BIGINT                  NOT NULL,
    QuestionId      INT                     NOT NULL,
    AnswerText      NVARCHAR(MAX)           NOT NULL,

    -- Grading lifecycle. 'Pending' until a human (or, later, AI) grades it.
    Status          NVARCHAR(20)            NOT NULL
        CONSTRAINT DF_QuizAttemptEssayAnswers_Status DEFAULT ('Pending'),
    AwardedPoints   TINYINT                 NULL,
    Feedback        NVARCHAR(MAX)           NULL,
    GradedBy        NVARCHAR(20)            NULL,   -- 'Human' | 'Ai'
    GradedAt        DATETIME2(3)            NULL,

    CreatedAt       DATETIME2(3)            NOT NULL
        CONSTRAINT DF_QuizAttemptEssayAnswers_CreatedAt DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT PK_QuizAttemptEssayAnswers PRIMARY KEY CLUSTERED (Id),

    -- One answer per question per attempt. Makes a double submit a DB error,
    -- exactly like UQ_QuizAttemptMistakes_AttemptId_QuestionId does for MCQ.
    CONSTRAINT UQ_QuizAttemptEssayAnswers_AttemptId_QuestionId
        UNIQUE (QuizAttemptId, QuestionId),

    CONSTRAINT FK_QuizAttemptEssayAnswers_QuizAttempts FOREIGN KEY (QuizAttemptId)
        REFERENCES Assessment.QuizAttempts (Id) ON DELETE CASCADE,

    CONSTRAINT FK_QuizAttemptEssayAnswers_Questions FOREIGN KEY (QuestionId)
        REFERENCES Assessment.Questions (Id),

    -- The essay must be one of the questions this attempt actually contained.
    CONSTRAINT FK_QuizAttemptEssayAnswers_QuizAttemptQuestions
        FOREIGN KEY (QuizAttemptId, QuestionId)
        REFERENCES Assessment.QuizAttemptQuestions (QuizAttemptId, QuestionId),

    CONSTRAINT CK_QuizAttemptEssayAnswers_Status
        CHECK (Status IN ('Pending', 'Graded', 'Skipped')),

    CONSTRAINT CK_QuizAttemptEssayAnswers_GradedBy
        CHECK (GradedBy IS NULL OR GradedBy IN ('Human', 'Ai')),

    -- A graded answer must carry its grade and its grader; a pending one must not.
    CONSTRAINT CK_QuizAttemptEssayAnswers_GradedIsComplete
        CHECK (
            (Status = 'Graded'  AND AwardedPoints IS NOT NULL AND GradedAt IS NOT NULL AND GradedBy IS NOT NULL)
         OR (Status <> 'Graded' AND AwardedPoints IS NULL     AND GradedAt IS NULL     AND GradedBy IS NULL)
        )
);
GO

CREATE INDEX IX_QuizAttemptEssayAnswers_QuestionId
    ON Assessment.QuizAttemptEssayAnswers (QuestionId);
GO

-- Serves the "what is waiting for a grader" queue.
CREATE INDEX IX_QuizAttemptEssayAnswers_Status
    ON Assessment.QuizAttemptEssayAnswers (Status)
    WHERE Status = 'Pending';
GO


/* ============================================================================
   SECTION 5 — Languages
   Current : no language concept anywhere.
   Change  : a lookup table, seeded with en and ar.
   Why     : adding a third language must be an INSERT, not a schema change.
             Every translation table FKs to this, so an unknown language code
             cannot enter the database at all.
   ========================================================================== */

CREATE TABLE Assessment.Languages
(
    Code        NVARCHAR(5)     NOT NULL,      -- ISO 639-1, lowercase
    Name        NVARCHAR(50)    NOT NULL,
    IsActive    BIT             NOT NULL CONSTRAINT DF_Languages_IsActive DEFAULT (1),

    CONSTRAINT PK_Languages PRIMARY KEY CLUSTERED (Code),
    CONSTRAINT UQ_Languages_Name UNIQUE (Name)
);
GO

INSERT INTO Assessment.Languages (Code, Name) VALUES
    (N'en', N'English'),
    (N'ar', N'Arabic');
GO


/* ============================================================================
   SECTION 6 — Translation tables
   Current : one text column per field, single language.
   Change  : a translation table per localizable entity, keyed
             (ParentId, LanguageCode).
   Why     : side-by-side columns (TitleEn / TitleAr) need a schema change per
             new language, which the extensibility requirement rules out.
             A single generic Translations(EntityType, EntityId, Field, Value)
             table is rejected for the opposite reason: no foreign keys, no
             typing, and no way for the database to stop an orphaned row.
             One table per entity keeps real FKs and real cascade behaviour.
   Base col: the original column is KEPT and becomes the last-resort fallback,
             which is what makes this migration backward compatible — every
             existing read path still works before a single translation exists.
   ========================================================================== */

CREATE TABLE Assessment.QuizTranslations
(
    Id              INT IDENTITY(1,1)   NOT NULL,
    QuizId          INT                 NOT NULL,
    LanguageCode    NVARCHAR(5)         NOT NULL,
    Title           NVARCHAR(300)       NOT NULL,
    Description     NVARCHAR(MAX)       NULL,

    CONSTRAINT PK_QuizTranslations PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_QuizTranslations_QuizId_Language UNIQUE (QuizId, LanguageCode),
    CONSTRAINT FK_QuizTranslations_Quizzes FOREIGN KEY (QuizId)
        REFERENCES Assessment.Quizzes (Id) ON DELETE CASCADE,
    CONSTRAINT FK_QuizTranslations_Languages FOREIGN KEY (LanguageCode)
        REFERENCES Assessment.Languages (Code)
);
GO

CREATE TABLE Assessment.QuestionTranslations
(
    Id              INT IDENTITY(1,1)   NOT NULL,
    QuestionId      INT                 NOT NULL,
    LanguageCode    NVARCHAR(5)         NOT NULL,
    QuestionText    NVARCHAR(MAX)       NOT NULL,

    CONSTRAINT PK_QuestionTranslations PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_QuestionTranslations_QuestionId_Language UNIQUE (QuestionId, LanguageCode),
    CONSTRAINT FK_QuestionTranslations_Questions FOREIGN KEY (QuestionId)
        REFERENCES Assessment.Questions (Id) ON DELETE CASCADE,
    CONSTRAINT FK_QuestionTranslations_Languages FOREIGN KEY (LanguageCode)
        REFERENCES Assessment.Languages (Code)
);
GO

CREATE TABLE Assessment.QuestionOptionTranslations
(
    Id                  INT IDENTITY(1,1)   NOT NULL,
    QuestionOptionId    INT                 NOT NULL,
    LanguageCode        NVARCHAR(5)         NOT NULL,
    OptionText          NVARCHAR(MAX)       NULL,   -- NULL when the option is image-only

    CONSTRAINT PK_QuestionOptionTranslations PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_QuestionOptionTranslations_OptionId_Language UNIQUE (QuestionOptionId, LanguageCode),
    CONSTRAINT FK_QuestionOptionTranslations_QuestionOptions FOREIGN KEY (QuestionOptionId)
        REFERENCES Assessment.QuestionOptions (Id) ON DELETE CASCADE,
    CONSTRAINT FK_QuestionOptionTranslations_Languages FOREIGN KEY (LanguageCode)
        REFERENCES Assessment.Languages (Code)
);
GO

CREATE TABLE Assessment.TopicTranslations
(
    Id              INT IDENTITY(1,1)   NOT NULL,
    TopicId         INT                 NOT NULL,
    LanguageCode    NVARCHAR(5)         NOT NULL,
    Name            NVARCHAR(200)       NOT NULL,
    Description     NVARCHAR(MAX)       NULL,

    CONSTRAINT PK_TopicTranslations PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_TopicTranslations_TopicId_Language UNIQUE (TopicId, LanguageCode),
    CONSTRAINT FK_TopicTranslations_Topics FOREIGN KEY (TopicId)
        REFERENCES Assessment.Topics (Id) ON DELETE CASCADE,
    CONSTRAINT FK_TopicTranslations_Languages FOREIGN KEY (LanguageCode)
        REFERENCES Assessment.Languages (Code)
);
GO

CREATE TABLE Assessment.CategoryTranslations
(
    Id              INT IDENTITY(1,1)   NOT NULL,
    CategoryId      TINYINT             NOT NULL,
    LanguageCode    NVARCHAR(5)         NOT NULL,
    Name            NVARCHAR(100)       NOT NULL,

    CONSTRAINT PK_CategoryTranslations PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_CategoryTranslations_CategoryId_Language UNIQUE (CategoryId, LanguageCode),
    CONSTRAINT FK_CategoryTranslations_Categories FOREIGN KEY (CategoryId)
        REFERENCES Assessment.Categories (Id) ON DELETE CASCADE,
    CONSTRAINT FK_CategoryTranslations_Languages FOREIGN KEY (LanguageCode)
        REFERENCES Assessment.Languages (Code)
);
GO


/* ============================================================================
   SECTION 7 — Backfill the English translation from the existing columns
   ----------------------------------------------------------------------------
   ⚠ READ BEFORE RUNNING.
   This copies the CURRENT base-column text into an 'en' translation row.
   The base columns are NOT guaranteed to contain English — seeded Categories
   are English ('Components', 'Concepts') while authored questions are Arabic.
   This backfill therefore produces rows labelled 'en' that may hold Arabic.

   It is still the right default: it guarantees the fallback chain always
   resolves and nothing 404s on day one. Review and correct the labelling
   afterwards with:
       SELECT * FROM Assessment.QuestionTranslations WHERE LanguageCode = N'en';

   If you would rather label the existing content Arabic, change N'en' to N'ar'
   in the five statements below BEFORE running them.
   ========================================================================== */

INSERT INTO Assessment.QuizTranslations (QuizId, LanguageCode, Title, Description)
SELECT q.Id, N'en', q.Title, q.Description
FROM Assessment.Quizzes AS q
WHERE NOT EXISTS (SELECT 1 FROM Assessment.QuizTranslations t
                  WHERE t.QuizId = q.Id AND t.LanguageCode = N'en');
GO

INSERT INTO Assessment.QuestionTranslations (QuestionId, LanguageCode, QuestionText)
SELECT q.Id, N'en', q.QuestionText
FROM Assessment.Questions AS q
WHERE NOT EXISTS (SELECT 1 FROM Assessment.QuestionTranslations t
                  WHERE t.QuestionId = q.Id AND t.LanguageCode = N'en');
GO

INSERT INTO Assessment.QuestionOptionTranslations (QuestionOptionId, LanguageCode, OptionText)
SELECT o.Id, N'en', o.OptionText
FROM Assessment.QuestionOptions AS o
WHERE NOT EXISTS (SELECT 1 FROM Assessment.QuestionOptionTranslations t
                  WHERE t.QuestionOptionId = o.Id AND t.LanguageCode = N'en');
GO

INSERT INTO Assessment.TopicTranslations (TopicId, LanguageCode, Name, Description)
SELECT t.Id, N'en', t.Name, t.Description
FROM Assessment.Topics AS t
WHERE NOT EXISTS (SELECT 1 FROM Assessment.TopicTranslations x
                  WHERE x.TopicId = t.Id AND x.LanguageCode = N'en');
GO

INSERT INTO Assessment.CategoryTranslations (CategoryId, LanguageCode, Name)
SELECT c.Id, N'en', c.Name
FROM Assessment.Categories AS c
WHERE NOT EXISTS (SELECT 1 FROM Assessment.CategoryTranslations x
                  WHERE x.CategoryId = c.Id AND x.LanguageCode = N'en');
GO


/* ============================================================================
   SECTION 8 — Hint language
   Current : QuestionHints.HintText, single language.
   Change  : a LanguageCode column, NOT a translation table.
   Why     : a hint is GENERATED per attempt in one language, not authored and
             then translated. Two hints in two languages are two independent
             generations, not two renderings of one string. A translation table
             would imply a canonical hint that gets translated, which is false.
             UQ_QuestionHints_MistakeId_Sequence is therefore extended to
             include the language, so a mistake can hold sequence 1 in both
             'ar' and 'en' without collision.
   Data    : existing hints are Arabic (the AI prompt and all content are
             Arabic), so the DEFAULT labels them 'ar' correctly.
   ========================================================================== */

ALTER TABLE Assessment.QuestionHints
    ADD LanguageCode NVARCHAR(5) NOT NULL
        CONSTRAINT DF_QuestionHints_LanguageCode DEFAULT (N'ar');
GO

ALTER TABLE Assessment.QuestionHints
    ADD CONSTRAINT FK_QuestionHints_Languages FOREIGN KEY (LanguageCode)
        REFERENCES Assessment.Languages (Code);
GO

-- Replace the uniqueness rule so sequence numbering is per-language.
ALTER TABLE Assessment.QuestionHints
    DROP CONSTRAINT UQ_QuestionHints_MistakeId_Sequence;
GO

ALTER TABLE Assessment.QuestionHints
    ADD CONSTRAINT UQ_QuestionHints_MistakeId_Language_Sequence
        UNIQUE (QuizAttemptMistakeId, LanguageCode, HintSequence);
GO


/* ============================================================================
   SECTION 9 — Verification
   All four should return 0 rows.
   ========================================================================== */

-- Options with neither text nor image (should be impossible after the CHECK):
SELECT Id FROM Assessment.QuestionOptions
WHERE OptionText IS NULL AND ImageUrl IS NULL;

-- Auto-graded snapshot rows missing an answer key:
SELECT Id FROM Assessment.QuizAttemptQuestions
WHERE CorrectOptionId IS NULL AND QuestionType <> 'Essay';

-- Snapshot type disagreeing with the live question (informational only —
-- a disagreement here is legitimate if an admin changed the type after the
-- attempt started, which is exactly what the snapshot exists to survive):
SELECT aq.Id, aq.QuestionType AS SnapshotType, q.QuestionType AS CurrentType
FROM Assessment.QuizAttemptQuestions aq
JOIN Assessment.Questions q ON q.Id = aq.QuestionId
WHERE aq.QuestionType <> q.QuestionType;

-- Entities with no translation in any language:
SELECT q.Id FROM Assessment.Questions q
WHERE NOT EXISTS (SELECT 1 FROM Assessment.QuestionTranslations t WHERE t.QuestionId = q.Id);
GO
