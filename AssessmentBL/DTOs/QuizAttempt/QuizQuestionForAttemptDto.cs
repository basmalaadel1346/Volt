namespace AssessmentBL.DTOs.QuizAttempt
{
    public class QuizQuestionForAttemptDto
    {
        public int QuestionId { get; set; }

        public string QuestionText { get; set; } = null!;

        /// <summary>MultipleChoice | TrueFalse | Essay. Drives how Flutter renders
        /// the question and which answer field it submits.</summary>
        public string QuestionType { get; set; } = null!;

        /// <summary>Optional illustration, server-relative path.</summary>
        public string? ImageUrl { get; set; }

        public string Difficulty { get; set; } = null!;

        public short DisplayOrder { get; set; }

        public byte Points { get; set; }

        public string? CurrentHint { get; set; }

        /// <summary>
        /// Empty for Essay. Exactly two entries for TrueFalse. Two or more for
        /// MultipleChoice. Never contains IsCorrect.
        /// </summary>
        public IReadOnlyList<QuizAnswerOptionDto> Options { get; set; }
            = [];
    }
}
