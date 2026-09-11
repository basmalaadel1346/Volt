using System.Collections.Generic;

namespace AssessmentBL.DTOs.QuizAttempt
{
    public class SubmitQuizAttemptDto
    {
        /// <summary>
        /// Answers to MultipleChoice and TrueFalse questions. Send every answered
        /// question — the backend re-checks each against the frozen answer key and
        /// keeps only the genuinely wrong ones. Existing clients that send only
        /// wrong answers keep working unchanged.
        /// </summary>
        public List<QuizAttemptMistakeDto> Mistakes { get; set; } = new();

        /// <summary>
        /// Answers to Essay questions. Optional — omit or leave empty when the
        /// attempt contains no essays.
        /// </summary>
        public List<QuizAttemptEssayAnswerDto> EssayAnswers { get; set; } = new();
    }
}
