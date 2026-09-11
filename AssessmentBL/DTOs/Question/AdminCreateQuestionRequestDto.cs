namespace AssessmentBL.DTOs.Question
{
    public class CreateQuestionDto
    {
        public int QuizId { get; set; }

        public int TopicId { get; set; }

        public string QuestionText { get; set; } = null!;

        /// <summary>MultipleChoice | TrueFalse | Essay. Blank defaults to MultipleChoice.</summary>
        public string? QuestionType { get; set; }

        /// <summary>Optional. Upload via POST /api/content/media/images first, then send the returned url.</summary>
        public string? ImageUrl { get; set; }

        /// <summary>
        /// Admin-only semantic description of the image, for the AI. NEVER
        /// returned to the child.
        /// </summary>
        public string? ImageDescription { get; set; }

        public string Difficulty { get; set; } = null!;

        public short DisplayOrder { get; set; }

        public byte Points { get; set; }
    }
}
