/* ============================================================================
   Migration 004 — Semantic descriptions for question and option images
   ----------------------------------------------------------------------------
   PROBLEM
     When a child selects an image-only option, the AI receives
         "wrongOptionText": ""
     because QuizAttemptService.GenerateHintsAsync falls back to string.Empty.
     The AI cannot see the image, so it has no information about what the child
     chose and cannot explain the mistake.

   ROOT CAUSE
     There is no column anywhere that holds a semantic description of an image.
     The data was never modelled — it is not being lost in a mapping.

   CHANGE
     Two nullable columns holding admin-authored, child-INVISIBLE metadata:
         Assessment.Questions.ImageDescription
         Assessment.QuestionOptions.ImageDescription
     Plus a CHECK that makes the bug structurally impossible for new data.

   SAFE TO RUN ALONE. Idempotent, additive, adds ImageUrl too if migration 003
   has not been applied yet. No column is dropped, no row is modified.
   ========================================================================== */

USE VoltDB;
GO

/* ----------------------------------------------------------------------------
   §1 — ImageUrl (from migration 003; repeated so 004 can run standalone)
   -------------------------------------------------------------------------- */

IF COL_LENGTH('Assessment.Questions', 'ImageUrl') IS NULL
    ALTER TABLE Assessment.Questions ADD ImageUrl NVARCHAR(500) NULL;
GO

IF COL_LENGTH('Assessment.QuestionOptions', 'ImageUrl') IS NULL
    ALTER TABLE Assessment.QuestionOptions ADD ImageUrl NVARCHAR(500) NULL;
GO


/* ----------------------------------------------------------------------------
   §2 — ImageDescription
   Why : the semantic equivalent of QuestionText / OptionText for an image.
         Sent to the AI; NEVER returned to the child.
   Impact : none. Nullable additions; existing rows keep NULL.
   -------------------------------------------------------------------------- */

IF COL_LENGTH('Assessment.Questions', 'ImageDescription') IS NULL
    ALTER TABLE Assessment.Questions ADD ImageDescription NVARCHAR(1000) NULL;
GO

IF COL_LENGTH('Assessment.QuestionOptions', 'ImageDescription') IS NULL
    ALTER TABLE Assessment.QuestionOptions ADD ImageDescription NVARCHAR(1000) NULL;
GO

EXEC sys.sp_addextendedproperty
    @name = N'MS_Description',
    @value = N'Admin-authored semantic description of ImageUrl. Sent to the AI so it can reason about an image it cannot see. NEVER returned in a child-facing response.',
    @level0type = N'SCHEMA', @level0name = N'Assessment',
    @level1type = N'TABLE',  @level1name = N'QuestionOptions',
    @level2type = N'COLUMN', @level2name = N'ImageDescription';
GO


/* ----------------------------------------------------------------------------
   §3 — GATED: make the bug structurally impossible
   ----------------------------------------------------------------------------
   An option the child can ONLY see as an image must carry a description, or
   the AI has nothing to work with. This is the database-level guarantee that
   replaces the silent "" fallback.

   Reads as: an option must have readable text, OR a description standing in
   for its image.

   ⚠ Run the gate query FIRST. If it returns rows, those options are image-only
   with no description — fill in their descriptions before adding the CHECK.
   Do NOT invent descriptions to satisfy the constraint.
   -------------------------------------------------------------------------- */

-- GATE: must return 0 rows.
SELECT o.Id AS OptionId, o.QuestionId, o.ImageUrl
FROM Assessment.QuestionOptions AS o
WHERE (o.OptionText IS NULL OR LTRIM(RTRIM(o.OptionText)) = N'')
  AND o.ImageDescription IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = 'CK_QuestionOptions_ImageOptionHasDescription')
   AND NOT EXISTS (SELECT 1 FROM Assessment.QuestionOptions
                   WHERE (OptionText IS NULL OR LTRIM(RTRIM(OptionText)) = N'')
                     AND ImageDescription IS NULL)
BEGIN
    ALTER TABLE Assessment.QuestionOptions
        ADD CONSTRAINT CK_QuestionOptions_ImageOptionHasDescription
            CHECK (
                (OptionText IS NOT NULL AND LTRIM(RTRIM(OptionText)) <> N'')
                OR ImageDescription IS NOT NULL
            );
END
GO


/* ----------------------------------------------------------------------------
   §4 — Verification
   -------------------------------------------------------------------------- */

-- Image-only options the AI cannot interpret (expect 0 after §3):
SELECT Id, QuestionId FROM Assessment.QuestionOptions
WHERE (OptionText IS NULL OR LTRIM(RTRIM(OptionText)) = N'')
  AND ImageDescription IS NULL;

-- Questions whose text is blank and have no description either (expect 0;
-- these would reach the AI with an empty question):
SELECT Id, QuizId FROM Assessment.Questions
WHERE LTRIM(RTRIM(QuestionText)) = N''
  AND ImageDescription IS NULL;

-- Descriptions present with no image — harmless, but usually an authoring slip:
SELECT Id, QuestionId FROM Assessment.QuestionOptions
WHERE ImageDescription IS NOT NULL AND ImageUrl IS NULL;
GO
