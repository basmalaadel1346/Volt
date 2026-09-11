# AI Integration — Exact Provider Request/Response Contract

**Verification basis:** every `ACTUAL` claim below was produced by running code, not read from documentation. The wire format in §3 was **captured by intercepting the real outgoing HTTP request** ([AiProviderContractTests.cs](Tests/Assessment.Tests/AiProviderContractTests.cs)); every failure mode in §9 is pinned by a passing test asserting the exception the code really throws. **52/52 tests pass.**

Two labels are used throughout and never mixed:

- **`ACTUAL`** — verified from the code as it stands on `basmala-dev`.
- **`RECOMMENDED`** — a proposal. Not built. Not tested.

---

## ⚠ Premise correction — read this first

Your brief asks me to inspect the "Anthropic client configuration", "model configuration", "output configuration", "JSON schema if used" and "fake AI implementation". I have to correct that premise before documenting anything:

```
There is NO Anthropic integration in this codebase.
```

Verified by inspection:

| Thing the brief assumes | Reality |
|---|---|
| Anthropic SDK | **Not referenced by any `.csproj`** — `grep "Anthropic" *.csproj` returns nothing |
| `ClaudeHintProvider.cs` | **Does not exist** as a file |
| `AiUnavailableException.cs` | **Does not exist** |
| System prompt | **Does not exist** — there is no prompt anywhere in the solution |
| Model configuration | **Does not exist** — `AiSettings` has no `Model` |
| `OutputConfig` / `JsonOutputFormat` / JSON schema | **Not used** — no structured output anywhere |
| Timeout configuration | **Does not exist** — `HttpClient`'s 100 s default applies |
| API key | **Does not exist** — no auth header is sent at all |
| `FakeAiHintGenerator` | **Did not exist.** I created it in this session — see §14 |

The Claude/Anthropic implementation you are thinking of is the **`RECOMMENDED` code I wrote inside `docs/TECHNICAL_AUDIT.md` (Part 12)**. It is a proposal in a markdown file. Nothing in `AIIntegration/` calls Anthropic.

**What actually exists** is a 5-file, ~90-line integration that POSTs JSON to a generic, unauthenticated HTTP endpoint whose URL is an empty string in the only populated config file. So today:

```
Any quiz submission containing a wrong answer → InvalidOperationException
  → 500 "حدث خطأ داخلي في الخادم" → the attempt is NOT completed
```

Everything below documents that reality, then says precisely what would change.

---

## 1. End-to-end AI flow — `ACTUAL`

```
Flutter
  │  POST /api/quiz-attempts/42/submit?language=ar     (JWT bearer)
  │  { "mistakes":[…], "essayAnswers":[…] }
  ▼
QuizAttemptController.Submit                          ElectroWorld API
  │  userId  ← JWT "sub" claim          (never from the body)
  │  language ← ?language= or Accept-Language, normalized
  ▼
QuizAttemptService.SubmitAsync                        Assessment module
  │  1. load attempt (tracked, carries RowVersion)
  │  2. ownership: attempt.UserId != userId → 403
  │  3. status != InProgress → 400
  │  4. load the frozen snapshot (CorrectOptionId, QuestionType) per question
  │  5. validate every submitted option; grade against the SNAPSHOT
  │  6. build GenerateHintsRequest — localized question + wrong-option text,
  │     previous hints filtered to the SAME language
  ▼
IAiHintGenerator  →  AiHintGenerator                  Shared contract → AIIntegration
  │  guard: Questions.Count == 0 → ArgumentException (no HTTP call)
  ▼
IExternalAiProvider → HttpExternalAiProvider
  │  guard: HintsEndpoint blank → InvalidOperationException  ← FIRES TODAY
  │  HttpClient.PostAsJsonAsync(HintsEndpoint, request, ct)
  ▼
❌ NOT Anthropic — a generic HTTP endpoint. No key, no model, no prompt.
  ▼
Response → EnsureSuccessStatusCode() → ReadFromJsonAsync<GenerateHintsResponse>()
  ▼
QuizAttemptService validates: exactly one non-blank hint per wrong question,
  unexpected questionIds dropped; otherwise InvalidOperationException
  ▼
BEGIN TRANSACTION                                     ← AI already finished
  INSERT QuizAttemptMistakes · QuizAttemptEssayAnswers
  UPDATE QuizAttempts → Completed   (RowVersion guard → 409 on a race)
  INSERT QuestionHints (with LanguageCode)
  UPDATE UserTopicStats
COMMIT
  ▼
QuizAttemptResultDto → Flutter (score + retryQuestions[].currentHint)
```

