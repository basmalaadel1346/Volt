# ElectroWorld — Full System Audit

**Source of truth:** the SQL Server schema you supplied (scripted 2026-09-11).
**Method:** the EF Core model was *dumped programmatically* (tables, columns, provider types, nullability, indexes, FKs, delete behaviour) and diffed against that DDL. Nothing in §1–§3 is eyeballed.
**Build state:** solution builds, 52/52 tests pass — **and that is exactly the problem.** See the verdict.

---

# 1. EXECUTIVE VERDICT

## ❌ NOT PRODUCTION READY

Not because of design. The architecture is sound, the Assessment domain model is genuinely well designed, and ownership/concurrency are correct. It is not production ready because **the deployed database and the compiled code are not the same system.**

The code compiles and every test passes because **no test and no compiler check touches the real schema**. EF Core does not validate the schema at startup, so the application will **start successfully and then fail on the first request** to any Assessment endpoint.

**Verified: 6 tables and 3 columns that the code queries do not exist in the database, and one table that does exist has a completely different shape.** The entire child-facing Assessment flow — start attempt, submit, read attempt — is dead against this database.

> Someone applied a **different migration** from `db/migrations/002_question_types_media_localization.sql`. Part of it landed (`QuestionType`, the snapshot columns, `LanguageCode`, nullable `CorrectOptionId`), part did not (image columns, essay table, 4 of 5 translation tables), and one part was implemented with a **different design** (`QuestionTranslations` as EAV). This is the single most important finding in this report.

---

# 2. PRODUCTION BLOCKERS

| ID | Severity | Category | Status | Blocker |
|---|---|---|---|---|
| **B-1** | **P0** | BUG / DATA | **VERIFIED** | 6 tables the code queries do not exist |
| **B-2** | **P0** | BUG | **VERIFIED** | 3 columns the code selects do not exist |
| **B-3** | **P0** | BUG | **VERIFIED** | `QuestionTranslations` has a different shape than the entity |
| **B-4** | **P0** | CONFIGURATION | **VERIFIED** | Production cannot start — no `Jwt` section |
| **B-5** | **P0** | SECURITY | **VERIFIED** | JWT signing key committed to git |
| **B-6** | **P0** | BUG | **VERIFIED** | AI endpoint unconfigured → every wrong answer 500s |
| **B-7** | **P1** | DATA INTEGRITY | **VERIFIED** | `QuestionHints` lost its uniqueness constraint |
| **B-8** | **P1** | SECURITY | **VERIFIED** | Plaintext reset OTP written to stdout |

---

# 3. DATABASE ↔ CODE COMPARISON (VERIFIED BY MODEL DUMP)

## 3.1 Tables the EF model requires that the database does not have

EF expects **17** tables in `Assessment`. The schema has **11**.

| EF expects | Database | Consequence |
|---|---|---|
| `Assessment.QuizAttemptEssayAnswers` | **missing** | Essay submit → `SqlException 208: Invalid object name` |
| `Assessment.Languages` | **exists as `dbo.Languages`** | Wrong schema → invalid object name |
| `Assessment.QuizTranslations` | **missing** | |
| `Assessment.QuestionOptionTranslations` | **missing** | **Every** localized option read fails |
| `Assessment.TopicTranslations` | **missing** | |
| `Assessment.CategoryTranslations` | **missing** | |

## 3.2 Columns the EF model selects that do not exist

| Table | EF column | Database | Consequence |
|---|---|---|---|
| `Questions` | `ImageUrl nvarchar(500) NULL` | **missing** | `Invalid column name 'ImageUrl'` on **every** question read |
| `QuestionOptions` | `ImageUrl nvarchar(500) NULL` | **missing** | `Invalid column name 'ImageUrl'` on **every** option read |
| `Languages` | `IsActive bit NOT NULL` | **missing** (`dbo.Languages` is `Code`,`Name` only) | invalid column |

## 3.3 `QuestionTranslations` — fundamentally different design

```
DATABASE (source of truth)              EF ENTITY
──────────────────────────────          ──────────────────────────────
PK (QuestionId, LanguageCode, Field)    PK (Id)          ← no Id column in DB
QuestionId   int                        QuestionId  int
LanguageCode varchar(5)                 LanguageCode nvarchar(5)
Field        nvarchar(50)  ← EAV        QuestionText nvarchar(max)  ← not in DB
Value        nvarchar(max)
FK → dbo.Languages                      FK → Assessment.Languages
```

