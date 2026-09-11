namespace AssessmentBL.DTOs.Question
{
    public class UpdateQuestionDto
    {
        public int TopicId { get; set; }

        public string QuestionText { get; set; } = null!;

        /// <summary>MultipleChoice | TrueFalse | Essay. Blank keeps the current type.</summary>
        public string? QuestionType { get; set; }

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
    }
}
