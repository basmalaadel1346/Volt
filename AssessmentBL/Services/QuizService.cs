using AssessmentBL.Services.Constants;
using AssessmentBL.DTOs.Quiz;
using AssessmentBL.DTOs.Quiz.Common;
using AssessmentBL.Interfaces;
using AssessmentDA.Context;
using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Abstractions;
using System.Linq.Expressions;

namespace AssessmentBL.Services
{
    public class QuizService : IQuizService
    {
        private const int MaxPageSize = 100;

        // Kept as an Expression so EF Core translates it into a SQL column
        // list instead of materializing whole Quiz entities client-side.
        private static readonly Expression<Func<Quiz, QuizResponseDto>> ProjectToResponse =
            quiz => new QuizResponseDto
            {
                Id = quiz.Id,
                Title = quiz.Title,
                Description = quiz.Description,
                QuizType = quiz.QuizType,
                LevelId = quiz.LevelId,
                LessonId = quiz.LessonId,
                IsActive = quiz.IsActive,
                CreatedAt = quiz.CreatedAt,
                UpdatedAt = quiz.UpdatedAt
            };

        private readonly AssessmentDbContext _db;
        private readonly IDateTimeProvider _clock;

        public QuizService(AssessmentDbContext db, IDateTimeProvider clock)
        {
            _db = db;
            _clock = clock;
        }

        public async Task<QuizResponseDto> GetByIdAsync(int quizId, CancellationToken cancellationToken = default)
        {
            var quiz = await _db.Quizzes
                .AsNoTracking()
                .Where(q => q.Id == quizId)
                .Select(ProjectToResponse)
                .FirstOrDefaultAsync(cancellationToken: cancellationToken);

            return quiz ?? throw new KeyNotFoundException($"الاختبار رقم {quizId} غير موجود");
        }

        public async Task<PagedResult<QuizResponseDto>> GetAsync(QuizFilterDto filter, CancellationToken cancellationToken = default)
        {
            var pageNumber = filter.PageNumber < 1 ? 1 : filter.PageNumber;
            var pageSize = filter.PageSize < 1 ? 1
                : filter.PageSize > MaxPageSize ? MaxPageSize
                : filter.PageSize;

            var query = _db.Quizzes.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(filter.QuizType))
                query = query.Where(q => q.QuizType == filter.QuizType);

            if (filter.LevelId.HasValue)
                query = query.Where(q => q.LevelId == filter.LevelId.Value);

            if (filter.LessonId.HasValue)
                query = query.Where(q => q.LessonId == filter.LessonId.Value);

            if (filter.IsActive.HasValue)
                query = query.Where(q => q.IsActive == filter.IsActive.Value);

            var totalCount = await query.CountAsync(cancellationToken: cancellationToken);

            // Ordering by the clustered PK keeps paging deterministic and
            // lets SQL Server serve the OFFSET/FETCH straight from the index.
            var items = await query
                .OrderByDescending(q => q.Id)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(ProjectToResponse)
                .ToListAsync(cancellationToken: cancellationToken);

            return new PagedResult<QuizResponseDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<QuizResponseDto> CreateAsync(CreateQuizDto request, CancellationToken cancellationToken = default)
        {
            var title = NormalizeTitle(request.Title);
            var quizType = string.IsNullOrWhiteSpace(request.QuizType)
                ? QuizTypes.Standalone
                : request.QuizType;

            ValidateQuizType(quizType);
            ValidateTypeMatchesReference(quizType, request.LevelId, request.LessonId);

            var quiz = new Quiz
            {
                Title = title,
                Description = request.Description,
                QuizType = quizType,
                LevelId = request.LevelId,
                LessonId = request.LessonId,
                // Set explicitly: the DB default is 1, but `false` is also the
                // CLR default for bool, so EF cannot tell "unset" from
                // "deliberately inactive" on insert.
                IsActive = true,
                CreatedAt = _clock.UtcNow
            };

            _db.Quizzes.Add(quiz);
            await _db.SaveChangesAsync(cancellationToken: cancellationToken);

            return ToResponse(quiz);
        }

        public async Task<QuizResponseDto> UpdateAsync(int quizId, UpdateQuizDto request, CancellationToken cancellationToken = default)
        {
            var quiz = await _db.Quizzes.FirstOrDefaultAsync(q => q.Id == quizId, cancellationToken: cancellationToken)
                ?? throw new KeyNotFoundException($"الاختبار رقم {quizId} غير موجود");

            // QuizType / LevelId / LessonId are absent from UpdateQuizDto by
            // design — a quiz cannot be re-pointed at a different level or
            // lesson after creation, which also keeps
            // CK_Quizzes_TypeMatchesReference satisfied.
            quiz.Title = NormalizeTitle(request.Title);
            quiz.Description = request.Description;
            quiz.IsActive = request.IsActive;
            quiz.UpdatedAt = _clock.UtcNow;

            await _db.SaveChangesAsync(cancellationToken: cancellationToken);

            return ToResponse(quiz);
        }

        public async Task SetActiveAsync(int quizId, bool isActive, CancellationToken cancellationToken = default)
        {
            var quiz = await _db.Quizzes.FirstOrDefaultAsync(q => q.Id == quizId, cancellationToken: cancellationToken)
                ?? throw new KeyNotFoundException($"الاختبار رقم {quizId} غير موجود");

            if (quiz.IsActive == isActive)
                return;

            quiz.IsActive = isActive;
            quiz.UpdatedAt = _clock.UtcNow;

            await _db.SaveChangesAsync(cancellationToken: cancellationToken);
        }

        private static string NormalizeTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("عنوان الاختبار مطلوب", nameof(title));

            var trimmed = title.Trim();
            if (trimmed.Length > 300)
                throw new ArgumentException("عنوان الاختبار لا يتجاوز 300 حرف", nameof(title));

            return trimmed;
        }

        private static void ValidateQuizType(string quizType)
        {
            if (!QuizTypes.All.Contains(quizType))
                throw new ArgumentException($"نوع الاختبار '{quizType}' غير صالح", nameof(quizType));
        }

        // Mirrors CK_Quizzes_TypeMatchesReference. Validated here so the caller
        // gets a readable message instead of a raw SQL constraint violation.
        private static void ValidateTypeMatchesReference(string quizType, int? levelId, int? lessonId)
        {
            var valid = quizType switch
            {
                QuizTypes.LevelAssessment => levelId.HasValue && !lessonId.HasValue,
                QuizTypes.LessonQuiz or QuizTypes.LessonReview => lessonId.HasValue && !levelId.HasValue,
                QuizTypes.Standalone => !levelId.HasValue && !lessonId.HasValue,
                _ => false
            };

            if (!valid)
                throw new ArgumentException(
                    $"نوع الاختبار '{quizType}' لا يتوافق مع LevelId/LessonId المرسلة");
        }

        private static QuizResponseDto ToResponse(Quiz quiz) => new()
        {
            Id = quiz.Id,
            Title = quiz.Title,
            Description = quiz.Description,
            QuizType = quiz.QuizType,
            LevelId = quiz.LevelId,
            LessonId = quiz.LessonId,
            IsActive = quiz.IsActive,
            CreatedAt = quiz.CreatedAt,
            UpdatedAt = quiz.UpdatedAt
        };
    }
}
