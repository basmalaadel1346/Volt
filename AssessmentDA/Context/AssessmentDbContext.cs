using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;

namespace AssessmentDA.Context;

public partial class AssessmentDbContext : DbContext
{
    public AssessmentDbContext()
    {
    }

    public AssessmentDbContext(DbContextOptions<AssessmentDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Category> Categories { get; set; }

    public virtual DbSet<Question> Questions { get; set; }

    public virtual DbSet<QuestionHint> QuestionHints { get; set; }

    public virtual DbSet<QuestionOption> QuestionOptions { get; set; }

    public virtual DbSet<Quiz> Quizzes { get; set; }

    public virtual DbSet<QuizAttempt> QuizAttempts { get; set; }

    public virtual DbSet<QuizAttemptMistake> QuizAttemptMistakes { get; set; }

    public virtual DbSet<Topic> Topics { get; set; }

    public virtual DbSet<UserTopicStat> UserTopicStats { get; set; }
    public virtual DbSet<QuizAttemptQuestion> QuizAttemptQuestions { get; set; }

    public virtual DbSet<QuizAttemptEssayAnswer> QuizAttemptEssayAnswers { get; set; }

    public virtual DbSet<Language> Languages { get; set; }

    public virtual DbSet<QuizTranslation> QuizTranslations { get; set; }

    public virtual DbSet<QuestionTranslation> QuestionTranslations { get; set; }

    public virtual DbSet<QuestionOptionTranslation> QuestionOptionTranslations { get; set; }

    public virtual DbSet<TopicTranslation> TopicTranslations { get; set; }

    public virtual DbSet<CategoryTranslation> CategoryTranslations { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AssessmentDbContext).Assembly);

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}