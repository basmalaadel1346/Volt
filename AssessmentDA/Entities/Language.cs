using System;
using System.Collections.Generic;

namespace AssessmentDA.Entities;

/// <summary>
/// Supported content languages. Adding a language is an INSERT here, not a
/// schema change — every translation table has an FK to this.
/// </summary>
public partial class Language
{
    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public bool IsActive { get; set; }
}