**The AI call is outside every database transaction** — verified: `BeginTransactionAsync` is called *after* `GenerateHintsAsync` returns. But it runs *before* the commit, which is the availability problem in §9.

---

## 2. Backend → AI endpoint — `ACTUAL`

| Property | Value |
|---|---|
| Method | **POST** (from `PostAsJsonAsync`) |
| URL | **whatever `Ai:HintsEndpoint` contains — currently `""`** |
| API version | none — no versioning concept exists |
| Authentication | **NONE.** No `Authorization`, no `x-api-key`, no header of any kind |
| `Content-Type` | `application/json; charset=utf-8` |
| Timeout | `HttpClient` default — **100 seconds**, not configured |
| Retry | `HttpClient` default — **none** |

Captured from the intercepted request:

```
METHOD : POST
URL    : https://ai.example.internal/v1/hints      (the value under test)
CTYPE  : application/json; charset=utf-8
AUTH   : <none>
```

> `AUTH : <none>` is asserted, not observed in passing: the test explicitly checks `Headers.Authorization is null` and that no `x-api-key` header is present.

---

## 3. Exact AI request — `ACTUAL`, captured from the wire

The body is `GenerateHintsRequest` serialized by `PostAsJsonAsync`, which uses `JsonSerializerDefaults.Web` → **camelCase**. This is the byte-for-byte captured body:

```json
{"language":"ar","questions":[{"questionId":101,"questionText":"ما وحدة قياس المقاومة الكهربية؟","wrongOptionText":"الفولت","previousHints":["فكّر في العالم الألماني."]}]}
```

**Arabic is `\uXXXX`-escaped**, because the Web defaults do not use `UnsafeRelaxedJsonEscaping`. It is still valid JSON and decodes to the same string — but any provider-side logging or prompt assembly must decode it. I would have written un-escaped Arabic here from memory; capturing the request is what caught it.

Formatted for reading (identical content):

```json
{
  "language": "ar",
  "questions": [
    {
      "questionId": 101,
      "questionText": "ما وحدة قياس المقاومة الكهربية؟",
      "wrongOptionText": "الفولت",
      "previousHints": ["فكّر في العالم الألماني."]
    }
  ]
}
```

### System prompt vs. user content

```
System instructions : NONE. There is no system prompt in this codebase.
User/content data   : the entire body above — there is no role separation,
                      because this is not a chat-completions API call.
```

The receiving endpoint is expected to own the prompt entirely. **Prompt quality, model choice, language enforcement and child-safety all live outside this repository.** That is the single most important architectural fact in this document.

### Exactly which Assessment fields are sent

| Field | Sent? | Notes |
|---|---|---|
| `language` | **Yes** | `"en"` or `"ar"`, normalized |
| Question text | **Yes** | already localized to `language` |
| Wrong option text | **Yes** | the option the child chose, localized |
| Previous hints | **Yes** | same question, **same language only**, oldest first |
| `questionId` | **Yes** | an internal integer, needed to map hints back |
| **Correct answer** | **No** | deliberately withheld — the provider is never told the right answer |
| Topic / TopicId | **No** | |
| Difficulty | **No** | |
| Question type | **No** | |
| Question or option **images** | **No** | image-only options send `wrongOptionText: ""` — see the gap in §5 |
| Points, score, attempt counts | **No** | |
| Mistake history beyond hints | **No** | |

