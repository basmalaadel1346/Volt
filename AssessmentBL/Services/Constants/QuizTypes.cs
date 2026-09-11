namespace AssessmentBL.Services.Constants
{
    public static class QuizTypes
    {
        public const string LevelAssessment = "LevelAssessment";
        public const string LessonQuiz = "LessonQuiz";
        public const string LessonReview = "LessonReview";
        public const string Standalone = "Standalone";

        // مصفوفة بتحتوي على كل الأنواع عشان نستخدمها في الـ Validation
        // في سطر: QuizTypes.All.Contains(quizType)
        public static readonly string[] All =
        {
            LevelAssessment,
            LessonQuiz,
            LessonReview,
            Standalone
        };
    }
}