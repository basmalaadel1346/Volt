using System;
using System.Collections.Generic;

namespace AssessmentDA.Entities;

public partial class Quiz
{
    public int Id { get; set; }

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public string QuizType { get; set; } = null!;

    public int? LevelId { get; set; }

    public int? LessonId { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<Question> Questions { get; set; } = new List<Question>();

    public virtual ICollection<QuizAttempt> QuizAttempts { get; set; } = new List<QuizAttempt>();

    public virtual ICollection<QuizTranslation> QuizTranslations { get; set; }
        = new List<QuizTranslation>();
}
