using AssessmentBL.DTOs.QuizAttempt;
using AssessmentBL.Interfaces;
using AssessmentBL.Services.Constants;
using AssessmentDA.Context;
using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Assessment.AI;
using Shared.Common.Abstractions;
using Shared.Common.Exceptions;

namespace AssessmentBL.Services
{
    public class QuizAttemptService : IQuizAttemptService
    {
        private readonly AssessmentDbContext _db;
        private readonly IUserTopicStatService _userTopicStatService;
        private readonly IDateTimeProvider _clock;
        private readonly IAiHintGenerator _aiHintGenerator;
        private readonly ILogger<QuizAttemptService> _logger;

        public QuizAttemptService(
            AssessmentDbContext db,
            IUserTopicStatService userTopicStatService,
            IDateTimeProvider clock,
            IAiHintGenerator aiHintGenerator,
            ILogger<QuizAttemptService> logger)
        {
            _db = db;
            _userTopicStatService = userTopicStatService;
            _clock = clock;
            _aiHintGenerator = aiHintGenerator;
            _logger = logger;
        }

        public async Task<QuizAttemptResponseDto> StartAsync(
            int quizId,
            Guid userId,
            long? previousAttemptId = null,
            string? language = null,
            CancellationToken cancellationToken = default)
        {
            var resolvedLanguage = ContentLanguages.Normalize(language);

            var quizIsActive = await _db.Quizzes
                .AsNoTracking()
                .Where(q => q.Id == quizId)
                .Select(q => (bool?)q.IsActive)
                .FirstOrDefaultAsync(cancellationToken);

            if (quizIsActive is null)
                throw new KeyNotFoundException($"الاختبار رقم {quizId} غير موجود");

            if (quizIsActive == false)
                throw new InvalidOperationException($"الاختبار رقم {quizId} غير مفعّل");

            return previousAttemptId is null
                ? await StartFirstAttemptAsync(quizId, userId, resolvedLanguage, cancellationToken)
                : await StartRetryAttemptAsync(quizId, userId, previousAttemptId.Value, resolvedLanguage, cancellationToken);
        }

        private async Task<QuizAttemptResponseDto> StartFirstAttemptAsync(
            int quizId,
            Guid userId,
            string language,
            CancellationToken cancellationToken)
        {
            var rows = await ProjectQuestions(
                    _db.Questions.AsNoTracking().Where(q => q.QuizId == quizId && q.IsActive), language)
                .ToListAsync(cancellationToken);

            if (rows.Count == 0)
                throw new InvalidOperationException(
                    $"الاختبار رقم {quizId} لا يحتوي على أسئلة مفعّلة");

            var snapshots = await LoadQuestionSnapshotsAsync(
                rows.Select(r => r.QuestionId).ToList(), cancellationToken);

            return await PersistAttemptAsync(
                quizId, userId, previousAttemptId: null, rows, snapshots, language, cancellationToken);
        }

        private async Task<QuizAttemptResponseDto> StartRetryAttemptAsync(
            int quizId,
            Guid userId,
            long previousAttemptId,
            string language,
            CancellationToken cancellationToken)
        {
            var previousAttempt = await _db.QuizAttempts
                .AsNoTracking()
                .Where(a => a.Id == previousAttemptId)
                .Select(a => new { a.Id, a.UserId, a.QuizId, a.Status })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"المحاولة رقم {previousAttemptId} غير موجودة");

            if (previousAttempt.UserId != userId)
                throw new UnauthorizedAccessException("لا يمكنك إعادة محاولة مستخدم آخر");

            if (previousAttempt.QuizId != quizId)
                throw new InvalidOperationException(
                    $"المحاولة رقم {previousAttemptId} لا تخص الاختبار رقم {quizId}");

            if (previousAttempt.Status != QuizAttemptStatuses.Completed)
                throw new InvalidOperationException(
                    $"لا يمكن إعادة المحاولة رقم {previousAttemptId} لأنها لم تكتمل");

            var alreadyRetried = await _db.QuizAttempts
                .AsNoTracking()
                .AnyAsync(a => a.PreviousAttemptId == previousAttemptId, cancellationToken);

