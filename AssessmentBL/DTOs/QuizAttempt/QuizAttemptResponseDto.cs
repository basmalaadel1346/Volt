namespace AssessmentBL.DTOs.QuizAttempt
{
    public class QuizAttemptResponseDto
    {
        public long AttemptId { get; set; }

        public int QuizId { get; set; }

        public DateTime StartedAt { get; set; }

        /// <summary>The language this response was resolved in ("en" | "ar").</summary>
        public string Language { get; set; } = null!;

        /// <summary>
        /// True when at least one text field fell back to another language because
        /// the requested translation was missing. Fallback is per field.
        /// </summary>
        public bool LanguageFallbackApplied { get; set; }

        public List<QuizQuestionForAttemptDto> Questions { get; set; } = new();
    }
}
