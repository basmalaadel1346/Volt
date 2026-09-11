using System;
using System.Collections.Generic;

namespace AssessmentDA.Entities;

/// <summary>
/// A child's free-text answer to an Essay question. Separate from
/// QuizAttemptMistake because it has a grading lifecycle and does not mean
/// "this answer was wrong", and separate from QuizAttemptQuestion because that
/// row is written once at attempt start and never modified.
/// </summary>
public partial class QuizAttemptEssayAnswer
{
    public long Id { get; set; }

    public long QuizAttemptId { get; set; }

    public int QuestionId { get; set; }

    public string AnswerText { get; set; } = null!;

    /// <summary>Pending | Graded | Skipped.</summary>
    public string Status { get; set; } = null!;

    public byte? AwardedPoints { get; set; }

    public string? Feedback { get; set; }

    /// <summary>Human | Ai. Null until graded.</summary>
    public string? GradedBy { get; set; }

    public DateTime? GradedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual QuizAttempt QuizAttempt { get; set; } = null!;

    public virtual Question Question { get; set; } = null!;
}