---

## 4. Child data privacy — `ACTUAL`, verified

| Data | Sent? |
|---|---|
| User ID / `sub` | **NO** — `GenerateHintsRequest` has no field for it |
| Name, email, age | **NO** |
| Authentication info (JWT, refresh token) | **NO** |
| Password, OTP, reset token | **NO** |
| API keys, DB credentials | **NO** |
| Attempt ID, quiz ID | **NO** |
| Progress, score, statistics | **NO** |
| Question content | **Yes** — required |
| The child's chosen (wrong) answer | **Yes** — required |
| Prior hints for that question | **Yes** — required to escalate |

**This is genuinely correct and is the strongest property of the current AI integration.** It is enforced *structurally* — the request type has no field for identity, so nothing can leak through it by accident. A test asserts the serialized body contains none of `userId`, `attemptId`, `email`, `token`, `password`, `jwt`.

**Do not add a user identifier to `GenerateHintsRequest`.** Hint quality does not need it, and adding it would send child data to a third party for no benefit.

---

## 5. Localization — `ACTUAL` (plumbed) + a reported gap

`GenerateHintsRequest.Language` is populated end to end, verified by a parameterized test:

```
Flutter ?language=ar
  → ContentLanguages.Normalize("ar") → "ar"       ("AR", "ar-EG" also → "ar")
  → question/option text projected via the ar translation (fallback ar → en → base column)
  → GenerateHintsRequest { Language = "ar", … }
  → body carries  "language":"ar"                  ← captured on the wire
  → QuestionHints.LanguageCode = "ar"              ← persisted per language
  → previous hints filtered to LanguageCode == "ar"
```

```
Requested language: ar  →  request says "language":"ar", text is Arabic
Requested language: en  →  request says "language":"en", text is English
```

> **⚠ REPORTED LIMITATION — this does not yet produce localized hints.**
> The language is *transported*, but **nothing consumes it**. There is no prompt to put it in and no model to honour it; the receiving endpoint is an empty string. So: *the current implementation cannot reliably produce Arabic — or English — hints, because it cannot produce hints at all.* The plumbing is real and tested; the capability is not there.

> **⚠ Second gap, found while writing this.** For an **image-only option** (`OptionText` is null, `ImageUrl` set — newly possible), `WrongOptionText` is sent as `""`. The AI would be asked to explain a wrong answer it cannot see. `RECOMMENDED`: skip hint generation for image-only options, or add the image to the request once a vision-capable provider exists. Not fixed — flagging it.

---

## 6. Exact AI response — `ACTUAL`

The backend deserializes into `GenerateHintsResponse`. Deserialization is **case-insensitive** (Web defaults), so `questionId` and `QuestionId` both bind.

```json
{
  "hints": [
    { "questionId": 101, "hintText": "افتكر إن الوحدة اسمها على اسم العالم الألماني." },
    { "questionId": 103, "hintText": "فكّر في اتجاه سريان التيار." }
  ]
}
```

| Property | Type | Required by the provider? | Nullable | Notes |
|---|---|---|---|---|
| `hints` | array | **No** — absent ⇒ empty list, no error | no (defaults to `[]`) | Verified: `{"somethingElse":true}` yields `Hints = []` |
| `hints[].questionId` | integer | effectively yes | no | Absent ⇒ `0`, which fails the service's expected-id check |
| `hints[].hintText` | string | effectively yes | no (defaults to `""`) | Blank fails the service's non-blank check |

No nested objects, no enums, no arrays beyond `hints`. There is **no** `hint`, `explanation`, `usage`, `model`, or `id` field — the response type has exactly two properties.

**Where the shape is actually enforced:** not in `AIIntegration` (which accepts any JSON object) but in `QuizAttemptService.GenerateHintsAsync`:

```csharp
// unexpected ids dropped, then:
if (generatedByQuestion.Count != expectedIds.Count
    || generatedByQuestion.Values.Any(string.IsNullOrWhiteSpace))
    throw new InvalidOperationException(
        "The AI response did not contain one valid hint per wrong question");
```

