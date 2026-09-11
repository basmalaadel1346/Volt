using System;
using System.Collections.Generic;

namespace AssessmentDA.Entities;

public partial class UserTopicStat
{
    public long Id { get; set; }

    public Guid UserId { get; set; }

    public int TopicId { get; set; }

    public string Difficulty { get; set; } = null!;

    public int QuestionsAnsweredCount { get; set; }

    public int CorrectCount { get; set; }

    public int? WrongCount { get; set; }

    public int HintsUsedCount { get; set; }

    public long? LastQuizAttemptId { get; set; }

    public DateTime? LastPracticedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual QuizAttempt? LastQuizAttempt { get; set; }

    public virtual Topic Topic { get; set; } = null!;
}