            if (alreadyRetried)
                throw new InvalidOperationException(
                    $"تمت إعادة المحاولة رقم {previousAttemptId} من قبل");

            // Only auto-graded questions can produce a mistake, so a retry never
            // contains an Essay by construction.
            var wrongQuestionIds = await _db.QuizAttemptMistakes
                .AsNoTracking()
                .Where(m => m.QuizAttemptId == previousAttemptId)
                .Select(m => m.QuestionId)
                .ToListAsync(cancellationToken);

            if (wrongQuestionIds.Count == 0)
                throw new InvalidOperationException(
                    $"المحاولة رقم {previousAttemptId} لا تحتوي على إجابات خاطئة لإعادتها");

            var rows = await ProjectQuestions(
                    _db.Questions.AsNoTracking().Where(q => wrongQuestionIds.Contains(q.Id)), language)
                .ToListAsync(cancellationToken);

            var latestHints = await GetLatestHintPerQuestionAsync(previousAttemptId, language, cancellationToken);

            foreach (var row in rows)
            {
                if (latestHints.TryGetValue(row.QuestionId, out var hintText))
                    row.CurrentHint = hintText;
            }

            var snapshots = await LoadQuestionSnapshotsAsync(wrongQuestionIds, cancellationToken);

            return await PersistAttemptAsync(
                quizId, userId, previousAttemptId, rows, snapshots, language, cancellationToken);
        }

        /// <summary>
        /// Reads the question fields that must be frozen for the lifetime of an
        /// attempt: its classification, its type, and its answer key. An Essay has
        /// no key, so CorrectOptionId is null for it — which the database allows
        /// only for Essay (CK_QuizAttemptQuestions_EssayHasNoKey).
        /// </summary>
        private async Task<Dictionary<int, QuestionSnapshot>> LoadQuestionSnapshotsAsync(
            IReadOnlyCollection<int> questionIds,
            CancellationToken cancellationToken)
        {
            var rows = await _db.Questions
                .AsNoTracking()
                .Where(q => questionIds.Contains(q.Id))
                .Select(q => new
                {
                    q.Id,
                    q.TopicId,
                    q.Difficulty,
                    q.QuestionType,
                    CorrectOptionId = q.QuestionOptions
                        .Where(o => o.IsCorrect)
                        .Select(o => (int?)o.Id)
                        .FirstOrDefault()
                })
                .ToListAsync(cancellationToken);

            var unanswerable = rows
                .Where(r => r.CorrectOptionId is null && QuestionTypes.IsAutoGraded(r.QuestionType))
                .Select(r => r.Id)
                .ToList();

            if (unanswerable.Count > 0)
                throw new InvalidOperationException(
                    $"لا يمكن بدء المحاولة: الأسئلة أرقام {string.Join(", ", unanswerable)} ليس لها إجابة صحيحة");

            return rows.ToDictionary(
                r => r.Id,
                r => new QuestionSnapshot(r.TopicId, r.Difficulty, r.QuestionType, r.CorrectOptionId));
        }

        private sealed record QuestionSnapshot(
            int TopicId, string Difficulty, string QuestionType, int? CorrectOptionId);

        private async Task<QuizAttemptResponseDto> PersistAttemptAsync(
            int quizId,
            Guid userId,
            long? previousAttemptId,
            IReadOnlyList<LocalizedQuestionRow> rows,
            IReadOnlyDictionary<int, QuestionSnapshot> snapshots,
            string language,
            CancellationToken cancellationToken)
        {
            var attempt = new QuizAttempt
            {
                QuizId = quizId,
                UserId = userId,
                PreviousAttemptId = previousAttemptId,
                TotalQuestionsAtAttempt = (short)rows.Count,
                QuestionsAnsweredCount = 0,
                CorrectAnswersCount = 0,
                ScorePercentage = 0m,
                Status = QuizAttemptStatuses.InProgress,
                StartedAt = _clock.UtcNow
            };

            foreach (var row in rows)
            {
                var snapshot = snapshots[row.QuestionId];

                attempt.QuizAttemptQuestions.Add(new QuizAttemptQuestion
                {
                    QuestionId = row.QuestionId,
                    TopicId = snapshot.TopicId,
                    Difficulty = snapshot.Difficulty,
                    QuestionType = snapshot.QuestionType,
                    CorrectOptionId = snapshot.CorrectOptionId
                });
            }

            _db.QuizAttempts.Add(attempt);

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (previousAttemptId is not null
                                               && ex.IsUniqueViolationOf("UQ_QuizAttempts_PreviousAttemptId"))
            {
                throw new ConflictException(
                    $"تمت إعادة المحاولة رقم {previousAttemptId} بالفعل", ex);
            }

            return new QuizAttemptResponseDto
            {
                AttemptId = attempt.Id,
                QuizId = attempt.QuizId,
                StartedAt = attempt.StartedAt,
                Language = language,
                LanguageFallbackApplied = rows.Any(r => r.UsedFallback),
                Questions = rows.Select(r => r.ToDto()).ToList()
            };
        }

