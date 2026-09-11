/* ============================================================================
   Assessment.QuizAttemptQuestions — historical snapshot columns
   ----------------------------------------------------------------------------
   Why: Questions and QuestionOptions are mutable content. Without pinning the
   classification and the answer key at attempt start, an admin edit between
   Start and Submit silently re-grades an in-flight attempt and re-attributes
   its statistics to the new Topic/Difficulty.

   Only three columns are added. QuestionText, Points, DisplayOrder, IsActive
   and QuizId are deliberately NOT snapshotted — see the review notes.

   Requires data migration: yes, for existing QuizAttemptQuestions rows.
   Run the steps in order. Step 3 must return 0 rows before step 4 is run.
   ========================================================================== */

USE VoltDB;
GO

-- ----------------------------------------------------------------------------
-- Step 1: add the columns as NULLable so existing rows survive the ALTER.
-- ----------------------------------------------------------------------------
ALTER TABLE Assessment.QuizAttemptQuestions
    ADD TopicId         INT             NULL,
        Difficulty      NVARCHAR(20)    NULL,
        CorrectOptionId INT             NULL;
GO

-- ----------------------------------------------------------------------------
-- Step 2: backfill from the current definitions. This is the best available
-- approximation for rows created before the snapshot existed — the true
-- historical values were never recorded.
-- ----------------------------------------------------------------------------
UPDATE aq
SET aq.TopicId    = q.TopicId,
    aq.Difficulty = q.Difficulty
FROM Assessment.QuizAttemptQuestions AS aq
JOIN Assessment.Questions AS q ON q.Id = aq.QuestionId
WHERE aq.TopicId IS NULL OR aq.Difficulty IS NULL;
GO

UPDATE aq
SET aq.CorrectOptionId = o.Id
FROM Assessment.QuizAttemptQuestions AS aq
JOIN Assessment.QuestionOptions AS o
    ON o.QuestionId = aq.QuestionId AND o.IsCorrect = 1
WHERE aq.CorrectOptionId IS NULL;
GO

-- ----------------------------------------------------------------------------
-- Step 3: verification gate. MUST return 0 rows before continuing.
-- A row here means a question in a past attempt has no correct option at all,
-- which has to be resolved by hand (fix the question, or delete the orphaned
-- attempt) — there is no safe automatic answer.
-- ----------------------------------------------------------------------------
SELECT Id, QuizAttemptId, QuestionId
FROM Assessment.QuizAttemptQuestions
WHERE TopicId IS NULL OR Difficulty IS NULL OR CorrectOptionId IS NULL;
GO

-- ----------------------------------------------------------------------------
-- Step 4: tighten to NOT NULL.
-- ----------------------------------------------------------------------------
ALTER TABLE Assessment.QuizAttemptQuestions ALTER COLUMN TopicId         INT          NOT NULL;
ALTER TABLE Assessment.QuizAttemptQuestions ALTER COLUMN Difficulty      NVARCHAR(20) NOT NULL;
ALTER TABLE Assessment.QuizAttemptQuestions ALTER COLUMN CorrectOptionId INT          NOT NULL;
GO

-- ----------------------------------------------------------------------------
-- Step 5: database-enforced integrity on the snapshot.
-- ----------------------------------------------------------------------------

-- Mirrors CK_Questions_Difficulty / CK_UserTopicStats_Difficulty, so a
-- snapshot can never carry a difficulty the stats table would reject.
ALTER TABLE Assessment.QuizAttemptQuestions
    ADD CONSTRAINT CK_QuizAttemptQuestions_Difficulty
        CHECK (Difficulty IN ('Easy', 'Medium', 'Hard', 'Advanced'));
GO

-- The historical topic must remain resolvable; NO ACTION blocks deleting a
-- Topic that past attempts were classified under.
ALTER TABLE Assessment.QuizAttemptQuestions
    ADD CONSTRAINT FK_QuizAttemptQuestions_Topics FOREIGN KEY (TopicId)
        REFERENCES Assessment.Topics (Id);
GO

-- Composite FK against the existing UQ_QuestionOptions_QuestionId_Id unique
-- index. Guarantees the snapshotted answer key really is an option of this
-- question, and blocks deleting an option that an attempt still grades
-- against. Same pattern as FK_QuizAttemptMistakes_QuestionId_SelectedOptionId.
ALTER TABLE Assessment.QuizAttemptQuestions
    ADD CONSTRAINT FK_QuizAttemptQuestions_QuestionId_CorrectOptionId
        FOREIGN KEY (QuestionId, CorrectOptionId)
        REFERENCES Assessment.QuestionOptions (QuestionId, Id);
GO

/* ----------------------------------------------------------------------------
   No additional index is created.
   - UQ_QuizAttemptQuestions_AttemptId_QuestionId already serves the
     "load this attempt's snapshot" lookup that SubmitAsync performs.
   - FK_QuizAttemptQuestions_Topics is only ever traversed in the
     already-filtered per-attempt set, so it needs no index of its own.
   -------------------------------------------------------------------------- */
