# Question Types, Media &amp; Localization — Design + Change Report

**Applies to:** ElectroWorld Assessment module.
**Status:** code implemented and building (30/30 tests pass). **SQL written but NOT executed** — no database was reachable from this environment. Nothing below is claimed as verified against a live database.

---

## 1. Decisions, stated before the implementation

### 1.1 Question types — how each behaves

| Type | Storage | Options | Correct answer | Flutter submits | Scored |
|---|---|---|---|---|---|
| `MultipleChoice` | unchanged | 2+ in `QuestionOptions` | exactly one `IsCorrect` | `selectedOptionId` | **Yes** |
| `TrueFalse` | **unchanged — reuses `QuestionOptions`** | exactly 2 | exactly one `IsCorrect` | `selectedOptionId` | **Yes** |
| `Essay` | **new `QuizAttemptEssayAnswers` table** | none | none | `answerText` | **No — stored Pending** |

**True/False deliberately reuses `QuestionOptions`.** Two ordinary option rows ("True"/"False", or "صح"/"خطأ") mean **zero** new grading code: the existing composite FKs, the `CorrectOptionId` snapshot, mistake recording, hints, retry and statistics all work untouched. `QuestionType` exists so Flutter can render two large buttons instead of a list — it is a *presentation* discriminator, not a second grading path. A dedicated boolean column would have duplicated the answer-key machinery for no gain.

**Essay genuinely required schema change,** because three existing NOT NULL columns make it impossible otherwise: `QuizAttemptQuestions.CorrectOptionId`, `QuizAttemptMistakes.SelectedOptionId`, and the composite FK from the mistake to `QuestionOptions`.

### 1.2 Essay grading — stated honestly

```
Essay answers are NOT automatically graded. There is no AI essay grading.
```

The attempt still **completes immediately**. The answer is stored with `Status = 'Pending'` and takes no part in the score. The table carries `AwardedPoints`, `Feedback`, `GradedBy ('Human' | 'Ai')` and `GradedAt` so a grading flow — human or AI — can be added later **without a further schema change**, but no such flow exists today.

**How this integrates with the existing scoring model** (no new mechanism was introduced):

```
autoGraded  = questions in the attempt where QuestionType <> 'Essay'
correct     = autoGraded − confirmedMistakes
score       = round(correct * 100 / autoGraded, 2)      ← denominator EXCLUDES essays
QuestionsAnsweredCount = TotalQuestionsAtAttempt         ← unchanged
```

Excluding essays from the denominator is the whole point: including them would cap every mixed quiz below 100% forever, since an essay can never be counted correct.

`CK_QuizAttempts_CorrectVsAnswered` still holds: `correct ≤ autoGraded ≤ total = answered`.

**Essays are also excluded from `UserTopicStats`** until graded — counting them as "answered" with no possible "correct" would permanently depress the child's mastery for that topic.

**Essays never appear in a retry.** A retry serves the previous attempt's *mistakes*, and an essay can never produce one. **Essays get no hints**, for the same reason.

### 1.3 Images

Question and option images reuse the **existing** upload endpoint (`POST /api/content/media/images`, admin-only) and store the **server-relative path** it returns — identical to how `LessonContent.MediaUrl` already works.

```
Format:   .jpg .jpeg .png .webp        (enforced by LocalImageStorageService)
Max size: 5 MB service-side, 6 MB request cap
URL:      RELATIVE, e.g. /uploads/lessons/8f1c2b3a-….png
          Flutter must prefix the base URL: '$baseUrl$imageUrl'
Nullable: YES on both question and option
Auth:     the image itself is served by UseStaticFiles() with NO auth
```

**Image upload is not part of the quiz-taking request.** Uploading happens at authoring time; taking an attempt only reads paths.

### 1.4 `OptionText` becomes nullable

An option must be text-only, image-only, or both — **never neither**. `OptionText` is widened to `NULL` and guarded by `CK_QuestionOptions_TextOrImage`, so making it nullable cannot create empty options. This rule was **not previously enforced at all**; it is now enforced in the database *and* pre-checked in the service for a readable message.

### 1.5 Localization design — and why

**Chosen: one translation table per localizable entity**, keyed `(ParentId, LanguageCode)`, with a real FK to a `Languages` lookup table.