The database uses a **scoped EAV** design (`Field`/`Value`); the entity uses typed columns. `QuizAttemptService.ProjectQuestions` emits `SELECT ... t.QuestionText ... FROM Assessment.QuestionTranslations t` — **`QuestionText` does not exist**. Every localized read fails.

## 3.4 Type, nullability and constraint mismatches

| # | Object | EF | Database | Severity | Effect |
|---|---|---|---|---|---|
| M-1 | `QuestionOptions.OptionText` | `nvarchar(max) **NULL**` | `nvarchar(max) **NOT NULL**` | **P1** | Image-only option INSERT → constraint violation. The image-only feature cannot work |
| M-2 | `QuizAttemptMistakes.SelectedOptionId` | `int **NOT NULL**` | `int **NULL**` | **P2** | A NULL row would throw on materialization. No code writes NULL today, so latent |
| M-3 | `QuestionHints.LanguageCode` | `nvarchar(5)`, default `'ar'` | `varchar(5)`, default `'en'` | **P2** | Type mismatch forces implicit conversion; **defaults disagree**, so a DB-side insert lands in the wrong language |
| M-4 | `Questions.QuestionType` | `nvarchar(20)` | `nvarchar(30)` | P3 | EF would reject a 21-char value the DB accepts. Both CHECKs cap the real values, so benign |
| M-5 | `QuizAttemptQuestions.QuestionType` | `nvarchar(20)` | `nvarchar(30)` | P3 | as above |
| M-6 | `CK_QuizAttemptQuestions_EssayHasNoKey` | `CorrectOptionId IS NOT NULL OR Type='Essay'` | `(Essay AND NULL) OR (¬Essay AND NOT NULL)` | P4 | **The database is stricter and better** — it also forbids an Essay *having* a key. Config text is migration-only; no runtime effect. Adopt the DB version |
| M-7 | `CK_QuestionOptions_TextOrImage` | declared in EF | **absent** | P2 | No DB guard once `OptionText` becomes nullable |
| M-8 | `FK_QuestionHints_Languages` | declared | **absent** | P3 | Unvalidated language codes |
| M-9 | `UQ_QuestionHints_MistakeId_(Language_)Sequence` | UNIQUE | **NO unique constraint at all** | **P1** | The original was dropped and the replacement never created — see §3.5 |

## 3.5 B-7 — the uniqueness regression

The live `QuestionHints` table has **only a primary key**. Both the original `UQ_QuestionHints_MistakeId_Sequence` and the intended per-language replacement are absent.

```
Consequence: nothing prevents two hints claiming sequence 1 for the same mistake.
"Latest hint per question" then becomes non-deterministic (OrderByDescending
on a tie), so a child can see different hints on successive reads.
```

This is a **data-integrity regression** introduced by the partial migration — the schema was *worse* after it than before.

## 3.6 What the database has that the code does not use

- `Assessment.Topics` / `Categories` — read only as FK targets. **No endpoint exposes them**, so `UserTopicStats.TopicId` is unlabelled (MISSING FEATURE, P2).
- `Users.ParentChildLinks` — **no code reads or writes it**. Keep the table; do not build on it until the feature exists.
- `QuizAttempts.Status = 'Abandoned'` — permitted by the CHECK, **never written**.
- `QuizAttempts.DurationSeconds`, `WrongAnswersCount`, `UserTopicStats.WrongCount` — computed columns, correctly never assigned. **No change required.**

## 3.7 What is correct and must not be changed

**VERIFIED correct** across DB and code:

1. `QuizAttemptQuestions` snapshot (`TopicId`, `Difficulty`, `CorrectOptionId`, `QuestionType`) — grading and statistics survive content edits.
2. `RowVersion timestamp` + EF concurrency token — double submit yields exactly one completion.
3. `UQ_QuizAttempts_PreviousAttemptId` — one retry per attempt, enforced by the database.
4. Composite FKs `(QuestionId, SelectedOptionId)` and `(QuestionId, CorrectOptionId)` → `QuestionOptions(QuestionId, Id)`.
5. `UserId uniqueidentifier` in both `QuizAttempts` and `UserTopicStats`, matching `Guid` — the INT→GUID migration is fully consistent.
6. `LearningContent` schema — `ContentDbContext` maps all four tables to `LearningContent` **correctly**. I checked this specifically because it was the most likely place for a silent schema break. **PASS.**
7. All 9 `CK_QuizAttempts_*` and 5 `CK_UserTopicStats_*` constraints present and mirrored in EF.
8. `Users.Role` CHECK now includes `'Admin'` — the schema half of the audit's C-4 is **resolved**.

