using AssessmentBL.DTOs.Quiz;
using AssessmentBL.DTOs.Quiz.Common;
namespace AssessmentBL.Interfaces
{
    public interface IQuizService
    {
        Task<QuizResponseDto> GetByIdAsync(int quizId, CancellationToken cancellationToken = default);

        Task<PagedResult<QuizResponseDto>> GetAsync(QuizFilterDto filter, CancellationToken cancellationToken = default);

        Task<QuizResponseDto> CreateAsync(CreateQuizDto request, CancellationToken cancellationToken = default);

        Task<QuizResponseDto> UpdateAsync(int quizId, UpdateQuizDto request, CancellationToken cancellationToken = default);

        Task SetActiveAsync(int quizId, bool isActive, CancellationToken cancellationToken = default);
    }
}