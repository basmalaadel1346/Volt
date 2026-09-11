using AssessmentBL.DTOs.QuestionOption;
namespace AssessmentBL.DTOs.Question
{
    public class AdminQuestionResponseDto
    {
        public int Id { get; set; }

        public int QuizId { get; set; }

        public int TopicId { get; set; }

        public string QuestionText { get; set; } = null!;

        public string QuestionType { get; set; } = null!;

        public string? ImageUrl { get; set; }

        /// <summary>
        /// Admin-only semantic description of the image, for the AI. NEVER
        /// returned to the child.
        /// </summary>
        public string? ImageDescription { get; set; }

        public string Difficulty { get; set; } = null!;

        public short DisplayOrder { get; set; }

        public byte Points { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        public IReadOnlyList<AdminQuestionOptionResponseDto> Options { get; set; }
            = [];
    }
}
