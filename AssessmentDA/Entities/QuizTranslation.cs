using System;
using System.Collections.Generic;

namespace AssessmentDA.Entities;

public partial class QuizTranslation
{
    public int Id { get; set; }

    public int QuizId { get; set; }

    public string LanguageCode { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public virtual Quiz Quiz { get; set; } = null!;
}