| Approach | Verdict |
|---|---|
| Side-by-side columns (`TitleEn`, `TitleAr`) | **Rejected** — a third language means a schema change on every table. Fails the extensibility requirement outright. |
| One generic `Translations(EntityType, EntityId, Field, Value)` | **Rejected** — no foreign keys, no typing, nothing stops an orphaned or misspelled row. Classic EAV. |
| **One table per entity** | **Chosen** — real FKs, real cascade behaviour, and a new language is one `INSERT` into `Languages`. |

Localized: `Quizzes`, `Questions`, `QuestionOptions`, `Topics`, `Categories`.

**`QuestionHints` is deliberately NOT given a translation table.** A hint is *generated* in a language, not authored and then translated — two hints in two languages are two independent generations, not two renderings of one string. So `QuestionHints` gets a `LanguageCode` **column**, and the sequence uniqueness constraint becomes per-language.

**Base columns are kept** and become the last-resort fallback. That is what makes this migration backward compatible: every existing read path still works before a single translation row exists.

### 1.6 Language selection mechanism

```
?language=en          (explicit query parameter — wins)
Accept-Language: en   (standard header — used when no query parameter)
neither               → "ar" (the app's primary audience and the existing content language)
```

Both are supported because the query parameter matches the module's existing house style (`?quizId=`, `?previousAttemptId=`) and is testable in Swagger, while `Accept-Language` is what a well-behaved HTTP client already sends. An **unsupported or malformed value is normalized, never rejected** — a bad language header must never fail a quiz submission. `ar-EG` and `EN` both resolve correctly.

### 1.7 Fallback rule — defined, not left ambiguous

```
Per FIELD:   requested language → 'en' → the base column
```

The chain always resolves, because base columns are `NOT NULL`. **No endpoint 404s over a missing translation.**

Fallback is **per field**, so a partially translated question *can* return Arabic question text with an English option. Rather than hide that, every localized response carries:

```json
"language": "ar",
"languageFallbackApplied": true
```

`languageFallbackApplied` is `true` when *any* field in the response fell back. Flutter can show a "translation incomplete" affordance; the admin UI should enforce complete translations. The API returns **only the resolved language**, never a multi-language object.

### 1.8 AI + localization

```
Flutter (?language=ar)
   → QuizAttemptController.ResolveLanguage
   → QuizAttemptService (resolvedLanguage)
   → GenerateHintsRequest { Language = "ar", Questions[...] }   ← contract extended
   → IAiHintGenerator → provider
   → hints persisted with LanguageCode = "ar"
```

Question text and wrong-option text sent to the AI are **already localized** to the requested language, and `PreviousHints` are filtered to the **same language** — an Arabic hint is not useful context for generating an English one.

> **⚠ Reported limitation, as required.** The current AI integration **cannot produce Arabic or English hints at all**: `AiSettings` contains only `HintsEndpoint`, that endpoint is an empty string in the only populated config file, and there is no API key, model, prompt or timeout. `GenerateHintsRequest.Language` is now plumbed end to end, but **nothing consumes it yet**. Implementing the provider (Phase 5 of the audit) is what makes localized hints actually work. This is plumbing, not a working feature, and is not claimed otherwise.

---

## 2. Mandatory SQL

Full executable script: **[db/migrations/002_question_types_media_localization.sql](db/migrations/002_question_types_media_localization.sql)** (456 lines, SQL Server).

> **⚠ Depends on migration 001** (`2026-09-10_assessment_attempt_question_snapshot.sql`), which adds the snapshot columns that section 3 alters. 001 must be applied first; the script opens with a check for it.

### Section-by-section safety analysis

