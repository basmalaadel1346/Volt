namespace AssessmentBL.DTOs.QuizAttempt
{
    /// <summary>One free-text answer submitted for an Essay question.</summary>
    public class QuizAttemptEssayAnswerDto
    {
        public int QuestionId { get; set; }

        public string AnswerText { get; set; } = null!;
    }
}
