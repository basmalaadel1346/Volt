namespace AssessmentBL.Services.Constants
{
    /// <summary>
    /// Content languages. The database owns the authoritative list
    /// (Assessment.Languages); these are the two the API currently accepts.
    /// </summary>
    public static class ContentLanguages
    {
        public const string English = "en";
        public const string Arabic = "ar";

        /// <summary>
        /// Used when the client asks for nothing. Arabic, because that is the
        /// app's primary audience and the language the existing content is in.
        /// </summary>
        public const string Default = Arabic;

        /// <summary>
        /// Second step of the fallback chain: requested → Fallback → base column.
        /// </summary>
        public const string Fallback = English;

        public static readonly IReadOnlyList<string> Supported = [English, Arabic];

        /// <summary>
        /// Normalizes anything a client sends ("EN", "ar-EG", null) to a supported
        /// code, falling back to <see cref="Default"/> rather than erroring — a
        /// bad language header must never fail a quiz submission.
        /// </summary>
        public static string Normalize(string? requested)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return Default;

            var code = requested.Trim().ToLowerInvariant();

            // Accept "ar-EG", "en-US" and similar by taking the primary subtag.
            var dash = code.IndexOf('-');
            if (dash > 0)
                code = code[..dash];

            return Supported.Contains(code) ? code : Default;
        }
    }
}
