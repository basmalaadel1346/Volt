using System;
using System.Collections.Generic;

namespace AssessmentDA.Entities;

public partial class QuestionOption
{
    public int Id { get; set; }

    public int QuestionId { get; set; }

    /// <summary>Null when the option is image-only. CK_QuestionOptions_TextOrImage
    /// guarantees at least one of OptionText / ImageUrl is present.</summary>
    public string? OptionText { get; set; }

    /// <summary>Optional image. Server-relative path.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Admin-authored semantic description of <see cref="ImageUrl"/>. This is
    /// the AI-visible equivalent of <see cref="OptionText"/> for an image-only
    /// option — without it the AI receives nothing for the child's answer.
    /// CK_QuestionOptions_ImageOptionHasDescription requires it whenever
    /// OptionText is blank. NEVER returned in a child-facing response.
    /// </summary>
    public string? ImageDescription { get; set; }

    public bool IsCorrect { get; set; }

    public short DisplayOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Question Question { get; set; } = null!;

    public virtual ICollection<QuizAttemptMistake> QuizAttemptMistakes { get; set; } = new List<QuizAttemptMistake>();

    public virtual ICollection<QuestionOptionTranslation> QuestionOptionTranslations { get; set; }
        = new List<QuestionOptionTranslation>();
}
