using System;
using System.Collections.Generic;

namespace AssessmentDA.Entities;

public partial class QuestionOptionTranslation
{
    public int Id { get; set; }

    public int QuestionOptionId { get; set; }

    public string LanguageCode { get; set; } = null!;

    /// <summary>Null when the option is image-only in this language.</summary>
    public string? OptionText { get; set; }

    public virtual QuestionOption QuestionOption { get; set; } = null!;
}