---

# 4. EF CORE / DATABASE-FIRST AUDIT

**This project is Database-First. The database is truth. The EF model must be brought back to it — not the reverse, except where the database is genuinely missing something the product requires.**

Two categories, handled differently:

| Category | Items | Direction |
|---|---|---|
| **Database is missing a required product feature** | essay table, 4 translation tables, image columns, nullable `OptionText`, hint uniqueness | **Add to the database** (§13 SQL) |
| **Code drifted from the database** | `Languages` schema + `IsActive`, `QuestionTranslations` shape, `SelectedOptionId` nullability, `QuestionType` lengths, `LanguageCode` type/default, EssayHasNoKey text | **Fix the EF model** (§14) |

**No Code-First migration is recommended anywhere in this report.**

---

# 5. MODULE-BY-MODULE AUDIT

| Module | Owns | DbContext | Cross-context access? | Verdict |
|---|---|---|---|---|
| **Identity** (`UsersBL`/`UsersDA`) | `Users.*` | `UsersDbContext` | **No** | **PASS** — boundaries clean |
| **Content** (`ContentBL`/`ContentDA`) | `LearningContent.*` | `ContentDbContext` | **No** | **PASS** on boundaries; schema mapping verified correct |
| **Assessment** (`AssessmentBL`/`AssessmentDA`) | `Assessment.*` | `AssessmentDbContext` | **No** | **FAIL** — model does not match the database |
| **AIIntegration** | nothing | none | n/a | **FAIL** — unconfigured, no key, no provider |
| **Gamification** | — | — | — | **Does not exist.** No XP, streak or badge in any table, entity, service or endpoint |
| **Shared** | contracts + utilities | none | n/a | **PASS** |

**ARCHITECTURAL VIOLATIONS: none found.** Verified: no service references another module's `DbContext`, and the project references make it impossible. Dependencies form a DAG; **no circular dependency**. Business logic is in services, not controllers. `Shared.Assessment.AI` is a legitimate contract seam.

**Do not restructure.** The architecture is not the problem.

---

# 6. CROSS-MODULE INTEGRATION AUDIT

```
Identity ──JWT sub claim──▶ Content        ✅ works
Identity ──JWT sub claim──▶ Assessment     ✅ works (ownership verified)
Content  ─────lessonId────▶ Assessment     ❌ BROKEN — no endpoint exists
Assessment ───────────────▶ AI             ❌ BROKEN — unconfigured
Assessment ───────────────▶ Statistics     ⚠️ works, but excludes essays correctly
Anything ─────────────────▶ Gamification   n/a — does not exist
```

**MISSING FEATURE, P1, VERIFIED — Content → Assessment has no path.** `GET /api/quizzes` is `[Authorize(Roles="Admin")]`; no Content response exposes a quiz id. **A child cannot obtain a `quizId`, so the core product loop has no entry point.** Fix in §14.

---

# 7. END-TO-END BUSINESS FLOW AUDIT

Traced HTTP → controller → service → EF → SQL for every flow.

| Flow | Status | Blocking issue |
|---|---|---|
| Register / Login / Guest / Google / Refresh / Logout | **PASS** | Google-email collision → 500 (P2) |
| Password reset / OTP | **PASS** functionally | OTP logged in plaintext (B-8); `Random.Shared` not CSPRNG (P2) |
| Current user, update profile | **PASS** | GET returns 404, PUT returns 400 for the same condition (P4) |
| Admin role | **PASS now** — DB accepts `'Admin'` | `NormalizeRole` still cannot produce it (P1, §9) |
| Levels / Lessons / LessonContent | **PASS** | Unpublished lessons served to everyone (P2) |
| Lesson completion | **MISSING** | No progress model exists anywhere |
| Lesson → Quiz navigation | **FAIL** | §6 |
| **Quiz start** | **FAIL — P0** | `SELECT ... ImageUrl ... QuestionTranslations.QuestionText ... QuestionOptionTranslations` → invalid column / invalid object |
| **Answer submission (MC / TF)** | **FAIL — P0** | same projection is used to build retry questions |
| **Answer submission (Essay)** | **FAIL — P0** | `QuizAttemptEssayAnswers` does not exist |
| **Quiz completion / scoring** | logic correct, unreachable | blocked by the above |
| Mistakes | **PASS** (schema present) | reachable only after the P0s |
| Hints | **FAIL** | AI unconfigured (B-6); uniqueness gone (B-7) |
| User topic statistics | **PASS** | N+1 + unbounded scan (§10) |
| Retry | logic correct, unreachable | |
| Concurrent submission | **PASS — verified correct** | RowVersion; loser writes nothing |
| AI request/response/parse/validate | **PASS** as code | never executes — unconfigured |
| AI failure handling | **FAIL** | AI failure loses the child's completed quiz (§11) |
| Localization | **FAIL — P0** | translation tables missing/mismatched |

