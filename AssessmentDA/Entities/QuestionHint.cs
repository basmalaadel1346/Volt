using System;
using System.Collections.Generic;

namespace AssessmentDA.Entities;

public partial class QuestionHint
{
    public long Id { get; set; }

    public long QuizAttemptMistakeId { get; set; }

    public string HintText { get; set; } = null!;

    public byte HintSequence { get; set; }

    /// <summary>The language this hint was GENERATED in — not a translation of
    /// another hint. Sequence numbering is per-language.</summary>
    public string LanguageCode { get; set; } = null!;

    public DateTime GeneratedAt { get; set; }

    public virtual QuizAttemptMistake QuizAttemptMistake { get; set; } = null!;
}
