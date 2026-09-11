# ElectroWorld Backend — Technical & Business Audit

**Scope:** full solution on `basmala-dev` @ `a60a5f8` — Identity, Content, Assessment, AI Integration, Shared, API host, SQL schema, EF configurations.
**Method:** read the actual code and configuration. Where this report states behaviour, it was traced through the source, not inferred from names.
**Constraint honoured:** no Clean Architecture, CQRS, MediatR, domain events, or repository pattern is recommended anywhere in this document. The modular monolith is the right shape for this product and is kept.

---

# PART 1 — EXECUTIVE SYSTEM AUDIT

## 1.1 What the system actually does

ElectroWorld is a children's electricity-and-circuits learning backend. Three business modules over **one SQL Server database with one schema per module** (`Users`, `Assessment`, and the Content tables), each with its own `DbContext`:

| Module | Owns | Real surface |
|---|---|---|
| **Identity** (`UsersBL`/`UsersDA`) | `Users`, `RefreshTokens`, `PasswordResetOTPs`, `ParentChildLinks` | 11 endpoints: guest/email/Google auth, refresh, logout, password reset, profile |
| **Content** (`ContentBL`/`ContentDA`) | `Levels`, `Lessons`, `LessonContents`, `ContentTypes` | 19 endpoints: a Level → Lesson → LessonContent tree plus image upload |
| **Assessment** (`AssessmentBL`/`AssessmentDA`) | `Categories`…`UserTopicStats` (10 tables) | 19 endpoints: quiz authoring (admin) + attempt/submit/retry (child) |
| **AIIntegration** | nothing | no endpoints — one internal HTTP call for hint generation |

**A finding that reframes the rest of this report:** the product is *not* currently runnable end-to-end. Three independent configuration defects (§1.9 C-1, C-2, C-3) mean the app cannot start in Production, cannot generate a hint in Development, and ships a JWT signing key in git. These are configuration, not architecture — the architecture is largely sound.

## 1.2 Main business flows

Two, and only two, real flows exist:

```
A. Learn:    authenticate → list levels → list lessons → read lesson contents
B. Assess:   authenticate → start attempt → answer locally → submit → score + AI hints → one retry
```

Everything else is admin authoring. There is **no progress flow, no XP, no streak, no badge, and no gamification module** — those concepts appear in no entity, table, service or endpoint. Flow B is the product's centre of gravity and where nearly all engineering value sits.

## 1.3 How the modules interact

Traced, not assumed:

- **No module queries another module's `DbContext`.** `AssessmentBL` touches only `AssessmentDbContext`; `ContentBL` only `ContentDbContext`; `UsersBL` only `UsersDA`. **No change required** — this is the single most important rule of a modular monolith and it is genuinely respected.
- The only cross-module coupling is **identity by value**: every module reads `Guid userId` from the JWT `sub` claim. No module calls Identity at runtime; token validation is framework middleware.
- `Assessment.Quizzes.LevelId` / `LessonId` are **loose integer references to Content with no FK**, deliberately. Correct for this architecture.
- `Shared` holds JWT/Google/hashing/clock/email plus the AI contracts (`Shared.Assessment.AI`). The AI contract placement is a legitimate seam.

**There are no circular dependencies.** Project references form a DAG: `API → {UsersBL, ContentBL, AssessmentBL, AIIntegration} → {*DA, Shared}`.

## 1.4 Does the architecture match the business requirements?

**Mostly yes — NO ISSUE on the big structural calls.** Three-tier + modular monolith + DTOs + services is proportionate to a two-flow product. Introducing CQRS or repositories here would add cost and no capability.

Two genuine mismatches:

1. **The Content module cannot express the product's own learning loop.** There is no progress, no completion, no unlocking, no prerequisite. A child can read any lesson in any order forever, and the backend has no memory that they did. For a *learning* app, this is a missing business capability, not a missing nice-to-have. (IMPORTANT — §1.11)
2. **Assessment is unreachable from Content.** No endpoint maps a lesson or level to its quiz, and quiz listing is admin-only. The two modules that form the product's core loop are not connected in the direction the app traverses them. (CRITICAL — §1.7)

## 1.5 Are the module boundaries correct?

**Yes. NO ISSUE.** Data ownership is clean, `DbContext` isolation is real, and the loose cross-module reference is the appropriate mechanism at this scale. Two boundary observations that are *not* defects but are worth naming:

- `Shared` mixes truly-common utilities (`IDateTimeProvider`, `IHashGenerator`) with identity-specific types (`JwtTokenGenerator`, `GoogleAuthValidator`). It is not yet a dumping ground, but it is on the path. HARDENING.
- The Content module uses a repository + UnitOfWork layer while Assessment and Identity query `DbContext` directly. This inconsistency is cosmetic and **not worth fixing** — rewriting working Content code to match Assessment would be churn with no behavioural benefit. **No change required.**

## 1.6 Major architectural risks

| ID | Risk | Class |
|---|---|---|
| A-1 | AI availability is a hard dependency of assessment submission — one external service being down breaks the product's primary flow (§Part 13) | **CRITICAL** |
| A-2 | Two `DbContext`s carry hardcoded `OnConfiguring` fallback connection strings pointing at **different databases** (`Volt` vs `VoltDB`) with `TrustServerCertificate=True` | **IMPORTANT** |
| A-3 | No cross-module contract exists for Content → Assessment, so the app's core loop has no server-side path | **CRITICAL** |
| A-4 | `Shared` accreting module-specific types | HARDENING |

## 1.7 Major business-flow gaps

| ID | Gap | Class |
|---|---|---|
| B-1 | **No way for a child to discover a quiz.** `GET /api/quizzes` is `[Authorize(Roles="Admin")]`; no Content response exposes a quiz id. Flow B has no entry point. | **CRITICAL** |
| B-2 | **No progress persistence.** Nothing records a completed lesson or level. | **IMPORTANT** |
| B-3 | **No attempt history.** A lost `attemptId` is unrecoverable; a finished score can never be re-read (`GetByIdAsync` returns questions only). | **IMPORTANT** |
| B-4 | `Abandoned` attempt status is defined in the schema and CHECK constraint but **never written by any code path**. Closing the app leaves an attempt `InProgress` forever, and it still blocks nothing — but it also never gets cleaned up. | HARDENING |
| B-5 | No topic names anywhere — `UserTopicStats.TopicId` is returned unlabelled and `Topics` has no endpoint. Statistics are unrenderable. | **IMPORTANT** |

## 1.8 Major database risks

| ID | Risk | Class |
|---|---|---|
| D-1 | `RefreshTokens.TokenHash` has **no index**. Every login, refresh and logout table-scans a table that grows one row per authentication and retains 30 days. | **IMPORTANT** |
| D-2 | Content ordering has **no unique constraint** on `(LevelId, SortOrder)` or `(LessonId, SortOrder)`, and `SortOrder` is assigned by a read-then-write (`GetMaxSortOrderAsync`). Duplicate ordering is reachable under concurrency. Assessment does this correctly; Content does not. | **IMPORTANT** |
| D-3 | Unbounded growth with no cleanup: guest `Users` (anonymously creatable, see S-3), `RefreshTokens`, `PasswordResetOTPs`. | **IMPORTANT** |
| D-4 | `PasswordResetOTPs.ResetTokenHash` is queried but unindexed. | HARDENING |

Everything else in the Assessment schema is **correct and should not be changed** — see §1.12 and Part 7.

## 1.9 Major security risks

| ID | Risk | Class |
|---|---|---|
| **C-3 / S-1** | **The JWT signing key is committed to git.** `appsettings.Development.json` is tracked and contains `Jwt:SecretKey` (58 chars), the DB connection string, and the SMTP password. `.gitignore` has no `appsettings` rule. Anyone with repo read access can **mint a valid token for any `userId` with any `role` — including `Admin`**, which unlocks all 24 admin endpoints that no legitimate user can reach. This is a full privilege-escalation and impersonation path. | **CRITICAL** |
| S-2 | **The plaintext password-reset OTP is written to stdout.** `AuthService.ForgotPasswordAsync` catches SMTP failure and `Console.WriteLine`s the email address and the OTP. Anyone who can read logs can reset any account. | **CRITICAL** |
| S-3 | `POST /api/auth/guest` is anonymous, unthrottled, and writes a `User` + a `RefreshToken` per call. Unbounded remote row insertion. | **IMPORTANT** |
| S-4 | **No rate limiting anywhere.** Login is brute-forceable; OTP request is an email-bombing vector. (The 5-attempt cap protects one OTP, not the endpoint.) | **IMPORTANT** |
| S-5 | Password reset does not revoke refresh tokens — an attacker keeps access for up to 30 days after the victim resets. | **IMPORTANT** |
| S-6 | `RegisterEmailRequest.Role` lets a client self-assign `Parent`. Harmless today (no endpoint checks `Parent`), latent if `ParentChildLinks` is ever used for authorization. | HARDENING |
| S-7 | Unpublished lessons are served to every authenticated user — neither lesson endpoint filters `IsPublished`. | **IMPORTANT** |
| S-8 | Uploaded images are served by `UseStaticFiles()` with no auth; URLs are guessable-by-leak, not enumerable (GUID names). Acceptable for lesson art. | NO ISSUE |
| S-9 | No CORS policy configured. Mobile Flutter is unaffected; a Flutter **web** build cannot call the API at all. | HARDENING |

**Verified clean and requiring no change:** no IDOR in any module (every user-owned operation derives identity from `sub`); no client-supplied `UserId` accepted anywhere; no entity is ever returned from a controller; `IsCorrect` never reaches a child-facing response; BCrypt is used correctly for passwords; refresh tokens are stored **hashed** (SHA-256) and rotated on use.

## 1.10 Major performance risks

| ID | Risk | Class |
|---|---|---|
| P-1 | **N+1 plus sync-over-async in `UserTopicStatService`.** `buckets` is computed over a materialized list, but the predicate calls `_db.QuizAttemptMistakes.Any(...)` — one **blocking** DB round trip per question, on every submit. | **IMPORTANT** |
| P-2 | The same method loads **every hint the user has ever received**, unfiltered by attempt, on every submit. Cost grows with account age. | **IMPORTANT** |
| P-3 | The AI call sits in the submit request path with **no timeout configured** — `HttpClient`'s 100-second default. A hung provider holds a request thread for 100s. | **IMPORTANT** |
| P-4 | Content repositories never use `AsNoTracking()`; every read materializes tracked entities. Assessment does this correctly. | HARDENING |
| P-5 | `GetMaxSortOrderAsync` issues `AnyAsync` + `MaxAsync` (two round trips) where `MaxAsync(x => (int?)x)` is one. | HARDENING |
| P-6 | No caching of `ContentTypes` / `Levels` — near-static data read on every app open. In-memory is sufficient; **Redis is not warranted**. | HARDENING |

## 1.11 Missing functionality

**Must have (blocks the product):** child-facing quiz discovery (B-1); AI graceful degradation (A-1); topic names (B-5).
**Should have:** progress persistence (B-2); attempt history / result re-read (B-3); rate limiting (S-4); guest cleanup (D-3).
**Future:** `Abandoned` handling, content versioning, audit logging, soft deletion, health checks.

## 1.12 Things that are already correct — do NOT change

Stated explicitly, because a rewrite here would destroy real value:

1. **The attempt-time snapshot on `QuizAttemptQuestions`** (`TopicId`, `Difficulty`, `CorrectOptionId`) — grading and statistics are historically correct under content edits. This is the strongest design decision in the codebase.
2. **`RowVersion` optimistic concurrency on `QuizAttempts`** — a double submit yields exactly one completion; the loser writes nothing.
3. **`UQ_QuizAttempts_PreviousAttemptId`** — enforces one retry per attempt at the database, not in application code.
4. **The composite FKs** `(QuestionId, SelectedOptionId)` and `(QuestionId, CorrectOptionId)` → `QuestionOptions(QuestionId, Id)` — the DB itself rejects an option belonging to another question.
5. **`IsCorrect` is structurally absent** from `QuizAnswerOptionDto` — not filtered, absent. It cannot leak.
6. **Refresh tokens hashed + rotated**; passwords BCrypt-hashed; user-enumeration protection on forgot-password.
7. **`DbContext` isolation between modules.**
8. **The loose `LevelId`/`LessonId` reference with no FK** — correct cross-module coupling.
9. **AI receives no child PII** — only question text, wrong option text, and prior hints.
10. **Computed columns** (`WrongAnswersCount`, `WrongCount`, `DurationSeconds`) — SQL Server owns them; the app never assigns them.

---

# PART 2 — COMPLETE BUSINESS FLOW

## 2.1 Authentication flows (traced)

### Guest

```
POST /api/auth/guest  {fullName, role?}          [Anonymous]
  → AuthController.RegisterGuest
  → AuthService.RegisterGuestAsync
      NormalizeRole(role)  →  "Parent" only if literally "Parent"; else "Child"
      INSERT Users (Id=NewGuid, AuthProvider='Guest', PasswordHash=NULL, Email=NULL)
  → IssueTokensAsync
      JWT{sub, role, authProvider, jti}
      INSERT RefreshTokens (TokenHash=SHA256(plain), ExpiresAt=+30d)
  → 200 ApiResponse<AuthResponse>
```