---

# 8. CONTROLLER, DTO & API CONTRACT AUDIT

**Controllers: PASS.** All 8 are thin — constructor injection, one service call, one status mapping. `CancellationToken` accepted and forwarded on **every** action. All async. **No entity is ever returned.** No endpoint lets a client set attempt status, score or counts.

**DTOs: PASS on safety.** `IsCorrect` is *structurally absent* from `QuizAnswerOptionDto` — it cannot leak. `correctOptionId` is likewise kept out of every child DTO. No `UserId` is accepted from any client; identity always comes from the JWT `sub` claim.

**API contract issues (all P2–P4):**

| Issue | Severity |
|---|---|
| Four response envelopes (`ApiResponse<T>` / raw DTO / `{statusCode,message}` / `ValidationProblemDetails`) | P2 |
| `DateTime` serialised two ways — in-memory carries `Z`, DB-loaded does not, on the same field | P2 |
| No validation attributes on any request DTO; `""` is an acceptable email | P2 |
| Score served exactly once — `GET /api/quiz-attempts/{id}` returns no score or status | P2 |
| `topicId` returned with no name (no topics endpoint) | P2 |
| `POST /api/question-options` returns a `Location` pointing at the list endpoint | P4 |

---

# 9. IDENTITY & SECURITY AUDIT

| Check | Status |
|---|---|
| **IDOR — any module** | **PASS.** Verified: every user-owned operation derives identity from `sub`; another user's attempt → 403 from the service layer |
| Client-supplied `UserId` | **PASS** — never accepted |
| Password hashing (BCrypt) | **PASS** |
| Refresh tokens hashed (SHA-256) + rotated + revocable | **PASS** |
| JWT validation (issuer/audience/lifetime/key) | **PASS** |
| Role claim wiring (`MapInboundClaims=false` + `ClaimTypes.Role`) | **PASS** |
| SQL injection | **PASS** — EF LINQ only, no raw SQL anywhere |
| Mass assignment / over-posting | **PASS** — explicit DTOs, no entity binding |
| **Secrets in git** | **FAIL — B-5.** `appsettings.Development.json` is tracked and holds `Jwt:SecretKey`, the DB connection string and the SMTP password; `.gitignore` has no `appsettings` rule. **Anyone with repo access can mint a token with `role: Admin`** — and the DB now accepts that role |
| **Secrets in logs** | **FAIL — B-8.** Plaintext reset OTP + email `Console.WriteLine`d on SMTP failure |
| `Admin` grantable | **FAIL — P1.** DB now allows it; `NormalizeRole` still returns only Parent/Child |
| Rate limiting | **FAIL — P2.** None anywhere: login brute force, OTP bombing, anonymous guest flooding |
| Refresh-token reuse detection | **FAIL — P2** |
| Concurrent refresh | **FAIL — P2.** Two simultaneous refreshes both succeed |
| Reset revokes sessions | **FAIL — P2.** Tokens survive a password reset for up to 30 days |
| OTP randomness | **FAIL — P2.** `Random.Shared`, not CSPRNG |
| CORS | Not configured — P3 (mobile unaffected; Flutter web blocked) |
| AI prompt injection | **PASS** — essay text is never sent to AI; only admin-authored content is |
| AI data privacy | **PASS** — zero child identifiers leave the system |

---

# 10. PERFORMANCE AUDIT