        public async Task<QuizAttemptResultDto> SubmitAsync(
            long attemptId,
            Guid userId,
            SubmitQuizAttemptDto dto,
            string? language = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(dto);

            var resolvedLanguage = ContentLanguages.Normalize(language);

            var attempt = await _db.QuizAttempts
                .FirstOrDefaultAsync(a => a.Id == attemptId, cancellationToken)
                ?? throw new KeyNotFoundException($"المحاولة رقم {attemptId} غير موجودة");

            if (attempt.UserId != userId)
                throw new UnauthorizedAccessException("لا يمكنك تسليم محاولة مستخدم آخر");

            if (attempt.Status != QuizAttemptStatuses.InProgress)
                throw new InvalidOperationException(
                    $"المحاولة رقم {attemptId} تم تسليمها بالفعل");

            // The attempt-start snapshot is the answer key AND the type authority.
            var attemptQuestions = await _db.QuizAttemptQuestions
                .AsNoTracking()
                .Where(aq => aq.QuizAttemptId == attemptId)
                .Select(aq => new { aq.QuestionId, aq.CorrectOptionId, aq.QuestionType })
                .ToListAsync(cancellationToken);

            var autoGradedByQuestion = attemptQuestions
                .Where(aq => QuestionTypes.IsAutoGraded(aq.QuestionType))
                .ToDictionary(aq => aq.QuestionId, aq => aq.CorrectOptionId!.Value);

            var essayQuestionIds = attemptQuestions
                .Where(aq => !QuestionTypes.IsAutoGraded(aq.QuestionType))
                .Select(aq => aq.QuestionId)
                .ToHashSet();

            var submittedMistakes = ValidateSubmittedMistakes(dto, autoGradedByQuestion.Keys.ToHashSet());
            var submittedEssays = ValidateSubmittedEssays(dto, essayQuestionIds);

            var selectedOptionIds = submittedMistakes.Select(m => m.SelectedOptionId).ToList();

            var selectedOptions = await _db.QuestionOptions
                .AsNoTracking()
                .Where(o => selectedOptionIds.Contains(o.Id))
                .Select(o => new { o.Id, o.QuestionId })
                .ToListAsync(cancellationToken);

            var optionsById = selectedOptions.ToDictionary(o => o.Id);
            var confirmedMistakes = new List<QuizAttemptMistakeDto>(submittedMistakes.Count);

            foreach (var mistake in submittedMistakes)
            {
                if (!optionsById.TryGetValue(mistake.SelectedOptionId, out var option))
                    throw new ArgumentException(
                        $"الاختيار رقم {mistake.SelectedOptionId} غير موجود", nameof(dto));

                if (option.QuestionId != mistake.QuestionId)
                    throw new ArgumentException(
                        $"الاختيار رقم {mistake.SelectedOptionId} لا يخص السؤال رقم {mistake.QuestionId}",
                        nameof(dto));

                if (mistake.SelectedOptionId != autoGradedByQuestion[mistake.QuestionId])
                    confirmedMistakes.Add(mistake);
            }

            var totalQuestions = attempt.TotalQuestionsAtAttempt;
            var autoGradedCount = (short)autoGradedByQuestion.Count;
            var correctAnswers = (short)(autoGradedCount - confirmedMistakes.Count);

            var hints = await GenerateHintsAsync(userId, confirmedMistakes, resolvedLanguage, cancellationToken);

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            var recordedMistakes = new List<QuizAttemptMistake>(confirmedMistakes.Count);

            foreach (var mistake in confirmedMistakes)
            {
                var recorded = new QuizAttemptMistake
                {
                    QuizAttemptId = attemptId,
                    QuestionId = mistake.QuestionId,
                    SelectedOptionId = mistake.SelectedOptionId
                };

                _db.QuizAttemptMistakes.Add(recorded);
                recordedMistakes.Add(recorded);
            }

            // Essay answers are stored, never scored. They sit Pending until a
            // grader reviews them, and do not affect ScorePercentage.
            foreach (var essay in submittedEssays)
            {
                _db.QuizAttemptEssayAnswers.Add(new QuizAttemptEssayAnswer
                {
                    QuizAttemptId = attemptId,
                    QuestionId = essay.QuestionId,
                    AnswerText = essay.AnswerText.Trim(),
                    Status = EssayAnswerStatuses.Pending
                });
            }

            attempt.QuestionsAnsweredCount = totalQuestions;
            attempt.CorrectAnswersCount = correctAnswers;
            attempt.ScorePercentage = CalculateScorePercentage(correctAnswers, autoGradedCount);
            attempt.Status = QuizAttemptStatuses.Completed;
            attempt.CompletedAt = _clock.UtcNow;

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new ConflictException(
                    $"المحاولة رقم {attemptId} تم تسليمها بالفعل", ex);
            }
            catch (DbUpdateException ex) when (
                ex.IsUniqueViolationOf("UQ_QuizAttemptMistakes_AttemptId_QuestionId")
             || ex.IsUniqueViolationOf("UQ_QuizAttemptEssayAnswers_AttemptId_QuestionId"))
            {
                throw new ConflictException(
                    $"المحاولة رقم {attemptId} تم تسليمها بالفعل", ex);
            }

