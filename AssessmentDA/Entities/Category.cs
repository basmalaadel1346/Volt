using System;
using System.Collections.Generic;

namespace AssessmentDA.Entities;

public partial class Category
{
    public byte Id { get; set; }

    public string Name { get; set; } = null!;

    public short SortOrder { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<Topic> Topics { get; set; } = new List<Topic>();

    public virtual ICollection<CategoryTranslation> CategoryTranslations { get; set; }
        = new List<CategoryTranslation>();
}