| # | Issue | Class | Location |
|---|---|---|---|
| P-1 | **N+1 + sync-over-async.** `attemptQuestions` materialized, then `_db.QuizAttemptMistakes.Any(...)` runs **one blocking query per question** inside the submit transaction | **High** | `UserTopicStatService.UpdateAfterQuizAttemptAsync` |
| P-2 | Aggregates **every hint the user has ever received** across a 3-table join, on every submit | **High** | same method |
| P-3 | AI call in the request path with **no timeout** — `HttpClient`'s 100 s default | **Critical** | `HttpExternalAiProvider` |
| P-4 | `RefreshTokens.TokenHash` unindexed — table scan on every login/refresh/logout | **High** | `Users.RefreshTokens` |
| P-5 | Localized projection adds **4 correlated subqueries per question and per option** | **Medium** | `ProjectQuestions` |
| P-6 | Content repositories never use `AsNoTracking()` | **Medium** | `ContentDA/Repositories/*` |
| P-7 | `GetMaxSortOrderAsync` issues `AnyAsync` + `MaxAsync` | **Low** | |
| P-8 | `ContentTypes`/`Levels` re-read constantly — `IMemoryCache` is sufficient, **not Redis** | **Low** | |

**Verified absent:** lazy loading (no proxy package), cartesian explosion, raw SQL. Pagination exists on `GET /api/quizzes` only — acceptable at current volumes; **do not add it speculatively.**

---

# 11. TRANSACTION, CONCURRENCY & AI CONSISTENCY

**Transaction boundaries: PASS.** One explicit transaction in `SubmitAsync`; no nesting; `BeginTransactionAsync` is called **after** the AI returns, so **no external call is inside a transaction** — verified.

**Concurrency: PASS.** Verified by reading the generated behaviour: EF appends `WHERE RowVersion = @rv`, the loser affects 0 rows, throws `DbUpdateConcurrencyException`, and its whole transaction rolls back. **No double completion, no duplicate mistakes, no corrupted statistics.**

## ❌ The one hard failure — AI availability gates submission

Your stated requirement:

> AI availability must NOT be a prerequisite for saving a completed quiz attempt.

**VERIFIED VIOLATED.** The AI call runs *before* the transaction opens. Any AI failure means nothing is written and the child's completed quiz is **lost**. Today, with `HintsEndpoint` empty, that is *every* submission containing a wrong answer.

**Required fix (RECOMMENDED, not implemented):**

```
BEGIN TRANSACTION
  mistakes · essay answers · attempt→Completed (RowVersion) · UserTopicStats
COMMIT                                        ← the child's work is SAFE
   ↓
try   { hints = GenerateHints(); persist in a second short transaction }
catch { log warning; leave currentHint null }
   ↓
200 either way
```

`currentHint` is already `string?`, so **no DTO contract change**. Consequences to accept: `HintsUsedCount` under-counts by one attempt; a "top up missing hints" path on retry-start becomes worthwhile.

---

# 12. ERROR HANDLING, LOGGING & TESTING

**Error handling.** `ExceptionMiddleware` maps KeyNotFound→404, Conflict→409, Argument/InvalidOperation→400, Unauthorized→403, else 500 with a fixed Arabic string. **No stack trace, SQL, connection string or secret is ever returned** — PASS. Three defects:

- `InvalidOperationException → 400` is too broad; EF and the BCL throw it for genuine faults, which would surface as a misleading 400. **Introduce `BusinessRuleException`.** (P2)
- `GetUserId()` throws `InvalidOperationException` when `sub` is missing → **400 instead of 401**; a malformed `sub` → `FormatException` → **500**. (P2)
- Identity unique-violations (duplicate registration, Google-email collision) are unhandled → **500 instead of 409**. (P2)

**Logging — FAIL, P2.** `ILogger` is injected in **exactly one place** (`ExceptionMiddleware`). Nothing logs authentication failures, authorization failures, AI failures, or concurrency conflicts. There is one `Console.WriteLine`, and **it prints a secret**. No health checks.

**Testing — FAIL for production, P2.** 52 tests, and they are **not** just deserialization: they include EF model-invariant assertions, language normalization, and — genuinely valuable — an **HTTP-intercepting AI contract suite** that pins the exact outgoing request and every failure mapping without a network call.

**But the gap is decisive:**

| Not tested | Consequence |
|---|---|
| **Any query against a real database** | **This is why B-1/B-2/B-3 shipped undetected.** A single integration test hitting a real or LocalDB instance would have failed immediately |
| Authorization / IDOR scenarios | 403 paths unverified |
| Concurrency (double submit) | correctness argued, never reproduced |
| Controllers end-to-end | no `WebApplicationFactory` test exists |
| Database constraints | no test asserts a CHECK actually rejects bad data |
| AI-down → submit still 200 | cannot pass today; correctly **not written** |