            await PersistHintsAsync(recordedMistakes, hints.HintTextByQuestion, resolvedLanguage, cancellationToken);

            await _userTopicStatService.UpdateAfterQuizAttemptAsync(attemptId, userId, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new QuizAttemptResultDto
            {
                AttemptId = attempt.Id,
                TotalQuestions = totalQuestions,
                AutoGradedQuestions = autoGradedCount,
                PendingEssayQuestions = (short)submittedEssays.Count,
                CorrectAnswers = correctAnswers,
                WrongAnswers = (short)confirmedMistakes.Count,
                ScorePercentage = attempt.ScorePercentage,
                Language = resolvedLanguage,
                LanguageFallbackApplied = hints.UsedFallback,
                RetryQuestions = hints.RetryQuestions
            };
        }

        private async Task<GeneratedHints> GenerateHintsAsync(
            Guid userId,
            IReadOnlyList<QuizAttemptMistakeDto> mistakes,
            string language,
            CancellationToken cancellationToken)
        {
            if (mistakes.Count == 0)
                return GeneratedHints.None(language);

            var questionIds = mistakes.Select(m => m.QuestionId).ToList();
            var selectedOptionIds = mistakes.Select(m => m.SelectedOptionId).ToList();

            var rows = await ProjectQuestions(
                    _db.Questions.AsNoTracking().Where(q => questionIds.Contains(q.Id)), language)
                .ToListAsync(cancellationToken);

            var options = await _db.QuestionOptions
                .AsNoTracking()
                .Where(o => selectedOptionIds.Contains(o.Id))
                .Select(o => new
                {
                    o.Id,
                    OptionText = o.QuestionOptionTranslations
                            .Where(t => t.LanguageCode == language)
                            .Select(t => t.OptionText)
                            .FirstOrDefault()
                        ?? o.QuestionOptionTranslations
                            .Where(t => t.LanguageCode == ContentLanguages.Fallback)
                            .Select(t => t.OptionText)
                            .FirstOrDefault()
                        ?? o.OptionText,
                    // Needed so an image-only option can still be described to
                    // the AI, which cannot see the image itself.
                    o.ImageDescription
                })
                .ToDictionaryAsync(o => o.Id, cancellationToken);

            // Previous hints in the SAME language — an Arabic hint is not context
            // for an English one.
            var previousHints = await _db.QuestionHints
                .AsNoTracking()
                .Where(h => questionIds.Contains(h.QuizAttemptMistake.QuestionId)
                         && h.QuizAttemptMistake.QuizAttempt.UserId == userId
                         && h.LanguageCode == language)
                .OrderBy(h => h.QuizAttemptMistake.QuizAttemptId)
                .ThenBy(h => h.HintSequence)
                .Select(h => new { h.QuizAttemptMistake.QuestionId, h.HintText })
                .ToListAsync(cancellationToken);

            var hintsByQuestion = previousHints
                .GroupBy(h => h.QuestionId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(h => h.HintText).ToList());
            var rowsById = rows.ToDictionary(r => r.QuestionId);

            // Resolve the AI-visible semantic text for each mistake. A question or
            // option carried by an image contributes its admin-authored
            // description instead — the image itself is never sent.
            var hintable = new List<GenerateHintQuestion>(mistakes.Count);

            foreach (var m in mistakes)
            {
                var row = rowsById[m.QuestionId];
                var option = options[m.SelectedOptionId];

                var questionSemanticText = ResolveQuestionSemanticText(row.QuestionText, row.ImageDescription);
                var answerSemanticText = ResolveOptionSemanticText(option.OptionText, option.ImageDescription);

                // Never send an empty answer. Without a description an image-only
                // option is uninterpretable, so the question is excluded rather
                // than asking the AI to explain nothing — and it is reported.
                if (answerSemanticText is null)
                {
                    _logger.LogWarning(
                        "Skipping hint generation for question {QuestionId}: selected option {OptionId} "
                      + "has no text and no ImageDescription, so the AI cannot interpret the child's answer. "
                      + "An admin must supply ImageDescription for that option.",
                        m.QuestionId, m.SelectedOptionId);
                    continue;
                }

                if (questionSemanticText is null)
                {
                    _logger.LogWarning(
                        "Skipping hint generation for question {QuestionId}: the question has neither text "
                      + "nor an ImageDescription, so the AI has no question context.",
                        m.QuestionId);
                    continue;
                }

                hintable.Add(new GenerateHintQuestion
                {
                    QuestionId = m.QuestionId,
                    QuestionText = questionSemanticText,
                    WrongOptionText = answerSemanticText,
                    PreviousHints = hintsByQuestion.TryGetValue(m.QuestionId, out var history)
                        ? history
                        : []
                });
            }

            if (hintable.Count == 0)
                return new GeneratedHints(
                    new Dictionary<int, string>(),
                    BuildRetryQuestions(rows, new Dictionary<int, string>()),
                    language,
                    rows.Any(r => r.UsedFallback));

            var request = new GenerateHintsRequest
            {
                Language = language,
                Questions = hintable
            };

            var response = await _aiHintGenerator.GenerateHintsAsync(request, cancellationToken);

            // Validate against what was actually ASKED, not every mistake — a
            // question skipped above must not make the response look invalid.
            var expectedIds = hintable.Select(q => q.QuestionId).ToHashSet();
            var generatedByQuestion = response.Hints
                .Where(h => expectedIds.Contains(h.QuestionId))
                .GroupBy(h => h.QuestionId)
                .ToDictionary(g => g.Key, g => g.Single().HintText.Trim());

            if (generatedByQuestion.Count != expectedIds.Count
                || generatedByQuestion.Values.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("The AI response did not contain one valid hint per wrong question");

            return new GeneratedHints(
                generatedByQuestion,
                BuildRetryQuestions(rows, generatedByQuestion),
                language,
                rows.Any(r => r.UsedFallback));
        }

        /// <summary>
        /// A question skipped for hint generation still belongs in the retry set —
        /// it just carries no hint, so CurrentHint stays null.
        /// </summary>
        private static List<QuizQuestionForAttemptDto> BuildRetryQuestions(
            IReadOnlyList<LocalizedQuestionRow> rows,
            IReadOnlyDictionary<int, string> hintTextByQuestion) =>
            rows
                .OrderBy(r => r.DisplayOrder)
                .Select(r =>
                {
                    var dto = r.ToDto();
                    dto.CurrentHint = hintTextByQuestion.TryGetValue(r.QuestionId, out var hint)
                        ? hint
                        : null;
                    return dto;
                })
                .ToList();

        /// <summary>
        /// The AI-visible text for a question. Text-only questions are unchanged.
        /// A question carried by an image contributes its admin-authored
        /// description; when both exist the description is appended, because a
        /// question image usually holds information the text refers to.
        /// Returns null when neither is available.
        /// </summary>
        private static string? ResolveQuestionSemanticText(string? questionText, string? imageDescription)
        {
            var text = string.IsNullOrWhiteSpace(questionText) ? null : questionText.Trim();
            var description = string.IsNullOrWhiteSpace(imageDescription) ? null : imageDescription.Trim();

            if (text is not null && description is not null)
                return $"{text}\n{description}";

            return text ?? description;
        }

        /// <summary>
        /// The AI-visible text for the option the child selected. Text options are
        /// unchanged. An image-only option contributes its admin-authored
        /// description. Returns null when the option has neither — the caller
        /// must then skip the question rather than send an empty answer.
        /// </summary>
        private static string? ResolveOptionSemanticText(string? optionText, string? imageDescription)
        {
            var text = string.IsNullOrWhiteSpace(optionText) ? null : optionText.Trim();
            if (text is not null)
                return text;

            return string.IsNullOrWhiteSpace(imageDescription) ? null : imageDescription.Trim();
        }

        private async Task PersistHintsAsync(
            IReadOnlyList<QuizAttemptMistake> recordedMistakes,
            IReadOnlyDictionary<int, string> hintTextByQuestion,
            string language,
            CancellationToken cancellationToken)
        {
            if (recordedMistakes.Count == 0)
                return;

            var mistakeIds = recordedMistakes.Select(m => m.Id).ToList();

            // Sequence numbering is per (mistake, language) — mirrors
            // UQ_QuestionHints_MistakeId_Language_Sequence.
            var nextSequences = await _db.QuestionHints
                .Where(h => mistakeIds.Contains(h.QuizAttemptMistakeId) && h.LanguageCode == language)
                .GroupBy(h => h.QuizAttemptMistakeId)
                .ToDictionaryAsync(g => g.Key, g => (byte)(g.Max(h => h.HintSequence) + 1), cancellationToken);

            var persistedHints = recordedMistakes.Select(m => new QuestionHint
            {
                QuizAttemptMistakeId = m.Id,
                HintText = hintTextByQuestion[m.QuestionId],
                LanguageCode = language,
                HintSequence = nextSequences.TryGetValue(m.Id, out var sequence) ? sequence : (byte)1
            }).ToList();

            _db.QuestionHints.AddRange(persistedHints);
            await _db.SaveChangesAsync(cancellationToken);
        }

        private sealed record GeneratedHints(
            IReadOnlyDictionary<int, string> HintTextByQuestion,
            List<QuizQuestionForAttemptDto> RetryQuestions,
            string Language,
            bool UsedFallback)
        {
            public static GeneratedHints None(string language) =>
                new(new Dictionary<int, string>(), [], language, false);
        }

        public async Task<QuizAttemptResponseDto> GetByIdAsync(
            long attemptId,
            Guid userId,
            string? language = null,
            CancellationToken cancellationToken = default)
        {
            var resolvedLanguage = ContentLanguages.Normalize(language);

            var attempt = await _db.QuizAttempts
                .AsNoTracking()
                .Where(a => a.Id == attemptId)
                .Select(a => new { a.Id, a.QuizId, a.UserId, a.StartedAt })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"المحاولة رقم {attemptId} غير موجودة");

            if (attempt.UserId != userId)
                throw new UnauthorizedAccessException("لا يمكنك الوصول إلى محاولة مستخدم آخر");

            var rows = await ProjectQuestions(
                    _db.QuizAttemptQuestions
                        .AsNoTracking()
                        .Where(aq => aq.QuizAttemptId == attemptId)
                        .Select(aq => aq.Question),
                    resolvedLanguage)
                .ToListAsync(cancellationToken);

            var latestHints = await GetLatestHintPerQuestionAsync(attemptId, resolvedLanguage, cancellationToken);

            foreach (var row in rows)
            {
                if (latestHints.TryGetValue(row.QuestionId, out var hintText))
                    row.CurrentHint = hintText;
            }

            return new QuizAttemptResponseDto
            {
                AttemptId = attempt.Id,
                QuizId = attempt.QuizId,
                StartedAt = attempt.StartedAt,
                Language = resolvedLanguage,
                LanguageFallbackApplied = rows.Any(r => r.UsedFallback),
                Questions = rows.Select(r => r.ToDto()).ToList()
            };
        }

