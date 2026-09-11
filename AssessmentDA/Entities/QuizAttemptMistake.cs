using System;
using System.Collections.Generic;

namespace AssessmentDA.Entities;

public partial class QuizAttemptMistake
{
    public long Id { get; set; }

    public long QuizAttemptId { get; set; }

    public int QuestionId { get; set; }

    public int SelectedOptionId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Question Question { get; set; } = null!;

    public virtual ICollection<QuestionHint> QuestionHints { get; set; } = new List<QuestionHint>();

    public virtual QuestionOption QuestionOption { get; set; } = null!;

    public virtual QuizAttempt QuizAttempt { get; set; } = null!;
    public virtual QuizAttemptQuestion QuizAttemptQuestion { get; set; } = null!;
}
