namespace AssessmentBL.Services.Constants 
{
    public static class QuestionDifficulties
    {
        public const string Easy = "Easy";
        public const string Medium = "Medium";
        public const string Hard = "Hard";
        public const string Advanced = "Advanced";

        // مصفوفة بتحتوي على كل المستويات عشان نستخدمها في الـ Validation
        public static readonly string[] All =
        {
            Easy,
            Medium,
            Hard,
            Advanced
        };
    }
}