So validation is **exactly one non-blank hint per wrong question, no extras**. Verified by the fake's `ReturningNothing`, `ReturningBlankHints` and `ReturningUnrelatedQuestionIds` behaviours.

---

## 7. Provider response vs. our parsed result — `ACTUAL`

There is **no SDK layer**, so the two are unusually close:

```
HTTP response body (raw JSON)
        ↓  EnsureSuccessStatusCode()                 → HttpRequestException on 4xx/5xx
        ↓  ReadFromJsonAsync<GenerateHintsResponse>() → JsonException on malformed
        ↓                                            → InvalidOperationException on literal "null"
GenerateHintsResponse                                  ← the Shared DTO, 2 properties
        ↓  QuizAttemptService: filter to expected ids, group, Trim()
Dictionary<int questionId, string hintText>             ← internal
        ↓
QuestionHints rows (+ LanguageCode, + HintSequence)     ← persisted
        ↓
QuizQuestionForAttemptDto.CurrentHint                   ← what Flutter sees
```

`GenerateHintsResponse` is **our** DTO in `Shared.Assessment.AI`, not a provider type. If Anthropic were adopted, a real SDK response object would appear between the HTTP body and this DTO — today there is nothing in between.

---

## 8. JSON schema / structured output — `ACTUAL`: not used

```
OutputConfig      — not used
JsonOutputFormat  — not used
JSON schema       — none
Structured output — none
```

Nothing constrains what the endpoint returns. Correctness rests entirely on the post-hoc check in §6, which catches wrong counts and blank text but **cannot** catch a hint that reveals the answer, is in the wrong language, or is inappropriate for a child.

**`RECOMMENDED` (not implemented)** — with a real LLM provider, constrain the output so malformed JSON becomes structurally impossible:

```json
{
  "type": "object",
  "additionalProperties": false,
  "required": ["hints"],
  "properties": {
    "hints": {
      "type": "array",
      "items": {
        "type": "object",
        "additionalProperties": false,
        "required": ["questionId", "hintText"],
        "properties": {
          "questionId": { "type": "integer" },
          "hintText":   { "type": "string" }
        }
      }
    }
  }
}
```

Why: it moves "did we get parseable JSON of the right shape" from a runtime gamble to a provider guarantee, leaving our validation to check only semantics (one per question, non-blank, length, language). The full C# for this sits in `docs/TECHNICAL_AUDIT.md` Part 12 — **still a proposal.**

---

## 9. AI failure cases — `ACTUAL`, every row pinned by a passing test

