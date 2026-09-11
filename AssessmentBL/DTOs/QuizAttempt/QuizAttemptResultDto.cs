namespace AssessmentBL.DTOs.QuizAttempt
{
    public class QuizAttemptResultDto
    {
        public long AttemptId { get; set; }

        /// <summary>Every question in the attempt, essays included.</summary>
        public short TotalQuestions { get; set; }

        /// <summary>
        /// Questions the backend could score by itself — total minus essays.
        /// This is the denominator of ScorePercentage.
        /// </summary>
        public short AutoGradedQuestions { get; set; }

        /// <summary>Essay answers stored for review. Not part of the score.</summary>
        public short PendingEssayQuestions { get; set; }

        public short CorrectAnswers { get; set; }

        public short WrongAnswers { get; set; }

        /// <summary>correct / AutoGradedQuestions, 2dp. 0 when nothing was auto-graded.</summary>
        public decimal ScorePercentage { get; set; }

        public string Language { get; set; } = null!;

        public bool LanguageFallbackApplied { get; set; }

        public List<QuizQuestionForAttemptDto> RetryQuestions { get; set; } = new();
    }
}
