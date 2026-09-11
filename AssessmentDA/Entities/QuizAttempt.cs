using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AssessmentDA.Entities;

public partial class QuizAttempt
{
    public long Id { get; set; }

    public int QuizId { get; set; }

    public Guid UserId { get; set; }

    public short QuestionsAnsweredCount { get; set; }

    public short TotalQuestionsAtAttempt { get; set; }

    public short CorrectAnswersCount { get; set; }

    public short? WrongAnswersCount { get; set; }

    public decimal ScorePercentage { get; set; }

    public string Status { get; set; } = null!;

    public DateTime StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public int? DurationSeconds { get; set; }

    public virtual Quiz Quiz { get; set; } = null!;

    public virtual ICollection<QuizAttemptMistake> QuizAttemptMistakes { get; set; } = new List<QuizAttemptMistake>();

    public virtual ICollection<UserTopicStat> UserTopicStats { get; set; } = new List<UserTopicStat>();
    public long? PreviousAttemptId { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = null!;
    public virtual QuizAttempt? PreviousAttempt { get; set; }

    public virtual QuizAttempt? NextAttempt { get; set; }

    public virtual ICollection<QuizAttemptQuestion> QuizAttemptQuestions { get; set; }
        = new List<QuizAttemptQuestion>();

    public virtual ICollection<QuizAttemptEssayAnswer> QuizAttemptEssayAnswers { get; set; }
        = new List<QuizAttemptEssayAnswer>();
}