        private static List<QuizAttemptMistakeDto> ValidateSubmittedMistakes(
            SubmitQuizAttemptDto dto,
            HashSet<int> autoGradedQuestionIds)
        {
            var mistakes = dto.Mistakes ?? new List<QuizAttemptMistakeDto>();
            var seenQuestionIds = new HashSet<int>(mistakes.Count);

            foreach (var mistake in mistakes)
            {
                if (mistake is null)
                    throw new ArgumentException("قائمة الأخطاء تحتوي على عنصر فارغ", nameof(dto));

                if (!seenQuestionIds.Add(mistake.QuestionId))
                    throw new ArgumentException(
                        $"السؤال رقم {mistake.QuestionId} مكرر في قائمة الأخطاء", nameof(dto));

                if (!autoGradedQuestionIds.Contains(mistake.QuestionId))
                    throw new ArgumentException(
                        $"السؤال رقم {mistake.QuestionId} لا يخص هذه المحاولة أو لا يُصحّح تلقائيًا", nameof(dto));
            }

            return mistakes.ToList();
        }

        private static List<QuizAttemptEssayAnswerDto> ValidateSubmittedEssays(
            SubmitQuizAttemptDto dto,
            HashSet<int> essayQuestionIds)
        {
            var essays = dto.EssayAnswers ?? new List<QuizAttemptEssayAnswerDto>();
            var seenQuestionIds = new HashSet<int>(essays.Count);

            foreach (var essay in essays)
            {
                if (essay is null)
                    throw new ArgumentException("قائمة الإجابات المقالية تحتوي على عنصر فارغ", nameof(dto));

                if (!seenQuestionIds.Add(essay.QuestionId))
                    throw new ArgumentException(
                        $"السؤال رقم {essay.QuestionId} مكرر في قائمة الإجابات المقالية", nameof(dto));

                if (!essayQuestionIds.Contains(essay.QuestionId))
                    throw new ArgumentException(
                        $"السؤال رقم {essay.QuestionId} ليس سؤالًا مقاليًا في هذه المحاولة", nameof(dto));

                if (string.IsNullOrWhiteSpace(essay.AnswerText))
                    throw new ArgumentException(
                        $"إجابة السؤال رقم {essay.QuestionId} فارغة", nameof(dto));
            }

            return essays.ToList();
        }

