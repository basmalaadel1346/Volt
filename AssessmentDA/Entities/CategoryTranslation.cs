using System;
using System.Collections.Generic;

namespace AssessmentDA.Entities;

public partial class CategoryTranslation
{
    public int Id { get; set; }

    public byte CategoryId { get; set; }

    public string LanguageCode { get; set; } = null!;

    public string Name { get; set; } = null!;

    public virtual Category Category { get; set; } = null!;
}
