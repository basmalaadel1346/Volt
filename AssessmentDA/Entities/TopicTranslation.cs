using System;
using System.Collections.Generic;

namespace AssessmentDA.Entities;

public partial class TopicTranslation
{
    public int Id { get; set; }

    public int TopicId { get; set; }

    public string LanguageCode { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public virtual Topic Topic { get; set; } = null!;
}