        /// <summary>
        /// Score is computed over auto-graded questions only. Essay answers are
        /// stored for review and never enter the numerator or the denominator.
        /// </summary>
        private static decimal CalculateScorePercentage(short correctAnswers, short autoGradedQuestions)
        {
            if (autoGradedQuestions <= 0)
                return 0m;

            return Math.Round(correctAnswers * 100m / autoGradedQuestions, 2, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Latest hint per question in the requested language. When a question has
        /// no hint in that language (the child switched language mid-chain), the
        /// latest hint in any language is returned rather than nothing.
        /// </summary>
        private async Task<Dictionary<int, string>> GetLatestHintPerQuestionAsync(
            long quizAttemptId,
            string language,
            CancellationToken cancellationToken)
        {
            var hints = await _db.QuestionHints
                .AsNoTracking()
                .Where(h => h.QuizAttemptMistake.QuizAttemptId == quizAttemptId)
                .Select(h => new
                {
                    h.QuizAttemptMistake.QuestionId,
                    h.HintSequence,
                    h.HintText,
                    h.LanguageCode
                })
                .ToListAsync(cancellationToken);

            return hints
                .GroupBy(h => h.QuestionId)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var inLanguage = g.Where(h => h.LanguageCode == language).ToList();
                        var pool = inLanguage.Count > 0 ? inLanguage : g.ToList();
                        return pool.OrderByDescending(h => h.HintSequence).First().HintText;
                    });
        }

