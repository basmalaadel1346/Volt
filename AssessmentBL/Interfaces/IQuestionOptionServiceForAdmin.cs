using AssessmentBL.DTOs.Question;
using AssessmentBL.DTOs.QuestionOption;

namespace AssessmentBL.Interfaces
{
    public interface IQuestionOptionServiceForAdmin
    {
        Task<IReadOnlyList<AdminQuestionOptionResponseDto>> GetByQuestionIdAsync(
            int questionId, CancellationToken cancellationToken = default);

        Task<AdminQuestionOptionResponseDto> CreateOptionAsync(
            CreateQuestionOptionDto request, CancellationToken cancellationToken = default);

        Task<AdminQuestionOptionResponseDto> UpdateOptionAsync(
            int optionId,
            UpdateQuestionOptionDto request, CancellationToken cancellationToken = default);

        Task DeleteOptionAsync(int optionId, CancellationToken cancellationToken = default);
    }
}