**The single highest-value test to add:** a `WebApplicationFactory` integration test that starts an attempt against a migrated database. It would have caught all three P0s.

---

# 13. REQUIRED SQL CHANGES

Written to **[db/migrations/003_align_schema_to_code.sql](db/migrations/003_align_schema_to_code.sql)**. Every statement is idempotent and additive; **no column is dropped and no row is deleted.**

Each change below follows: Current Problem → Required Change → Why → SQL → Data Impact → EF Impact.

### 13.1 Missing image columns (B-2)

- **Current problem:** `Questions` and `QuestionOptions` have no `ImageUrl`; EF selects it on every read.
- **Why:** the product requires optional question and option images.
- **Data impact:** none — nullable additions.
- **EF impact:** none; the entities already have the properties.

### 13.2 `OptionText` must become nullable (M-1)

- **Current problem:** `NOT NULL` in the DB, `NULL` in EF. An image-only option cannot be inserted.
- **Why:** an option may be text-only, image-only, or both — never neither.
- **Data impact:** widening only; every existing row has text, so the guard CHECK passes on all of them.
- **EF impact:** none.

### 13.3 `QuizAttemptEssayAnswers` (B-1)

- **Current problem:** the table does not exist; essay submission throws.
- **Why:** `QuizAttemptMistakes.SelectedOptionId` carries a composite FK to `QuestionOptions` and cannot represent free text; `QuizAttemptQuestions` is written once at start and must stay immutable.
- **Data impact:** new table.
- **EF impact:** none; the entity and configuration exist.

### 13.4 Translation tables — **adopting the database's design, not mine** (B-3)

- **Current problem:** `QuestionTranslations` exists as scoped EAV `(QuestionId, LanguageCode, Field, Value)`; the entity expects typed columns; the four sibling tables are absent.
- **Decision:** **the database wins.** It is the source of truth, the EAV shape generalizes to multiple fields per entity with one table each, and changing five entities is a smaller, safer change than rewriting a table that already holds data.
- **The one real risk of EAV, mitigated:** a typo in `Field` silently yields no translation and a silent fallback. **A CHECK constraint on the permitted field names removes that risk** — included in the SQL.
- **Data impact:** existing `QuestionTranslations` rows are untouched.
- **EF impact:** **five entities and their configurations must be rewritten** — see §14.

### 13.5 Restore hint uniqueness (B-7)

- **Current problem:** `QuestionHints` has only a PK. Duplicate sequences are possible; "latest hint" is non-deterministic.
- **Why:** this is a regression the partial migration introduced.
- **Data impact:** ⚠ **the script checks for existing duplicates first and will not create the index until they are resolved.**
- **EF impact:** none.

### 13.6 Align `LanguageCode` type and default (M-3)

- **Current problem:** `varchar(5)` default `'en'` vs EF `nvarchar(5)` default `'ar'`.
- **Decision:** **change EF to match the database** (`varchar`, `'en'`) — a type change on a populated column is riskier than a config change, and `'en'` is a defensible default.
- **EF impact:** `HasColumnType("varchar(5)")` + `HasDefaultValue("en")`.

### 13.7 Restore `SelectedOptionId NOT NULL` (M-2)

- **Current problem:** nullable in DB, non-nullable in EF. A NULL row would throw on materialization, and the composite FK is unenforced for NULLs.
- **Decision:** **tighten the database.** No code path writes NULL, and NOT NULL restores the FK guarantee. The script verifies zero NULLs first.
- **Data impact:** fails safely if any NULL exists.

### 13.8 `RefreshTokens.TokenHash` index (P-4)

- **Why:** every login, refresh and logout table-scans a table that gains a row per authentication and retains 30 days.

### 13.9 Not changed, deliberately

```
No schema change required for: QuizAttempts, UserTopicStats, Topics, Categories,
Quizzes, QuizAttemptQuestions (beyond §13.7's sibling table), LearningContent.*,
or the computed columns. Their constraints are correct and complete.
```

`CK_QuizAttemptQuestions_EssayHasNoKey` — **keep the database's stricter version.** Update the EF config text to match it, not the reverse.

---

# 14. REQUIRED CODE CHANGES

