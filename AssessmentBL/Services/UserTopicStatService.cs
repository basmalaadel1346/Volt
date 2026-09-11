using AssessmentBL.DTOs.UserTopicStat;
using AssessmentBL.Interfaces;
using AssessmentDA.Context;
using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Abstractions;
using System.Linq.Expressions;
using AssessmentBL.Services.Constants;

namespace AssessmentBL.Services
{
    public class UserTopicStatService : IUserTopicStatService
    {
        private static readonly Expression<Func<UserTopicStat, UserTopicStatResponseDto>> ProjectToResponse =
            stat => new UserTopicStatResponseDto
            {
                Id = stat.Id,
                UserId = stat.UserId,
                TopicId = stat.TopicId,
                Difficulty = stat.Difficulty,
                QuestionsAnsweredCount = stat.QuestionsAnsweredCount,
                CorrectCount = stat.CorrectCount,
                // Computed persisted column; the CLR property is nullable only
                // because the scaffolder made it so.
                WrongCount = stat.WrongCount ?? 0,
                HintsUsedCount = stat.HintsUsedCount,
                LastQuizAttemptId = stat.LastQuizAttemptId,
                LastPracticedAt = stat.LastPracticedAt,
                UpdatedAt = stat.UpdatedAt
            };

        private readonly AssessmentDbContext _db;
        private readonly IDateTimeProvider _clock;

        public UserTopicStatService(AssessmentDbContext db, IDateTimeProvider clock)
        {
            _db = db;
            _clock = clock;
        }

        public async Task<IReadOnlyList<UserTopicStatResponseDto>> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            return await _db.UserTopicStats
                .AsNoTracking()
                .Where(s => s.UserId == userId)
                .OrderBy(s => s.TopicId)
                .ThenBy(s => s.Difficulty)
                .Select(ProjectToResponse)
                .ToListAsync(cancellationToken);
        }

        public async Task<UserTopicStatResponseDto?> GetByTopicAndDifficultyAsync(
            Guid userId,
            int topicId,
            string difficulty,
            CancellationToken cancellationToken = default)
        {
            // Hits UQ_UserTopicStats_UserId_TopicId_Difficulty directly.
            return await _db.UserTopicStats
                .AsNoTracking()
                .Where(s => s.UserId == userId && s.TopicId == topicId && s.Difficulty == difficulty)
                .Select(ProjectToResponse)
                .FirstOrDefaultAsync(cancellationToken);
        }

        /// <summary>
        /// Folds one completed attempt into the (UserId, TopicId, Difficulty)
        /// aggregates. Counts come from that attempt's own QuizAttemptQuestion
        /// rows, so a retry contributes only the questions it actually
        /// contained. WrongCount is never assigned — SQL Server computes it.
        /// </summary>
        public async Task UpdateAfterQuizAttemptAsync(
            long quizAttemptId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var attempt = await _db.QuizAttempts
                .AsNoTracking()
                .Where(a => a.Id == quizAttemptId)
                .Select(a => new
                {
                    a.Id,
                    a.UserId,
                    a.Status,
                    a.CompletedAt
                })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"المحاولة رقم {quizAttemptId} غير موجودة");

            if (attempt.UserId != userId)
                throw new UnauthorizedAccessException("لا يمكنك الوصول إلى محاولة مستخدم آخر");

            if (attempt.Status != QuizAttemptStatuses.Completed)
                throw new InvalidOperationException(
                    $"المحاولة رقم {quizAttemptId} لم تكتمل بعد");

            // The authoritative set of questions this attempt contained, with
            // the classification frozen at attempt start. Reading TopicId /
            // Difficulty from the live Question row here would re-attribute
            // this attempt's counters if an admin later re-classified it.
            var attemptQuestions = await _db.QuizAttemptQuestions
                .AsNoTracking()
                .Where(aq => aq.QuizAttemptId == quizAttemptId)
                .Select(aq => new
                {
                    aq.QuestionId,
                    aq.TopicId,
                    aq.Difficulty,
                    aq.QuestionType
                })
                .ToListAsync(cancellationToken);

            // Essay answers are not auto-graded, so counting them as "answered"
            // with no possible "correct" would permanently depress the child's
            // mastery for that topic. They are excluded until a grader scores them.
            attemptQuestions = attemptQuestions
                .Where(q => QuestionTypes.IsAutoGraded(q.QuestionType))
                .ToList();

            if (attemptQuestions.Count == 0)
                return;

            var hintCountsByTopicAndDifficulty = await _db.QuestionHints
                .AsNoTracking()
                .Where(h => h.QuizAttemptMistake.QuizAttempt.UserId == userId)
                .GroupBy(h => new
                {
                    h.QuizAttemptMistake.QuizAttemptQuestion.TopicId,
                    h.QuizAttemptMistake.QuizAttemptQuestion.Difficulty
                })
                .Select(g => new { g.Key.TopicId, g.Key.Difficulty, Count = g.Count() })
                .ToListAsync(cancellationToken);
            var hintCounts = hintCountsByTopicAndDifficulty.ToDictionary(
                x => (x.TopicId, x.Difficulty), x => x.Count);

            var buckets = attemptQuestions
                .GroupBy(q => new { q.TopicId, q.Difficulty })
                .Select(g => new
                {
                    g.Key.TopicId,
                    g.Key.Difficulty,
                    Answered = g.Count(),
                    Correct = g.Count(q => !_db.QuizAttemptMistakes.Any(m =>
                        m.QuizAttemptId == quizAttemptId && m.QuestionId == q.QuestionId)),
                    Hints = hintCounts.GetValueOrDefault((g.Key.TopicId, g.Key.Difficulty))
                })
                .ToList();

            var topicIds = buckets.Select(b => b.TopicId).Distinct().ToList();

            // One tracked read for every row we might touch — no per-bucket query.
            var existingStats = await _db.UserTopicStats
                .Where(s => s.UserId == userId && topicIds.Contains(s.TopicId))
                .ToListAsync(cancellationToken);

            // CK_QuizAttempts_CompletedRequiresAllAnswered guarantees a
            // completed attempt has CompletedAt, so the fallback is belt-and-braces.
            var practisedAt = attempt.CompletedAt ?? _clock.UtcNow;

            foreach (var bucket in buckets)
            {
                var stat = existingStats.FirstOrDefault(
                    s => s.TopicId == bucket.TopicId && s.Difficulty == bucket.Difficulty);

                if (stat is null)
                {
                    stat = new UserTopicStat
                    {
                        UserId = userId,
                        TopicId = bucket.TopicId,
                        Difficulty = bucket.Difficulty,
                        QuestionsAnsweredCount = 0,
                        CorrectCount = 0,
                        HintsUsedCount = 0
                    };

                    _db.UserTopicStats.Add(stat);
                    existingStats.Add(stat);
                }

                stat.QuestionsAnsweredCount += bucket.Answered;
                stat.CorrectCount += bucket.Correct;
                stat.HintsUsedCount = bucket.Hints;
                stat.LastQuizAttemptId = quizAttemptId;
                stat.LastPracticedAt = practisedAt;
                stat.UpdatedAt = practisedAt;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

    }
}
