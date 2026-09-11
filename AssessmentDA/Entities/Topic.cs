using System;
using System.Collections.Generic;

namespace AssessmentDA.Entities;

public partial class Topic
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public byte CategoryId { get; set; }

    public string LearningLevel { get; set; } = null!;

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Category Category { get; set; } = null!;

    public virtual ICollection<Question> Questions { get; set; } = new List<Question>();

    public virtual ICollection<UserTopicStat> UserTopicStats { get; set; } = new List<UserTopicStat>();

    public virtual ICollection<TopicTranslation> TopicTranslations { get; set; }
        = new List<TopicTranslation>();
}
