using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssessmentBL.DTOs.Quiz
{
    public class QuizFilterDto
    {
        public string? QuizType { get; set; }

        public int? LevelId { get; set; }

        public int? LessonId { get; set; }

        public bool? IsActive { get; set; }

        public int PageNumber { get; set; } = 1;

        public int PageSize { get; set; } = 20;
    }
}