- **Failure:** none. No validation path exists — the method cannot fail.
- **Sent twice:** two *different* guest accounts. Not idempotent, and not required to be.
- **Risk:** anonymous unbounded row creation (S-3).

### Email register

```
POST /api/auth/register  {email, password, fullName, role?, age?, existingGuestUserId?}
  → EmailExistsAsync?  → 400 "البريد الإلكتروني مستخدم بالفعل"
  → if existingGuestUserId:
        load user; must exist AND AuthProvider=='Guest'  → else 400
        UPDATE same row: Email, PasswordHash=BCrypt, AuthProvider='Email',
                         ConvertedFromGuestAt=now      ← same Id, progress preserved
  → else INSERT new user
  → IssueTokensAsync → 200
```

- **Guest conversion is genuinely correct**: the row is mutated in place, so every `QuizAttempt.UserId` and `UserTopicStats.UserId` keeps pointing at it. **No change required.**
- **Sent twice:** second call hits the email-exists check → 400. Safe.
- **Race:** two simultaneous registrations with the same email both pass `EmailExistsAsync`, then `UQ_Users_Email` (filtered unique) rejects one with a `DbUpdateException` → **unhandled → 500**. Should be a 409. IMPORTANT.

### Email login

```
POST /api/auth/login → GetByEmailAsync → BCrypt.Verify
  user null OR PasswordHash null OR verify fails → 401 (single message, no enumeration)
  !IsActive → 401
  → IssueTokensAsync
```

A guest can never log in here (`PasswordHash is null`) — correct.

### Google

```
POST /api/auth/google {idToken, role?, existingGuestUserId?}
  → GoogleJsonWebSignature.ValidateAsync(idToken, Audience=Google:ClientId)
      InvalidJwtException → null → 400
  → GetByProviderAsync('Google', sub)
      found     → login  (existingGuestUserId IGNORED)
      not found + existingGuestUserId → convert guest in place
      not found                        → INSERT new user
```

**Gap G-1 (IMPORTANT):** *"Google email belongs to an existing local account"* — from your list — **is not handled**. If `ahmed@x.com` registered with a password and then signs in with Google, `GetByProviderAsync` misses, so a **second `User` row** is created with the same email. `UQ_Users_Email` is a filtered unique index on `Email` → the INSERT **fails with a DbUpdateException → 500**. The user is hard-stuck, with a 500 and no explanation. There is no account-linking path.

**Gap G-2:** Google's `email_verified` is never checked; `GoogleUserInfo` does not carry it.

### Refresh (rotation)

```
POST /api/auth/refresh {refreshToken}
  hash = SHA256(incoming);  GetByHashAsync(hash)      ← UNINDEXED SCAN (D-1)
  null || RevokedAt != null || ExpiresAt <= now → 401
  storedToken.RevokedAt = now                          ← rotation
  user null || !IsActive → 401  ... but the revoke is NOT rolled back
  SaveChanges → IssueTokensAsync (new pair)
```

- **Sent twice concurrently:** both read the same active row, both pass, both set `RevokedAt`, both issue new tokens. **Two valid refresh tokens are minted from one.** No unique/concurrency guard. IMPORTANT (S-10) — the window is small but real, and mobile clients retry aggressively.
- **No reuse detection:** presenting an already-revoked token returns 401 but does not revoke the family, so a stolen token is not contained.

### Logout

```
POST /api/auth/logout [Authorize] {refreshToken}
  GetByHashAsync → null → 400 "Token غير موجود"
  RevokedAt = now → 200
```

Revokes one device. The **access token stays valid until expiry** — correct for stateless JWT, but the client must clear storage itself. Sent twice → 400 the second time (already revoked rows are still found by hash, so actually it succeeds again and re-stamps `RevokedAt`). Harmless.

### Password reset

```
forgot-password → always 200 (enumeration protection)
   only AuthProvider=='Email' users get an OTP
   DeleteAllUsableForUserAsync → one live OTP at a time
   OTP = Random.Shared.Next(100000,999999), stored SHA-256, 15 min
   SMTP failure → Console.WriteLine(email + PLAINTEXT OTP)     ← S-2 CRITICAL
verify-reset-otp → Attempts>=5 → 400; wrong code → Attempts++ → 400
   correct → ResetToken (64 random bytes), 10 min, stored hashed
reset-password → single use; clears ResetTokenHash
   does NOT revoke refresh tokens                              ← S-5
```

**`Random.Shared` is not cryptographically secure.** For a 6-digit reset code this is a real weakness — use `RandomNumberGenerator.GetInt32`. IMPORTANT.

## 2.2 The learn flow

```
GET /api/content/levels                    [Authenticated] → LevelService → Levels
GET /api/content/levels/{id}/lessons       [Authenticated] → LessonService → Lessons
GET /api/content/lessons/{id}              [Authenticated] → LessonService → Lessons+LessonContents+ContentTypes
```

- Identity: authenticated but **never used** — Content is not user-scoped at all.
- Authorization: `[Authorize]` only. **No `IsPublished` filter** (S-7).
- Failure: unknown level → `200` with `[]` (not 404); unknown lesson → 404.
- Twice: pure reads, idempotent.
- Data changes between requests: no snapshot, no versioning — the child sees current content. Correct for a reader.

**The flow ends here.** There is no completion call, and no link to a quiz.

## 2.3 The assess flow

```
POST /api/quiz-attempts?quizId=15                    [Authenticated]
 → QuizAttemptController.Start → QuizAttemptService.StartAsync
   quiz active? → StartFirstAttemptAsync
     SELECT active questions (+options) ORDER BY DisplayOrder
     LoadQuestionSnapshotsAsync → TopicId, Difficulty, CorrectOptionId per question
       any question with no correct option → 400 (attempt refused)
     INSERT QuizAttempts (Status='InProgress', RowVersion auto)
          + QuizAttemptQuestions (WITH the frozen snapshot)   ← one SaveChanges, atomic
 → 201 QuizAttemptResponseDto  (options carry NO IsCorrect)

POST /api/quiz-attempts/{id}/submit                  [Authenticated]
 → ownership: attempt.UserId != sub → 403
   status != InProgress → 400
   validate each submitted (questionId, selectedOptionId):
     duplicate question → 400 · not in attempt → 400 · option not found → 400
     option.QuestionId != questionId → 400
   grade: selectedOptionId != snapshot.CorrectOptionId → confirmed mistake
   ── AI CALL (outside any transaction) ──────────────────────
   GenerateHintsAsync → IAiHintGenerator → HTTP POST
       failure here → exception → 500 → NOTHING is written        ← A-1
   ── BEGIN TRANSACTION ─────────────────────────────────────
   INSERT QuizAttemptMistakes; UPDATE QuizAttempts (Completed, counts, score)
   SaveChanges  → RowVersion mismatch → 409
   INSERT QuestionHints
   UserTopicStatService.UpdateAfterQuizAttemptAsync (same DbContext, same tx)
   COMMIT
 → 200 QuizAttemptResultDto (+ retryQuestions with hints)

POST /api/quiz-attempts?quizId=15&previousAttemptId=42
 → ownership → 403 · not Completed → 400 · already retried → 400 (pre-check) / 409 (race)
 → wrong question ids from previous attempt → questions + latest hint each
 → new attempt with a FRESH snapshot taken now
```

**Concurrency, resolved correctly:** two simultaneous submits both read `Status='InProgress'`, both call the AI (wasted), both open transactions; the `UPDATE` carries `WHERE RowVersion = @rv`, so one affects 0 rows → `DbUpdateConcurrencyException` → **409**, and its whole transaction — mistakes, hints, stats — rolls back. **No double-processing of score, mistakes, stats, or completion state.** NO ISSUE.

**Data changing between requests:** an admin editing the question's topic, difficulty, text, or correct answer between Start and Submit changes **nothing** about grading or statistics, because both read the snapshot. This is correct and rare. NO ISSUE.

## 2.4 Missing business cases — verdicts

| Case | Current behaviour | Verdict |
|---|---|---|
| Starts a quiz, closes the app | Attempt stays `InProgress` forever. Nothing blocks a *new* attempt on the same quiz (no uniqueness on active attempts), so the child simply starts again. Rows accumulate. | HARDENING |
| Submits twice (sequential) | 400 "already submitted" | NO ISSUE |
| Submits twice (concurrent) | One 200, one 409; loser writes nothing | NO ISSUE |
| Loses network during submission | Ambiguous: the write may have committed. Client cannot tell — a retry returns 400, and **no endpoint returns the score**, so the result screen is unrecoverable | **IMPORTANT** (B-3) |
| Retry started, original attempt changes | Impossible — a Completed attempt is immutable | NO ISSUE |
| Question becomes inactive | Excluded from new attempts; **still appears in retries** of older attempts, graded against the frozen key | NO ISSUE (correct) |
| Lesson becomes unpublished | Still served to everyone | **IMPORTANT** (S-7) |
| User changes account | Attempts are keyed by `UserId` Guid; a new account sees nothing. Correct | NO ISSUE |
| Guest converts to registered | Same row, same Id, all progress preserved | NO ISSUE |
| Refresh token expires | 401 | NO ISSUE |
| Google account already exists | Logs in | NO ISSUE |
| **Google email = existing local account** | **500 from a unique-index violation; no linking path** | **IMPORTANT** (G-1) |
| AI unavailable | **Whole submit fails, 500, attempt lost** | **CRITICAL** (A-1) |
| AI times out | Same, after **100 s** (HttpClient default) | **CRITICAL** |
| AI returns malformed data | `ReadFromJsonAsync` throws or validation rejects → 500 → attempt lost | **CRITICAL** |
| DB succeeds, AI fails | Cannot happen — AI runs first | NO ISSUE |
| AI succeeds, DB fails | Transaction rolls back; AI result discarded; client gets 500; safe to retry | NO ISSUE |

---

# PART 3 — ASSESSMENT BUSINESS FLOW AUDIT

The 21 questions, answered from the code.

