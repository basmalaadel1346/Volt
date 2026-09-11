namespace AssessmentBL.DTOs.QuestionOption
{
    public class AdminQuestionOptionResponseDto
    {
        public int Id { get; set; }

        public string? OptionText { get; set; }

        public string? ImageUrl { get; set; }

        /// <summary>
        /// Admin-only semantic description of the image, for the AI. NEVER
        /// returned to the child.
        /// </summary>
        public string? ImageDescription { get; set; }

        public bool IsCorrect { get; set; }

        public short DisplayOrder { get; set; }
    }
}