        /// <summary>
        /// Localized projection. Text resolves requested → fallback → base column,
        /// per field, and records whether the requested translation was missing so
        /// the response can tell the client.
        /// </summary>
        private static IQueryable<LocalizedQuestionRow> ProjectQuestions(
            IQueryable<Question> questions, string language) =>
            questions
                .OrderBy(q => q.DisplayOrder)
                .Select(q => new LocalizedQuestionRow
                {
                    QuestionId = q.Id,
                    QuestionText =
                        q.QuestionTranslations
                            .Where(t => t.LanguageCode == language)
                            .Select(t => t.QuestionText)
                            .FirstOrDefault()
                        ?? q.QuestionTranslations
                            .Where(t => t.LanguageCode == ContentLanguages.Fallback)
                            .Select(t => t.QuestionText)
                            .FirstOrDefault()
                        ?? q.QuestionText,
                    HasRequestedTranslation =
                        q.QuestionTranslations.Any(t => t.LanguageCode == language),
                    QuestionType = q.QuestionType,
                    ImageUrl = q.ImageUrl,
                    // Admin-only metadata. Carried on the INTERNAL row so the AI
                    // can read it; deliberately not copied by ToDto().
                    ImageDescription = q.ImageDescription,
                    Difficulty = q.Difficulty,
                    DisplayOrder = q.DisplayOrder,
                    Points = q.Points,
                    CurrentHint = null,
                    // Essay questions simply have no option rows, so this comes back empty.
                    Options = q.QuestionOptions
                        .OrderBy(o => o.DisplayOrder)
                        .Select(o => new LocalizedOptionRow
                        {
                            OptionId = o.Id,
                            OptionText =
                                o.QuestionOptionTranslations
                                    .Where(t => t.LanguageCode == language)
                                    .Select(t => t.OptionText)
                                    .FirstOrDefault()
                                ?? o.QuestionOptionTranslations
                                    .Where(t => t.LanguageCode == ContentLanguages.Fallback)
                                    .Select(t => t.OptionText)
                                    .FirstOrDefault()
                                ?? o.OptionText,
                            ImageUrl = o.ImageUrl,
                            ImageDescription = o.ImageDescription,
                            DisplayOrder = o.DisplayOrder,
                            HasRequestedTranslation =
                                o.QuestionOptionTranslations.Any(t => t.LanguageCode == language)
                        })
                        .ToList()
                });

