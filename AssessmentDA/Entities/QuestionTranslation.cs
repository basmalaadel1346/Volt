using System;
using System.Collections.Generic;

namespace AssessmentDA.Entities;

public partial class QuestionTranslation
{
    public int Id { get; set; }

    public int QuestionId { get; set; }

    public string LanguageCode { get; set; } = null!;

    public string QuestionText { get; set; } = null!;

    public virtual Question Question { get; set; } = null!;
}