| # | Question | Answer |
|---|---|---|
| 1 | **What happens when a user starts a quiz?** | Quiz existence + `IsActive` checked; active questions projected with options; a snapshot row per question is written inside one `SaveChanges` with the attempt. Atomic via EF's implicit transaction — **no explicit transaction needed, none used. Correct.** |
| 2 | **Which questions are selected?** | First attempt: **all** `IsActive` questions of the quiz, ordered by `DisplayOrder`. No sampling, no difficulty targeting, no limit. Retry: exactly the previous attempt's wrong questions. |
| 3 | **Are questions frozen for the attempt?** | **Partially, and correctly so.** `QuizAttemptQuestions` freezes the *membership* plus `TopicId`, `Difficulty`, `CorrectOptionId`. Text, option text, `Points`, `DisplayOrder` and `IsActive` are read live. |
| 4 | **What if a question changes afterward?** | Grading and statistics are unaffected (they read the snapshot). Wording/option-text edits show through — desirable for typo fixes. `Points` changes are irrelevant (see #19). |
| 5 | **How is the correct answer determined?** | `mistake.SelectedOptionId != QuizAttemptQuestions.CorrectOptionId`. **`QuestionOption.IsCorrect` is never read during grading.** |
| 6 | **How is score calculated?** | `correct = TotalQuestionsAtAttempt − confirmedMistakes.Count`; `score = round(correct × 100 / total, 2, AwayFromZero)`. Question-count based. |
| 7 | **How are mistakes recorded?** | One `QuizAttemptMistakes` row per confirmed mistake, inside the submit transaction. `UQ_QuizAttemptMistakes_AttemptId_QuestionId` prevents duplicates. |
| 8 | **How are hints generated?** | Batch call to `IAiHintGenerator` with `{questionText, wrongOptionText, previousHints[]}` per wrong question; response validated to exactly one non-empty hint per question; persisted as `QuestionHints` with `HintSequence`. |
| 9 | **How does retry work?** | `previousAttemptId` on the Start endpoint. Loads the previous attempt's wrong question ids, attaches the latest hint per question, creates a new attempt with a **fresh** snapshot. |
| 10 | **Can a retry be created twice?** | **No.** `AnyAsync` pre-check gives a friendly 400; `UQ_QuizAttempts_PreviousAttemptId` is the real guard and a concurrent race yields 409. **NO ISSUE.** |
| 11 | **Can a user access another user's attempt?** | **No.** `attempt.UserId != userId → UnauthorizedAccessException → 403` on Get, Submit and retry-Start. **NO ISSUE.** |
| 12 | **Can an attempt be submitted twice?** | **No.** Status check (sequential → 400) + `RowVersion` (concurrent → 409, zero writes). **NO ISSUE.** |
| 13 | **Can a user manipulate QuestionId / OptionId?** | **No.** Question must belong to the attempt; option must exist and belong to that question; both are validated before grading, and the composite FK backstops at the DB. Submitting another question's option → 400. **NO ISSUE.** |
| 14 | **Can an inactive question appear?** | Not in a new attempt. **Yes in a retry** of an older attempt — deliberate, and the frozen key makes it gradeable. **NO ISSUE.** |
| 15 | **How are Topic/Difficulty statistics calculated?** | Grouped from `QuizAttemptQuestions.TopicId/Difficulty` (the snapshot); `Answered` = questions in bucket, `Correct` = those with no mistake row, `HintsUsed` = **total hints ever** in that bucket (assignment, not increment). |
| 16 | **Are historical statistics correct?** | **Yes**, since the snapshot fix. Counters accumulate with `+=` and are therefore **not idempotent** — which is why exposing a recalculate endpoint was a defect and was removed. |
| 17 | **`Question.TopicId` changes?** | Past attempts keep their original topic bucket. New attempts use the new one. **Correct.** |
| 18 | **`Question.Difficulty` changes?** | Same. **Correct.** |
| 19 | **`Points` changes?** | **No effect at all** — `Points` is returned for display but never enters scoring. Snapshotting it would be a redundant column. **No schema change required.** |
| 20 | **Options change?** | Text edits show through (same ids). Adding an option makes it selectable in a retry. Deleting one is **blocked** if it was ever selected or is a snapshotted answer key. |
| 21 | **The correct answer changes?** | Past and in-flight attempts are graded against the frozen `CorrectOptionId`; new attempts use the new key. **Exactly the required behaviour.** |

## Verdict

**The Assessment model is production-safe on correctness, concurrency and historical integrity.** It is *not* production-safe on availability — the AI dependency (A-1) can fail the entire flow.

**No schema change required for Part 3.** The snapshot columns, composite FKs, `RowVersion`, and the retry uniqueness constraint already close every integrity gap the 21 questions probe.

Two behavioural gaps remain, neither needing schema:

- `Abandoned` is never written. If you want stale-attempt cleanup, it is a scheduled `UPDATE`, not a schema change.
- Nothing prevents a user holding many concurrent `InProgress` attempts on the same quiz. Harmless today (each is independent and separately submittable); if you want one-at-a-time, that is a filtered unique index — **but only add it if the product actually requires it.**

---

# PART 4 — CONTENT / LEARNING MODULE AUDIT

Entities: `Level` (Id, Title, Description, Order) → `Lesson` (Id, LevelId, Title, Description, SortOrder, IsPublished, CreatedAt) → `LessonContent` (Id, LessonId, ContentTypeId, Content?, MediaUrl?, SortOrder) + `ContentType` lookup.

**There is no `Course` entity.** The hierarchy is Level → Lesson → Content. If "Course" is a product concept, it does not exist in the backend.

| # | Question | Answer |
|---|---|---|
| 1 | **Is the data model sufficient?** | For *delivering* content, yes. For a *learning* app, **no** — there is no progress, completion, prerequisite, or unlocking concept anywhere. |
| 2 | **Are relationships correct?** | Yes. `Restrict` on every FK, so nothing is silently orphaned. **No change required.** |
| 3 | **Is ordering logic correct?** | Functionally yes (`max + 1` on create, explicit swap endpoints). Structurally unsafe — see #4. |
| 4 | **Can duplicate ordering occur?** | **Yes.** `GetMaxSortOrderAsync` reads then writes with no constraint behind it. Two concurrent creates in the same level both compute the same `max+1`. **Assessment has `UQ_Questions_QuizId_DisplayOrder`; Content has no equivalent.** (D-2) |
| 5 | **Can users access content they should not?** | **Yes — unpublished lessons** are returned to every authenticated user (S-7). Otherwise content is intentionally public-to-members. |
| 6 | **How does the backend determine the current lesson?** | **It does not.** No concept of "current" exists server-side. |
| 7 | **How is lesson completion represented?** | **It is not represented at all.** |
| 8 | **How does Assessment know which lesson/level it belongs to?** | Via `Quizzes.LevelId` / `LessonId` — Assessment → Content. **The reverse direction does not exist**, which is precisely gap B-1. |
| 9 | **Are the loose references appropriate?** | **Yes.** Integer ids with no cross-schema FK is the right call for a modular monolith. **No change required.** |
| 10 | **Hidden coupling problems?** | One: `Quizzes.LevelId`/`LessonId` are never validated against Content, so orphan quizzes are creatable. Accept it (loose by design) or validate in the admin service — **do not add a cross-schema FK.** |
| 11 | **Content deactivated after a user has progress?** | No progress exists, so nothing breaks. Assessment is immune regardless — its snapshot is independent of Content. |
| 12 | **Lesson moved to another level?** | `UpdateAsync` re-assigns `SortOrder` to the end of the new level. Any quiz pointing at it keeps working (it references `LessonId`, not the level). Correct. |
| 13 | **Level changed?** | Title/description only; `Order` is untouched by `Update` and moves only via `swap-order`. Correct. |
| 14 | **Content deleted?** | Level delete is blocked while lessons exist. Lesson delete cascades in the service. **The image file on disk is never deleted** — orphaned uploads accumulate. HARDENING. |
| 15 | **Is historical learning progress preserved?** | There is none to preserve. |

## Content ↔ Assessment separation

**Correctly separated** — different `DbContext`s, no shared entity, no cross-schema FK. The separation is right; the *connection* is missing. The minimal fix does not violate the boundary: expose the quiz id from Assessment, keyed by the lesson id Content already knows.

## Required schema change for Content

**Yes — one, for D-2 (duplicate ordering).** This is a data-integrity invariant SQL can enforce and C# currently cannot enforce safely.

```sql
-- Content ordering: prevent two lessons/contents claiming the same slot.
-- Run the duplicate check FIRST; both statements fail if duplicates exist.
SELECT LevelId, SortOrder, COUNT(*) FROM dbo.Lessons
GROUP BY LevelId, SortOrder HAVING COUNT(*) > 1;

SELECT LessonId, SortOrder, COUNT(*) FROM dbo.LessonContents
GROUP BY LessonId, SortOrder HAVING COUNT(*) > 1;
GO

ALTER TABLE dbo.Lessons
    ADD CONSTRAINT UQ_Lessons_LevelId_SortOrder UNIQUE (LevelId, SortOrder);
GO

ALTER TABLE dbo.LessonContents
    ADD CONSTRAINT UQ_LessonContents_LessonId_SortOrder UNIQUE (LessonId, SortOrder);
GO
```

**EF change** (`ContentDbContext.OnModelCreating`):

```csharp
// Lesson
entity.HasIndex(e => new { e.LevelId, e.SortOrder }, "UQ_Lessons_LevelId_SortOrder").IsUnique();
// LessonContent
entity.HasIndex(e => new { e.LessonId, e.SortOrder }, "UQ_LessonContents_LessonId_SortOrder").IsUnique();
```

**Service change:** the `swap-order` endpoints now need care — swapping A↔B by two `UPDATE`s inside one `SaveChanges` will violate the constraint mid-statement. Use a three-step swap through a temporary sentinel, or defer with a single `UPDATE ... CASE`. This is the one place the constraint costs something, and it is worth it.

> **Note:** `Levels.Order` is deliberately left without a unique constraint — levels are few, admin-managed, and a duplicate there is cosmetic. **No schema change required for `Levels`.**

---

# PART 5 — IDENTITY MODULE AUDIT

## 5.1 Traced: Register → Login → JWT → Refresh → authenticated request → authorization → ownership

```
Register/Login → AuthService.IssueTokensAsync
    JwtTokenGenerator.GenerateAccessToken(user.Id, user.Role, user.AuthProvider)
      claims: sub=Guid, ClaimTypes.Role=Role, authProvider, jti
      HMAC-SHA256 over Jwt:SecretKey
    RefreshToken: 64 random bytes (RandomNumberGenerator) → SHA-256 → stored
        ↓
Authenticated request
    Program.cs JwtBearer: ValidateIssuer/Audience/Lifetime/SigningKey = true
    MapInboundClaims = false  → "sub" stays "sub"
        ↓
Authorization
    [Authorize]                 → any valid token
    [Authorize(Roles="Admin")]  → matches ClaimTypes.Role (the long URI) — wiring is CORRECT
        ↓
Ownership
    User.GetUserId() → FindFirst("sub") → Guid.Parse
    service compares against the row's UserId → 403
```

**The role wiring is correct and subtle:** `MapInboundClaims = false` keeps `sub` readable, and because `JwtTokenGenerator` emits `ClaimTypes.Role` (the full Microsoft URI) rather than a short `"role"`, the default `RoleClaimType` still matches. **No change required.**

**But the role is unobtainable.** `NormalizeRole` returns `Parent` or `Child` only. Combined with S-1 (leaked signing key), the only way to be an Admin today is to forge a token — which is exactly the wrong security posture.

## 5.2 GuestId vs UserId

**They are the same thing, and that is the correct design.** There is no separate `GuestId` column or concept — a guest is a `User` row with `AuthProvider='Guest'`, a real `Guid` Id, and a real JWT. Consequences:

- Guest progress is stored against a normal `UserId`, so conversion needs **no data migration** — the row is updated in place.
- There is no client-held "GuestId" to spoof; the guest is authenticated like anyone else.

**Verdict: correctly separated (by not separating them). No change required.**

## 5.3 Spoofing analysis

| Client tries to spoof | Possible? | Why |
|---|---|---|
| `UserId` | **No** | Never read from any request. Every ownership check uses `User.GetUserId()` |
| `ChildProfileId` | **N/A** | No such concept exists. `ParentChildLinks` exists in the schema but **no code reads or writes it** |
| `GuestId` | **No** | Not a client-supplied value |
| `Role` | **Yes, at registration** — a client can self-assign `Parent` (S-6). Cannot self-assign `Admin` (normalized away) | Latent, not currently exploitable |
| Claims generally | **Yes, if the leaked key is used** (S-1) | Forged token = arbitrary `sub` and `role` |

## 5.4 IDOR sweep across all modules

| Module | User-owned resource | Ownership enforced where | Verdict |
|---|---|---|---|
| Identity | own profile | `/me` routes only — no id in the route at all | **NO ISSUE** |
| Content | none | content is not user-scoped | **N/A** |
| Assessment | `QuizAttempt` | `QuizAttemptService` — Get, Submit, retry-Start all compare `attempt.UserId` to the JWT | **NO ISSUE** |
| Assessment | `UserTopicStats` | queried **by** `userId` from the JWT; no id parameter accepted | **NO ISSUE** |

**No IDOR was found anywhere.** Ownership lives in the service layer, not duplicated in controllers — the correct placement. **No change required.**

## 5.5 Refresh-token security

Assessed against the actual implementation:

| Property | Status |
|---|---|
| Stored hashed | **Yes** — SHA-256, plaintext never persisted. Correct (a 64-byte random token needs no salt/KDF) |
| Rotated on use | **Yes** — old revoked, new issued |
| Revocable | **Yes** — logout stamps `RevokedAt` |
| Expiry enforced | **Yes** — 30 days, checked on use |
| **Indexed for lookup** | **No** — table scan on every auth operation (D-1) |
| **Reuse detection** | **No** — a replayed revoked token 401s but does not revoke the family |
| **Concurrent-refresh safe** | **No** — two simultaneous refreshes both succeed (S-10) |
| **Revoked on password reset** | **No** (S-5) |
| Cleanup of expired rows | **No** (D-3) |

**Required schema change — yes, one, and it is cheap:**

```sql
-- D-1 + S-10: make hash lookup an index seek, and make the rotation race impossible.
-- TokenHash is a SHA-256 hex string; it is unique by construction.
CREATE UNIQUE INDEX UQ_RefreshTokens_TokenHash
    ON Users.RefreshTokens (TokenHash);
GO

-- D-4: the reset-token lookup is also a scan today.
CREATE INDEX IX_PasswordResetOTPs_ResetTokenHash
    ON Users.PasswordResetOTPs (ResetTokenHash)
    WHERE ResetTokenHash IS NOT NULL;
GO
```

**EF change** (`UsersDbContext.OnModelCreating`):

```csharp
// RefreshToken
entity.HasIndex(e => e.TokenHash, "UQ_RefreshTokens_TokenHash").IsUnique();
// PasswordResetOtp
entity.HasIndex(e => e.ResetTokenHash, "IX_PasswordResetOTPs_ResetTokenHash")
      .HasFilter("([ResetTokenHash] IS NOT NULL)");
```

The unique index does **not** by itself fix S-10 (both refreshes revoke the *same* row, then insert *different* new rows). The minimal service fix is a conditional update — see Part 20, Phase 1.

## 5.6 Does Identity expose too much?

`UserProfileResponse` returns `id, email, fullName, role, authProvider, age, isActive, convertedFromGuestAt, createdAt`. **No `passwordHash`, no `providerUserId`, no tokens.** Correct — and it is reachable only for the caller's own profile. **No change required.**

---

# PART 6 — CONTROLLERS / API DESIGN AUDIT

## 6.1 Overall verdict

**Controllers are genuinely thin.** Across all 8 controllers: constructor injection, one service call, one status mapping. **No business logic in any controller.** `CancellationToken` is accepted and forwarded on **every** action. All actions are async. No entity is ever returned. **NO ISSUE on the structural questions — no change required.**

The API is also already business-shaped where it matters: `POST /api/quiz-attempts`, `POST /{id}/submit`, and retry-via-`previousAttemptId`. **There is no endpoint that lets a client set an attempt's status, score, or counts.** That is the property Part 6 asks about, and it holds. **NO ISSUE.**

## 6.2 Problems worth fixing

### 6.2.1 The stat-recalculation endpoint (already fixed this session)

```
Current:      POST /api/user-topic-stats/recalculate?quizAttemptId=42   [Authorize]
Recommended:  (removed — no endpoint)
Reason:       UpdateAfterQuizAttemptAsync accumulates with +=, so it is not idempotent.
              Any user could re-post it for their own completed attempt and inflate their
              own QuestionsAnsweredCount / CorrectCount without bound. It is an internal
              step of SubmitAsync and belongs inside that transaction only.
```

### 6.2.2 Missing child-facing quiz discovery (B-1, CRITICAL)

```
Current:      no endpoint. GET /api/quizzes is [Authorize(Roles="Admin")].
Recommended:  GET /api/content/lessons/{lessonId}/quiz          [Authorize]
              → { quizId, title, questionCount }  or 404
Reason:       Without it the child app cannot start an attempt. This is the single
              missing link in the product's core loop.
```

Implementation note that preserves the module boundary: put the endpoint in the **Assessment** module (it owns `Quizzes`), keyed by the `lessonId` Content already gave the client. Assessment never queries `ContentDbContext`; it filters its own table by an integer it was handed.

```csharp
// AssessmentBL — IQuizService
Task<QuizSummaryDto?> GetActiveByLessonIdAsync(int lessonId, CancellationToken ct = default);

// QuizService
public async Task<QuizSummaryDto?> GetActiveByLessonIdAsync(int lessonId, CancellationToken ct = default) =>
    await _db.Quizzes
        .AsNoTracking()
        .Where(q => q.LessonId == lessonId && q.IsActive)
        .Select(q => new QuizSummaryDto
        {
            QuizId = q.Id,
            Title = q.Title,
            QuestionCount = q.Questions.Count(x => x.IsActive)
        })
        .FirstOrDefaultAsync(ct);
```

```csharp
// New child-facing controller — separate from the admin QuizController
[ApiController]
[Route("api/quizzes")]
[Authorize]                                  // NOT Roles = "Admin"
public class QuizDiscoveryController : ControllerBase
{
    private readonly IQuizService _quizService;
    public QuizDiscoveryController(IQuizService quizService) => _quizService = quizService;

    [HttpGet("for-lesson/{lessonId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuizSummaryDto>> GetForLesson(int lessonId, CancellationToken ct)
    {
        var quiz = await _quizService.GetActiveByLessonIdAsync(lessonId, ct);
        return quiz is null ? NotFound() : Ok(quiz);
    }
}
```

### 6.2.3 Missing result re-read (B-3, IMPORTANT)

```
Current:      GET /api/quiz-attempts/{id} returns questions only — no score, no status.
              The result is served exactly once, by submit.
Recommended:  add status + score to that response, or GET /api/quiz-attempts/{id}/result
Reason:       A network failure during submit makes the result screen unrecoverable, and
              the client cannot tell whether the submit committed.
```

### 6.2.4 Two inconsistencies not worth a redesign

```
Current:      Assessment creates → 201, deletes → 204;
              Content/Identity creates → 200, deletes → 200.
              GET /api/users/me → 404 when missing; PUT /api/users/me → 400 for the same case.
Recommended:  align PUT /api/users/me to 404. Leave the 200-vs-201 split alone.
Reason:       The 404/400 split is a real client-side bug source. The 200/201 split is
              cosmetic and changing it would break the existing Flutter contract for no gain.
```

### 6.2.5 `POST /api/question-options` returns a misleading `Location`

```
Current:      CreatedAtAction(nameof(GetByQuestionId), ...) — points at the list endpoint
Recommended:  either add GET /api/question-options/{optionId}, or return 200 Ok(option)
Reason:       201 + Location is a promise that the header addresses the created resource.
```

## 6.3 Recommended API surface (delta only)

| Action | Endpoint |
|---|---|
| **Add** | `GET /api/quizzes/for-lesson/{lessonId}` `[Authorize]` — unblocks the core loop |
| **Add** | `GET /api/assessment/topics` `[Authorize]` — labels `UserTopicStats.topicId` |
| **Extend** | `GET /api/quiz-attempts/{id}` — include `status` and, when completed, the score block |
| **Removed** | `POST /api/user-topic-stats/recalculate` (done) |
| **Keep unchanged** | everything else |

---

# PART 7 — DATABASE AUDIT

## 7.1 Table-by-table verdicts

| Table | Necessary? | Constraints | Indexes | Can invalid/duplicate data enter? | Verdict |
|---|---|---|---|---|---|
| `Categories` | Yes | PK, `UQ_Name` | clustered PK | No | **NO ISSUE** |
| `Topics` | Yes | PK, `UQ_Name`, FK→Categories, CK LearningLevel | CategoryId, LearningLevel | No | **NO ISSUE** — but no endpoint exposes it (B-5) |
| `Quizzes` | Yes | PK, CK QuizType, CK TypeMatchesReference | QuizType, LevelId, LessonId, IsActive | `LevelId`/`LessonId` may reference nothing (deliberate) | **NO ISSUE** |
| `Questions` | Yes | PK, `UQ(QuizId,DisplayOrder)`, FKs, CK Difficulty, CK Points>0 | TopicId, Difficulty | No | **NO ISSUE** |
| `QuestionOptions` | Yes | PK, `UQ(QuestionId,DisplayOrder)`, `UQ(QuestionId,Id)`, filtered `UQ` one-correct | above | "exactly one correct" is app-enforced; "at most one" is DB-enforced — the correct split | **NO ISSUE** |
| `QuizAttempts` | Yes | PK, FK Quiz, 7 CKs, self-FK, `UQ_PreviousAttemptId`, `RowVersion` | QuizId, (UserId,QuizId,StartedAt) | No | **NO ISSUE — exemplary** |
| `QuizAttemptQuestions` | Yes | PK, `UQ(AttemptId,QuestionId)`, FKs incl. composite answer-key FK, CK Difficulty | QuestionId | No | **NO ISSUE** |
| `QuizAttemptMistakes` | Yes | PK, `UQ(AttemptId,QuestionId)`, composite FK to options, FK to attempt-questions | QuestionId | No — option/question mismatch is impossible | **NO ISSUE** |
| `QuestionHints` | Yes | PK, `UQ(MistakeId,Sequence)`, FK cascade, CK Sequence>0 | — | No | **NO ISSUE** |
| `UserTopicStats` | Yes | PK, `UQ(UserId,TopicId,Difficulty)`, FKs, 5 CKs, computed `WrongCount` | TopicId | No | **NO ISSUE** |
| `Users` | Yes | PK, filtered `UQ_Email`, filtered `UQ_Provider` | — | No | **NO ISSUE** |
| `RefreshTokens` | Yes | PK, FK | **UserId only** | Duplicate hashes possible in principle | **IMPORTANT — D-1** |
| `PasswordResetOTPs` | Yes | PK, FK | **UserId only** | — | **HARDENING — D-4** |
| `ParentChildLinks` | **Questionable** | PK, `UQ`, 2 FKs | both sides | — | **No code reads or writes it.** Keep the table (cheap, clearly intended); do not build on it until the feature exists |
| `Levels` / `Lessons` / `LessonContents` / `ContentTypes` | Yes | FKs `Restrict`, `UQ_ContentTypes_Name` | — | **Duplicate SortOrder possible** | **IMPORTANT — D-2** |

## 7.2 Invariants that live only in C# but SQL could safely enforce

| Invariant | Today | Verdict |
|---|---|---|
| Lesson/content ordering uniqueness | C# read-then-write only | **Move to SQL** — Part 4 migration |
| Refresh-token hash uniqueness | nothing | **Move to SQL** — Part 5 migration |
| "Exactly one correct option per question" | C# (`EnsureExactlyOneCorrectOptionAsync`), DB enforces "at most one" | **Leave as is.** SQL cannot express "exactly one" without a trigger, and a trigger here would block the legitimate intermediate state while an admin authors a question. The current split is correct |
| Quiz→Level/Lesson referential integrity | nothing | **Leave as is** — cross-module by design |
| Score = f(counts) | C# | **Leave as is** — a computed column would fight `CK_QuizAttempts_ScorePercentage` and add nothing |

## 7.3 Data types, cascades, nullability

Reviewed; all appropriate. `DATETIME2(3)` throughout, `DECIMAL(5,2)` for score, `TINYINT`/`SMALLINT` sized to real ranges, `UNIQUEIDENTIFIER` for user ids after the INT→GUID migration. Cascade behaviour is deliberate: `CASCADE` only where a child is meaningless alone (attempt→questions/mistakes, mistake→hints, quiz→questions, question→options); `NO ACTION` everywhere history must survive; `SET NULL` on `UserTopicStats.LastQuizAttemptId` (correctly, since it is nullable).

**No schema change required** for anything in §7.3.

---

# PART 8 — EF CORE / DATA ACCESS AUDIT

## 8.1 What is already correct

Assessment services use `AsNoTracking()` on every read path, project with `Select` instead of `Include`+map, and load only the columns they need. `ProjectToResponse` expressions are `static readonly Expression<...>` so EF translates them to a SQL column list rather than materializing entities. `SaveChanges` is called once per logical operation. The submit transaction is explicit and correctly scoped. **No change required for the Assessment data access layer** — it is the best-written code in the solution.

## 8.2 Issue 1 — N+1 plus sync-over-async (IMPORTANT, P-1)

**Current behaviour** — `UserTopicStatService.UpdateAfterQuizAttemptAsync`:

```csharp
var attemptQuestions = await _db.QuizAttemptQuestions...ToListAsync(ct);   // materialized

var buckets = attemptQuestions
    .GroupBy(q => new { q.TopicId, q.Difficulty })
    .Select(g => new {
        ...,
        Correct = g.Count(q => !_db.QuizAttemptMistakes.Any(m =>          // ← per question!
            m.QuizAttemptId == quizAttemptId && m.QuestionId == q.QuestionId)),
        ...
    })
    .ToList();
```

**Problem.** `attemptQuestions` is a `List<>`, so the `GroupBy`/`Select` runs in LINQ-to-Objects. The inner `_db.QuizAttemptMistakes.Any(...)` is therefore a **separate, synchronous, blocking database round trip per question**. A 20-question quiz issues 20 blocking queries on the thread-pool thread, inside the submit transaction, on top of the AI call.

**Recommended change** — the mistake set for this attempt is tiny and already needed; load it once:

```csharp
var mistakeQuestionIds = await _db.QuizAttemptMistakes
    .AsNoTracking()
    .Where(m => m.QuizAttemptId == quizAttemptId)
    .Select(m => m.QuestionId)
    .ToListAsync(cancellationToken);

var mistakeSet = mistakeQuestionIds.ToHashSet();

var buckets = attemptQuestions
    .GroupBy(q => new { q.TopicId, q.Difficulty })
    .Select(g => new
    {
        g.Key.TopicId,
        g.Key.Difficulty,
        Answered = g.Count(),
        Correct = g.Count(q => !mistakeSet.Contains(q.QuestionId)),
        Hints = hintCounts.GetValueOrDefault((g.Key.TopicId, g.Key.Difficulty))
    })
    .ToList();
```

**Expected benefit.** N+1 blocking queries → 1 async query. Removes thread-pool blocking from the hottest write path and shortens the transaction.

## 8.3 Issue 2 — unbounded historical scan on every submit (IMPORTANT, P-2)

**Current behaviour**, same method:

```csharp
var hintCountsByTopicAndDifficulty = await _db.QuestionHints
    .AsNoTracking()
    .Where(h => h.QuizAttemptMistake.QuizAttempt.UserId == userId)   // ALL attempts, ever
    .GroupBy(...)
    .ToListAsync(ct);
```

**Problem.** Every submit aggregates the user's entire hint history across a three-table join. Cost grows linearly with account age, forever, to produce a counter.

**Recommended change.** Two options, in order of preference:

1. **Make `HintsUsedCount` incremental** like the other counters — add only this attempt's hint count. It becomes `+=`, consistent with `QuestionsAnsweredCount` and `CorrectCount`, and the query narrows to one attempt. This is a **behaviour change** (the column becomes a running total maintained incrementally rather than recomputed), so state it before shipping.
2. If the recomputed semantics must be preserved, at minimum scope the query to the buckets this attempt touches rather than all of them.

**Expected benefit.** Constant-time instead of history-proportional. Removes the slowest query in the submit path.

## 8.4 Issue 3 — Content reads are tracked (HARDENING, P-4)

**Current.** No `AsNoTracking()` in `LevelRepository`, `LessonRepository`, `LessonContentRepository`, `ContentTypeRepository`.

**Problem.** Every read builds change-tracker entries that are never used — extra allocation and identity-map work on the app's most-called endpoints.

**Recommended change.** Add `.AsNoTracking()` to the read-only methods (`GetAllAsync`, `GetByLevelIdAsync`, `GetWithContentsAsync`, `GetAllAsync` on content types). **Do not** add it to `GetByIdAsync` where the result is subsequently mutated and saved — check each call site first; `LessonService.UpdateAsync` and `DeleteAsync` rely on tracking.

**Expected benefit.** Lower allocation and CPU per content read. Modest but free.

## 8.5 Issue 4 — two round trips for a max (HARDENING, P-5)

**Current.** `await query.AnyAsync(ct) ? await query.MaxAsync(l => l.SortOrder, ct) : 0`

**Recommended.**

```csharp
public async Task<int> GetMaxSortOrderAsync(int levelId, CancellationToken ct = default) =>
    await _context.Lessons
        .Where(l => l.LevelId == levelId)
        .MaxAsync(l => (int?)l.SortOrder, ct) ?? 0;
```

**Expected benefit.** One query instead of two. (It does **not** fix the ordering race — only the constraint in Part 4 does that.)

## 8.6 Issue 5 — hardcoded `OnConfiguring` fallbacks (IMPORTANT, A-2)

**Current.** `UsersDbContext` falls back to `Server=.;Database=Volt;...`; `ContentDbContext` falls back to `Server=.;Database=VoltDB;...`. **Two different database names.** Both set `TrustServerCertificate=True`.

**Problem.** If DI registration is ever missed, the context silently connects to a *local, possibly wrong, possibly non-existent* database instead of failing loudly. That is precisely the class of bug that produced the `app.Run()` DI defect. `TrustServerCertificate=True` also disables TLS validation.

**Recommended change.** Delete both `OnConfiguring` overrides. Design-time tooling should use an `IDesignTimeDbContextFactory`, not a production code path.

**Expected benefit.** Misconfiguration fails at startup instead of silently reading an empty database.

## 8.7 Patterns specifically searched for

| Pattern | Found? |
|---|---|
| `ToListAsync()` then in-memory filtering that SQL could do | **Yes, once** — §8.2 |
| `Include(...)` then `Select(...)` where projection would do | **Once**, `LessonService.GetDetailAsync`. Low value to change — the include is filtered and the result set is one lesson. **No change required** |
| Loading whole entities for a few fields | **Content repositories** (§8.4) |
| Lazy loading | **Not enabled anywhere.** Navigations are `virtual` but no proxy package is referenced — no lazy-loading N+1 risk. **NO ISSUE** |
| Missing `AsNoTracking` | Content only |
| Cartesian explosion from multiple collection `Include`s | **None** |

---

# PART 9 — PERFORMANCE AUDIT

## 9.1 Ranked by real impact

| Rank | Item | Fix | Class |
|---|---|---|---|
| 1 | AI call in the request path with the 100 s `HttpClient` default | timeout + degradation (Parts 12–13) | **CRITICAL** |
| 2 | N+1 blocking queries per submit | §8.2 | **IMPORTANT** |
| 3 | Full hint-history scan per submit | §8.3 | **IMPORTANT** |
| 4 | `RefreshTokens.TokenHash` scan per auth call | index (Part 5) | **IMPORTANT** |
| 5 | Content reads tracked | `AsNoTracking` | HARDENING |
| 6 | `ContentTypes`/`Levels` re-read constantly | in-memory cache | HARDENING |

## 9.2 What to cache, and how

**`IMemoryCache` is sufficient. Do not introduce Redis** — there is one API process, the cacheable data is a few kilobytes, and cross-instance invalidation is not a problem you have.

| Data | Cache? | TTL / invalidation |
|---|---|---|
| `ContentTypes` | **Yes** — 3 rows, immutable lookup | `IMemoryCache`, 1 h or app lifetime |
| `Levels` list | **Yes** | 5–15 min, or evict in `LevelService` create/update/delete/swap |
| Lesson detail | **Yes**, per lesson id | 5 min; evict on update/publish/content change |
| Quiz questions for an attempt | **No** | Correctness depends on reading live rows at Start |
| `UserTopicStats` | **No** | Per-user, changes on every submit, cheap to read |
| Attempts / mistakes / hints | **No** | Transactional |
| AI hints | **Situational** — see Part 11 | Key on `(questionId, wrongOptionId, previousHintCount)` |

## 9.3 Pagination, filtering, sorting, projection

- **Pagination exists on exactly one endpoint**: `GET /api/quizzes` (`PagedResult<T>`, `pageSize` clamped to 100). Correct where implemented.
- **Everything else returns full lists.** Acceptable *today* given realistic volumes (levels: tens; lessons per level: tens; questions per quiz: tens; stats per user: tens). **Do not add pagination speculatively.** The one to watch is `GET /api/questions?quizId=` for a large quiz — revisit if quizzes exceed ~100 questions.
- `PagedResult<T>` lacks `hasNextPage`/`totalPages`; clients compute them. Cosmetic.

## 9.4 Limits, timeouts, cancellation

| Control | Status |
|---|---|
| Cancellation tokens | **Threaded end-to-end**, controller → service → EF. Excellent. **No change required** |
| Request size limit | Only on image upload (6 MB) + service-level 5 MB. Correct |
| **HTTP timeout on the AI client** | **Not configured** — 100 s default. **Must fix** |
| **Rate limiting** | **None anywhere.** Add ASP.NET Core's built-in `AddRateLimiter` on `/api/auth/*` and the submit endpoint. Not a third-party dependency |
| DB command timeout | Default (30 s). Fine |
| Health checks | None. `AddHealthChecks()` + `/health` is a five-line addition — worth it before production |

---

# PART 10 — ARCHITECTURE AUDIT

## 10.1 Data ownership

| Data | Owner | Read by |
|---|---|---|
| `Users`, `RefreshTokens`, `PasswordResetOTPs`, `ParentChildLinks` | **Identity** | Identity only |
| `Levels`, `Lessons`, `LessonContents`, `ContentTypes` | **Content** | Content only |
| All 10 `Assessment.*` tables | **Assessment** | Assessment only |
| — | **AIIntegration** | owns no data |

## 10.2 The rule check

> *A module must not directly query another module's `DbContext`.*

**Verified by inspection: the rule holds without exception.** No service in any module references another module's `DbContext` type, and the project references make it impossible (`AssessmentBL` does not reference `ContentDA` or `UsersDA`).

## 10.3 Illegal dependencies, DTO leakage, entity leakage

| Check | Result |
|---|---|
| Cross-module `DbContext` access | **None** |
| Entities crossing module boundaries | **None** — every module's entities stay in its own assembly |
| DTOs crossing boundaries | Only `Shared.Assessment.AI` (`GenerateHintsRequest/Response`), shared between `AssessmentBL` and `AIIntegration`. **Appropriate** — that is exactly what a contract assembly is for |
| Entities exposed from controllers | **None** — every endpoint returns a DTO |
| Circular dependencies | **None** — the reference graph is a DAG |

## 10.4 Is it actually modular, or just folders?

**Actually modular.** The evidence: separate assemblies with enforced references, separate `DbContext`s, separate DI registration methods (`AddUsersModule`, `AddContentModule`, `AddAssessmentModule`, `AddAiIntegration`), and no shared entity types. A folder-only structure would compile even if `AssessmentBL` queried `UsersDbContext`; here it cannot.

**One structural asymmetry, deliberately left alone:** Content uses repositories + `UnitOfWork`; Assessment and Identity use `DbContext` directly. **No change required** — normalizing this is churn.

## 10.5 Interfaces and `Shared`

Interfaces sit next to their implementations' consumers (`AssessmentBL/Interfaces`, `ContentBL/Interfaces`) — correct. `Shared` currently holds: `Common/` (clock, hashing, password, email, results, ApiResponse), `Users/` (JWT, Google, claims extensions), `Assessment/AI/` (contracts).

**Assessment:** not yet a dumping ground, but `Shared.Users` is module-specific and lives in Shared only so other modules can read claims. That is a legitimate reason. **HARDENING at most; no action needed now.**

## 10.6 Cross-module communication for B-1

The simplest mechanism consistent with this architecture, and the one recommended: **the client carries the identifier.** Content returns `lessonId`; the client passes `lessonId` to an Assessment endpoint; Assessment filters its own table. No events, no in-process bus, no shared context. See §6.2.2.

---

# PART 11 — AI INTEGRATION AUDIT

## 11.1 What exists

Three files, ~60 lines total: `IAiHintGenerator` (contract, in Shared), `AiHintGenerator` (validates the request is non-empty, delegates), `HttpExternalAiProvider` (`PostAsJsonAsync` to `Ai:HintsEndpoint`, `EnsureSuccessStatusCode`, deserialize).

**`AiSettings` contains exactly one property: `HintsEndpoint`.** No API key, no model, no timeout, no provider. `Ai:HintsEndpoint` is an **empty string** in `appsettings.Development.json`, so the provider throws `"AI hints endpoint is not configured"` — meaning **every submit containing a wrong answer currently returns 500** (C-1).

## 11.2 The seventeen questions

| # | Question | Answer |
|---|---|---|
| 1 | What AI does the product need? | One thing: a short Arabic hint per wrong answer, aware of prior hints so a retry escalates. Nothing else. **Do not build a chatbot.** |
| 2 | What data is sent? | `questionText`, `wrongOptionText`, `previousHints[]` per wrong question |
| 3 | What must never be sent? | Child name, email, `userId`, age, attempt id, scores, `IsCorrect`/the correct answer. **None of these are sent today — this is already correct and must stay correct.** |
| 4 | Which module owns it? | `AIIntegration`, contract in `Shared.Assessment.AI`. **Correct placement** |
| 5 | Separate module/service? | **Module: yes, already. Separate service/process: no** — no scaling or isolation need justifies it |
| 6 | Sync or async? | **Synchronous, in the submit request path** |
| 7 | If unavailable? | **Whole submit fails, 500, attempt not completed** |
| 8 | On timeout? | Same, after ~100 s |
| 9 | On rate limit? | Unhandled — `EnsureSuccessStatusCode` throws on 429 → 500 |
| 10 | Invalid JSON? | `ReadFromJsonAsync` throws → 500 |
| 11 | Unsafe/unexpected text? | **No safety validation of any kind.** Whatever the endpoint returns is stored and shown to a child |
| 12 | Prompts centralized? | **There is no prompt.** The remote endpoint owns it — so prompt quality, model choice and safety are outside this codebase |
| 13 | Responses validated? | **Yes, structurally** — exactly one non-empty hint per expected question, unexpected ids dropped. Good |
| 14 | Persisted? | Yes, `QuestionHints` with a sequence |
| 15 | **Inside a DB transaction?** | **No — correctly outside.** Already fixed |
| 16 | Cached? | **No.** Identical (question, wrong option) pairs regenerate every time for every child |
| 17 | Costs? | **Unbounded.** No cache, no rate limit, no per-user cap. Every wrong answer by every child is a call |

## 11.3 Verdict

The **shape** is right — a narrow interface, no PII, response validation, outside the transaction, batched per attempt. The **substance** is missing: no provider, no key, no timeout, no retry, no fallback, no cache, no safety layer, no prompt. Part 12 fills that in.

---

# PART 12 — REAL AI API INTEGRATION DESIGN

## 12.1 Flow

```
QuizAttemptService                                   (Assessment owns the business rule)
   └─ IAiHintGenerator                               (Shared contract — UNCHANGED)
        └─ ClaudeHintProvider : IExternalAiProvider  (AIIntegration owns the provider)
             ├─ prompt construction        (centralized here, versioned in code)
             ├─ AnthropicClient            (official SDK, typed exceptions)
             ├─ structured output schema   (model must return valid JSON)
             ├─ response validation        (one non-empty hint per question)
             └─ safety guard               (length, language, banned-pattern check)
        └─ returns GenerateHintsResponse   (or throws → caller degrades gracefully)
```

**`IAiHintGenerator`, `GenerateHintsRequest` and `GenerateHintsResponse` do not change.** Only the provider implementation behind them does. That is the payoff of the existing abstraction — keep it.

## 12.2 Provider choice

Anthropic's Claude, using the **official `Anthropic` NuGet SDK** (not raw HTTP — a first-party SDK exists for C#, so use it). Model: **`claude-opus-5`**. The task is short-form Arabic pedagogical text for children, where instruction-following and safety matter more than throughput.

> Verified against the current SDK surface rather than recalled: client is `AnthropicClient`, calls go through `client.Messages.Create(new MessageCreateParams { Model, MaxTokens, Messages })`, content blocks unwrap via `.Value` + `OfType<TextBlock>()`, structured output uses `OutputConfig { Format = new JsonOutputFormat { Schema = ... } }`, and typed exceptions live in `Anthropic.Exceptions` (`AnthropicRateLimitException`, `Anthropic5xxException`, `AnthropicIOException`, `AnthropicApiException`).

## 12.3 Configuration

```jsonc
// appsettings.json — structure only, NO secrets
{
  "Ai": {
    "Provider": "Claude",
    "Model": "claude-opus-5",
    "TimeoutSeconds": 20,
    "MaxRetries": 2,
    "MaxHintLength": 220,
    "Enabled": true
  }
}
```

```csharp
namespace AIIntegration;

public sealed class AiSettings
{
    public const string SectionName = "Ai";

    public string Provider { get; set; } = "Claude";
    public string Model { get; set; } = "claude-opus-5";
    public int TimeoutSeconds { get; set; } = 20;
    public int MaxRetries { get; set; } = 2;
    public int MaxHintLength { get; set; } = 220;

    /// <summary>When false the provider is skipped entirely and submission degrades gracefully.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Never bound from appsettings — see ApiKey resolution below.</summary>
    public string? ApiKey { get; set; }
}
```

### Where secrets live

| Environment | Mechanism |
|---|---|
| **Development** | .NET User Secrets — `dotnet user-secrets set "Ai:ApiKey" "sk-ant-..."`. Stored outside the repo, never committed |
| **Production** | Environment variable `ANTHROPIC_API_KEY` (the SDK reads it with no code), or a managed secret store (Azure Key Vault / AWS Secrets Manager) surfaced as configuration |
| **Never** | `appsettings*.json`, source control, logs, exception messages |

The same rule retroactively applies to `Jwt:SecretKey`, `ConnectionStrings:DefaultConnection` and `EmailSettings:SenderPassword` — all three are currently committed (S-1).

## 12.4 Implementation

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Shared.Assessment.AI;

namespace AIIntegration;

public static class DependencyInjection
{
    public static IServiceCollection AddAiIntegration(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiSettings>(configuration.GetSection(AiSettings.SectionName));

        // The provider is a singleton: AnthropicClient is thread-safe and holds
        // the connection pool. A scoped client would rebuild it per request.
        services.AddSingleton<IExternalAiProvider, ClaudeHintProvider>();
        services.AddScoped<IAiHintGenerator, AiHintGenerator>();

        return services;
    }
}
```

```csharp
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Assessment.AI;

namespace AIIntegration;

internal sealed class ClaudeHintProvider : IExternalAiProvider
{
    private const string SystemPrompt = """
        You write one short hint for an Arabic-speaking child (age 8-12) learning basic
        electricity. The child answered a multiple-choice question incorrectly.

        Rules, all mandatory:
        - Write in simple Modern Standard Arabic. One or two short sentences.
        - NEVER state or name the correct answer. Nudge the child's thinking instead.
        - If previous hints are supplied, give a MORE specific hint than those, and do
          not repeat them.
        - Be warm and encouraging. Never mock the child or call the answer stupid.
        - No links, no questions back to the child, no emoji, no markdown.
        - Return one hint for every questionId you are given, and no others.
        """;

    private static readonly Dictionary<string, JsonElement> HintSchema = new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "hints" }),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            hints = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "questionId", "hintText" },
                    properties = new
                    {
                        questionId = new { type = "integer" },
                        hintText = new { type = "string" }
                    }
                }
            }
        })
    };

    private readonly AnthropicClient _client;
    private readonly AiSettings _settings;
    private readonly ILogger<ClaudeHintProvider> _logger;

    public ClaudeHintProvider(IOptions<AiSettings> options, ILogger<ClaudeHintProvider> logger)
    {
        _settings = options.Value;
        _logger = logger;

        // ApiKey resolution order: explicit config (user-secrets / key vault) then
        // ANTHROPIC_API_KEY, which the SDK reads on its own.
        _client = string.IsNullOrWhiteSpace(_settings.ApiKey)
            ? new AnthropicClient()
            : new AnthropicClient { ApiKey = _settings.ApiKey };
    }

    public async Task<GenerateHintsResponse> GenerateHintsAsync(
        GenerateHintsRequest request,
        CancellationToken cancellationToken = default)
    {
        // A hard ceiling independent of the caller's token, so one slow call can never
        // hold a request thread for the SDK's default timeout.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));

        var userPrompt = BuildPrompt(request);

        try
        {
            var message = await _client.Messages.Create(new MessageCreateParams
            {
                Model = _settings.Model,
                MaxTokens = 2048,
                System = SystemPrompt,
                Messages = [new() { Role = Role.User, Content = userPrompt }],
                OutputConfig = new OutputConfig
                {
                    Format = new JsonOutputFormat { Schema = HintSchema }
                }
            }, cancellationToken: timeout.Token);

            var json = string.Concat(
                message.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));

            return Parse(json, request);
        }
        catch (AnthropicRateLimitException ex)                 // 429
        {
            _logger.LogWarning(ex, "AI hint generation rate limited for {Count} questions",
                request.Questions.Count);
            throw new AiUnavailableException("AI rate limit reached", ex);
        }
        catch (Anthropic5xxException ex)                       // provider outage
        {
            _logger.LogWarning(ex, "AI provider returned a server error");
            throw new AiUnavailableException("AI provider unavailable", ex);
        }
        catch (AnthropicIOException ex)                        // network failure
        {
            _logger.LogWarning(ex, "AI provider unreachable");
            throw new AiUnavailableException("AI provider unreachable", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("AI hint generation timed out after {Seconds}s",
                _settings.TimeoutSeconds);
            throw new AiUnavailableException("AI request timed out", ex);
        }
        catch (AnthropicApiException ex)                       // 4xx: bad key, bad model
        {
            _logger.LogError(ex, "AI request rejected — check Ai:Model and the API key");
            throw new AiUnavailableException("AI request rejected", ex);
        }
    }

    private static string BuildPrompt(GenerateHintsRequest request)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Write one hint per question below.\n");

        foreach (var q in request.Questions)
        {
            sb.AppendLine($"questionId: {q.QuestionId}");
            sb.AppendLine($"question: {q.QuestionText}");
            sb.AppendLine($"the child chose (wrong): {q.WrongOptionText}");

            if (q.PreviousHints.Count > 0)
                sb.AppendLine($"hints already given: {string.Join(" | ", q.PreviousHints)}");

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private GenerateHintsResponse Parse(string json, GenerateHintsRequest request)
    {
        HintPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<HintPayload>(json);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "AI returned unparseable JSON");
            throw new AiUnavailableException("AI returned malformed JSON", ex);
        }

        if (payload?.Hints is null)
            throw new AiUnavailableException("AI returned no hints");

        var expected = request.Questions.Select(q => q.QuestionId).ToHashSet();

        var hints = payload.Hints
            .Where(h => expected.Contains(h.QuestionId))
            .Select(h => new GeneratedHint
            {
                QuestionId = h.QuestionId,
                HintText = Sanitize(h.HintText)
            })
            .Where(h => !string.IsNullOrWhiteSpace(h.HintText))
            .ToList();

        return new GenerateHintsResponse { Hints = hints };
    }

    /// <summary>
    /// Child-safety guard. The model is instructed not to produce these, the provider
    /// runs its own safety classifiers, and this is the last line: anything with markup,
    /// a link, or excessive length is rejected rather than shown to a child.
    /// </summary>
    private string Sanitize(string? hintText)
    {
        if (string.IsNullOrWhiteSpace(hintText)) return string.Empty;

        var text = hintText.Trim();

        if (text.Length > _settings.MaxHintLength) return string.Empty;
        if (text.Contains("http://") || text.Contains("https://")) return string.Empty;
        if (text.Contains('<') || text.Contains('>')) return string.Empty;

        return text;
    }

    private sealed record HintPayload(List<HintItem> Hints);
    private sealed record HintItem(int QuestionId, string HintText);
}
```

```csharp
namespace AIIntegration;

