using AssessmentBL.DTOs.Question;

namespace AssessmentBL.Interfaces
{
    public interface IQuestionServiceForAdmin
    {
        Task<IReadOnlyList<AdminQuestionResponseDto>> GetByQuizIdAsync(int quizId, CancellationToken cancellationToken = default);

        Task<AdminQuestionResponseDto> GetByQuestionIdAsync(int questionId, CancellationToken cancellationToken = default);

        Task<AdminQuestionResponseDto> CreateQuestionAsync(
            CreateQuestionDto request, CancellationToken cancellationToken = default);

        Task<AdminQuestionResponseDto> UpdateQuestionAsync(
            int questionId,
            UpdateQuestionDto request, CancellationToken cancellationToken = default);

        Task SetActiveAsync(int questionId, bool isActive, CancellationToken cancellationToken = default);
    }
}