| § | Current schema | Change | Why required | Existing data affected? | Backward compatible? | Data migration? | EF change? |
|---|---|---|---|---|---|---|---|
| 1 | no type column | `Questions.QuestionType` + CHECK + index | types must be distinguishable at delivery and grading | No — `DEFAULT 'MultipleChoice'` makes every existing row correct by definition | **Yes** | No | **Yes** |
| 2 | `OptionText` NOT NULL, no images | `Questions.ImageUrl`, `QuestionOptions.ImageUrl`, widen `OptionText` to NULL + `CK_QuestionOptions_TextOrImage` | image-only options; widening without the CHECK would allow empty options | No — all existing options have text, so the CHECK passes on every row | **Yes** (widening only) | No | **Yes** |
| 3 | `CorrectOptionId` NOT NULL (from 001) | drop FK → widen to NULL → recreate FK; add `QuestionType` snapshot + 2 CHECKs | an Essay has no answer key, so NOT NULL makes essays impossible | No — existing rows are all MCQ with a key | **Yes** | `UPDATE` backfills snapshot type from the live question (no-op on a fresh install) | **Yes** |
| 4 | nothing can hold free text | `CREATE TABLE QuizAttemptEssayAnswers` | `QuizAttemptMistakes.SelectedOptionId` is NOT NULL with a composite FK and cannot represent an essay | No — new table | **Yes** | No | **Yes** |
| 5 | no language concept | `CREATE TABLE Languages` + seed en/ar | a new language must be an INSERT, not a schema change | No | **Yes** | seed only | **Yes** |
| 6 | single-language columns | 5 translation tables | extensibility; see §1.5 | No — base columns kept | **Yes** | No | **Yes** |
| 7 | — | backfill `'en'` rows from base columns | guarantees the fallback chain resolves on day one | **Reads** existing data, writes only new rows; `WHERE NOT EXISTS` makes it re-runnable | **Yes** | **Yes — see warning below** | No |
| 8 | `HintText`, one language | `QuestionHints.LanguageCode` + FK; replace sequence UNIQUE with a per-language one | two languages need independent sequence 1 | No — `DEFAULT 'ar'` labels existing hints correctly (all current content is Arabic) | **Yes** | No | **Yes** |

### ⚠ The one place existing data needs judgement

Section 7 copies current base-column text into rows **labelled `'en'`**. The base columns are **not guaranteed to be English** — seeded `Categories` are English (`Components`, `Concepts`) while authored questions are Arabic. The backfill therefore produces `'en'` rows that may contain Arabic.

It is still the right default (the fallback always resolves, nothing 404s on day one), but **review it afterwards**:

```sql
SELECT * FROM Assessment.QuestionTranslations WHERE LanguageCode = N'en';
```

To label existing content Arabic instead, change `N'en'` to `N'ar'` in the five statements **before running them**. Nothing is dropped or overwritten either way.

**No data is deleted or overwritten anywhere in this migration.** Every step is additive or widening.

---

## 3. Database-First sequencing

```
SQL database must be updated first.
Then EF Core entities/configurations must be re-scaffolded or manually synchronized.
```

I synchronized by hand rather than re-scaffolding, to preserve the existing comments and explicit constraint names. **The EF model now expects the migrated schema — running this code against an un-migrated database will fail.**

| Artifact | Change |
|---|---|
| **New entities** | `Language`, `QuizTranslation`, `QuestionTranslation`, `QuestionOptionTranslation`, `TopicTranslation`, `CategoryTranslation`, `QuizAttemptEssayAnswer` |
| **Modified entities** | `Question` (+`QuestionType`, +`ImageUrl`, +2 collections) · `QuestionOption` (`OptionText` → `string?`, +`ImageUrl`, +collection) · `QuizAttemptQuestion` (`CorrectOptionId` → `int?`, +`QuestionType`) · `QuestionHint` (+`LanguageCode`) · `Quiz`/`Topic`/`Category`/`QuizAttempt` (+collections) |
| **New configurations** | `LocalizationConfigurations.cs` (6 classes), `QuizAttemptEssayAnswerConfiguration.cs` |
| **Modified configurations** | `QuestionConfiguration`, `QuestionOptionConfiguration`, `QuizAttemptQuestionConfiguration`, `QuestionHintConfiguration` |
| **DbContext** | 7 new `DbSet`s |

---

## 4. API contract changes

### 4.1 Language parameter — every child-facing Assessment endpoint

```http
GET  /api/quiz-attempts/42?language=en
POST /api/quiz-attempts?quizId=15&language=ar
POST /api/quiz-attempts/42/submit?language=ar
```

or, equivalently:

```http
Accept-Language: en
```

Every localized response now carries `language` and `languageFallbackApplied` at the top level.

### 4.2 `POST /api/quiz-attempts?quizId=15&language=en` — mixed-type attempt

