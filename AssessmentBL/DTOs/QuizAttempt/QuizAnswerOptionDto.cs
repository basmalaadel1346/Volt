namespace AssessmentBL.DTOs.QuizAttempt
{
    public class QuizAnswerOptionDto
    {
        public int OptionId { get; set; }

        /// <summary>
        /// Null when the option is image-only. Never both this and ImageUrl null —
        /// CK_QuestionOptions_TextOrImage guarantees at least one.
        /// </summary>
        public string? OptionText { get; set; }

        /// <summary>Optional image, server-relative path. Null when text-only.</summary>
        public string? ImageUrl { get; set; }

        public short DisplayOrder { get; set; }
    }
}
