using AssessmentBL.DTOs.QuizAttempt;

namespace AssessmentBL.Interfaces
{
    public interface IQuizAttemptService
    {
        /// <summary>
        /// Starts a new quiz attempt.
        /// If previousAttemptId is null, this is the first attempt
        /// and all quiz questions are returned without hints.
        /// If previousAttemptId is provided, this is a retry attempt
        /// and only the previously incorrect questions are returned
        /// with their current AI-generated hints.
        /// </summary>
        Task<QuizAttemptResponseDto> StartAsync(
            int quizId,
            Guid userId,
            long? previousAttemptId = null,
            string? language = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Submits the entire quiz attempt in bulk.
        /// Receives the questions answered incorrectly by the user,
        /// calculates the final score, completes the attempt,
        /// and updates the user's topic statistics.
        /// </summary>
        Task<QuizAttemptResultDto> SubmitAsync(
            long attemptId,
            Guid userId,
            SubmitQuizAttemptDto dto,
            string? language = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves the details of a previous quiz attempt
        /// for the specified user.
        /// </summary>
        Task<QuizAttemptResponseDto> GetByIdAsync(
            long attemptId,
            Guid userId,
            string? language = null,
            CancellationToken cancellationToken = default);
    }
}