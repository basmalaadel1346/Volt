namespace AssessmentBL.Services.Constants
{
    /// <summary>Mirrors CK_Questions_QuestionType.</summary>
    public static class QuestionTypes
    {
        public const string MultipleChoice = "MultipleChoice";
        public const string TrueFalse = "TrueFalse";
        public const string Essay = "Essay";

        public static readonly IReadOnlyList<string> All = [MultipleChoice, TrueFalse, Essay];

        /// <summary>
        /// True when the backend can score the answer itself. Essay cannot —
        /// it is stored for review and excluded from the score denominator.
        /// </summary>
        public static bool IsAutoGraded(string questionType) => questionType != Essay;

        /// <summary>TrueFalse is a MultipleChoice with exactly two options.</summary>
        public static bool UsesOptions(string questionType) => questionType != Essay;
    }
}