```json
{
  "attemptId": 42,
  "quizId": 15,
  "startedAt": "2026-09-11T09:30:00.0000000Z",
  "language": "en",
  "languageFallbackApplied": false,
  "questions": [
    {
      "questionId": 101,
      "questionText": "What is voltage?",
      "questionType": "MultipleChoice",
      "imageUrl": null,
      "difficulty": "Easy",
      "displayOrder": 1,
      "points": 1,
      "currentHint": null,
      "options": [
        { "optionId": 1001, "optionText": "Electrical potential difference", "imageUrl": null, "displayOrder": 1 },
        { "optionId": 1002, "optionText": "Resistance to current", "imageUrl": null, "displayOrder": 2 }
      ]
    },
    {
      "questionId": 102,
      "questionText": "Current flows from the negative terminal to the positive terminal inside the circuit.",
      "questionType": "TrueFalse",
      "imageUrl": null,
      "difficulty": "Medium",
      "displayOrder": 2,
      "points": 2,
      "currentHint": null,
      "options": [
        { "optionId": 1004, "optionText": "True",  "imageUrl": null, "displayOrder": 1 },
        { "optionId": 1005, "optionText": "False", "imageUrl": null, "displayOrder": 2 }
      ]
    },
    {
      "questionId": 103,
      "questionText": "Which component is shown in the image?",
      "questionType": "MultipleChoice",
      "imageUrl": "/uploads/lessons/8f1c2b3a-6b4d-4e2a-9c1f-3d7e5a2b1c0d.png",
      "difficulty": "Medium",
      "displayOrder": 3,
      "points": 2,
      "currentHint": null,
      "options": [
        { "optionId": 1006, "optionText": "Resistor", "imageUrl": "/uploads/lessons/2c4e6a8b-….png", "displayOrder": 1 },
        { "optionId": 1007, "optionText": null,       "imageUrl": "/uploads/lessons/9b1d3f5a-….png", "displayOrder": 2 },
        { "optionId": 1008, "optionText": "Capacitor","imageUrl": null, "displayOrder": 3 }
      ]
    },
    {
      "questionId": 104,
      "questionText": "Explain in your own words why a lamp lights up when the circuit is closed.",
      "questionType": "Essay",
      "imageUrl": null,
      "difficulty": "Hard",
      "displayOrder": 4,
      "points": 3,
      "currentHint": null,
      "options": []
    }
  ]
}
```

Arabic (`?language=ar`) returns the identical structure with `"language": "ar"` and translated `questionText` / `optionText`. `imageUrl`, `optionId`, `questionType`, `displayOrder` and `points` are **language-independent**. Full pair: `mock_start_attempt_mixed_en.json` / `mock_start_attempt_mixed_ar.json`.

### 4.3 `POST /api/quiz-attempts/42/submit?language=en`

**Request** — `mistakes` unchanged (existing clients keep working), `essayAnswers` added:

```json
{
  "mistakes": [
    { "questionId": 101, "selectedOptionId": 1002 },
    { "questionId": 102, "selectedOptionId": 1004 },
    { "questionId": 103, "selectedOptionId": 1006 }
  ],
  "essayAnswers": [
    { "questionId": 104, "answerText": "Because closing the circuit gives the current a complete path…" }
  ]
}
```

**Response** — note the three new count fields:

```json
{
  "attemptId": 42,
  "totalQuestions": 4,
  "autoGradedQuestions": 3,
  "pendingEssayQuestions": 1,
  "correctAnswers": 2,
  "wrongAnswers": 1,
  "scorePercentage": 66.67,
  "language": "en",
  "languageFallbackApplied": false,
  "retryQuestions": [ { "questionId": 101, "…": "…", "currentHint": "Think about which unit is named after…" } ]
}
```

`scorePercentage` is `2 / 3 = 66.67` — the essay is **excluded from the denominator**, not counted wrong.

### 4.4 New error responses

| Code | Message | Cause |
|---|---|---|
| 400 | `السؤال رقم 104 لا يخص هذه المحاولة أو لا يُصحّح تلقائيًا` | an Essay id sent in `mistakes` |
| 400 | `السؤال رقم 101 ليس سؤالًا مقاليًا في هذه المحاولة` | a non-Essay id sent in `essayAnswers` |
| 400 | `إجابة السؤال رقم 104 فارغة` | blank `answerText` |
| 400 | `الاختيار يجب أن يحتوي على نص أو صورة على الأقل` | option with neither |
| 400 | `السؤال رقم 104 سؤال مقالي ولا يقبل اختيارات` | adding an option to an Essay |
| 400 | `سؤال الصح والخطأ رقم 102 يجب أن يحتوي على اختيارين بالضبط` | activating TrueFalse without exactly 2 options |
| 409 | `المحاولة رقم 42 تم تسليمها بالفعل` | now also covers a duplicate essay answer |

