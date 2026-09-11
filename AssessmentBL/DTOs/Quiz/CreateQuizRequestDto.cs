using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssessmentBL.DTOs.Quiz
{
    
        public class CreateQuizDto
        {
            public string Title { get; set; } = null!;

            public string? Description { get; set; }

            public string QuizType { get; set; } = null!;

            public int? LevelId { get; set; }

            public int? LessonId { get; set; }
        }
    }

