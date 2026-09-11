using System.Runtime.CompilerServices;

// Lets the test project exercise the internal provider directly, so the exact
// outgoing HTTP request can be captured and asserted rather than assumed.
[assembly: InternalsVisibleTo("Assessment.Tests")]
