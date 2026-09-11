using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssessmentBL.DTOs.Quiz
{
    public class UpdateQuizDto
    {
        public string Title { get; set; } = null!;

        public string? Description { get; set; }
        public bool IsActive { get; set;} 
    }
}
