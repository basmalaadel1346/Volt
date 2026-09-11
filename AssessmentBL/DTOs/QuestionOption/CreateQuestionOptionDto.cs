namespace AssessmentBL.DTOs.QuestionOption
{
    public class CreateQuestionOptionDto
    {
        public int QuestionId { get; set; }

        /// <summary>Null only when ImageUrl is supplied.</summary>
        public string? OptionText { get; set; }

        /// <summary>Null only when OptionText is supplied.</summary>
        public string? ImageUrl { get; set; }

        /// <summary>
        /// Admin-only semantic description of the image, for the AI. REQUIRED
        /// when OptionText is blank — without it the AI cannot interpret the
        /// child's answer. NEVER returned to the child.
        /// </summary>
        public string? ImageDescription { get; set; }

        public bool IsCorrect { get; set; }

        public short DisplayOrder { get; set; }
    }
}