/// <summary>
/// Signals that hints could not be produced. The caller degrades gracefully —
/// it must never turn this into a failed quiz submission.
/// </summary>
public sealed class AiUnavailableException : Exception
{
    public AiUnavailableException(string message) : base(message) { }
    public AiUnavailableException(string message, Exception inner) : base(message, inner) { }
}
```

**Retry policy.** The SDK already retries `408/409/429/5xx` and connection errors twice with backoff. **Do not add Polly on top** — it would multiply the wall-clock (`timeout × SDK retries × Polly retries`) inside a user-facing request. Configure `MaxRetries` on the client if you want a different count; the linked `CancellationTokenSource` above is the real ceiling.

## 12.5 Testing without calling the provider

Three layers, no network in any of them.

**1. Fake provider** — the whole point of `IAiHintGenerator` already existing:

```csharp
public sealed class FakeAiHintGenerator : IAiHintGenerator
{
    private readonly Func<GenerateHintsRequest, GenerateHintsResponse> _behaviour;

    public FakeAiHintGenerator(Func<GenerateHintsRequest, GenerateHintsResponse> behaviour)
        => _behaviour = behaviour;

    public static FakeAiHintGenerator Succeeding() => new(req => new GenerateHintsResponse
    {
        Hints = req.Questions
            .Select(q => new GeneratedHint { QuestionId = q.QuestionId, HintText = $"تلميح للسؤال {q.QuestionId}" })
            .ToList()
    });