| Case | Exception thrown | Backend behaviour | Transaction | Flutter receives |
|---|---|---|---|---|
| **Endpoint not configured** (today's default) | `InvalidOperationException`, `"AI hints endpoint is not configured"` — **no HTTP call is made** | propagates out of Submit | **never opened** — nothing written | **500** `{"statusCode":500,"message":"حدث خطأ داخلي في الخادم"}` |
| Invalid API key (401) | `HttpRequestException` | propagates | never opened | 500 |
| Provider 4xx | `HttpRequestException` | propagates | never opened | 500 |
| Provider 5xx | `HttpRequestException` | propagates | never opened | 500 |
| **Rate limited (429)** | `HttpRequestException` — **indistinguishable from any other status** | propagates | never opened | 500 (never 429) |
| Network failure | `HttpRequestException` | propagates | never opened | 500 |
| **Timeout** | `TaskCanceledException` after **~100 s** | propagates | never opened | 500 |
| Malformed JSON | `JsonException` | propagates | never opened | 500 |
| Literal `null` body | `InvalidOperationException`, `"The AI provider returned an empty response"` | propagates | never opened | 500 |
| **Missing `hints` property** | **none** — yields `Hints = []` | fails the service's count check → `InvalidOperationException` | never opened | **400** (`InvalidOperationException` maps to 400) |
| Empty hint text | none at the provider | same service check | never opened | 400 |
| Extra / unknown `questionId` | none | dropped, then count check fails | never opened | 400 |
| Caller cancellation | `OperationCanceledException` | propagates | never opened | client already gone |

### The product requirement is currently violated

> **"AI failure must NOT cause quiz submission to fail. The child's quiz result must already be persisted before AI hint generation becomes relevant."**

```
ACTUAL:      AI runs BEFORE the transaction opens.
             Any AI failure ⇒ nothing is written ⇒ the completed quiz is LOST.
REQUIREMENT: commit first, hints best-effort after.
```

The current ordering was a deliberate earlier trade — it guarantees a completed attempt always has hints. Against your stated requirement, that trade is **wrong**, and this document is where it gets reversed.

**`RECOMMENDED` (not implemented)** — reorder, and swallow AI failure:

```
BEGIN TRANSACTION
  mistakes · essay answers · attempt → Completed (RowVersion) · UserTopicStats
COMMIT                                            ← the child's work is SAFE
   ↓
try { hints = await GenerateHintsAsync(…);  persist them in a second short tx }
catch (AI failure) { log a warning; leave currentHint null }
   ↓
200 either way
```

Consequences to accept, stated plainly: `retryQuestions[].currentHint` becomes genuinely `null` in practice (the DTO already declares `string?`, so **no contract change**); `HintsUsedCount` under-counts by one attempt until the next submit; and a "top up missing hints" path on retry-start becomes worthwhile so case A self-heals. Design detail in `docs/TECHNICAL_AUDIT.md` Part 13.

---

## 10. Complete example — `ACTUAL`

### Backend → AI

```http
POST https://ai.example.internal/v1/hints
Content-Type: application/json; charset=utf-8
```

> No `Authorization` header. That is not an omission in this document — the code sends none.

```json
{
  "language": "ar",
  "questions": [
    {
      "questionId": 101,
      "questionText": "ما وحدة قياس المقاومة الكهربية؟",
      "wrongOptionText": "الفولت",
      "previousHints": []
    }
  ]
}
```

(on the wire the Arabic is `\uXXXX`-escaped — §3)

### AI → Backend

```http
200 OK
Content-Type: application/json
```

```json
{
  "hints": [
    { "questionId": 101, "hintText": "افتكر إن الوحدة اسمها على اسم العالم الألماني اللي ربط الجهد بالتيار." }
  ]
}
```

### Conversion into our DTO

1. `EnsureSuccessStatusCode()` — 200, passes.
2. `ReadFromJsonAsync<GenerateHintsResponse>()` → `Hints = [ { QuestionId = 101, HintText = "افتكر…" } ]`.
3. `QuizAttemptService` filters to expected ids `{101}`, groups, `Trim()`s → `{ 101 → "افتكر…" }`.
4. Count check passes (1 expected, 1 non-blank).
5. Inside the transaction: `QuestionHints { QuizAttemptMistakeId, HintText, LanguageCode = "ar", HintSequence = 1 }`.
6. Surfaced as `retryQuestions[0].currentHint`.

---

## 11. Flutter → ElectroWorld — `ACTUAL`

```
Flutter → ElectroWorld Backend → AI Provider          ✅ this is the architecture
Flutter → Anthropic                                   ❌ never happens
```

**Flutter does not call any AI provider directly, and cannot.** The only AI configuration is `Ai:HintsEndpoint`, read server-side from `IOptions<AiSettings>`; no endpoint returns it, and there is no key to leak because none exists yet. When one is added it must stay server-side (§13).

**The real route is `POST /api/quiz-attempts/{attemptId}/submit`** — note it is *not* `/api/assessment/attempts/{id}/submit`; there is no `/api/assessment` prefix anywhere.

### Flutter request

```http
POST /api/quiz-attempts/42/submit?language=ar
Authorization: Bearer <JWT>
Content-Type: application/json
```

```json
{
  "mistakes": [
    { "questionId": 101, "selectedOptionId": 1005 },
    { "questionId": 102, "selectedOptionId": 1004 }
  ],
  "essayAnswers": [
    { "questionId": 104, "answerText": "لأن غلق الدائرة يوفر مسارًا كاملًا للتيار." }
  ]
}
```

### ElectroWorld response

```json
{
  "attemptId": 42,
  "totalQuestions": 4,
  "autoGradedQuestions": 3,
  "pendingEssayQuestions": 1,
  "correctAnswers": 2,
  "wrongAnswers": 1,
  "scorePercentage": 66.67,
  "language": "ar",
  "languageFallbackApplied": false,
  "retryQuestions": [
    {
      "questionId": 101,
      "questionText": "ما وحدة قياس المقاومة الكهربية؟",
      "questionType": "MultipleChoice",
      "imageUrl": null,
      "difficulty": "Medium",
      "displayOrder": 1,
      "points": 2,
      "currentHint": "افتكر إن الوحدة اسمها على اسم العالم الألماني اللي ربط الجهد بالتيار.",
      "options": [
        { "optionId": 1004, "optionText": "الأوم",   "imageUrl": null, "displayOrder": 1 },
        { "optionId": 1005, "optionText": "الفولت",  "imageUrl": null, "displayOrder": 2 }
      ]
    }
  ]
}
```

---

## 12. How Flutter gets hints — `ACTUAL`: **Option A**

Hints arrive **inline, in the submit response** — `retryQuestions[].currentHint`. There is no separate hint endpoint.

```
ACTUAL:  Option A — synchronous. Submit returns the score AND the hints together.
         Nothing resembling GET /api/…/mistakes/{id}/hints exists.
```

Hints are also re-served, in the requested language, by:
- `POST /api/quiz-attempts?quizId=…&previousAttemptId=42` — retry questions carry their latest hint
- `GET /api/quiz-attempts/42?language=ar` — `questions[].currentHint`

### The gap, and the smallest fix

Once the `RECOMMENDED` degradation in §9 lands, a submit can legitimately return `currentHint: null`. Flutter would then have **no way to obtain the hint later**, because both paths above only read hints that already exist — nothing triggers generation.

**`RECOMMENDED`, smallest possible change — no new endpoint, no background job:**

> In the existing retry-start path, generate hints for any mistake of the previous attempt that has **no hint in the requested language**, then return them. Retry-start is already the moment a child asks for help, already reads hints, and already calls nothing else.

That reuses one endpoint and adds no architecture. A dedicated `POST /api/quiz-attempts/{id}/hints` is the alternative if you want Flutter to retry hints without starting a retry — more surface, more Flutter state, and I would not add it first.

---

## 13. AI configuration

### `ACTUAL` — one key, and it is empty

```
Ai:
  HintsEndpoint        Required (the code throws without it) · currently ""
```

That is the entire `AiSettings` class. Verified across `appsettings.json` (no `Ai` section), `appsettings.Development.json` (`"Ai": { "HintsEndpoint": "" }`), `appsettings.Production.json` (`{}` — empty).

### `RECOMMENDED` — not implemented

```
Ai:
  Provider             Required        "Claude"
  Model                Required        "claude-opus-5"
  ApiKey               Required        NEVER in appsettings — see below
  TimeoutSeconds       Required        20
  MaxRetries           Optional        2
  MaxHintLength        Optional        220
  Enabled              Optional        true — false skips the provider entirely
  HintsEndpoint        Removed         the SDK owns the URL
```

| Environment | Where the key lives |
|---|---|
| **Development** | .NET User Secrets — `dotnet user-secrets set "Ai:ApiKey" "sk-ant-…"`. Outside the repo |
| **Production** | environment variable or a managed secret store (Key Vault / Secrets Manager) surfaced as configuration |
| **Never** | `appsettings*.json`, source control, logs, exception messages, any API response |

> **⚠ This rule is currently broken for other secrets.** `appsettings.Development.json` is **git-tracked** and contains `Jwt:SecretKey`, the DB connection string and the SMTP password; `.gitignore` has no `appsettings` rule. There is no AI key to leak *yet* — do not add one until that is fixed, or it will be committed the same way.

---

## 14. Fake AI contract — created in this session

**`FakeAiHintGenerator` did not exist before now.** I created it as real, tested code: [Tests/Assessment.Tests/FakeAiHintGenerator.cs](Tests/Assessment.Tests/FakeAiHintGenerator.cs).

It implements `IAiHintGenerator` — the same interface the real provider implements — so it substitutes anywhere via DI with no production change.

| Factory | Behaviour | Simulates |
|---|---|---|
| `Succeeding()` | one hint per question, **in the requested language** (`تلميح للسؤال 101` / `Hint for question 101`) | the happy path |
| `Unavailable()` | throws `HttpRequestException` | outage, 4xx/5xx, rate limit, network failure |
| `NotConfigured()` | throws `InvalidOperationException` with the real message | **today's production default** |
| `TimingOut()` | throws `TaskCanceledException` | the 100 s timeout |
| `ReturningNothing()` | `Hints = []` | provider answered nothing |
| `ReturningBlankHints()` | whitespace `hintText` | blank-hint validation |
| `ReturningUnrelatedQuestionIds()` | `questionId = -999` | provider answered a question we never asked |

**Receives** a `GenerateHintsRequest`; **returns** a `GenerateHintsResponse`; **records** every request in `Received` so tests can assert the language and question set that were actually sent.

Swap it in for an integration test:

```csharp
builder.ConfigureTestServices(services =>
{
    services.RemoveAll<IAiHintGenerator>();
    services.AddScoped<IAiHintGenerator>(_ => FakeAiHintGenerator.Unavailable());
});
```

The test that matters most, once §9's reorder lands:

> **AI unavailable → submit still returns 200, the attempt is `Completed`, mistakes exist, `currentHint` is null.**

That test cannot pass today, because the current ordering makes it a 500. **It is not written yet** — writing it before the reorder would be writing a failing test for behaviour that does not exist.

---

## 15. Security review — `ACTUAL`

| Check | Result |
|---|---|
| AI API key not committed to git | ✅ **vacuously** — no AI key exists. ⚠ But `Jwt:SecretKey`, the DB connection string and the SMTP password **are** committed |
| AI API key not returned to Flutter | ✅ no endpoint exposes any AI config |
| AI API key not logged | ✅ nothing in `AIIntegration` logs at all — see the gap below |
| JWT never sent to AI | ✅ structurally impossible — no field for it |
| Refresh tokens never sent to AI | ✅ structurally impossible |
| Passwords / OTPs never sent to AI | ✅ structurally impossible |
| Child identifiers minimized | ✅ **zero** identifiers sent; only `questionId` |
| Prompt / input sanitized | ⚠ **No.** Question and option text go through verbatim. Low risk (admin-authored content, not child free text) — but the child's **essay text is never sent to AI**, which is what keeps prompt injection out of reach today |
| AI output validated before persist/display | ⚠ **Partially.** Count and non-blank are checked; **length, language, HTML/links and child-appropriateness are not.** Whatever comes back is stored and shown to a child |

**Two `RECOMMENDED` additions** (neither implemented): a `Sanitize` guard rejecting over-long hints, markup and URLs (full code in the audit, Part 12); and **any logging at all** in `AIIntegration` — currently an AI failure is invisible except as a 500, so you cannot tell an outage from a rate limit from a bad key.

---

## 16. Final AI contract table

| Direction | Endpoint | Request | Response | Authentication |
|---|---|---|---|---|
| **Flutter → Backend** | `POST /api/quiz-attempts/{attemptId}/submit?language=ar` | `{ mistakes[], essayAnswers[] }` | `QuizAttemptResultDto` — score + `retryQuestions[].currentHint` | **JWT bearer**; `userId` from the `sub` claim, never from the body |
| **Backend → AI** | `POST {Ai:HintsEndpoint}` — **currently `""`** | `{ language, questions[{ questionId, questionText, wrongOptionText, previousHints[] }] }` — camelCase, Arabic `\u`-escaped | — | **NONE** — no header of any kind is sent |
| **AI → Backend** | — | — | `{ hints[{ questionId, hintText }] }`; anything else deserializes to `Hints = []` | provider response; `EnsureSuccessStatusCode()` collapses every failure status into one `HttpRequestException` |
| **Backend → Flutter** | — | — | `currentHint` on each retry question, plus `language` and `languageFallbackApplied` | JSON over the same JWT-authenticated response |

---

## Your ten questions, answered

| # | Question | Answer |
|---|---|---|
| 1 | **What does Flutter send to my backend?** | `POST /api/quiz-attempts/{id}/submit?language=ar` with a JWT, `mistakes[]` (questionId + selectedOptionId) and `essayAnswers[]` (questionId + answerText). Never a `userId`. |
| 2 | **What does my backend send to the AI?** | A POST to `Ai:HintsEndpoint` with `{language, questions[{questionId, questionText, wrongOptionText, previousHints[]}]}` — **and no credentials**. |
| 3 | **What exactly does the AI receive?** | The captured body in §3. camelCase, Arabic `\uXXXX`-escaped, no system prompt, no model, no schema, no auth header. |
| 4 | **What exactly does the AI return?** | `{hints:[{questionId, hintText}]}` is what we *parse*. Nothing constrains what it *sends* — any JSON object is accepted and becomes `Hints = []`. |
| 5 | **How does my backend parse the response?** | `EnsureSuccessStatusCode()` → `ReadFromJsonAsync<GenerateHintsResponse>()` → `QuizAttemptService` drops unexpected ids and requires exactly one non-blank hint per wrong question, else throws. |
| 6 | **What does Flutter finally receive?** | `retryQuestions[].currentHint`, inline in the submit response (Option A). Also on retry-start and `GET /api/quiz-attempts/{id}`. |
| 7 | **What happens if the AI fails?** | **Today: the whole submission fails and the child's completed quiz is lost** — 500 for transport failures, 400 for shape failures. This violates your stated requirement; the fix is the reorder in §9. |
| 8 | **How does Arabic vs English affect it?** | `language` is normalized, drives which translation is projected into `questionText`/`wrongOptionText`, filters `previousHints` to the same language, travels in the request body, and is stored on `QuestionHints.LanguageCode`. **All plumbed and tested — but nothing consumes it, so no localized hint is actually produced.** |
| 9 | **Where is the AI API key stored?** | **Nowhere. There is no AI API key.** When you add one: User Secrets in dev, environment variable or secret store in prod — and fix the already-committed `Jwt:SecretKey` first. |
| 10 | **Can I test the whole flow without a real AI call?** | **Yes.** `FakeAiHintGenerator` (7 behaviours) covers success and every failure mode; `AiProviderContractTests` intercepts the real HTTP request so the wire contract itself is asserted. 52/52 passing, zero network calls. |

---

## Verification summary

**Verified by running code:** the exact outgoing method, URL, content type, absence of any auth header, and byte-for-byte body; that `language` reaches the wire for both `ar` and `en`; that no user identifier appears in the body; and each of these failure mappings — unconfigured endpoint (no HTTP call made), 401/429/400/500/503 → `HttpRequestException`, malformed JSON → `JsonException`, literal `null` → `InvalidOperationException`, missing `hints` → silently `[]`, network failure, caller cancellation, and the empty-question-list guard short-circuiting before any HTTP call.

**Not verified, and not claimed:** nothing here has run against a real AI provider or a live database. The `RECOMMENDED` Claude implementation is **not built** — no Anthropic SDK is referenced, and `ClaudeHintProvider` does not exist as a file. The graceful-degradation reorder in §9 is **not implemented**; the "AI down → submit still 200" test therefore cannot pass yet and has not been written.