| # | Change | Files | Severity |
|---|---|---|---|
| C-1 | **Rewrite 5 translation entities + configs to the EAV shape**, and rewrite `ProjectQuestions` to read `Field`/`Value` | `AssessmentDA/Entities/*Translation.cs`, `LocalizationConfigurations.cs`, `QuizAttemptService.ProjectQuestions` | **P0** |
| C-2 | Map `Language` to **`dbo.Languages`** and **remove `IsActive`** | `LocalizationConfigurations.cs`, `Language.cs` | **P0** |
| C-3 | `LanguageCode` → `varchar(5)`, default `"en"`; `ContentLanguages.Default` → `"en"` | `QuestionHintConfiguration.cs`, `ContentLanguages.cs` | **P1** |
| C-4 | `QuestionType` `HasMaxLength(30)` in both configs | `QuestionConfiguration`, `QuizAttemptQuestionConfiguration` | P3 |
| C-5 | **Commit the attempt before calling AI**; swallow AI failure | `QuizAttemptService.SubmitAsync` | **P0** |
| C-6 | Rotate secrets; untrack `appsettings.*.json`; add `.gitignore` rule; User Secrets + env vars | config | **P0** |
| C-7 | Populate Production config; validate required keys at startup | `appsettings.Production.json`, `Program.cs` | **P0** |
| C-8 | Delete the OTP `Console.WriteLine` | `AuthService.ForgotPasswordAsync` | **P0** |
| C-9 | Add `Admin` to `UserRoles` + a deliberate grant path (**never** from `RegisterEmailRequest.Role`) | `UsersBL/Constants.cs` | P1 |
| C-10 | Add `GET /api/quizzes/for-lesson/{lessonId}` `[Authorize]` | new controller + `IQuizService` | P1 |
| C-11 | Implement a real AI provider (timeout, key, prompt, structured output, safety guard) | `AIIntegration/*` | P1 |
| C-12 | Fix the N+1 and the unbounded hint scan | `UserTopicStatService` | P2 |
| C-13 | `GetUserId()` → 401, not 400/500 | `ClaimsPrincipalExtensions`, middleware | P2 |
| C-14 | Filter `IsPublished` for non-admins | `LessonService` | P2 |
| C-15 | Rate limiting, DTO validation attributes, CSPRNG OTP, logging, health checks | various | P2 |

---

# 15. SYSTEM HEALTH SCORE

| Area | Score | Justification |
|---|---|---|
| **Database** | **72** | Excellent constraint discipline, computed columns, composite FKs, RowVersion. Loses points for the lost hint uniqueness, missing image columns, missing essay table, and 4 missing translation tables |
| **EF Core / Data Access** | **38** | Assessment queries are well written (`AsNoTracking`, projection, no lazy loading) — but the model does not match the database, which is disqualifying |
| **Architecture** | **88** | Genuinely modular, enforced by project references. No cross-context access, no circular dependency, thin controllers. Deductions only for `Shared` drift and the missing Content→Assessment seam |
| **Identity / Security** | **45** | Strong fundamentals (BCrypt, hashed+rotated refresh tokens, no IDOR, no SQL injection) destroyed by a committed signing key and a logged OTP |
| **Content** | **65** | Correct schema mapping, clean boundaries. No progress model; unpublished lessons leak; ordering has no uniqueness constraint |
| **Assessment** | **55** | The best-designed domain in the system — snapshot, concurrency, retry uniqueness all correct — but currently **non-functional** against this database |
| **AI Integration** | **20** | Correct shape and excellent privacy posture (zero child PII). No provider, no key, no prompt, no timeout, and it can fail the core flow |
| **API Design** | **62** | Thin controllers, no entity leakage, `IsCorrect` structurally safe. Four response envelopes and two DateTime formats |
| **Performance** | **50** | N+1 with blocking calls in the hottest write path, unbounded historical scan, unindexed auth lookup, no AI timeout |
| **Testing** | **40** | 52 meaningful tests including a genuinely good AI-contract suite — but **zero database integration tests**, which is precisely why the P0s shipped |
| **Configuration / Deployment** | **15** | Production cannot start. Secrets in git. AI unconfigured. No health checks, no Docker, no documented deploy path |
| **Production Readiness** | **18** | Three P0 schema mismatches, cannot start in Production, and a leaked signing key |
| **OVERALL** | **47 / 100** | A well-architected system that is currently not deployable |

---

# 16. FINAL END-TO-END STATUS