### 4.5 Flutter notes

- **Switch the whole question widget on `questionType`** — `MultipleChoice` → option list, `TrueFalse` → two buttons (options still arrive, use their real `optionId`s), `Essay` → text field, `options` is `[]`.
- **`optionText` is now `String?`.** An option with `optionText: null` is image-only — render the image alone. Both null is impossible.
- **`imageUrl` is relative** on both question and option. Prefix the base URL.
- **Send every answered MC/TF question in `mistakes`** (the backend filters), and every answered essay in `essayAnswers`. An unanswered essay is simply omitted — no row is created.
- **Read `scorePercentage` against `autoGradedQuestions`, not `totalQuestions`**, or the maths will look wrong on mixed quizzes.
- **`pendingEssayQuestions > 0`** → show "your written answer is with your teacher", not a score.
- 16 new mock files under `docs/mocks/`, all valid JSON.

---

## 5. Final change report

| Area | Current state | Required change | DB change? | SQL provided? |
|---|---|---|---|---|
| **Question Type** | no column; every question implicitly MCQ | `QuestionType` + CHECK + index; snapshot on `QuizAttemptQuestions`; type-aware activation rules | **Yes** | **Yes** |
| **Question Image** | none | nullable `Questions.ImageUrl`; reuses the existing upload endpoint | **Yes** | **Yes** |
| **Option Image** | none; `OptionText` NOT NULL | nullable `QuestionOptions.ImageUrl`; `OptionText` widened to NULL; `CK_QuestionOptions_TextOrImage` | **Yes** | **Yes** |
| **Arabic Content** | base columns only (mixed language) | `Languages` + 5 translation tables; per-field fallback | **Yes** | **Yes** |
| **English Content** | base columns only | same mechanism; backfilled as `'en'` | **Yes** | **Yes** |
| **Hint Localization** | single language | `QuestionHints.LanguageCode` + per-language sequence uniqueness; language plumbed into the AI request | **Yes** | **Yes** |
| **Essay Questions** | impossible — 3 NOT NULL columns block it | `QuizAttemptEssayAnswers`; `CorrectOptionId` nullable; score excludes essays; excluded from stats | **Yes** | **Yes** |
| **True/False** | not distinguishable | `QuestionType` only — **reuses `QuestionOptions`, no new storage** | **No** (beyond §1) | N/A |
| **API Contract** | no type, image or language | `language` param + `Accept-Language`; `questionType`, `imageUrl`, `essayAnswers`, `autoGradedQuestions`, `pendingEssayQuestions`, `language`, `languageFallbackApplied` | **No** | N/A |

## 6. Prioritized implementation list

| # | Step | Status |
|---|---|---|
| 1 | **Database changes** — run migration 001 then 002 | **NOT RUN** — script provided, no DB reachable here |
| 2 | **EF Core changes** — 7 new entities, 4 modified, 8 configurations, 7 DbSets | **Done, builds** |
| 3 | **DTO changes** — 6 child-facing + 6 admin DTOs | **Done, builds** |
| 4 | **Service/business logic** — snapshot, essay flow, score denominator, localized projections, stats exclusion, type validation | **Done, builds** |
| 5 | **Controller/API** — `?language=` + `Accept-Language` on all three child endpoints | **Done, builds** |
| 6 | **AI integration** — `Language` on `GenerateHintsRequest`, per-language previous hints and persistence | **Plumbed only — the provider itself is still a stub and produces no hints in any language** |
| 7 | **API documentation** — this file + the contract | **Done** |
| 8 | **Flutter mock JSON** — 16 new files, 50 total | **Done, all valid** |

### What was verified, and what was not

**Verified:** the solution builds with 0 errors; 30/30 tests pass, including new assertions that `CorrectOptionId` is nullable, `QuestionType` is snapshotted non-null, `OptionText`/`ImageUrl` are nullable, hint sequence uniqueness is per-language, and all 5 translation tables are unique per `(parent, language)`; all 50 mock files parse as valid JSON; language normalization handles `EN`, `ar-EG`, null and unsupported input.

**Not verified:** the SQL has **not been executed** — no database was reachable. Nothing here has been run end to end against SQL Server, no query plan was inspected, and no localized response was produced by a live server. Run migration 001, then 002, then exercise the endpoints before trusting any of it in an environment that matters.