        private sealed class LocalizedQuestionRow
        {
            public int QuestionId { get; set; }
            public string QuestionText { get; set; } = null!;
            public bool HasRequestedTranslation { get; set; }
            public string QuestionType { get; set; } = null!;
            public string? ImageUrl { get; set; }
            /// <summary>Admin-only. Never copied into a child-facing DTO.</summary>
            public string? ImageDescription { get; set; }
            public string Difficulty { get; set; } = null!;
            public short DisplayOrder { get; set; }
            public byte Points { get; set; }
            public string? CurrentHint { get; set; }
            public List<LocalizedOptionRow> Options { get; set; } = [];

            public bool UsedFallback =>
                !HasRequestedTranslation || Options.Any(o => !o.HasRequestedTranslation);

            public QuizQuestionForAttemptDto ToDto() => new()
            {
                QuestionId = QuestionId,
                QuestionText = QuestionText,
                QuestionType = QuestionType,
                ImageUrl = ImageUrl,
                Difficulty = Difficulty,
                DisplayOrder = DisplayOrder,
                Points = Points,
                CurrentHint = CurrentHint,
                Options = Options
                    .Select(o => new QuizAnswerOptionDto
                    {
                        OptionId = o.OptionId,
                        OptionText = o.OptionText,
                        ImageUrl = o.ImageUrl,
                        DisplayOrder = o.DisplayOrder
                    })
                    .ToList()
            };
        }

        private sealed class LocalizedOptionRow
        {
            public int OptionId { get; set; }
            public string? OptionText { get; set; }
            public string? ImageUrl { get; set; }
            /// <summary>Admin-only. Never copied into a child-facing DTO.</summary>
            public string? ImageDescription { get; set; }
            public short DisplayOrder { get; set; }
            public bool HasRequestedTranslation { get; set; }
        }
    }
}