| Area | Status | Main issue |
|---|---|---|
| Database | ⚠️ **PARTIAL** | Sound design; missing 6 tables/3 columns the code needs; hint uniqueness lost |
| EF Core | ❌ **FAIL** | Model does not match the database — 3 P0 mismatches |
| Identity | ⚠️ **PARTIAL** | Logic correct; signing key committed; OTP logged |
| Content | ✅ **PASS** | Schema mapping verified correct; no progress model (missing feature) |
| Assessment | ❌ **FAIL** | Excellent design, non-functional against this schema |
| AI | ❌ **FAIL** | Unconfigured; failure loses the child's completed quiz |
| API | ⚠️ **PARTIAL** | Thin and safe; four response envelopes; no quiz discovery |
| Security | ❌ **FAIL** | Committed JWT key ⇒ forgeable `Admin` tokens |
| Performance | ⚠️ **PARTIAL** | N+1 + blocking calls in submit; no AI timeout |
| Testing | ❌ **FAIL** | No database integration test — the reason the P0s exist |
| Production Config | ❌ **FAIL** | Application cannot start in Production |
| Deployment | ❌ **FAIL** | No health checks, no Docker, no migration runbook |

---

# 17. PRIORITIZED IMPLEMENTATION PLAN

### Phase 0 — stop the bleeding (hours)
1. **Rotate** the JWT secret, DB password and SMTP password. Untrack `appsettings.*.json`, add the `.gitignore` rule, move secrets to User Secrets / environment variables. *(C-6)*
2. Delete the OTP `Console.WriteLine`. *(C-8)*

### Phase 1 — make it run (days)
3. Run **`003_align_schema_to_code.sql`** — resolve the duplicate-hint gate first. *(§13)*
4. Rewrite the 5 translation entities to the EAV shape; map `Language` to `dbo.Languages`; align `LanguageCode` and `QuestionType`. *(C-1…C-4)*
5. Populate Production config + startup validation. *(C-7)*
6. **Add one `WebApplicationFactory` integration test that starts an attempt against a migrated database.** Without it, Phase 1 cannot be shown to be complete.

### Phase 2 — make it correct (days)
7. Commit the attempt before the AI call; degrade gracefully. *(C-5)*
8. Grantable `Admin`. *(C-9)*
9. Quiz discovery by lesson. *(C-10)*
10. 401 vs 400/500 for auth failures; 409 for identity unique-violations. *(C-13)*

### Phase 3 — make it safe (week)
11. Real AI provider with timeout, key, prompt, structured output, safety guard. *(C-11)*
12. Rate limiting, DTO validation, CSPRNG OTP, `IsPublished` filtering. *(C-14, C-15)*

### Phase 4 — make it fast and observable
13. Fix the N+1 and the hint scan; `RefreshTokens.TokenHash` index; `AsNoTracking` on Content. *(C-12)*
14. Practical logging + health checks.

### Phase 5 — close the gaps
15. Progress persistence, topics endpoint, attempt history, result re-read.

---

# 18. REMAINING RISKS & UNVERIFIED ITEMS

**UNVERIFIED — cannot confirm from the supplied material:**

- **Standalone indexes.** The scripts you supplied contain no `CREATE INDEX` statements at all, yet the original DDL created several (`IX_Topics_CategoryId`, `IX_Quizzes_*`, `IX_QuizAttempts_UserId_QuizId_StartedAt`, `IX_Questions_TopicId`, `IX_UserTopicStats_TopicId`, `IX_QuizAttemptMistakes_QuestionId`, and the filtered `UQ_QuestionOptions_OneCorrectPerQuestion`). SSMS table scripting appears to have omitted them. **I cannot confirm whether they exist.** Run `SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID('Assessment.QuestionOptions')` — the filtered one matters most, because "at most one correct option" depends on it.
- **`Users.Users`, `Users.RefreshTokens`, `Users.PasswordResetOTPs`** — the paste was truncated at `ParentChildLinks`. Identity findings rest on the earlier DDL plus the code; the live shape is unconfirmed.
- **Whether the database contains production data.** Every script is written to be safe either way.
- **`LearningContent` ordering constraints** — not visible in the supplied scripts.
- **Deployment surface** — no Dockerfile, CI config or runbook was supplied. *Cannot verify — required files not provided.*

**Risks that remain after every fix above:** no backup/restore strategy is evident; no migration runbook exists (migrations are hand-run SQL with manual gates); and the AI provider choice is still unmade, so hint quality and cost are unknown.

---

## The one-sentence answer

**The system is architecturally sound and functionally broken:** the code and the database are two different systems, and no test in the suite touches a real database — which is exactly why nobody noticed.