    public static FakeAiHintGenerator Failing() => new(_ => throw new AiUnavailableException("simulated outage"));

    public static FakeAiHintGenerator ReturningNothing() => new(_ => new GenerateHintsResponse { Hints = [] });

    public Task<GenerateHintsResponse> GenerateHintsAsync(
        GenerateHintsRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(_behaviour(request));
}
```

**2. Unit tests** — parsing and safety, using a captured payload, no client:

```csharp
[Fact]
public void Parse_RejectsHintsForQuestionsThatWereNotAsked() { /* extra questionId dropped */ }

[Fact]
public void Sanitize_RejectsMarkupAndLinks() { /* "<b>x</b>", "http://..." → empty */ }

[Fact]
public void Sanitize_RejectsOverlongHints() { /* > MaxHintLength → empty */ }
```

**3. Integration tests** — `WebApplicationFactory` with the fake swapped in:

```csharp
builder.ConfigureTestServices(services =>
{
    services.RemoveAll<IAiHintGenerator>();
    services.AddScoped<IAiHintGenerator>(_ => FakeAiHintGenerator.Failing());
});

// Then assert the behaviour that actually matters:
// submitting with a wrong answer while the AI is down still returns 200,
// the attempt is Completed, mistakes exist, and currentHint is null.
```

That last assertion is the contract Part 13 defines — and it is the single most valuable test in the suite.

---

# PART 13 — AI FAILURE / DATA CONSISTENCY

## 13.1 Required behaviour (a stated decision, not an assumption)

> **A child's quiz submission must never fail because an AI service is unavailable.**

This is the product requirement you stated in Part 13's brief. It **supersedes an earlier decision in this codebase**: the current ordering (AI → transaction → commit) was chosen so that a completed attempt always has hints. That guarantee is real, but it buys hint-completeness at the cost of submission availability — the wrong trade for a children's learning app. The correct trade is graceful degradation.

## 13.2 Target flow

```
validate submission
   ↓
BEGIN TRANSACTION
   INSERT QuizAttemptMistakes
   UPDATE QuizAttempts → Completed  (RowVersion guard)
   UPDATE UserTopicStats
COMMIT                                    ← the child's work is now SAFE
   ↓
try  GenerateHintsAsync   (short timeout, outside any transaction)
   ↓ success                    ↓ failure (AiUnavailableException)
INSERT QuestionHints          log a warning, swallow
(second short transaction)    hints stay absent
   ↓                            ↓
200 with hints              200 with currentHint = null
```

The AI call stays **outside every transaction** — that requirement is preserved and strengthened.

## 13.3 The seven scenarios

| | Scenario | Behaviour under the target design |
|---|---|---|
| **A** | **DB succeeds, AI fails** | Attempt is `Completed`, score correct, mistakes recorded, stats updated. Response returns `retryQuestions` with `currentHint: null`. The child sees their score and can retry — just without a hint. **This is the case the whole redesign exists for.** |
| **B** | **AI succeeds, DB fails** | The commit happened *before* the AI call, so a DB failure at hint-insert time loses only the hints. The attempt stands. If the *main* transaction fails, nothing is written and the client retries safely — the AI was never called. |
| **C** | **AI times out** | Bounded at `Ai:TimeoutSeconds` (20 s) by a linked `CancellationTokenSource`, not the SDK's default. Identical to case A. |
| **D** | **User submits again** | Unchanged and already correct: sequential → 400, concurrent → 409 via `RowVersion`. The second attempt writes nothing and triggers no AI call. |
| **E** | **AI generates duplicate hints** | `UQ_QuestionHints_MistakeId_Sequence` blocks a repeat at the same sequence. The hint-generation step must additionally **only run for mistakes that have no hint yet**, so a later top-up cannot append a spurious sequence 2. |
| **F** | **Malformed JSON** | Structured output makes it unlikely; `JsonException` is caught in `Parse` and converted to `AiUnavailableException` → case A. Never a 500. |
| **G** | **Inappropriate content for children** | Four layers: the system prompt's explicit rules; the provider's own safety classifiers; the `Sanitize` guard (length, markup, links); and the fact that hints are **stored, not streamed** — so a bad hint is auditable and deletable after the fact. Add a moderation review path if the product later needs one. |

## 13.4 Service change

`SubmitAsync` splits its single transaction into *commit the child's work* then *best-effort hints*:

```csharp
// ── the child's work commits first, with no external dependency ──────────
await using (var transaction = await _db.Database.BeginTransactionAsync(cancellationToken))
{
    // add mistakes, finalise the attempt (RowVersion guard), update stats
    ...
    await transaction.CommitAsync(cancellationToken);
}

// ── hints are a bonus, never a precondition ──────────────────────────────
IReadOnlyDictionary<int, string> hintTextByQuestion;
try
{
    hintTextByQuestion = await GenerateHintsAsync(userId, confirmedMistakes, cancellationToken);
    await PersistHintsAsync(recordedMistakes, hintTextByQuestion, cancellationToken);
}
catch (AiUnavailableException ex)
{
    _logger.LogWarning(ex,
        "Hints unavailable for attempt {AttemptId}; submission completed without them", attemptId);
    hintTextByQuestion = new Dictionary<int, string>();          // degrade, do not throw
}

return BuildResult(attempt, hintTextByQuestion);                 // currentHint may be null
```

**Consequences to accept, stated explicitly:**

- `retryQuestions[].currentHint` becomes genuinely nullable in practice. The DTO already declares `string?`, so **no DTO contract change** — but the Flutter client must render the null case.
- `UserTopicStats.HintsUsedCount` is written before the hints exist, so it under-counts by one attempt until the next submit. Acceptable; it is a display counter, not an invariant.
- A "top up missing hints" path becomes worthwhile: the retry-Start endpoint can generate hints for mistakes that have none. Cheap to add, and it makes case A self-healing.

**No schema change required for Part 13.** `QuestionHints` rows are already optional per mistake — nothing in the schema requires a hint to exist.

---

# PART 14 — SECURITY AUDIT

| Area | Finding | Class |
|---|---|---|
| **Secrets in source control** | `appsettings.Development.json` is git-tracked and contains `Jwt:SecretKey`, the DB connection string and the SMTP password. `.gitignore` has no `appsettings` rule. **A leaked signing key lets anyone mint a token with arbitrary `sub` and `role` — including `Admin`, unlocking all 24 admin endpoints.** | **CRITICAL (S-1)** |
| **Secrets in logs** | `ForgotPasswordAsync` writes the plaintext OTP and the email to stdout on SMTP failure. Log access = account takeover | **CRITICAL (S-2)** |
| Authentication | JWT validated on issuer, audience, lifetime and signing key. Correct | NO ISSUE |
| Authorization | `[Authorize]` / `[Authorize(Roles="Admin")]` wired correctly against `ClaimTypes.Role`. But **no user can obtain `Admin`** | **CRITICAL (C-4)** |
| **IDOR** | **None found in any module.** Ownership is enforced in services from the JWT `sub` | NO ISSUE |
| Password storage | BCrypt with per-hash salt via `BCrypt.Net`. Correct | NO ISSUE |
| Refresh tokens | Hashed (SHA-256), rotated, revocable, expiring. Missing: reuse detection, concurrent-refresh guard, revoke-on-password-reset | **IMPORTANT (S-5, S-10)** |
| Reset OTP generation | `Random.Shared.Next(100000, 999999)` — **not cryptographically secure**. Use `RandomNumberGenerator.GetInt32(100000, 1000000)` | **IMPORTANT** |
| Google OAuth | ID token validated by `GoogleJsonWebSignature` against the configured `ClientId` — correct. `email_verified` never checked; no account-linking path (G-1) | **IMPORTANT** |
| Guest accounts | Anonymous, unthrottled row creation; no cleanup | **IMPORTANT (S-3)** |
| **Rate limiting** | **None anywhere** — login brute-force, OTP email-bombing, guest-account flooding, AI cost amplification all unmitigated | **IMPORTANT (S-4)** |
| Input validation | No `[Required]`, `[EmailAddress]`, or length attributes on any request DTO. `""` is an acceptable email; `"1"` an acceptable password | **IMPORTANT** |
| SQL injection | **Not reachable** — everything goes through EF LINQ; no raw SQL, no string concatenation into queries | NO ISSUE |
| Over-posting / mass assignment | Explicit DTOs everywhere; no entity is ever model-bound | NO ISSUE |
| Sensitive response fields | No hash, token, or `providerUserId` in any response. `IsCorrect` structurally absent from child DTOs | NO ISSUE |
| Server-controlled state | **No endpoint lets a client set attempt status, score, counts, or `IsCorrect`.** The removed recalculate endpoint was the one violation | NO ISSUE (after the fix) |
| CORS | Not configured. Mobile unaffected; Flutter **web** cannot call the API | HARDENING (S-9) |
| Exception leakage | 500s return a fixed Arabic string; the real exception is logged, not returned. Correct | NO ISSUE |
| Request size | 6 MB cap on upload only; the global Kestrel default (~30 MB) applies elsewhere | HARDENING |
| File uploads | Extension allow-list + 5 MB + GUID filename. **Content is not sniffed** — a renamed executable is storable, though it is only ever served as a static file | HARDENING |
| **AI data leakage** | Only question text, wrong option text, and prior hints leave the system. **No child PII, no identifiers, no correct answers.** Already correct — keep it that way | NO ISSUE |
| Child-facing APIs | The three child endpoints expose no other child's data and no answer keys | NO ISSUE |

---

# PART 15 — VALIDATION AND ERROR HANDLING

## 15.1 Which layer should own what

| Rule | Correct owner | Today |
|---|---|---|
| Shape/format (required, email, length, range) | **Controller** (attributes / `[ApiController]` auto-400) | **Missing entirely** |
| Business rules (attempt not completed, exactly one correct option, quiz type matches reference) | **Service** | **Correct** |
| Integrity invariants (uniqueness, FK, check constraints) | **Database**, with a friendly service pre-check | **Correct** |

The service and database layers are right. **The controller layer is absent** — add data annotations to request DTOs and let `[ApiController]` return `ValidationProblemDetails`. That is not a new framework; it is the one already in the box.

## 15.2 Error mapping

`ExceptionMiddleware` maps `KeyNotFound→404`, `ConflictException→409`, `ArgumentException→400`, `InvalidOperationException→400`, `UnauthorizedAccessException→403`, else 500 with a fixed message. **Use this; do not add a second system.** Three fixes:

1. **`InvalidOperationException → 400` is too broad.** EF and the BCL throw it for genuine faults ("Sequence contains no elements", "connection not open"), which would surface as a misleading 400. Introduce a `BusinessRuleException` for deliberate business rejections and let real `InvalidOperationException`s fall through to 500.
2. **`GetUserId()` throws `InvalidOperationException`** when `sub` is missing → **400**, but the correct answer is **401**. `Guid.Parse` on a malformed `sub` throws `FormatException` → **500**. Both should be 401.
3. **Unique-constraint violations in Identity are unhandled.** Duplicate registration and the Google-email collision (G-1) both reach the client as 500. Reuse the `DbUpdateExceptionExtensions.IsUniqueViolationOf` helper already built for Assessment.

## 15.3 Response-shape inconsistency

Identity and Content return `ApiResponse<T>`; Assessment returns raw DTOs; the middleware returns `{statusCode, message}`; binding failures return `ValidationProblemDetails`. **Four shapes.** This is the single biggest client-side cost in the API. Converging Assessment and the middleware onto `ApiResponse<T>` is a contained change — but it **breaks the current Flutter contract**, so schedule it deliberately rather than slipping it in.

---

# PART 16 — OBSERVABILITY

## 16.1 Current state

`ILogger` is injected in **exactly one place**: `ExceptionMiddleware`. Nothing else logs — not authentication, not the AI provider, not concurrency conflicts. There is one `Console.WriteLine`, and it prints a **secret** (S-2).

Diagnosability today:

| Question | Answerable? |
|---|---|
| Why did authentication fail? | **No** — 401s never reach the middleware |
| Why did a submission fail? | Partially — 500s are logged with a stack trace; 400/403/409 are not |
| How often do concurrency conflicts happen? | **No** |
| Is the AI provider failing, and how? | **No** — zero logging in `AIIntegration` |
| Which queries are slow? | **No** |
| Did an external call fail? | **No** |

## 16.2 Practical recommendations

Log these, and nothing more:

| Event | Level | Fields |
|---|---|---|
| Failed login / refresh | Warning | `userId` when known, provider, reason category — **never the token or password** |
| Assessment submit failure | Warning | `attemptId`, `userId`, failure category |
| Concurrency conflict (409) | Warning | `attemptId` — this is the metric that tells you whether double-submit is real |
| AI call outcome | Info (success: duration, question count) / Warning (failure: category, duration) | **never the prompt or the hint text** |
| AI degradation triggered | Warning | `attemptId` — how often children lose hints |
| Startup config validation | Critical | which key is missing |

**Never log:** passwords, JWTs, refresh tokens, reset tokens, OTPs, API keys, connection strings, or child names/emails beyond a `userId`.

Add `AddHealthChecks()` with a SQL Server probe and an `/health` endpoint. Skip distributed tracing until there is more than one process.

---

# PART 17 — MISSING OR INCOMPLETE

## Must have — the product does not work without these

| # | Item | Why |
|---|---|---|
| 1 | **Working configuration** (`Jwt` in Production, `Ai` populated, secrets out of git) | The app cannot start in Production and cannot generate a hint in Development |
| 2 | **AI graceful degradation** | One provider outage breaks every quiz submission |
| 3 | **Child-facing quiz discovery** | Flow B has no entry point |
| 4 | **A grantable `Admin` role** | 24 endpoints are unreachable; content cannot be authored |
| 5 | **Topics endpoint** | `UserTopicStats` is unrenderable without names |

## Should have — needed before real users

| # | Item |
|---|---|
| 6 | Rate limiting on `/api/auth/*` and submit |
| 7 | Input validation attributes on request DTOs |
| 8 | Result re-read / attempt history (recovery after a dropped submit) |
| 9 | Progress persistence (lesson completion) |
| 10 | `RefreshTokens.TokenHash` index + concurrent-refresh guard + revoke-on-reset |
| 11 | Content ordering unique constraints |
| 12 | Google↔local account linking (G-1) |
| 13 | `IsPublished` filtering for non-admins |
| 14 | Guest / expired-token / used-OTP cleanup job |
| 15 | Practical logging + health checks |

## Future

Idempotency keys on submit; `Abandoned` handling; content versioning; audit logging; soft deletion; AI hint caching; parent dashboard on `ParentChildLinks`; gamification.

**Deliberately not recommended:** an event bus, an outbox, distributed tracing, Redis, CQRS, repositories in Assessment, a separate AI microservice, or API versioning. None solves a problem this system currently has.

---

# PART 18 — CROSS-MODULE DATA FLOW

```
                        ┌──────────────┐
                        │   Identity   │  owns Users, RefreshTokens, OTPs, Links
                        └──────┬───────┘
                               │ issues JWT  (sub = userId, role)
                               │ ── one-way, no runtime call ──
              ┌────────────────┼────────────────┐
              ▼                ▼                ▼
       ┌────────────┐   ┌────────────┐   ┌──────────────┐
       │  Content   │   │ Assessment │──▶│ AIIntegration│
       │  Levels    │   │ Quizzes …  │◀──│ (owns no data)│
       │  Lessons   │   │ Attempts   │   └──────────────┘
       └────────────┘   └────────────┘
              ▲                │
              └── lessonId ────┘   (integer, carried by the CLIENT — no server call)

       Progress / Gamification:  DOES NOT EXIST
```

| Interaction | Owner | Initiator | Identifier | Data crossing | Sync? | On failure |
|---|---|---|---|---|---|---|
| Identity → all modules | Identity | the client (bearer token) | `sub` Guid | signed claims only | validated in middleware | 401, no controller runs |
| Assessment → Content | Content | Assessment (authoring) | `LevelId` / `LessonId` int | nothing at runtime — the id is stored, never dereferenced | n/a | **no failure mode; also no validation** |
| Content → Assessment | Assessment | **the client** (recommended, §6.2.2) | `lessonId` int | `quizId` back | sync HTTP | 404 → no quiz for this lesson |
| Assessment → AI | AIIntegration | Assessment | none — **no user identifier crosses** | question text, wrong option text, prior hints | sync, outside any transaction | **degrade: submission still succeeds** (Part 13) |
| Assessment → Identity | — | — | — | **nothing** | — | — |

**Circular dependencies: none.** The only bidirectional pair is Assessment↔AIIntegration, and that is a call plus a return, not a reference cycle — `AIIntegration` depends on `Shared` only, never on `AssessmentBL`.

---

# PART 19 — RECOMMENDED FINAL ARCHITECTURE

**This is the current structure with four additions and no restructuring.** Nothing existing is moved.

```
ElectroWorld.slnx
│
├── ElectroWorld  (Volt.csproj)          ── API host
│   ├── Controllers/
│   │   ├── Users/          AuthController · UsersController
│   │   ├── Content/        Levels · Lessons · LessonContents · ContentTypes · Media
│   │   └── AssessmentModule/
│   │           Quiz · Question · QuestionOption          (admin)
│   │           QuizAttempt · UserTopicStat               (child)
│   │         + QuizDiscoveryController   ← NEW  (unblocks B-1)
│   │         + TopicController           ← NEW  (unblocks B-5)
│   ├── Middleware/         ExceptionMiddleware  (+ ConflictException, + BusinessRuleException)
│   ├── Swagger/
│   └── Program.cs          ← AddAssessmentModule/AddAiIntegration now before Build()
│                             + AddRateLimiter + AddHealthChecks + startup config validation
│
├── Modules
│   ├── Identity     UsersBL  → UsersDA        (schema: Users)
│   ├── Content      ContentBL → ContentDA     (schema: dbo)
│   └── Assessment   AssessmentBL → AssessmentDA (schema: Assessment)
│
├── AIIntegration                          ── depends on Shared ONLY
│   ├── AiSettings.cs                      (+ Provider, Model, Timeout, Enabled, ApiKey)
│   ├── IExternalAiProvider.cs
│   ├── ClaudeHintProvider.cs              ← replaces HttpExternalAiProvider
│   ├── AiUnavailableException.cs          ← NEW
│   └── AiHintGenerator.cs                 (unchanged)
│
├── Shared
│   ├── Common/       clock · hashing · password · email · Result · ApiResponse
│   │                 + Exceptions/ConflictException
│   ├── Users/        JwtTokenGenerator · GoogleAuthValidator · ClaimsPrincipalExtensions
│   └── Assessment/AI/  IAiHintGenerator · GenerateHints{Request,Response}   ← the seam
│
├── Tests/Assessment.Tests                 ── + FakeAiHintGenerator, degradation tests
└── db/migrations                          ── ordered SQL, run manually
```

## Dependency rules (enforced by project references, not convention)

```
ElectroWorld   → UsersBL, ContentBL, AssessmentBL, AIIntegration, Shared
UsersBL        → UsersDA, Shared
ContentBL      → ContentDA, Shared
AssessmentBL   → AssessmentDA, Shared          ← never ContentDA, never UsersDA
AIIntegration  → Shared                        ← never any module
*DA            → Shared
Shared         → (nothing)
```

## Responsibilities

| Layer | Owns | Must not |
|---|---|---|
| **Controller** | routing, status codes, `[Authorize]`, DTO in/out, `CancellationToken` | contain business logic, touch `DbContext`, read `UserId` from the body |
| **Service** | business rules, ownership checks, transactions, DTO mapping | return entities, call another module's `DbContext` |
| **DbContext / config** | schema mapping mirroring SQL exactly | contain business rules |
| **AIIntegration** | provider details, prompt, timeout, validation, safety | know about attempts, users, or scores |
| **Shared** | cross-module contracts and genuinely-common utilities | accumulate module-specific logic |

**Three DbContexts, one database, one schema per module.** Correct as-is — **no change required.**

---

# PART 20 — FINAL PRIORITIZED ACTION PLAN

## Phase 1 — Critical correctness & security (before any merge to main)

| # | Problem | Why it matters | Exact action | Files | Schema? | Risk if ignored |
|---|---|---|---|---|---|---|
| 1.1 | **JWT key, DB connection string and SMTP password committed to git** | Anyone with repo access can forge a token for any user with `role: Admin` | Rotate the JWT secret, DB password and SMTP password **now**. `git rm --cached` the settings files, add `appsettings.*.json` (except the base) to `.gitignore`, move secrets to User Secrets (dev) and environment variables (prod). Purge history if the repo was ever public | `appsettings.Development.json`, `.gitignore` | No | Full account impersonation and privilege escalation |
| 1.2 | **Plaintext OTP written to stdout** | Log access = password reset for any account | Delete the `Console.WriteLine`; replace with `_logger.LogWarning(ex, "Password reset email failed for user {UserId}", user.Id)` | `UsersBL/Services/AuthService.cs` | No | Account takeover via logs |
| 1.3 | **Production cannot start** — `appsettings.Production.json` is `{}` and the base file has no `Jwt` section, so `Program.cs` throws | Deployment fails on first boot | Populate Production config from environment variables; add explicit startup validation for `Jwt`, `ConnectionStrings`, `Ai` | `appsettings.Production.json`, `Program.cs` | No | Total outage on deploy |
| 1.4 | **`Ai:HintsEndpoint` is empty → every wrong answer returns 500** | The product's main flow is broken today | Implement `ClaudeHintProvider` (Part 12) and the degradation path (Part 13) | `AIIntegration/*`, `AssessmentBL/Services/QuizAttemptService.cs` | No | Quizzes unusable |
| 1.5 | **AI availability gates quiz submission** | One outage breaks the core loop | Commit the attempt first; hints best-effort after (§13.4) | `QuizAttemptService.cs` | No | Children lose completed work |
| 1.6 | **`Admin` role is never granted** | 24 endpoints unreachable; no content can be authored | Add `Admin` to `UserRoles` + the `Users.Role` CHECK constraint; grant it by seed/manual promotion, **never** from `RegisterEmailRequest.Role` | `UsersBL/Constants.cs`, SQL | **Yes** — `ALTER` the Role CHECK | Admin surface is dead code |
| 1.7 | **`GetUserId()` returns 400/500 for auth problems** | Clients cannot distinguish "log in again" from "bad request" | Throw an exception the middleware maps to 401 | `Shared/Users/ClaimsPrincipalExtensions.cs`, `ExceptionMiddleware.cs` | No | Broken client auth handling |

## Phase 2 — Business-flow fixes

| # | Problem | Why | Action | Files | Schema? | Risk |
|---|---|---|---|---|---|---|
| 2.1 | No child-facing quiz discovery | Flow B has no entry point | Add `GET /api/quizzes/for-lesson/{lessonId}` (§6.2.2) | `IQuizService`, `QuizService`, new controller | No | App cannot start a quiz |
| 2.2 | No topics endpoint | Stats unrenderable | Add `GET /api/assessment/topics` | new service + controller | No | Statistics screen impossible |
| 2.3 | Result served once; no history | A dropped submit loses the result screen | Add `status` + score to `GET /api/quiz-attempts/{id}` | `QuizAttemptService`, DTO | No | Unrecoverable UX on flaky networks |
| 2.4 | Unpublished lessons visible to all | Draft content leaks to children | Filter `IsPublished` for non-admin callers | `LessonService.cs` | No | Content leak |
| 2.5 | Google email collides with a local account → 500 | Users hard-stuck with no explanation | Detect the collision and return a clear 409, or implement linking | `AuthService.LoginOrRegisterWithGoogleAsync` | No | Permanent lockout for affected users |
| 2.6 | Duplicate registration race → 500 | Should be 409 | Catch the unique violation with the existing helper | `AuthService`, `UsersDA` | No | Confusing 500s |

## Phase 3 — Database & EF

| # | Problem | Why | Action | Files | Schema? | Risk |
|---|---|---|---|---|---|---|
| 3.1 | `RefreshTokens.TokenHash` unindexed | Table scan on every auth call, growing forever | `CREATE UNIQUE INDEX UQ_RefreshTokens_TokenHash` + EF `HasIndex` | SQL, `UsersDbContext` | **Yes** | Auth latency degrades with usage |
| 3.2 | Content ordering has no uniqueness | Duplicate `SortOrder` under concurrency | Add the two unique constraints (Part 4); rework `swap-order` to a 3-step swap | SQL, `ContentDbContext`, `LessonService`, `LessonContentService` | **Yes** | Corrupted lesson ordering |
| 3.3 | Concurrent refresh mints two valid tokens | Rotation guarantee broken | Conditional update: revoke `WHERE Id = @id AND RevokedAt IS NULL` and require 1 row affected | `RefreshTokenRepository`, `AuthService` | No | Token-rotation bypass |
| 3.4 | Password reset leaves sessions alive | Attacker retains access 30 days | Revoke the user's refresh tokens inside `ResetPasswordAsync` | `AuthService` | No | Ineffective compromise recovery |
| 3.5 | Hardcoded `OnConfiguring` fallbacks to two different DBs | Silent wrong-database connection | Delete both overrides; use `IDesignTimeDbContextFactory` if tooling needs one | `UsersDbContext`, `ContentDbContext` | No | Silent data loss/confusion |
| 3.6 | `PasswordResetOTPs.ResetTokenHash` unindexed | Scan per reset | Filtered index (Part 5) | SQL, `UsersDbContext` | **Yes** | Minor latency |

## Phase 4 — Performance

| # | Problem | Why | Action | Files | Schema? | Risk |
|---|---|---|---|---|---|---|
| 4.1 | N+1 blocking queries per submit | Thread-pool blocking on the hottest write path | Load the mistake set once (§8.2) | `UserTopicStatService.cs` | No | Throughput collapse under load |
| 4.2 | Full hint-history scan per submit | Cost grows with account age forever | Make `HintsUsedCount` incremental (§8.3) | `UserTopicStatService.cs` | No | Submits slow down permanently |
| 4.3 | No timeout on the AI call | 100 s thread hold | Linked `CancellationTokenSource` (Part 12) | `ClaudeHintProvider.cs` | No | Thread exhaustion during an outage |
| 4.4 | Content reads tracked | Wasted allocation on the most-called endpoints | `AsNoTracking()` on read-only repository methods | `ContentDA/Repositories/*` | No | Minor |
| 4.5 | Two round trips for a max | — | `MaxAsync(x => (int?)x) ?? 0` | `LessonRepository`, `LevelRepository`, `LessonContentRepository` | No | Minor |
| 4.6 | Static lookups re-read constantly | — | `IMemoryCache` for `ContentTypes` and `Levels`. **Not Redis** | `ContentTypeService`, `LevelService` | No | Minor |

## Phase 5 — AI integration

| # | Action | Files | Schema? |
|---|---|---|---|
| 5.1 | Implement `ClaudeHintProvider` with structured output, timeout, typed exception handling and the safety guard (Part 12) | `AIIntegration/*` | No |
| 5.2 | Expand `AiSettings`; resolve the key from User Secrets / environment, never from `appsettings` | `AiSettings.cs`, config | No |
| 5.3 | Add `AiUnavailableException` and the degradation path | `AIIntegration`, `QuizAttemptService` | No |
| 5.4 | Add `FakeAiHintGenerator` + degradation integration test ("AI down → submit still 200") | `Tests/Assessment.Tests` | No |
| 5.5 | Only generate hints for mistakes that have none, so a top-up cannot duplicate a sequence | `QuizAttemptService` | No |
| 5.6 | Optional: cache hints on `(questionId, wrongOptionId, previousHintCount)` to cap cost | `AIIntegration` | No |

## Phase 6 — Hardening

| # | Action |
|---|---|
| 6.1 | Rate limiting (`AddRateLimiter`) on `/api/auth/*` and submit — built-in, no new dependency |
| 6.2 | Validation attributes on every request DTO; let `[ApiController]` produce `ValidationProblemDetails` |
| 6.3 | `RandomNumberGenerator.GetInt32` for the reset OTP |
| 6.4 | Practical logging (Part 16) + `AddHealthChecks()` |
| 6.5 | Guest / expired-token / used-OTP cleanup job |
| 6.6 | `BusinessRuleException` so real `InvalidOperationException`s stop masquerading as 400 |
| 6.7 | CORS policy if a Flutter web build is planned |
| 6.8 | Align `PUT /api/users/me` to 404 on missing user |
| 6.9 | Converge Assessment onto `ApiResponse<T>` — **coordinate with the Flutter team; it is a breaking change** |
| 6.10 | Progress persistence (new Content table + endpoints) once the flow above is stable |

---

## Closing assessment

The **architecture is sound and should not be redesigned.** Module boundaries are real and enforced, `DbContext` isolation holds without exception, controllers are thin, ownership checks are correctly placed, and the Assessment schema — snapshot columns, `RowVersion`, composite FKs, retry uniqueness — is genuinely well designed. Nothing in this report recommends Clean Architecture, CQRS, MediatR, events, repositories, or microservices, because nothing in this system needs them.

What is wrong is **configuration, availability, and three missing connections**: secrets in git, a Production config that cannot boot, an AI dependency that can fail the core flow, an Admin role nobody can hold, and no server-side path from a lesson to its quiz. All five are fixable without touching the architecture — and until they are fixed, the product does not run end-to-end.
