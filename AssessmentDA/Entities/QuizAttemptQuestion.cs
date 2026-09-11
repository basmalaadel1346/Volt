using System;
using System.Collections.Generic;

namespace AssessmentDA.Entities;

public partial class QuizAttemptQuestion
{
    public long Id { get; set; }

    public long QuizAttemptId { get; set; }

    public int QuestionId { get; set; }

    /// <summary>
    /// Topic the question was classified under when the attempt started.
    /// Statistics for this attempt are attributed here, not to the
    /// question's current TopicId.
    /// </summary>
    public int TopicId { get; set; }

    /// <summary>
    /// Difficulty the question carried when the attempt started.
    /// </summary>
    public string Difficulty { get; set; } = null!;

    /// <summary>
    /// The answer key as it stood when the attempt started. Submissions are
    /// graded against this, never against QuestionOption.IsCorrect, so an
    /// admin editing the correct answer cannot retroactively change how an
    /// in-flight or past attempt was scored.
    /// </summary>
    /// <summary>Null only for Essay questions, which have no answer key.
    /// CK_QuizAttemptQuestions_EssayHasNoKey enforces that at the database.</summary>
    public int? CorrectOptionId { get; set; }

    /// <summary>
    /// The question's type when the attempt started. Frozen for the same reason
    /// Difficulty is: it decides how the answer is graded.
    /// </summary>
    public string QuestionType { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual QuizAttempt QuizAttempt { get; set; } = null!;

    public virtual Question Question { get; set; } = null!;

    public virtual Topic Topic { get; set; } = null!;

    public virtual QuestionOption? CorrectOption { get; set; }

    public virtual QuizAttemptMistake? QuizAttemptMistake { get; set; }
}
