# ElectroWorld API Contract — Flutter Integration Guide

**Audience:** Flutter / frontend team
**Generated from:** the code on branch `basmala-dev` (commit `a60a5f8`), by reading controllers, DTOs, services, `Program.cs` and `ExceptionMiddleware`.
**Goal:** build every screen against mock JSON, with no live backend.

> Every JSON block in this document is copy-paste ready. Files are also written under `docs/mocks/`.

---

## READ THIS FIRST — three response envelopes

The backend does **not** use one response shape. This is the single biggest thing to get right, and it is a real inconsistency in the backend (see §11).

| Module | Success shape | Wrapper class |
|---|---|---|
| **Identity** (`/api/auth`, `/api/users`) | `{ "success", "message", "data" }` | `ApiResponse<T>` |
| **Content** (`/api/content/*`) | `{ "success", "message", "data" }` | `ApiResponse<T>` |
| **Assessment** (`/api/quizzes`, `/api/questions`, `/api/question-options`, `/api/quiz-attempts`, `/api/user-topic-stats`) | **raw DTO, no wrapper** | — |
| **Any thrown exception** (Assessment only) | `{ "statusCode", "message" }` | `ExceptionMiddleware` |

Practically, in Dart:

```dart
// Identity + Content
final body = jsonDecode(res.body);
if (body['success'] == true) { model = Level.fromJson(body['data']); }

// Assessment — parse the DTO directly
model = QuizAttemptResponse.fromJson(jsonDecode(res.body));

// Assessment errors
final err = jsonDecode(res.body); // { "statusCode": 409, "message": "..." }
```

Identity/Content never throw to the middleware; they return `success:false` with HTTP 400/401/404. Assessment has no envelope and signals failure only through the HTTP status + `{statusCode, message}`.

---

## 1. Base URL

Not pinned in code — set by hosting. Local dev default from `launchSettings`/Kestrel:

```
https://localhost:7xxx
http://localhost:5xxx
```

All routes below are relative to that origin. **Note:** `app.UseHttpsRedirection()` is active, so plain HTTP is redirected.

There is **no** `/api/assessment/...` prefix. Assessment routes sit at the root: `/api/quizzes`, `/api/quiz-attempts`, etc.

---

## 2. Authentication

JWT Bearer. Obtain a token from any `/api/auth/*` endpoint, then send:

```http
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

### JWT claims the backend reads

| Claim | Maps to | Used for |
|---|---|---|
| `sub` | `UserId` (Guid) | every ownership check |
| `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` | `Role` | `[Authorize(Roles="Admin")]` |
| `authProvider` | `Email` / `Google` / `Guest` | informational |
| `jti` | token id | — |

`MapInboundClaims = false` in `Program.cs`, so the claim is literally `sub`, not the long `nameidentifier` URI. The role claim **is** the long Microsoft URI, because `JwtTokenGenerator` emits `ClaimTypes.Role`.

### Flutter must NOT send UserId

```text
Flutter must NOT send UserId in any request body, query string or path.
The backend takes UserId from the authenticated JWT `sub` claim
(User.GetUserId()) on every user-owned endpoint.
```

There is exactly one exception, and it is not an identity: `RegisterEmailRequest.existingGuestUserId` / `GoogleAuthRequest.existingGuestUserId`, used to upgrade an anonymous guest account into a real one. That is a pre-auth call, so there is no JWT yet.

### Token lifetimes

| Token | Lifetime | Source |
|---|---|---|
| Access token | `Jwt:AccessTokenExpirationMinutes` (config) | `accessTokenExpiresAt` in the response |
| Refresh token | **30 days** | hard-coded in `AuthService.IssueTokensAsync` |
| Password reset OTP | **15 minutes**, max **5** attempts | `AuthService.ForgotPasswordAsync` |
| Password reset token | **10 minutes**, single use | `AuthService.VerifyResetOtpAsync` |

Refresh tokens **rotate**: every successful `/api/auth/refresh` revokes the one you sent and returns a new one. Store the new one immediately or the user is logged out.

### Auth levels used in this document

| Level | Meaning |
|---|---|
| `Anonymous` | no token needed |
| `Authenticated` | any valid token (Parent or Child) |
| `Admin` | `[Authorize(Roles="Admin")]` |

> **⚠ `Admin` is currently unreachable.** `AuthService.NormalizeRole` can only ever produce `"Parent"` or `"Child"` — no code path assigns `"Admin"`. Every Admin endpoint in this document therefore returns **403** for every real user today. See §11, issue #2.

---

## 3. Common headers

**Request**

```http
Content-Type: application/json        # every endpoint with a JSON body
Authorization: Bearer <access_token>  # every Authenticated/Admin endpoint
Accept: application/json
```

Image upload is the one exception:

```http
Content-Type: multipart/form-data
```

**Response**

```http
Content-Type: application/json; charset=utf-8
```

Messages are in **Arabic**. Do not match on message text — match on HTTP status. The strings are UI-ready if you want to show them directly.

---

## 4. Common error formats

### 4a. Identity & Content — `ApiResponse`

Failure is carried in the body with `success: false`, alongside a 400/401/404 status.

```json
{
  "success": false,
  "message": "بيانات الدخول غير صحيحة",
  "data": null
}
```

For endpoints returning no data (`ApiResponse` without `<T>`), the `data` key is **absent entirely**, not null:

```json
{
  "success": false,
  "message": "Token غير موجود"
}
```

### 4b. Assessment — `ExceptionMiddleware`

Assessment services throw; the middleware maps the exception type to a status:

```json
{
  "statusCode": 409,
  "message": "المحاولة رقم 42 تم تسليمها بالفعل"
}
```

| Exception thrown | HTTP | When |
|---|---|---|
| `KeyNotFoundException` | **404** | entity id does not exist |
| `ConflictException` | **409** | lost a concurrency race / uniqueness violation |
| `ArgumentException` | **400** | invalid input |
| `InvalidOperationException` | **400** | business rule violated |
| `UnauthorizedAccessException` | **403** | resource belongs to another user |
| anything else | **500** | body is always `{"statusCode":500,"message":"حدث خطأ داخلي في الخادم"}` — the real error is never leaked |

### 4c. 401 from the framework

If the `Authorization` header is missing, malformed or expired, ASP.NET Core rejects the request **before** any controller code:

```http
401 Unauthorized
WWW-Authenticate: Bearer
```

**Body is empty.** Do not try to parse it. This applies to every Authenticated/Admin endpoint in every module.

### 4d. Model-binding failures — `ValidationProblemDetails`

`[ApiController]` auto-returns RFC 7807 when the JSON itself will not bind (wrong type, malformed body). This bypasses both envelopes:

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "traceId": "00-8f1c2b3a6b4d4e2a9c1f3d7e5a2b1c0d-1a2b3c4d5e6f7a8b-00",
  "errors": {
    "quizId": ["The value 'abc' is not valid."]
  }
}
```

Handle three 400 shapes: `ApiResponse`, `{statusCode,message}`, and this.

### 4e. Status codes NOT used

```text
422 Unprocessable Entity — never returned. Validation failures are 400.
429 Too Many Requests    — no rate limiting is implemented anywhere.
```

The OTP `Attempts >= 5` lockout returns **400**, not 429.

---

## 5. JSON conventions

### Property naming
**camelCase.** `AddControllers()` uses `System.Text.Json` defaults, so C# `QuestionText` → JSON `questionText`, `AttemptId` → `attemptId`.

### Types

| C# | JSON | Dart |
|---|---|---|
| `int`, `short`, `byte`, `long` | number | `int` |
| `decimal` | number (e.g. `66.67`) | `double` |
| `bool` | `true` / `false` | `bool` |
| `Guid` | string `"3fa85f64-5717-4562-b3fc-2c963f66afa6"` | `String` |
| `string?` | string or `null` | `String?` |
| `DateTime` | ISO 8601 string — **see below** | `DateTime` |

`long` (`attemptId`, `mistakeId`, stat `id`) exceeds 32-bit range in theory. Dart `int` is 64-bit natively, so `int` is correct — but if you ever target web, use `num`.

### ⚠ DateTime — two different formats from the same DTO

`System.Text.Json` writes `DateTime` based on its `Kind`, and the backend produces **both**:

| Origin | `Kind` | JSON | Example |
|---|---|---|---|
| Just computed in memory (`DateTime.UtcNow`) | `Utc` | **with `Z`** | `"2026-09-10T18:30:00.1234567Z"` |
| Read back from SQL Server `datetime2` | `Unspecified` | **no `Z`, no offset** | `"2026-09-10T18:30:00"` |

Concretely: `POST /api/quizzes` returns `createdAt` **with** `Z` (in-memory value), but `GET /api/quizzes/{id}` returns the same field **without** `Z` (loaded from the DB). Same DTO, same field, two formats.

**All timestamps are UTC** regardless of format — the backend only ever writes `DateTime.UtcNow` / `SYSUTCDATETIME()`. Parse defensively:

```dart
DateTime parseUtc(String s) =>
    DateTime.parse(s.endsWith('Z') ? s : '${s}Z').toUtc();
```

Fractional seconds are present or absent unpredictably (7 digits, or none). `DateTime.parse` handles both.

This is a backend inconsistency, logged as issue #4 in §11.

---

## 6. Enum-like string values

These are plain strings in JSON, validated by `CHECK` constraints in SQL Server. **Exact casing as shown.**

```text
QuizType (quizType)
- LevelAssessment
- LessonQuiz
- LessonReview
- Standalone

AttemptStatus (Status — internal; see note)
- InProgress
- Completed
- Abandoned

Difficulty (difficulty)
- Easy
- Medium
- Hard
- Advanced

LearningLevel (Topics — not exposed by any endpoint today)
- Beginner
- Intermediate
- Advanced

Role (role)
- Parent
- Child
                       ("Admin" is checked by [Authorize] but never issued)

AuthProvider (authProvider)
- Email
- Google
- Guest
```

**Note on `AttemptStatus`:** the values exist in the DB and drive backend behaviour, but **no response DTO exposes `status`**. Flutter infers state from context: a `QuizAttemptResponseDto` from Start is in progress; receiving a `QuizAttemptResultDto` means Completed. `Abandoned` is never set by any code path. Logged as issue #7 in §11.

---

## 7. Pagination

```text
Pagination: implemented on exactly ONE endpoint — GET /api/quizzes
Every other list endpoint: Pagination: Not implemented (full list returned)
```

`PagedResult<T>` has **no** `hasNextPage` / `totalPages`. Compute them client-side:

```dart
final totalPages = (totalCount / pageSize).ceil();
final hasNext = pageNumber < totalPages;
```

Details in §7.1.

---

## 8. Endpoint summary table

**49 endpoints implemented.** Auth column: `—` anonymous, `Auth` any valid token, `Admin` role-gated.

### Identity — `ApiResponse<T>` envelope

| Method | Endpoint | Auth | Request | Success | Main errors |
|---|---|---|---|---|---|
| POST | `/api/auth/guest` | — | JSON | 200 | — |
| POST | `/api/auth/register` | — | JSON | 200 | 400 |
| POST | `/api/auth/login` | — | JSON | 200 | 401 |
| POST | `/api/auth/google` | — | JSON | 200 | 400 |
| POST | `/api/auth/refresh` | — | JSON | 200 | 401 |
| POST | `/api/auth/logout` | Auth | JSON | 200 | 400, 401 |
| POST | `/api/auth/forgot-password` | — | JSON | 200 | — |
| POST | `/api/auth/verify-reset-otp` | — | JSON | 200 | 400 |
| POST | `/api/auth/reset-password` | — | JSON | 200 | 400 |
| GET | `/api/users/me` | Auth | None | 200 | 401, 404 |
| PUT | `/api/users/me` | Auth | JSON | 200 | 400, 401 |

### Content — `ApiResponse<T>` envelope

| Method | Endpoint | Auth | Request | Success | Main errors |
|---|---|---|---|---|---|
| GET | `/api/content/content-types` | Auth | None | 200 | 401 |
| GET | `/api/content/levels` | Auth | None | 200 | 401 |
| GET | `/api/content/levels/{id}` | Auth | None | 200 | 401, 404 |
| POST | `/api/content/levels` | Admin | JSON | 200 | 401, 403 |
| PUT | `/api/content/levels/{id}` | Admin | JSON | 200 | 400, 401, 403 |
| DELETE | `/api/content/levels/{id}` | Admin | None | 200 | 400, 401, 403 |
| POST | `/api/content/levels/swap-order` | Admin | JSON | 200 | 400, 401, 403 |
| GET | `/api/content/levels/{levelId}/lessons` | Auth | None | 200 | 401 |
| GET | `/api/content/lessons/{id}` | Auth | None | 200 | 401, 404 |
| POST | `/api/content/lessons` | Admin | JSON | 200 | 400, 401, 403 |
| PUT | `/api/content/lessons/{id}` | Admin | JSON | 200 | 400, 401, 403 |
| PATCH | `/api/content/lessons/{id}/publish` | Admin | JSON | 200 | 400, 401, 403 |
| DELETE | `/api/content/lessons/{id}` | Admin | None | 200 | 400, 401, 403 |
| POST | `/api/content/lessons/swap-order` | Admin | JSON | 200 | 400, 401, 403 |
| POST | `/api/content/lessons/{lessonId}/contents` | Admin | JSON | 200 | 400, 401, 403 |
| PUT | `/api/content/contents/{contentId}` | Admin | JSON | 200 | 400, 401, 403 |
| DELETE | `/api/content/contents/{contentId}` | Admin | None | 200 | 400, 401, 403 |
| POST | `/api/content/contents/swap-order` | Admin | JSON | 200 | 400, 401, 403 |
| POST | `/api/content/media/images` | Admin | multipart | 200 | 400, 401, 403 |

### Assessment — **no envelope**, raw DTOs

| Method | Endpoint | Auth | Request | Success | Main errors |
|---|---|---|---|---|---|
| GET | `/api/quizzes` | Admin | Query | 200 | 401, 403 |
| GET | `/api/quizzes/{quizId}` | Admin | None | 200 | 401, 403, 404 |
| POST | `/api/quizzes` | Admin | JSON | 201 | 400, 401, 403 |
| PUT | `/api/quizzes/{quizId}` | Admin | JSON | 200 | 400, 401, 403, 404 |
| PATCH | `/api/quizzes/{quizId}/active` | Admin | Query | 204 | 401, 403, 404 |
| GET | `/api/questions?quizId=` | Admin | Query | 200 | 401, 403, 404 |
| GET | `/api/questions/{questionId}` | Admin | None | 200 | 401, 403, 404 |
| POST | `/api/questions` | Admin | JSON | 201 | 400, 401, 403, 404, 409 |
| PUT | `/api/questions/{questionId}` | Admin | JSON | 200 | 400, 401, 403, 404, 409 |
| PATCH | `/api/questions/{questionId}/active` | Admin | Query | 204 | 400, 401, 403, 404 |
| GET | `/api/question-options?questionId=` | Admin | Query | 200 | 401, 403, 404 |
| POST | `/api/question-options` | Admin | JSON | 201 | 400, 401, 403, 404, 409 |
| PUT | `/api/question-options/{optionId}` | Admin | JSON | 200 | 400, 401, 403, 404, 409 |
| DELETE | `/api/question-options/{optionId}` | Admin | None | 204 | 400, 401, 403, 404 |
| **POST** | **`/api/quiz-attempts`** | **Auth** | **Query** | **201** | **400, 401, 403, 404, 409** |
| **POST** | **`/api/quiz-attempts/{attemptId}/submit`** | **Auth** | **JSON** | **200** | **400, 401, 403, 404, 409** |
| **GET** | **`/api/quiz-attempts/{attemptId}`** | **Auth** | **None** | **200** | **401, 403, 404** |
| GET | `/api/user-topic-stats` | Auth | None | 200 | 401 |
| GET | `/api/user-topic-stats/{topicId}/{difficulty}` | Auth | None | 200 | 401, 404 |

**The five bold/child-facing rows are the entire student app surface.** Everything else is admin tooling.

---

# 9. Identity API

## 9.1 POST /api/auth/guest

**Purpose:** create an anonymous account so a child can start using the app instantly. Returns a full token pair — the account is real, just without email/password.

**Authentication:** `Anonymous`

**Request body**

| Field | Type | Required | Rules |
|---|---|---|---|
| `fullName` | string | **yes** | no server-side validation — send a non-empty value |
| `role` | string? | no | `"Parent"` (case-insensitive) → Parent; **anything else, including null → `"Child"`** |

**Sample Request**

```http
POST /api/auth/guest
Content-Type: application/json
```

```json
{
  "fullName": "Youssef",
  "role": null
}
```

**Success Response:** `200 OK`

```json
{
  "success": true,
  "message": "تم إنشاء حساب Guest بنجاح",
  "data": {
    "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "fullName": "Youssef",
    "role": "Child",
    "authProvider": "Guest",
    "age": null,
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIzZmE4NWY2NC01NzE3LTQ1NjItYjNmYy0yYzk2M2Y2NmFmYTYifQ.mock-signature",
    "refreshToken": "cXVpY2tSZWZyZXNoVG9rZW5WYWx1ZUV4YW1wbGU=",
    "accessTokenExpiresAt": "2026-09-10T12:15:00.0000000Z"
  }
}
```

**Possible Errors:** none. `RegisterGuestAsync` has no failure path — it always succeeds.

**Flutter Notes:**
- Persist `userId` — you need it as `existingGuestUserId` if the child later upgrades to a real account, and it is the only way to keep their progress.
- `age` is always `null` for guests (no field to set it).
- `role` is `"Child"` unless you explicitly send `"Parent"`.

---

## 9.2 POST /api/auth/register

**Purpose:** create an email/password account, **or** upgrade an existing guest account in place (same `userId`, all progress preserved).

**Authentication:** `Anonymous`

**Request body**

| Field | Type | Required | Rules |
|---|---|---|---|
| `email` | string | **yes** | must not already exist. **No format validation** — the backend does not check it is an email |
| `password` | string | **yes** | **no length/complexity rule enforced** |
| `fullName` | string | **yes** | no validation |
| `role` | string? | no | `"Parent"` → Parent, else Child |
| `age` | int? | no | no range validation |
| `existingGuestUserId` | Guid? | no | if set, that guest row is converted instead of creating a new user |

**Sample Request**

```http
POST /api/auth/register
Content-Type: application/json
```

```json
{
  "email": "ahmed@example.com",
  "password": "P@ssw0rd123",
  "fullName": "Ahmed Ali",
  "role": "Parent",
  "age": 35,
  "existingGuestUserId": null
}
```

Guest upgrade — identical, but pass the stored guest id:

```json
{
  "email": "youssef.parent@example.com",
  "password": "P@ssw0rd123",
  "fullName": "Youssef",
  "role": "Child",
  "age": 9,
  "existingGuestUserId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

**Success Response:** `200 OK` — same `AuthResponse` shape as §9.1, with `"authProvider": "Email"`.

```json
{
  "success": true,
  "message": "تم التسجيل بنجاح",
  "data": {
    "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "fullName": "Ahmed Ali",
    "role": "Parent",
    "authProvider": "Email",
    "age": 35,
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.mock.token",
    "refreshToken": "cXVpY2tSZWZyZXNoVG9rZW5WYWx1ZUV4YW1wbGU=",
    "accessTokenExpiresAt": "2026-09-10T12:15:00.0000000Z"
  }
}
```

**Possible Errors**

`400` — email taken:

```json
{ "success": false, "message": "البريد الإلكتروني مستخدم بالفعل", "data": null }
```

`400` — guest upgrade with a bad/already-converted id:

```json
{ "success": false, "message": "حساب الـ Guest غير موجود أو اتحول قبل كده", "data": null }
```

**Flutter Notes:**
- On a guest upgrade the returned `userId` is the **same** guest id. Do not create a new local profile.
- The email-taken check runs **before** the guest branch, so upgrading to an address already in use fails with the first message.
- Validate email format and password strength **client-side** — the backend does not (issue #5).

---

## 9.3 POST /api/auth/login

**Purpose:** email/password sign-in.

**Authentication:** `Anonymous`

**Request body**

| Field | Type | Required |
|---|---|---|
| `email` | string | **yes** |
| `password` | string | **yes** |

**Sample Request**

```http
POST /api/auth/login
Content-Type: application/json
```

```json
{ "email": "ahmed@example.com", "password": "P@ssw0rd123" }
```

**Success Response:** `200 OK` — `AuthResponse`, message `"تم تسجيل الدخول بنجاح"`.

**Possible Errors**

`401` — wrong email or password (deliberately indistinguishable):

```json
{ "success": false, "message": "بيانات الدخول غير صحيحة", "data": null }
```

`401` — account disabled:

```json
{ "success": false, "message": "الحساب غير مفعّل", "data": null }
```

**Flutter Notes:** a guest account has no password, so it can never log in here — guests are recovered only from the stored token, or upgraded via §9.2.

---

## 9.4 POST /api/auth/google

**Purpose:** sign in or register with a Google ID token; can also upgrade a guest.

**Authentication:** `Anonymous`

**Request body**

| Field | Type | Required | Rules |
|---|---|---|---|
| `idToken` | string | **yes** | Google ID token from the Flutter Google Sign-In SDK. Validated against `Google:ClientId` |
| `role` | string? | no | used **only when creating a new account** |
| `existingGuestUserId` | Guid? | no | converts that guest to a Google account |

**Sample Request**

```http
POST /api/auth/google
Content-Type: application/json
```

```json
{
  "idToken": "eyJhbGciOiJSUzI1NiIsImtpZCI6IjFhMmIzYzRkIn0.google.id.token",
  "role": "Child",
  "existingGuestUserId": null
}
```

**Success Response:** `200 OK` — `AuthResponse` with `"authProvider": "Google"`. `age` is `null` for a fresh Google account (Google does not supply it).

**Possible Errors**

`400` — invalid/expired token or wrong audience:

```json
{ "success": false, "message": "Google Token غير صالح", "data": null }
```

`400` — bad guest id: `"حساب الـ Guest غير موجود أو اتحول قبل كده"`
`400` — disabled account: `"الحساب غير مفعّل"`

**Flutter Notes:**
- Send the **ID token**, not the access token.
- Resolution order: existing Google account → guest upgrade → brand-new account. If the user already has a Google account, `existingGuestUserId` is ignored.
- On a new account `fullName` falls back to the email when Google returns no name.

---

## 9.5 POST /api/auth/refresh

**Purpose:** exchange a refresh token for a new token pair.

**Authentication:** `Anonymous` (the refresh token *is* the credential — do **not** send the expired access token)

**Request body**

| Field | Type | Required |
|---|---|---|
| `refreshToken` | string | **yes** |

**Sample Request**

```http
POST /api/auth/refresh
Content-Type: application/json
```

```json
{ "refreshToken": "cXVpY2tSZWZyZXNoVG9rZW5WYWx1ZUV4YW1wbGU=" }
```

**Success Response:** `200 OK` — a full `AuthResponse` with a **new** `accessToken` **and a new `refreshToken`**.

**Possible Errors**

`401` — unknown, revoked, expired, or already-rotated token:

```json
{ "success": false, "message": "Refresh Token غير صالح أو منتهي", "data": null }
```

`401` — user deleted or disabled: `"المستخدم غير موجود أو غير مفعّل"`

**Flutter Notes:**
- **Rotation is mandatory.** The old refresh token is revoked on every successful call. Overwrite your stored copy atomically or you lock the user out.
- Serialise refresh calls behind a mutex — two concurrent refreshes mean one gets 401.
- On 401 here, clear storage and route to login.

---

## 9.6 POST /api/auth/logout

**Purpose:** revoke one refresh token.

**Authentication:** `Authenticated` — needs a valid access token **and** the refresh token in the body.

**Request body**

| Field | Type | Required |
|---|---|---|
| `refreshToken` | string | **yes** |

**Sample Request**

```http
POST /api/auth/logout
Authorization: Bearer <access_token>
Content-Type: application/json
```

```json
{ "refreshToken": "cXVpY2tSZWZyZXNoVG9rZW5WYWx1ZUV4YW1wbGU=" }
```

**Success Response:** `200 OK` — note there is **no `data` key**:

```json
{ "success": true, "message": "تم تسجيل الخروج بنجاح" }
```

**Possible Errors**

`400` — token not found (already logged out):

```json
{ "success": false, "message": "Token غير موجود" }
```

`401` — access token missing/expired. **Empty body.**

**Flutter Notes:**
- Revokes only the token you send; other devices stay signed in. There is no "log out everywhere".
- It does **not** invalidate the access token, which stays valid until `accessTokenExpiresAt`. Clear local storage yourself.
- Treat 400 as success for UX purposes — the end state is the same.

---

## 9.7 POST /api/auth/forgot-password

**Purpose:** email a 6-digit reset OTP.

**Authentication:** `Anonymous`

**Request body:** `{ "email": string }` — required.

**Sample Request**

```http
POST /api/auth/forgot-password
Content-Type: application/json
```

```json
{ "email": "ahmed@example.com" }
```

**Success Response:** `200 OK` — **always**, even for an unknown email (user-enumeration protection):

```json
{ "success": true, "message": "لو الإيميل مسجل، هيوصلك كود إعادة التعيين" }
```

**Possible Errors:** none. There is no failure path — including when SMTP itself fails (the send is wrapped in try/catch).

**Flutter Notes:**
- Never tell the user whether the address exists; mirror the backend's wording.
- Only `authProvider == "Email"` accounts get an OTP. Google/Guest users silently get nothing but still see 200.
- OTP is valid **15 minutes**; requesting a new one invalidates all previous ones.

---

## 9.8 POST /api/auth/verify-reset-otp

**Purpose:** exchange a valid OTP for a short-lived reset token.

**Authentication:** `Anonymous`

**Request body**

| Field | Type | Required | Rules |
|---|---|---|---|
| `email` | string | **yes** | |
| `otp` | string | **yes** | 6 digits, `"100000"`–`"999999"` |

**Sample Request**

```http
POST /api/auth/verify-reset-otp
Content-Type: application/json
```

```json
{ "email": "ahmed@example.com", "otp": "482913" }
```

**Success Response:** `200 OK`

```json
{
  "success": true,
  "message": "الكود صحيح",
  "data": {
    "resetToken": "xY9k2mP7qzR4tL8vN3wS6dF1gH5jK0aB7cE4nM2pQ9rT6uV8yZ3xW1sD5fG7hJ0k",
    "resetTokenExpiresAt": "2026-09-10T12:10:00.0000000Z"
  }
}
```

**Possible Errors**

`400` — expired, or **5 failed attempts** used up:

```json
{ "success": false, "message": "الكود منتهي أو غير صالح، اطلبي كود جديد", "data": null }
```

`400` — wrong code (this **increments the attempt counter**):

```json
{ "success": false, "message": "الكود غير صحيح", "data": null }
```

`400` — unknown email: `"بيانات غير صحيحة"`

**Flutter Notes:**
- The two messages differ: `"الكود غير صحيح"` → let them retry; `"الكود منتهي أو غير صالح"` → send them back to §9.7.
- Attempts are capped at **5** — consider showing a counter.
- The reset token lives **10 minutes**, shorter than the OTP's 15.

---

## 9.9 POST /api/auth/reset-password

**Purpose:** set a new password using the reset token from §9.8.

**Authentication:** `Anonymous`

**Request body**

| Field | Type | Required | Rules |
|---|---|---|---|
| `email` | string | **yes** | |
| `resetToken` | string | **yes** | from §9.8, single use |
| `newPassword` | string | **yes** | **no complexity rule enforced** |

**Sample Request**

```http
POST /api/auth/reset-password
Content-Type: application/json
```

```json
{
  "email": "ahmed@example.com",
  "resetToken": "xY9k2mP7qzR4tL8vN3wS6dF1gH5jK0aB7cE4nM2pQ9rT6uV8yZ3xW1sD5fG7hJ0k",
  "newPassword": "NewP@ssw0rd456"
}
```

**Success Response:** `200 OK`

```json
{ "success": true, "message": "تم تغيير كلمة المرور بنجاح" }
```

**Possible Errors**

`400` — token expired, wrong, or already used:

```json
{ "success": false, "message": "جلسة إعادة التعيين منتهية أو غير صالحة، ابدئي من كود جديد" }
```

`400` — unknown email: `"بيانات غير صحيحة"`

**Flutter Notes:**
- Single use — the token is cleared on success. A retry gets the expiry message.
- **Existing refresh tokens are NOT revoked** by a password reset. Other sessions stay signed in (issue #9).
- Enforce your own password rules client-side.

---

## 9.10 GET /api/users/me

**Purpose:** the authenticated user's profile.

**Authentication:** `Authenticated`. UserId comes from the JWT `sub` claim — **Flutter must not send it**.

**Path/query parameters:** none.
**Request Body: None**

**Sample Request**

```http
GET /api/users/me
Authorization: Bearer <access_token>
```

**Success Response:** `200 OK`

```json
{
  "success": true,
  "message": "تمت العملية بنجاح",
  "data": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "email": "ahmed@example.com",
    "fullName": "Ahmed Ali",
    "role": "Parent",
    "authProvider": "Email",
    "age": 35,
    "isActive": true,
    "convertedFromGuestAt": null,
    "createdAt": "2026-08-01T10:00:00"
  }
}
```

**Nullability**

| Field | Nullable | Dart |
|---|---|---|
| `id` | no | `String` |
| `email` | **yes** — always `null` for Guest | `String?` |
| `fullName` | no | `String` |
| `role` | no | `String` |
| `authProvider` | no | `String` |
| `age` | **yes** | `int?` |
| `isActive` | no | `bool` |
| `convertedFromGuestAt` | **yes** — set only on guest upgrade | `DateTime?` |
| `createdAt` | no | `DateTime` |

**Possible Errors**

`404` — token valid but the user row is gone:

```json
{ "success": false, "message": "المستخدم غير موجود", "data": null }
```

`401` — missing/expired token. Empty body.

**Flutter Notes:** `createdAt` is read from the DB, so it has **no `Z`** — use the defensive parser from §5.

---

## 9.11 PUT /api/users/me

**Purpose:** update the authenticated user's display name and age.

**Authentication:** `Authenticated`. **Flutter must NOT send UserId.**

**Request body**

| Field | Type | Required | Rules |
|---|---|---|---|
| `fullName` | string | **yes** | no validation |
| `age` | int? | no | `null` clears it |

Email, role and password **cannot** be changed here.

**Sample Request**

```http
PUT /api/users/me
Authorization: Bearer <access_token>
Content-Type: application/json
```

```json
{ "fullName": "Ahmed Ali", "age": 36 }
```

**Success Response:** `200 OK` — the full updated `UserProfileResponse`:

```json
{
  "success": true,
  "message": "تم تحديث البيانات بنجاح",
  "data": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "email": "ahmed@example.com",
    "fullName": "Ahmed Ali",
    "role": "Parent",
    "authProvider": "Email",
    "age": 36,
    "isActive": true,
    "convertedFromGuestAt": null,
    "createdAt": "2026-08-01T10:00:00"
  }
}
```

**Possible Errors**

`400` — user row missing (note: **400, not 404**, unlike GET):

```json
{ "success": false, "message": "المستخدم غير موجود", "data": null }
```

`401` — empty body.

**Flutter Notes:** this is a full replace, not a patch — always send both fields. Omitting `age` sets it to `null`.

---

# 10. Content / Learning API

All Content responses use the `ApiResponse<T>` envelope. Read endpoints are `Authenticated`; every write is `Admin`.

```text
Pagination: Not implemented on any Content endpoint. Full lists are returned.
Filtering:  Not implemented.
Search:     Not implemented.
```

## 10.1 GET /api/content/content-types

**Purpose:** the fixed lookup table of lesson-content kinds. Fetch once, cache forever.

**Authentication:** `Authenticated`
**Request Body: None**

**Sample Request**

```http
GET /api/content/content-types
Authorization: Bearer <access_token>
```

**Success Response:** `200 OK`

```json
{
  "success": true,
  "message": "تمت العملية بنجاح",
  "data": [
    { "id": 1, "name": "Text" },
    { "id": 2, "name": "Image" },
    { "id": 3, "name": "TextAndImage" }
  ]
}
```

**Ordering:** as stored. **Pagination: Not implemented.**

**Possible Errors:** `401` only (empty body). The service has no failure path.

**Flutter Notes:** `name` drives which widget renders a `LessonContentResponse`. Switch on `contentTypeName`, not `contentTypeId`.

---

## 10.2 GET /api/content/levels

**Purpose:** all learning levels, the top of the curriculum tree.

**Authentication:** `Authenticated`
**Request Body: None**

**Sample Request**

```http
GET /api/content/levels
Authorization: Bearer <access_token>
```

**Success Response:** `200 OK`

```json
{
  "success": true,
  "message": "تمت العملية بنجاح",
  "data": [
    { "id": 1, "title": "مقدمة في الكهرباء", "description": "مفاهيم أساسية", "order": 1 },
    { "id": 2, "title": "الدوائر البسيطة", "description": null, "order": 2 },
    { "id": 3, "title": "المستشعرات", "description": "حساسات الحرارة والضوء", "order": 3 }
  ]
}
```

**Ordering:** by `order` ascending. **Pagination: Not implemented.**

**Nullability:** `description` is `String?`; everything else always present.

**Possible Errors:** `401` (empty body). No other failure path — an empty curriculum returns `"data": []`.

**Flutter Notes:** there is **no** `lessonsCount`, `isCompleted` or `isLocked` field. Progress/locking is not modelled anywhere in the backend (issue #10) — you must derive it client-side or from Assessment stats.

---

## 10.3 GET /api/content/levels/{id}

**Purpose:** one level.

**Authentication:** `Authenticated`

**Path parameters**

| Name | Type | Required |
|---|---|---|
| `id` | int | **yes** |

**Request Body: None**

**Sample Request**

```http
GET /api/content/levels/1
Authorization: Bearer <access_token>
```

**Success Response:** `200 OK`

```json
{
  "success": true,
  "message": "تمت العملية بنجاح",
  "data": { "id": 1, "title": "مقدمة في الكهرباء", "description": "مفاهيم أساسية", "order": 1 }
}
```

**Possible Errors**

`404`:

```json
{ "success": false, "message": "المستوى غير موجود", "data": null }
```

`401` — empty body.

---

## 10.4 POST /api/content/levels

**Purpose:** create a level. `order` is assigned server-side as `max(order) + 1`.

**Authentication:** `Admin`

**Request body**

| Field | Type | Required | Rules |
|---|---|---|---|
| `title` | string | **yes** | no validation |
| `description` | string? | no | |

**Do not send `order`** — it is not in the DTO and is ignored.

**Sample Request**

```http
POST /api/content/levels
Authorization: Bearer <admin_access_token>
Content-Type: application/json
```

```json
{ "title": "المستشعرات", "description": null }
```

**Success Response:** `200 OK` (**not 201**)

```json
{
  "success": true,
  "message": "تم إنشاء المستوى بنجاح",
  "data": { "id": 3, "title": "المستشعرات", "description": null, "order": 3 }
}
```

**Possible Errors:** `401` (empty), `403` (empty — and **always**, today, per §2). `CreateAsync` has no failure path, so no 400.

---

## 10.5 PUT /api/content/levels/{id}

**Purpose:** edit a level's text. **Does not change `order`** — use §10.7.

**Authentication:** `Admin`

**Path parameters:** `id` (int, required)

**Request body:** `title` (string, required), `description` (string?, optional)

**Sample Request**

```http
PUT /api/content/levels/1
Authorization: Bearer <admin_access_token>
Content-Type: application/json
```

```json
{ "title": "مقدمة في الكهرباء - محدّث", "description": "وصف جديد" }
```

**Success Response:** `200 OK`

```json
{
  "success": true,
  "message": "تم تعديل المستوى بنجاح",
  "data": { "id": 1, "title": "مقدمة في الكهرباء - محدّث", "description": "وصف جديد", "order": 1 }
}
```

**Possible Errors**

`400` (**not 404**) — missing level:

```json
{ "success": false, "message": "المستوى غير موجود", "data": null }
```

`401`, `403` — empty bodies.

---

## 10.6 DELETE /api/content/levels/{id}

**Purpose:** delete an empty level.

**Authentication:** `Admin`
**Path parameters:** `id` (int, required)
**Request Body: None**

**Sample Request**

```http
DELETE /api/content/levels/3
Authorization: Bearer <admin_access_token>
```

**Success Response:** `200 OK` (**not 204**) — no `data` key:

```json
{ "success": true, "message": "تم مسح المستوى بنجاح" }
```

**Possible Errors**

`400` — level still has lessons:

```json
{ "success": false, "message": "مينفعش تمسحي المستوى ده لأن فيه دروس مرتبطة بيه، امسحي الدروس الأول" }
```

`400` — not found: `{ "success": false, "message": "المستوى غير موجود" }`

`401`, `403` — empty bodies.

---

## 10.7 POST /api/content/levels/swap-order

**Purpose:** swap two levels' positions.

**Authentication:** `Admin`

**Request body**

| Field | Type | Required |
|---|---|---|
| `firstLevelId` | int | **yes** |
| `secondLevelId` | int | **yes** |

**Sample Request**

```http
POST /api/content/levels/swap-order
Authorization: Bearer <admin_access_token>
Content-Type: application/json
```

```json
{ "firstLevelId": 1, "secondLevelId": 2 }
```

**Success Response:** `200 OK`

```json
{ "success": true, "message": "تم تبديل الترتيب بنجاح" }
```

**Possible Errors**

`400` — same id twice: `{ "success": false, "message": "مينفعش تبدلي ترتيب المستوى بنفسه" }`
`400` — one missing: `{ "success": false, "message": "واحد من المستويين غير موجود" }`
`401`, `403` — empty.

**Flutter Notes:** refetch §10.2 afterwards; the response carries no new ordering.

---

## 10.8 GET /api/content/levels/{levelId}/lessons

**Purpose:** all lessons in a level.

**Authentication:** `Authenticated`
**Path parameters:** `levelId` (int, required)
**Request Body: None**

**Sample Request**

```http
GET /api/content/levels/1/lessons
Authorization: Bearer <access_token>
```

**Success Response:** `200 OK`

```json
{
  "success": true,
  "message": "تمت العملية بنجاح",
  "data": [
    {
      "id": 5,
      "levelId": 1,
      "title": "مقدمة عن الدائرة الكهربية",
      "description": null,
      "sortOrder": 1,
      "isPublished": true,
      "createdAt": "2026-08-05T09:00:00"
    },
    {
      "id": 6,
      "levelId": 1,
      "title": "القياسات الكهربية",
      "description": "الفولت والأمبير",
      "sortOrder": 2,
      "isPublished": false,
      "createdAt": "2026-09-10T10:00:00"
    }
  ]
}
```

**Ordering:** by `sortOrder` ascending. **Pagination: Not implemented.**

**Nullability:** `description` is `String?`. All others always present.

**Possible Errors:** `401` only. A non-existent `levelId` returns `"data": []` with 200 — **not** 404.

**Flutter Notes:**
- ⚠ **`isPublished` is not filtered server-side.** Unpublished lessons are returned to every user, including children. Filter with `isPublished == true` in the app, or content leaks (issue #6).
- `createdAt` comes from the DB → **no `Z`**.

---

## 10.9 GET /api/content/lessons/{id}

**Purpose:** one lesson with its ordered content blocks — the lesson-reader screen payload.

**Authentication:** `Authenticated`
**Path parameters:** `id` (int, required)
**Request Body: None**

**Sample Request**

```http
GET /api/content/lessons/5
Authorization: Bearer <access_token>
```

**Success Response:** `200 OK`

```json
{
  "success": true,
  "message": "تمت العملية بنجاح",
  "data": {
    "id": 5,
    "levelId": 1,
    "title": "مقدمة عن الدائرة الكهربية",
    "description": null,
    "sortOrder": 1,
    "isPublished": true,
    "createdAt": "2026-08-05T09:00:00",
    "contents": [
      {
        "id": 10,
        "lessonId": 5,
        "contentTypeId": 1,
        "contentTypeName": "Text",
        "content": "الدائرة الكهربية بتتكون من مصدر للجهد وموصلات وحمل.",
        "mediaUrl": null,
        "sortOrder": 1
      },
      {
        "id": 11,
        "lessonId": 5,
        "contentTypeId": 2,
        "contentTypeName": "Image",
        "content": null,
        "mediaUrl": "/uploads/lessons/8f1c2b3a-6b4d-4e2a-9c1f-3d7e5a2b1c0d.png",
        "sortOrder": 2
      },
      {
        "id": 12,
        "lessonId": 5,
        "contentTypeId": 3,
        "contentTypeName": "TextAndImage",
        "content": "المقاومة بتقلل شدة التيار المار في الدائرة.",
        "mediaUrl": "/uploads/lessons/2c4e6a8b-1d3f-5a7c-9e1b-4f6a8c0e2d4f.png",
        "sortOrder": 3
      }
    ]
  }
}
```

**Ordering:** `contents` by `sortOrder` ascending. **Pagination: Not implemented.**

**Nullability inside `contents`**

| Field | Nullable | Notes |
|---|---|---|
| `content` | **yes** | `null` for a pure `Image` block |
| `mediaUrl` | **yes** | `null` for a pure `Text` block |

At least one of the two is always non-null (enforced: `"لازم يكون في نص أو صورة على الأقل"`). Both are populated for `TextAndImage`.

**Possible Errors**

`404`: `{ "success": false, "message": "الدرس غير موجود", "data": null }`
`401` — empty body.

**Flutter Notes:**
- **`mediaUrl` is a server-relative path**, e.g. `/uploads/lessons/....png`. Prefix it with your base URL: `'$baseUrl$mediaUrl'`. It is served by `app.UseStaticFiles()` from `wwwroot`.
- Static files are **not** access-controlled — anyone with the URL can fetch the image without a token.
- Render by `contentTypeName`: `Text` → text only, `Image` → image only, `TextAndImage` → both.

---

## 10.10 POST /api/content/lessons

**Purpose:** create a lesson. `sortOrder` is assigned server-side (`max + 1` within the level); the lesson starts **unpublished**.

**Authentication:** `Admin`

**Request body**

| Field | Type | Required | Rules |
|---|---|---|---|
| `levelId` | int | **yes** | level must exist |
| `title` | string | **yes** | |
| `description` | string? | no | |

**Do not send `sortOrder` or `isPublished`.**

**Sample Request**

```http
POST /api/content/lessons
Authorization: Bearer <admin_access_token>
Content-Type: application/json
```

```json
{ "levelId": 1, "title": "القياسات الكهربية", "description": null }
```

**Success Response:** `200 OK` (**not 201**)

```json
{
  "success": true,
  "message": "تم إنشاء الدرس بنجاح",
  "data": {
    "id": 6,
    "levelId": 1,
    "title": "القياسات الكهربية",
    "description": null,
    "sortOrder": 2,
    "isPublished": false,
    "createdAt": "2026-09-10T10:00:00.0000000Z"
  }
}
```

**Possible Errors**

`400`: `{ "success": false, "message": "المستوى المحدد غير موجود", "data": null }`
`401`, `403` — empty.

**Flutter Notes:** `createdAt` here **has `Z`** (in-memory value), unlike the same field from §10.8/§10.9.

---

## 10.11 PUT /api/content/lessons/{id}

**Purpose:** edit a lesson, optionally moving it to another level.

**Authentication:** `Admin`
**Path parameters:** `id` (int, required)

**Request body:** `levelId` (int, required), `title` (string, required), `description` (string?, optional)

**Sample Request**

```http
PUT /api/content/lessons/6
Authorization: Bearer <admin_access_token>
Content-Type: application/json
```

```json
{ "levelId": 1, "title": "القياسات الكهربية - محدّث", "description": "وصف جديد" }
```

**Success Response:** `200 OK` — full updated `LessonSummaryResponse`, message `"تم تعديل الدرس بنجاح"`.

**Possible Errors**

`400` — `"الدرس غير موجود"` or `"المستوى المحدد غير موجود"`
`401`, `403` — empty.

**Flutter Notes:** moving the lesson to a different `levelId` re-assigns `sortOrder` to the end of the new level automatically. Within the same level, order is untouched.

---

## 10.12 PATCH /api/content/lessons/{id}/publish

**Purpose:** publish/unpublish a lesson.

**Authentication:** `Admin`
**Path parameters:** `id` (int, required)

**Request body:** `{ "isPublished": bool }` — required. Note this is a **body**, not a query parameter (unlike the Assessment `active` toggles).

**Sample Request**

```http
PATCH /api/content/lessons/6/publish
Authorization: Bearer <admin_access_token>
Content-Type: application/json
```

```json
{ "isPublished": true }
```

**Success Response:** `200 OK` — message is `"تم نشر الدرس"` or `"تم إخفاء الدرس"`:

```json
{
  "success": true,
  "message": "تم نشر الدرس",
  "data": {
    "id": 6,
    "levelId": 1,
    "title": "القياسات الكهربية",
    "description": null,
    "sortOrder": 2,
    "isPublished": true,
    "createdAt": "2026-09-10T10:00:00"
  }
}
```

**Possible Errors:** `400` `"الدرس غير موجود"`; `401`, `403` empty.

---

## 10.13 DELETE /api/content/lessons/{id}

**Authentication:** `Admin` · **Path:** `id` (int) · **Request Body: None**

**Sample Request**

```http
DELETE /api/content/lessons/6
Authorization: Bearer <admin_access_token>
```

**Success Response:** `200 OK` — `{ "success": true, "message": "تم مسح الدرس بنجاح" }`

**Possible Errors:** `400` `{ "success": false, "message": "الدرس غير موجود" }`; `401`, `403` empty.

---

## 10.14 POST /api/content/lessons/swap-order

**Authentication:** `Admin`

**Request body:** `firstLessonId` (int, required), `secondLessonId` (int, required)

**Sample Request**

```http
POST /api/content/lessons/swap-order
Authorization: Bearer <admin_access_token>
Content-Type: application/json
```

```json
{ "firstLessonId": 5, "secondLessonId": 6 }
```

**Success Response:** `200 OK` — `{ "success": true, "message": "تم تبديل الترتيب بنجاح" }`

**Possible Errors**

`400` — `"مينفعش تبدلي ترتيب الدرس بنفسه"`, `"واحد من الدرسين غير موجود"`, or `"مينفعش تبدلي ترتيب درسين من مستويين مختلفين"`
`401`, `403` — empty.

---

## 10.15 POST /api/content/lessons/{lessonId}/contents

**Purpose:** append a content block to a lesson. `sortOrder` is assigned server-side.

**Authentication:** `Admin`
**Path parameters:** `lessonId` (int, required)

**Request body**

| Field | Type | Required | Rules |
|---|---|---|---|
| `contentTypeId` | int | **yes** | must exist in §10.1 |
| `content` | string? | conditional | **at least one of `content` / `mediaUrl` must be non-empty** |
| `mediaUrl` | string? | conditional | upload via §10.19 first, then send the returned `url` |

**Sample Request**

```http
POST /api/content/lessons/5/contents
Authorization: Bearer <admin_access_token>
Content-Type: application/json
```

```json
{
  "contentTypeId": 1,
  "content": "معلومة إضافية عن المقاومة الكهربية",
  "mediaUrl": null
}
```

**Success Response:** `200 OK`

```json
{
  "success": true,
  "message": "تم إضافة المحتوى بنجاح",
  "data": {
    "id": 12,
    "lessonId": 5,
    "contentTypeId": 1,
    "contentTypeName": "Text",
    "content": "معلومة إضافية عن المقاومة الكهربية",
    "mediaUrl": null,
    "sortOrder": 3
  }
}
```

**Possible Errors**

`400` — `"الدرس غير موجود"`, `"نوع المحتوى غير موجود"`, or `"لازم يكون في نص أو صورة على الأقل"`
`401`, `403` — empty.

**Flutter Notes:** `contentTypeId` and the content fields are **not cross-validated**. You can create a `Text` block carrying only a `mediaUrl`. Enforce the pairing in the admin UI.

---

## 10.16 PUT /api/content/contents/{contentId}

**Purpose:** edit one content block. `lessonId` is **not** in the path — the id is globally unique.

**Authentication:** `Admin`
**Path parameters:** `contentId` (int, required)
**Request body:** identical to §10.15 (`contentTypeId`, `content?`, `mediaUrl?`)

**Sample Request**

```http
PUT /api/content/contents/12
Authorization: Bearer <admin_access_token>
Content-Type: application/json
```

```json
{
  "contentTypeId": 2,
  "content": null,
  "mediaUrl": "/uploads/lessons/8f1c2b3a-6b4d-4e2a-9c1f-3d7e5a2b1c0d.png"
}
```

**Success Response:** `200 OK` — full updated `LessonContentResponse`, message `"تم تعديل المحتوى بنجاح"`.

**Possible Errors:** `400` — `"عنصر المحتوى غير موجود"`, `"نوع المحتوى غير موجود"`, `"لازم يكون في نص أو صورة على الأقل"`; `401`, `403` empty.

**Flutter Notes:** `sortOrder` is never changed here — use §10.18.

---

## 10.17 DELETE /api/content/contents/{contentId}

**Authentication:** `Admin` · **Path:** `contentId` (int) · **Request Body: None**

**Sample Request**

```http
DELETE /api/content/contents/12
Authorization: Bearer <admin_access_token>
```

**Success Response:** `200 OK` — `{ "success": true, "message": "تم مسح المحتوى بنجاح" }`

**Possible Errors:** `400` `{ "success": false, "message": "عنصر المحتوى غير موجود" }`; `401`, `403` empty.

**Flutter Notes:** the underlying image file is **not** deleted from disk — only the DB row.

---

## 10.18 POST /api/content/contents/swap-order

**Authentication:** `Admin`

**Request body:** `firstContentId` (int, required), `secondContentId` (int, required)

**Sample Request**

```http
POST /api/content/contents/swap-order
Authorization: Bearer <admin_access_token>
Content-Type: application/json
```

```json
{ "firstContentId": 10, "secondContentId": 11 }
```

**Success Response:** `200 OK` — `{ "success": true, "message": "تم تبديل الترتيب بنجاح" }`

**Possible Errors**

`400` — `"مينفعش تبدلي ترتيب العنصر بنفسه"`, `"واحد من العنصرين غير موجود"`, or `"مينفعش تبدلي ترتيب عنصرين من درسين مختلفين"`
`401`, `403` — empty.

---

## 10.19 POST /api/content/media/images

**Purpose:** upload a lesson image and get back the URL to put in `mediaUrl`.

**Authentication:** `Admin`

**Request headers**

```http
Authorization: Bearer <admin_access_token>
Content-Type: multipart/form-data
```

**Form fields**

| Field | Type | Required | Rules |
|---|---|---|---|
| `file` | file | **yes** | extension must be `.jpg`, `.jpeg`, `.png` or `.webp`; **max 5 MB** (request hard-capped at 6 MB) |

**Sample Request**

```http
POST /api/content/media/images
Authorization: Bearer <admin_access_token>
Content-Type: multipart/form-data; boundary=----WebKitFormBoundary

------WebKitFormBoundary
Content-Disposition: form-data; name="file"; filename="circuit.png"
Content-Type: image/png

<binary>
------WebKitFormBoundary--
```

**Success Response:** `200 OK`

```json
{
  "success": true,
  "message": "تم رفع الصورة بنجاح",
  "data": { "url": "/uploads/lessons/8f1c2b3a-6b4d-4e2a-9c1f-3d7e5a2b1c0d.png" }
}
```

**Possible Errors**

`400` — bad extension:

```json
{ "success": false, "message": "امتداد الصورة غير مسموح - المسموح بس jpg, jpeg, png, webp", "data": null }
```

`400` — over 5 MB (message from the service), `401`, `403` — empty bodies.

`413 Payload Too Large` — over the 6 MB `RequestSizeLimit`, returned by Kestrel with **no JSON body**.

**Flutter Notes:**
- Two-step flow: upload → take `data.url` → send it as `mediaUrl` in §10.15/§10.16.
- The form field **must** be named `file`.
- The returned URL is server-relative; prefix with the base URL to display.

---

# 11. Assessment API

> **No `ApiResponse` envelope here.** Success bodies are the raw DTO. Failures are `{ "statusCode", "message" }` from `ExceptionMiddleware`.

## 11.0 The complete child-facing flow

Only **three** endpoints matter for the student app. This is the real flow as implemented:

```text
(quizId comes from the Content module — see the warning below)
        ↓
POST /api/quiz-attempts?quizId=15                     ← Start first attempt
        ↓  201 Created
   QuizAttemptResponseDto
   ├── attemptId  ← ★ STORE THIS
   └── questions[] (all active questions, options WITHOUT isCorrect,
                    currentHint = null on a first attempt)
        ↓
   Display questions locally. Collect one selectedOptionId per question.
   ⚠ There is NO per-question submit endpoint. Nothing is sent until the end.
        ↓
POST /api/quiz-attempts/15/submit                     ← Submit the WHOLE attempt
   { "mistakes": [ { questionId, selectedOptionId }, ... ] }
        ↓  200 OK
   QuizAttemptResultDto
   ├── totalQuestions / correctAnswers / wrongAnswers / scorePercentage
   └── retryQuestions[]  ← the wrong ones, each with an AI currentHint
        ↓
   Show the result screen + mistakes + hints
        ↓
POST /api/quiz-attempts?quizId=15&previousAttemptId=15  ← Start the retry
        ↓  201 Created
   QuizAttemptResponseDto — only the previously-wrong questions,
   each carrying its latest currentHint
        ↓
   … submit again (same endpoint), one retry per attempt only
```

### IDs Flutter must preserve

| ID | Where it comes from | Where you send it later |
|---|---|---|
| `quizId` | Content module / your own config | query param on Start |
| `attemptId` | `POST /api/quiz-attempts` → `attemptId` | path of Submit; `previousAttemptId` of the retry |
| `questionId` | `questions[].questionId` | each `mistakes[].questionId` |
| `optionId` | `questions[].options[].optionId` | each `mistakes[].selectedOptionId` |

### What to store locally during an attempt

```text
STORE:  attemptId
        quizId
        the questions[] payload (so the child can navigate back and forth offline)
        a local Map<questionId, selectedOptionId> of the child's choices

DO NOT MODIFY / DO NOT INVENT:
        questionId, optionId — send them back exactly as received
        the questions list — the server already decided which questions
          this attempt contains; adding or removing is rejected
        scorePercentage, correctAnswers — always server-computed
```

### ⚠ Which questions to put in `mistakes[]` — read carefully

The field is named `mistakes`, but **Flutter cannot know which answers are wrong** — `isCorrect` is deliberately never sent to the client (§11.9).

**The backend re-validates every entry against the answer key** and keeps only the genuinely wrong ones:

```csharp
if (mistake.SelectedOptionId != correctOptionByQuestion[mistake.QuestionId])
    confirmedMistakes.Add(mistake);   // otherwise silently ignored
```

**So: send every answered question.** Correct answers are accepted, validated, and dropped without creating a mistake row. Scoring is identical either way:

```text
correctAnswers = totalQuestions − (confirmed mistakes)
```

Sending only-wrong-answers also works, if you ever gain that knowledge. Sending everything is what is actually possible today. (Naming issue #8.)

### ⚠ How a child obtains a `quizId`

**There is no child-facing quiz-listing endpoint.** `GET /api/quizzes` is `Admin`-only, and `Quiz.LevelId` / `LessonId` are not returned by any Content endpoint. Today Flutter must hard-code or configure quiz ids. This is gap #1 in §13.

---

## 11.1 POST /api/quiz-attempts — Start attempt (first or retry)

**Purpose:** open an attempt and receive its questions. One endpoint serves both the first attempt and the retry; the presence of `previousAttemptId` decides.

**Authentication:** `Authenticated`.

```text
Flutter must NOT send UserId — it is taken from the JWT `sub` claim.
```

**Query parameters**

| Name | Type | Required | Meaning |
|---|---|---|---|
| `quizId` | int | **yes** | quiz to attempt; must exist and be **active** |
| `previousAttemptId` | long | no | omit → first attempt (all active questions). Provide → retry (only the wrong ones) |

**Request Body: None**

**Sample Request — first attempt**

```http
POST /api/quiz-attempts?quizId=15
Authorization: Bearer <access_token>
```

**Sample Request — retry**

```http
POST /api/quiz-attempts?quizId=15&previousAttemptId=42
Authorization: Bearer <access_token>
```

**Success Response:** `201 Created`

```http
201 Created
Location: /api/quiz-attempts/42
Content-Type: application/json
```

First attempt — `currentHint` is `null` on every question:

```json
{
  "attemptId": 42,
  "quizId": 15,
  "startedAt": "2026-09-10T18:30:00.0000000Z",
  "questions": [
    {
      "questionId": 101,
      "questionText": "ما هو الجهد الكهربي؟",
      "difficulty": "Easy",
      "displayOrder": 1,
      "points": 1,
      "currentHint": null,
      "options": [
        { "optionId": 1001, "optionText": "فرق الجهد بين نقطتين", "displayOrder": 1 },
        { "optionId": 1002, "optionText": "مقاومة مرور التيار", "displayOrder": 2 },
        { "optionId": 1003, "optionText": "شدة التيار المار", "displayOrder": 3 }
      ]
    },
    {
      "questionId": 102,
      "questionText": "ما وحدة قياس المقاومة الكهربية؟",
      "difficulty": "Medium",
      "displayOrder": 2,
      "points": 2,
      "currentHint": null,
      "options": [
        { "optionId": 1004, "optionText": "الأوم", "displayOrder": 1 },
        { "optionId": 1005, "optionText": "الفولت", "displayOrder": 2 },
        { "optionId": 1006, "optionText": "الأمبير", "displayOrder": 3 }
      ]
    }
  ]
}
```

Retry — only previously-wrong questions, each with the **latest** hint:

```json
{
  "attemptId": 43,
  "quizId": 15,
  "startedAt": "2026-09-10T18:45:00.0000000Z",
  "questions": [
    {
      "questionId": 102,
      "questionText": "ما وحدة قياس المقاومة الكهربية؟",
      "difficulty": "Medium",
      "displayOrder": 2,
      "points": 2,
      "currentHint": "افتكر إن الوحدة اسمها على اسم العالم الألماني اللي اكتشف العلاقة بين الجهد والتيار.",
      "options": [
        { "optionId": 1004, "optionText": "الأوم", "displayOrder": 1 },
        { "optionId": 1005, "optionText": "الفولت", "displayOrder": 2 },
        { "optionId": 1006, "optionText": "الأمبير", "displayOrder": 3 }
      ]
    }
  ]
}
```

**Ordering:** `questions` by `displayOrder`, `options` by `displayOrder`. **Pagination: Not implemented.**

**Nullability**

| Field | Nullable | Dart |
|---|---|---|
| `attemptId`, `quizId`, `startedAt` | no | `int`, `int`, `DateTime` |
| `questions` | no (may be `[]`… but see errors) | `List<Question>` |
| `questionText`, `difficulty`, `displayOrder`, `points` | no | `String`, `String`, `int`, `int` |
| `currentHint` | **yes** — `null` on first attempts | `String?` |
| `options` | no | `List<Option>` |
| `optionId`, `optionText`, `displayOrder` | no | `int`, `String`, `int` |

**Possible Errors**

`404` — quiz does not exist:

```json
{ "statusCode": 404, "message": "الاختبار رقم 15 غير موجود" }
```

`404` — retry against an unknown attempt: `{ "statusCode": 404, "message": "المحاولة رقم 42 غير موجودة" }`

`400` — quiz inactive:

```json
{ "statusCode": 400, "message": "الاختبار رقم 15 غير مفعّل" }
```

`400` — quiz has no active questions: `{ "statusCode": 400, "message": "الاختبار رقم 15 لا يحتوي على أسئلة مفعّلة" }`
`400` — retrying an attempt that is not finished: `{ "statusCode": 400, "message": "لا يمكن إعادة المحاولة رقم 42 لأنها لم تكتمل" }`
`400` — nothing to retry: `{ "statusCode": 400, "message": "المحاولة رقم 42 لا تحتوي على إجابات خاطئة لإعادتها" }`
`400` — wrong quiz: `{ "statusCode": 400, "message": "المحاولة رقم 42 لا تخص الاختبار رقم 15" }`
`400` — a question has no correct answer configured: `{ "statusCode": 400, "message": "لا يمكن بدء المحاولة: الأسئلة أرقام 103 ليس لها إجابة صحيحة" }`

`403` — retrying **someone else's** attempt:

```json
{ "statusCode": 403, "message": "لا يمكنك إعادة محاولة مستخدم آخر" }
```

`409` — this attempt was already retried (also returned when two retry requests race; exactly one wins):

```json
{ "statusCode": 409, "message": "تمت إعادة المحاولة رقم 42 بالفعل" }
```

`401` — missing/expired token. **Empty body.**

**Flutter Notes:**
- **Store `attemptId` immediately.** Without it you can neither submit nor retry, and there is no "list my attempts" endpoint to recover it (gap #2).
- `Location` header also carries the attempt URL.
- **Exactly one retry per attempt.** A second retry of the same `previousAttemptId` gets 409. To chain further, retry the *retry's* id (43, then 44…).
- `startedAt` here **has `Z`** (in-memory). The same field from §11.3 has **no `Z`**.
- `points` is returned for display; it does **not** affect `scorePercentage`, which is a plain question count ratio.
- `403` and `409` are distinct: 403 = not your attempt, 409 = already retried.

---

## 11.2 POST /api/quiz-attempts/{attemptId}/submit — Submit the whole attempt

**Purpose:** finish the attempt, score it, generate AI hints for wrong answers, update statistics, and return the retry set. **This is a single bulk submit — there is no per-question endpoint.**

**Authentication:** `Authenticated`. **Flutter must NOT send UserId.**

**Path parameters**

| Name | Type | Required |
|---|---|---|
| `attemptId` | long | **yes** — the `attemptId` from §11.1 |

**Request body**

| Field | Type | Required | Rules |
|---|---|---|---|
| `mistakes` | array | **yes** | may be `[]` (all correct). `null` is treated as `[]` |
| `mistakes[].questionId` | int | **yes** | must belong to this attempt; **no duplicates** |
| `mistakes[].selectedOptionId` | int | **yes** | must exist and belong to that `questionId` |

**Sample Request**

```http
POST /api/quiz-attempts/42/submit
Authorization: Bearer <access_token>
Content-Type: application/json
```

```json
{
  "mistakes": [
    { "questionId": 102, "selectedOptionId": 1005 }
  ]
}
```

All answers correct:

```json
{ "mistakes": [] }
```

**Success Response:** `200 OK`

```json
{
  "attemptId": 42,
  "totalQuestions": 3,
  "correctAnswers": 2,
  "wrongAnswers": 1,
  "scorePercentage": 66.67,
  "retryQuestions": [
    {
      "questionId": 102,
      "questionText": "ما وحدة قياس المقاومة الكهربية؟",
      "difficulty": "Medium",
      "displayOrder": 2,
      "points": 2,
      "currentHint": "افتكر إن الوحدة اسمها على اسم العالم الألماني اللي اكتشف العلاقة بين الجهد والتيار.",
      "options": [
        { "optionId": 1004, "optionText": "الأوم", "displayOrder": 1 },
        { "optionId": 1005, "optionText": "الفولت", "displayOrder": 2 },
        { "optionId": 1006, "optionText": "الأمبير", "displayOrder": 3 }
      ]
    }
  ]
}
```

Perfect score — `retryQuestions` is empty and **no AI call is made**:

```json
{
  "attemptId": 42,
  "totalQuestions": 3,
  "correctAnswers": 3,
  "wrongAnswers": 0,
  "scorePercentage": 100.00,
  "retryQuestions": []
}
```

**Field notes**

| Field | Type | Nullable | Notes |
|---|---|---|---|
| `attemptId` | long | no | echo of the path |
| `totalQuestions` | int | no | from the attempt, **not** from your `mistakes` array |
| `correctAnswers` | int | no | `totalQuestions − confirmed mistakes` |
| `wrongAnswers` | int | no | count of confirmed mistakes |
| `scorePercentage` | decimal | no | `0.00`–`100.00`, 2 dp, half-away-from-zero |
| `retryQuestions` | array | no | `[]` when nothing was wrong |
| `retryQuestions[].currentHint` | string? | **non-null in practice** here | the freshly generated AI hint |

**Possible Errors**

`400` — duplicate question in the array:

```json
{ "statusCode": 400, "message": "السؤال رقم 102 مكرر في قائمة الأخطاء" }
```

`400` — question not part of this attempt: `{ "statusCode": 400, "message": "السؤال رقم 999 لا يخص هذه المحاولة" }`
`400` — unknown option: `{ "statusCode": 400, "message": "الاختيار رقم 9999 غير موجود" }`
`400` — option belongs to a different question: `{ "statusCode": 400, "message": "الاختيار رقم 1004 لا يخص السؤال رقم 101" }`
`400` — `null` entry in the array: `{ "statusCode": 400, "message": "قائمة الأخطاء تحتوي على عنصر فارغ" }`

`400` — **already submitted** (sequential re-submit):

```json
{ "statusCode": 400, "message": "المحاولة رقم 42 تم تسليمها بالفعل" }
```

`403` — submitting someone else's attempt:

```json
{ "statusCode": 403, "message": "لا يمكنك تسليم محاولة مستخدم آخر" }
```

`404` — unknown attempt: `{ "statusCode": 404, "message": "المحاولة رقم 42 غير موجودة" }`

`409` — **concurrent** double-submit; one request wins, the other gets:

```json
{ "statusCode": 409, "message": "المحاولة رقم 42 تم تسليمها بالفعل" }
```

`500` — the external AI hint service failed. The attempt is **not** completed and stays retryable:

```json
{ "statusCode": 500, "message": "حدث خطأ داخلي في الخادم" }
```

`401` — empty body.

**Flutter Notes:**
- **Send every answered question in `mistakes[]`** — see §11.0. The backend filters.
- ⚠ **400 and 409 both mean "already submitted."** 400 is the sequential retry (status already `Completed`); 409 is losing a concurrency race. Treat both as *done* and navigate to the result screen — but you will **not** get the result body, so you need §11.3 to rebuild the screen.
- **Disable the submit button on first tap.** Double-tap costs a wasted AI call even though data stays consistent.
- The AI call runs before the DB transaction commits, so this request can take a few seconds. Show a spinner and set a generous timeout.
- On `500`, the attempt is still `InProgress` — a plain retry of the same submit is safe and correct.
- `scorePercentage` is a JSON **number** (`66.67`) → parse as `double`, not `String`.
- `retryQuestions` is exactly what §11.1 returns for the retry, so reuse the same Dart model.

---

## 11.3 GET /api/quiz-attempts/{attemptId}

**Purpose:** re-read an attempt and its questions — for resuming after an app restart, or rebuilding the result screen after a 409.

**Authentication:** `Authenticated`. Ownership enforced server-side.

**Path parameters:** `attemptId` (long, required)
**Request Body: None**

**Sample Request**

```http
GET /api/quiz-attempts/42
Authorization: Bearer <access_token>
```

**Success Response:** `200 OK` — same `QuizAttemptResponseDto` shape as §11.1:

```json
{
  "attemptId": 42,
  "quizId": 15,
  "startedAt": "2026-09-10T18:30:00",
  "questions": [
    {
      "questionId": 101,
      "questionText": "ما هو الجهد الكهربي؟",
      "difficulty": "Easy",
      "displayOrder": 1,
      "points": 1,
      "currentHint": null,
      "options": [
        { "optionId": 1001, "optionText": "فرق الجهد بين نقطتين", "displayOrder": 1 },
        { "optionId": 1002, "optionText": "مقاومة مرور التيار", "displayOrder": 2 },
        { "optionId": 1003, "optionText": "شدة التيار المار", "displayOrder": 3 }
      ]
    },
    {
      "questionId": 102,
      "questionText": "ما وحدة قياس المقاومة الكهربية؟",
      "difficulty": "Medium",
      "displayOrder": 2,
      "points": 2,
      "currentHint": "افتكر إن الوحدة اسمها على اسم العالم الألماني اللي اكتشف العلاقة بين الجهد والتيار.",
      "options": [
        { "optionId": 1004, "optionText": "الأوم", "displayOrder": 1 },
        { "optionId": 1005, "optionText": "الفولت", "displayOrder": 2 },
        { "optionId": 1006, "optionText": "الأمبير", "displayOrder": 3 }
      ]
    }
  ]
}
```

**Possible Errors**

`404`: `{ "statusCode": 404, "message": "المحاولة رقم 42 غير موجودة" }`

`403`:

```json
{ "statusCode": 403, "message": "لا يمكنك الوصول إلى محاولة مستخدم آخر" }
```

`401` — empty body.

**Flutter Notes:**
- ⚠ **This does NOT return the score, status, or the child's answers** — only the questions. After a submit you cannot re-read `scorePercentage` or `correctAnswers` anywhere (gap #3). Cache `QuizAttemptResultDto` locally when you receive it.
- ⚠ **No `status` field.** You cannot tell from this response whether the attempt is still open or already submitted. Track it locally.
- `currentHint` is populated per question **after** submission (latest hint per question); before submission it is `null` on a first attempt.
- `startedAt` here comes from the DB → **no `Z`**.

---

## 11.4 GET /api/user-topic-stats

**Purpose:** the signed-in child's per-topic mastery statistics.

**Authentication:** `Authenticated`. **Flutter must NOT send UserId.**

**Request Body: None**

**Sample Request**

```http
GET /api/user-topic-stats
Authorization: Bearer <access_token>
```

**Success Response:** `200 OK` — a **bare array**, no envelope:

```json
[
  {
    "id": 7,
    "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "topicId": 3,
    "difficulty": "Easy",
    "questionsAnsweredCount": 12,
    "correctCount": 9,
    "wrongCount": 3,
    "hintsUsedCount": 3,
    "lastQuizAttemptId": 42,
    "lastPracticedAt": "2026-09-10T18:32:15.123",
    "updatedAt": "2026-09-10T18:32:15.123"
  },
  {
    "id": 8,
    "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "topicId": 3,
    "difficulty": "Medium",
    "questionsAnsweredCount": 5,
    "correctCount": 2,
    "wrongCount": 3,
    "hintsUsedCount": 4,
    "lastQuizAttemptId": 42,
    "lastPracticedAt": "2026-09-10T18:32:15.123",
    "updatedAt": "2026-09-10T18:32:15.123"
  }
]
```

**Ordering:** `topicId`, then `difficulty`. **Pagination: Not implemented.**

**Nullability**

| Field | Nullable | Dart |
|---|---|---|
| `id`, `userId`, `topicId`, `difficulty` | no | `int`, `String`, `int`, `String` |
| `questionsAnsweredCount`, `correctCount`, `wrongCount`, `hintsUsedCount` | no | `int` |
| `lastQuizAttemptId` | **yes** | `int?` |
| `lastPracticedAt` | **yes** | `DateTime?` |
| `updatedAt` | no | `DateTime` |

**Possible Errors:** `401` (empty body) only. A child with no history gets `[]` with 200.

**Flutter Notes:**
- One row per `(topicId, difficulty)` pair — a single topic appears up to 4 times.
- ⚠ **`topicId` has no name.** There is no topics endpoint (gap #4), so you cannot label these rows. Hard-code a map or ask for the endpoint.
- `wrongCount` is DB-computed (`answered − correct`); never compute it yourself.
- `hintsUsedCount` is a running **total for that bucket**, not per-attempt.
- `userId` is echoed but always equals the signed-in user — safe to ignore.

---

## 11.5 GET /api/user-topic-stats/{topicId}/{difficulty}

**Purpose:** one statistics row.

**Authentication:** `Authenticated`

**Path parameters**

| Name | Type | Required | Rules |
|---|---|---|---|
| `topicId` | int | **yes** | |
| `difficulty` | string | **yes** | `Easy` / `Medium` / `Hard` / `Advanced`, **case-sensitive** |

**Request Body: None**

**Sample Request**

```http
GET /api/user-topic-stats/3/Easy
Authorization: Bearer <access_token>
```

**Success Response:** `200 OK` — a single object (same shape as one array element in §11.4).

```json
{
  "id": 7,
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "topicId": 3,
  "difficulty": "Easy",
  "questionsAnsweredCount": 12,
  "correctCount": 9,
  "wrongCount": 3,
  "hintsUsedCount": 3,
  "lastQuizAttemptId": 42,
  "lastPracticedAt": "2026-09-10T18:32:15.123",
  "updatedAt": "2026-09-10T18:32:15.123"
}
```

**Possible Errors**

`404` — no row yet for that pair. **Body is empty** (`return NotFound()` with no payload) — do not try to parse it.

`401` — empty body.

**Flutter Notes:** 404 means "never practised", not an error — render a zero state.

---

## 11.6 GET /api/quizzes  *(Admin)*

**Purpose:** paginated quiz list. **The only paginated endpoint in the API.**

**Authentication:** `Admin`

**Query parameters**

| Name | Type | Required | Default | Rules |
|---|---|---|---|---|
| `quizType` | string? | no | — | `LevelAssessment` / `LessonQuiz` / `LessonReview` / `Standalone` |
| `levelId` | int? | no | — | |
| `lessonId` | int? | no | — | |
| `isActive` | bool? | no | — | |
| `pageNumber` | int | no | `1` | `< 1` is clamped to 1 |
| `pageSize` | int | no | `20` | `< 1` → 1; **`> 100` → 100** |

**Request Body: None**

**Sample Request**

```http
GET /api/quizzes?quizType=LessonQuiz&isActive=true&pageNumber=1&pageSize=20
Authorization: Bearer <admin_access_token>
```

**Success Response:** `200 OK`

```json
{
  "items": [
    {
      "id": 15,
      "title": "اختبار الدائرة الكهربية",
      "description": "أسئلة على مقدمة الدوائر",
      "quizType": "LessonQuiz",
      "levelId": null,
      "lessonId": 5,
      "isActive": true,
      "createdAt": "2026-08-10T09:00:00",
      "updatedAt": null
    },
    {
      "id": 16,
      "title": "تقييم المستوى الأول",
      "description": null,
      "quizType": "LevelAssessment",
      "levelId": 1,
      "lessonId": null,
      "isActive": true,
      "createdAt": "2026-08-12T11:20:00",
      "updatedAt": "2026-09-01T14:05:00"
    }
  ],
  "totalCount": 2,
  "pageNumber": 1,
  "pageSize": 20
}
```

**Ordering:** `id` **descending** (newest first).

**Pagination fields:** `totalCount`, `pageNumber`, `pageSize`. **No `hasNextPage` / `totalPages`** — compute client-side (§7).

**Nullability:** `description`, `levelId`, `lessonId`, `updatedAt` are all nullable. `levelId`/`lessonId` are mutually exclusive by `quizType` (see §11.8).

**Possible Errors:** `401`, `403` — empty bodies. Filters never 404; an unmatched filter returns `"items": []`.

---

## 11.7 GET /api/quizzes/{quizId}  *(Admin)*

**Authentication:** `Admin` · **Path:** `quizId` (int) · **Request Body: None**

**Sample Request**

```http
GET /api/quizzes/15
Authorization: Bearer <admin_access_token>
```

**Success Response:** `200 OK`

```json
{
  "id": 15,
  "title": "اختبار الدائرة الكهربية",
  "description": "أسئلة على مقدمة الدوائر",
  "quizType": "LessonQuiz",
  "levelId": null,
  "lessonId": 5,
  "isActive": true,
  "createdAt": "2026-08-10T09:00:00",
  "updatedAt": null
}
```

**Possible Errors:** `404` `{ "statusCode": 404, "message": "الاختبار رقم 15 غير موجود" }`; `401`, `403` empty.

---

## 11.8 POST /api/quizzes  *(Admin)*

**Authentication:** `Admin`

**Request body**

| Field | Type | Required | Rules |
|---|---|---|---|
| `title` | string | **yes** | non-blank, **max 300 chars**, trimmed |
| `description` | string? | no | |
| `quizType` | string | **yes** | one of the four; blank/omitted defaults to `Standalone` |
| `levelId` | int? | conditional | **required iff** `quizType == "LevelAssessment"` |
| `lessonId` | int? | conditional | **required iff** `quizType` is `LessonQuiz` or `LessonReview` |

**The type/reference rule is strict** (mirrors `CK_Quizzes_TypeMatchesReference`):

```text
LevelAssessment            → levelId  SET, lessonId NULL
LessonQuiz / LessonReview  → lessonId SET, levelId  NULL
Standalone                 → both NULL
```

New quizzes are created **active** (`isActive: true`).

**Sample Request**

```http
POST /api/quizzes
Authorization: Bearer <admin_access_token>
Content-Type: application/json
```

```json
{
  "title": "اختبار الدائرة الكهربية",
  "description": "أسئلة على مقدمة الدوائر",
  "quizType": "LessonQuiz",
  "levelId": null,
  "lessonId": 5
}
```

**Success Response:** `201 Created`

```http
201 Created
Location: /api/quizzes/15
```

```json
{
  "id": 15,
  "title": "اختبار الدائرة الكهربية",
  "description": "أسئلة على مقدمة الدوائر",
  "quizType": "LessonQuiz",
  "levelId": null,
  "lessonId": 5,
  "isActive": true,
  "createdAt": "2026-09-10T19:00:00.0000000Z",
  "updatedAt": null
}
```

**Possible Errors**

`400` — blank title: `{ "statusCode": 400, "message": "عنوان الاختبار مطلوب" }`
`400` — over 300 chars: `{ "statusCode": 400, "message": "عنوان الاختبار لا يتجاوز 300 حرف" }`
`400` — unknown type: `{ "statusCode": 400, "message": "نوع الاختبار 'Foo' غير صالح" }`
`400` — type/reference mismatch:

```json
{ "statusCode": 400, "message": "نوع الاختبار 'LessonQuiz' لا يتوافق مع LevelId/LessonId المرسلة" }
```

`401`, `403` — empty.

**Flutter Notes:** `levelId` / `lessonId` are **not** FK-validated — a non-existent lesson id is accepted (issue #11). `createdAt` here **has `Z`**.

---

## 11.9 PUT /api/quizzes/{quizId}  *(Admin)*

**Authentication:** `Admin` · **Path:** `quizId` (int)

**Request body**

| Field | Type | Required |
|---|---|---|
| `title` | string | **yes** — same rules as §11.8 |
| `description` | string? | no |
| `isActive` | bool | **yes** |

`quizType`, `levelId`, `lessonId` are **absent by design** — a quiz cannot be re-pointed after creation.

**Sample Request**

```http
PUT /api/quizzes/15
Authorization: Bearer <admin_access_token>
Content-Type: application/json
```

```json
{ "title": "اختبار الدائرة الكهربية - محدّث", "description": "نسخة محدّثة", "isActive": true }
```

**Success Response:** `200 OK` — full `QuizResponseDto` with `updatedAt` set (**with `Z`**).

**Possible Errors:** `404` `"الاختبار رقم 15 غير موجود"`; `400` title rules; `401`, `403` empty.

---

## 11.10 PATCH /api/quizzes/{quizId}/active  *(Admin)*

**Authentication:** `Admin`
**Path:** `quizId` (int) · **Query:** `isActive` (bool, **required**)
**Request Body: None** — the flag is a **query parameter**, not a body.

**Sample Request**

```http
PATCH /api/quizzes/15/active?isActive=false
Authorization: Bearer <admin_access_token>
```

**Success Response:** `204 No Content` — **empty body**.

**Possible Errors:** `404` `"الاختبار رقم 15 غير موجود"`; `401`, `403` empty.

**Flutter Notes:** deactivating blocks new attempts (§11.1 returns 400) but does not touch attempts already in flight.

---

## 11.11 GET /api/questions?quizId=  *(Admin)*

**Purpose:** all questions of a quiz **including inactive ones**, with `isCorrect` visible. Admin authoring only.

**Authentication:** `Admin`

**Query parameters:** `quizId` (int, **required**)
**Request Body: None**

**Sample Request**

```http
GET /api/questions?quizId=15
Authorization: Bearer <admin_access_token>
```

**Success Response:** `200 OK` — bare array:

```json
[
  {
    "id": 101,
    "quizId": 15,
    "topicId": 3,
    "questionText": "ما هو الجهد الكهربي؟",
    "difficulty": "Easy",
    "displayOrder": 1,
    "points": 1,
    "isActive": true,
    "createdAt": "2026-08-10T09:05:00",
    "options": [
      { "id": 1001, "optionText": "فرق الجهد بين نقطتين", "isCorrect": true,  "displayOrder": 1 },
      { "id": 1002, "optionText": "مقاومة مرور التيار",   "isCorrect": false, "displayOrder": 2 },
      { "id": 1003, "optionText": "شدة التيار المار",     "isCorrect": false, "displayOrder": 3 }
    ]
  }
]
```

**Ordering:** questions by `displayOrder`; nested options by `displayOrder`. **Pagination: Not implemented.**

**Possible Errors:** `404` `"الاختبار رقم 15 غير موجود"`; `401`, `403` empty.

**🔒 Flutter Notes — SECURITY:**
- This response **contains `isCorrect`**. It is the *admin* representation (`AdminQuestionResponseDto`) and is reachable only with the Admin role.
- **Never call this from the student app.** Never reuse this Dart model on a child-facing screen. Keep admin models in a separate file/package from the quiz-taking models so `isCorrect` can never leak into the student UI.
- Note the property is `id` here, but `questionId` / `optionId` in the child-facing DTOs — different names for the same values (issue #12).

---

## 11.12 GET /api/questions/{questionId}  *(Admin)*

**Authentication:** `Admin` · **Path:** `questionId` (int) · **Request Body: None**

**Sample Request**

```http
GET /api/questions/101
Authorization: Bearer <admin_access_token>
```

**Success Response:** `200 OK` — one `AdminQuestionResponseDto` (same shape as one element of §11.11, `isCorrect` included).

**Possible Errors:** `404` `"السؤال رقم 101 غير موجود"`; `401`, `403` empty.

---

## 11.13 POST /api/questions  *(Admin)*

**Purpose:** create a question. **It starts INACTIVE** and is invisible to attempts until activated (§11.15).

**Authentication:** `Admin`

**Request body**

| Field | Type | Required | Rules |
|---|---|---|---|
| `quizId` | int | **yes** | must exist |
| `topicId` | int | **yes** | must exist |
| `questionText` | string | **yes** | non-blank, trimmed |
| `difficulty` | string | no | one of the four; blank → `Medium` |
| `displayOrder` | short | **yes** | **unique within the quiz** |
| `points` | byte | no | `> 0`; `0` → `1` |

**Sample Request**

```http
POST /api/questions
Authorization: Bearer <admin_access_token>
Content-Type: application/json
```

```json
{
  "quizId": 15,
  "topicId": 3,
  "questionText": "ما وحدة قياس المقاومة الكهربية؟",
  "difficulty": "Medium",
  "displayOrder": 2,
  "points": 2
}
```

**Success Response:** `201 Created`

```http
201 Created
Location: /api/questions/102
```

```json
{
  "id": 102,
  "quizId": 15,
  "topicId": 3,
  "questionText": "ما وحدة قياس المقاومة الكهربية؟",
  "difficulty": "Medium",
  "displayOrder": 2,
  "points": 2,
  "isActive": false,
  "createdAt": "2026-09-10T19:10:00",
  "options": []
}
```

**Possible Errors**

`404` — `"الاختبار رقم 15 غير موجود"` or `"الموضوع رقم 3 غير موجود"`
`400` — `{ "statusCode": 400, "message": "نص السؤال مطلوب" }`
`400` — `{ "statusCode": 400, "message": "مستوى الصعوبة 'Foo' غير صالح" }`
`400` — display order taken (pre-check): `{ "statusCode": 400, "message": "الترتيب 2 مستخدم بالفعل في الاختبار رقم 15" }`

`409` — display order race (two admins at once):

```json
{ "statusCode": 409, "message": "الترتيب 2 مستخدم بالفعل في الاختبار رقم 15" }
```

`401`, `403` — empty.

**Flutter Notes:** authoring order is create question → add options (§11.16) → activate (§11.15). Activation fails until exactly one option is correct.

---

## 11.14 PUT /api/questions/{questionId}  *(Admin)*

**Authentication:** `Admin` · **Path:** `questionId` (int)

**Request body:** `topicId` (int, req), `questionText` (string, req), `difficulty` (string, opt), `displayOrder` (short, req), `points` (byte, opt), `isActive` (bool, req).

**`quizId` is absent by design** — a question cannot move between quizzes.

**Sample Request**

```http
PUT /api/questions/102
Authorization: Bearer <admin_access_token>
Content-Type: application/json
```

```json
{
  "topicId": 3,
  "questionText": "ما وحدة قياس المقاومة الكهربية؟",
  "difficulty": "Medium",
  "displayOrder": 2,
  "points": 2,
  "isActive": true
}
```

**Success Response:** `200 OK` — full `AdminQuestionResponseDto`.

**Possible Errors**

`404` — `"السؤال رقم 102 غير موجود"` / `"الموضوع رقم 3 غير موجود"`
`400` — `"نص السؤال مطلوب"`, invalid difficulty, or display order taken
`400` — activating without exactly one correct option:

```json
{ "statusCode": 400, "message": "السؤال رقم 102 يجب أن يحتوي على إجابة صحيحة واحدة بالضبط قبل تفعيله" }
```

`409` — display order race; `401`, `403` empty.

**Flutter Notes:** editing a question **never** re-grades past attempts — each attempt froze its own topic/difficulty/answer key when it started.

---

## 11.15 PATCH /api/questions/{questionId}/active  *(Admin)*

**Authentication:** `Admin`
**Path:** `questionId` (int) · **Query:** `isActive` (bool, **required**)
**Request Body: None**

**Sample Request**

```http
PATCH /api/questions/102/active?isActive=true
Authorization: Bearer <admin_access_token>
```

**Success Response:** `204 No Content` — empty body.

**Possible Errors**

`404` — `"السؤال رقم 102 غير موجود"`
`400` — activating without exactly one correct option (message as in §11.14)
`401`, `403` — empty.

**Flutter Notes:** deactivating keeps the question out of **new** attempts, but it still appears in **retries** of older attempts by design.

---

## 11.16 GET /api/question-options?questionId=  *(Admin)*

**Authentication:** `Admin` · **Query:** `questionId` (int, required) · **Request Body: None**

**Sample Request**

```http
GET /api/question-options?questionId=101
Authorization: Bearer <admin_access_token>
```

**Success Response:** `200 OK` — bare array, **`isCorrect` included**:

```json
[
  { "id": 1001, "optionText": "فرق الجهد بين نقطتين", "isCorrect": true,  "displayOrder": 1 },
  { "id": 1002, "optionText": "مقاومة مرور التيار",   "isCorrect": false, "displayOrder": 2 },
  { "id": 1003, "optionText": "شدة التيار المار",     "isCorrect": false, "displayOrder": 3 }
]
```

**Ordering:** `displayOrder`. **Pagination: Not implemented.**

**Possible Errors:** `404` `"السؤال رقم 101 غير موجود"`; `401`, `403` empty.

**🔒 Admin-only — never call from the student app.**

---

## 11.17 POST /api/question-options  *(Admin)*

**Authentication:** `Admin`

**Request body**

| Field | Type | Required | Rules |
|---|---|---|---|
| `questionId` | int | **yes** | must exist |
| `optionText` | string | **yes** | non-blank, trimmed |
| `isCorrect` | bool | **yes** | **at most one `true` per question** (DB-enforced) |
| `displayOrder` | short | **yes** | **unique within the question** |

**Sample Request**

```http
POST /api/question-options
Authorization: Bearer <admin_access_token>
Content-Type: application/json
```

```json
{ "questionId": 102, "optionText": "الأوم", "isCorrect": true, "displayOrder": 1 }
```

**Success Response:** `201 Created`

```json
{ "id": 1004, "optionText": "الأوم", "isCorrect": true, "displayOrder": 1 }
```

**Possible Errors**

`404` — `"السؤال رقم 102 غير موجود"`
`400` — `"نص الاختيار مطلوب"`
`400` — `{ "statusCode": 400, "message": "الترتيب 1 مستخدم بالفعل في السؤال رقم 102" }`
`400` — `{ "statusCode": 400, "message": "السؤال رقم 102 له إجابة صحيحة بالفعل" }`

`409` — either uniqueness rule lost a race:

```json
{ "statusCode": 409, "message": "السؤال رقم 102 له إجابة صحيحة بالفعل" }
```

`401`, `403` — empty.

**Flutter Notes:** the `Location` header points at the **by-question listing**, not a single-option URL, because no single-option GET exists (issue #13).

---

## 11.18 PUT /api/question-options/{optionId}  *(Admin)*

**Authentication:** `Admin` · **Path:** `optionId` (int)

**Request body:** `optionText` (string, req), `isCorrect` (bool, req), `displayOrder` (short, req). **`questionId` is absent** — an option cannot move between questions.

**Sample Request**

```http
PUT /api/question-options/1005
Authorization: Bearer <admin_access_token>
Content-Type: application/json
```

```json
{ "optionText": "الفولت", "isCorrect": false, "displayOrder": 2 }
```

**Success Response:** `200 OK`

```json
{ "id": 1005, "optionText": "الفولت", "isCorrect": false, "displayOrder": 2 }
```

**Possible Errors**

`404` — `"الاختيار رقم 1005 غير موجود"`
`400` — `"نص الاختيار مطلوب"`, display order taken, or `"السؤال رقم 102 له إجابة صحيحة بالفعل"`
`400` — un-setting the only correct option of an **active** question:

```json
{ "statusCode": 400, "message": "لا يمكن إلغاء الإجابة الصحيحة الوحيدة من السؤال رقم 102 وهو مفعّل" }
```

`409` — uniqueness race; `401`, `403` empty.

---

## 11.19 DELETE /api/question-options/{optionId}  *(Admin)*

**Authentication:** `Admin` · **Path:** `optionId` (int) · **Request Body: None**

**Sample Request**

```http
DELETE /api/question-options/1006
Authorization: Bearer <admin_access_token>
```

**Success Response:** `204 No Content` — empty body.

**Possible Errors**

`404` — `"الاختيار رقم 1006 غير موجود"`
`400` — chosen by a child in a past attempt:

```json
{ "statusCode": 400, "message": "لا يمكن حذف الاختيار رقم 1006 لأنه مستخدم في محاولات سابقة" }
```

`400` — it is the recorded answer key of a past attempt:

```json
{ "statusCode": 400, "message": "لا يمكن حذف الاختيار رقم 1006 لأنه الإجابة الصحيحة المسجّلة في محاولات سابقة" }
```

`400` — only correct option of an active question: `"لا يمكن حذف الإجابة الصحيحة الوحيدة من السؤال رقم 102 وهو مفعّل"`
`401`, `403` — empty.

**Flutter Notes:** options used by history are permanently undeletable by design. Edit the text instead, or deactivate the question.

---

# 12. AI API

```text
Implemented: NO public HTTP endpoints.
```

The AI integration exists (`IAiHintGenerator` → `HttpExternalAiProvider`) but is **entirely server-internal**. There is no controller, no route, and nothing for Flutter to call.

**How Flutter receives AI output:** as the `currentHint` string on question objects.

| Where | When populated |
|---|---|
| `POST /api/quiz-attempts/{id}/submit` → `retryQuestions[].currentHint` | freshly generated for every wrong answer |
| `POST /api/quiz-attempts?previousAttemptId=` → `questions[].currentHint` | latest stored hint per question |
| `GET /api/quiz-attempts/{id}` → `questions[].currentHint` | latest stored hint, or `null` |

**Contract:** exactly **one hint per wrong question**, in Arabic, plain text. Hints accumulate across the retry chain (`HintSequence` 1, 2, 3…) and the API always serves the **latest** one — earlier hints are not exposed.

**AI failure surface:** the AI is called *during* submit, so its failures appear as submit failures:

| Case | Response |
|---|---|
| AI service down / HTTP error / malformed reply | `500` `{"statusCode":500,"message":"حدث خطأ داخلي في الخادم"}` — **the attempt is NOT completed** and can be resubmitted |
| AI returns wrong number of hints | same `500` |
| Endpoint not configured | same `500` |
| Rate limit | **not implemented** — no `429` is ever returned |

**Flutter Notes:** treat a `500` from submit as retryable. Show "couldn't save your answers, try again", not "quiz failed". A perfect score never calls the AI, so it can never fail this way.

```text
Planned / Recommended: a dedicated AI endpoint (e.g. "explain this again"),
per-hint history, and rate limiting. None exist today.
```

---

# 13. Flutter model reference

Nullability below is taken from the C# DTOs and from actual backend behaviour, not guessed. `?` = the JSON value can be `null` or the key absent.

### Identity

```text
AuthResponse                        (data of every /api/auth/* success)
├── userId: String                  (Guid)
├── fullName: String
├── role: String                    Parent | Child
├── authProvider: String            Email | Google | Guest
├── age: int?
├── accessToken: String
├── refreshToken: String
└── accessTokenExpiresAt: DateTime  ← has Z

VerifyResetOtpResponse
├── resetToken: String
└── resetTokenExpiresAt: DateTime   ← has Z

UserProfileResponse
├── id: String                      (Guid)
├── email: String?                  null for Guest
├── fullName: String
├── role: String
├── authProvider: String
├── age: int?
├── isActive: bool
├── convertedFromGuestAt: DateTime? null unless upgraded from Guest
└── createdAt: DateTime             ← no Z
```

### Content

```text
ContentTypeResponse
├── id: int
└── name: String                    Text | Image | TextAndImage

LevelResponse
├── id: int
├── title: String
├── description: String?
└── order: int

LessonSummaryResponse
├── id: int
├── levelId: int
├── title: String
├── description: String?
├── sortOrder: int
├── isPublished: bool
└── createdAt: DateTime             ← no Z on reads, has Z on create

LessonDetailResponse
├── …all LessonSummaryResponse fields…
└── contents: List<LessonContentResponse>

LessonContentResponse
├── id: int
├── lessonId: int
├── contentTypeId: int
├── contentTypeName: String
├── content: String?                null for a pure Image block
├── mediaUrl: String?               null for a pure Text block; SERVER-RELATIVE
└── sortOrder: int

ImageUploadResponse
└── url: String                     server-relative
```

### Assessment — child-facing

```text
QuizAttemptResponseDto              ← Start (§11.1) and Get (§11.3)
├── attemptId: int                  (C# long — 64-bit)
├── quizId: int
├── startedAt: DateTime             ← has Z on Start, no Z on Get
└── questions: List<QuizQuestionForAttemptDto>

QuizQuestionForAttemptDto
├── questionId: int
├── questionText: String
├── difficulty: String              Easy | Medium | Hard | Advanced
├── displayOrder: int
├── points: int                     display only — does NOT affect the score
├── currentHint: String?            null before any hint exists
└── options: List<QuizAnswerOptionDto>

QuizAnswerOptionDto                 🔒 NO isCorrect FIELD — by design
├── optionId: int
├── optionText: String
└── displayOrder: int

SubmitQuizAttemptDto                ← request body of §11.2
└── mistakes: List<QuizAttemptMistakeDto>

QuizAttemptMistakeDto
├── questionId: int
└── selectedOptionId: int

QuizAttemptResultDto                ← response of §11.2
├── attemptId: int
├── totalQuestions: int
├── correctAnswers: int
├── wrongAnswers: int
├── scorePercentage: double         0.00–100.00
└── retryQuestions: List<QuizQuestionForAttemptDto>

UserTopicStatResponseDto
├── id: int                         (C# long)
├── userId: String                  (Guid)
├── topicId: int
├── difficulty: String
├── questionsAnsweredCount: int
├── correctCount: int
├── wrongCount: int                 DB-computed
├── hintsUsedCount: int
├── lastQuizAttemptId: int?
├── lastPracticedAt: DateTime?
└── updatedAt: DateTime             ← no Z
```

### Assessment — 🔒 ADMIN ONLY, keep in a separate Dart library

```text
QuizResponseDto
├── id: int
├── title: String
├── description: String?
├── quizType: String
├── levelId: int?
├── lessonId: int?
├── isActive: bool
├── createdAt: DateTime
└── updatedAt: DateTime?

PagedResult<QuizResponseDto>
├── items: List<QuizResponseDto>
├── totalCount: int
├── pageNumber: int
└── pageSize: int                   (no hasNextPage / totalPages)

AdminQuestionResponseDto
├── id: int                         ← note: "id", not "questionId"
├── quizId: int
├── topicId: int
├── questionText: String
├── difficulty: String
├── displayOrder: int
├── points: int
├── isActive: bool
├── createdAt: DateTime
└── options: List<AdminQuestionOptionResponseDto>

AdminQuestionOptionResponseDto      🔒 CONTAINS isCorrect
├── id: int                         ← note: "id", not "optionId"
├── optionText: String
├── isCorrect: bool                 🔒 NEVER surface in the student UI
└── displayOrder: int
```

### Envelopes

```text
ApiResponse<T>                      Identity + Content
├── success: bool
├── message: String
└── data: T?                        null on failure

ApiResponse                         Identity + Content, no payload
├── success: bool
└── message: String                 (NO data key at all)

ApiError                            Assessment failures
├── statusCode: int
└── message: String

ValidationProblemDetails            malformed JSON, any module
├── type: String
├── title: String
├── status: int
├── traceId: String
└── errors: Map<String, List<String>>
```

---

# 14. Flutter mock JSON files

All files are written to **`docs/mocks/`**. Load them without any HTTP:

```dart
final raw = await rootBundle.loadString('assets/mocks/mock_start_attempt.json');
final attempt = QuizAttemptResponse.fromJson(jsonDecode(raw));
```

Assessment mocks are the **raw DTO** (parse directly). Identity/Content mocks include the envelope (read `['data']`).

| File | Endpoint | State it covers |
|---|---|---|
| **Identity** | | |
| `mock_auth_guest.json` | §9.1 | guest session created |
| `mock_auth_register.json` | §9.2 | email registration |
| `mock_auth_login.json` | §9.3 | successful login |
| `mock_auth_login_invalid.json` | §9.3 | **401** invalid credentials |
| `mock_auth_refresh.json` | §9.5 | rotated token pair |
| `mock_auth_logout.json` | §9.6 | logout, no `data` key |
| `mock_auth_forgot_password.json` | §9.7 | OTP sent |
| `mock_auth_verify_otp.json` | §9.8 | reset token issued |
| `mock_auth_verify_otp_invalid.json` | §9.8 | **400** wrong code |
| `mock_users_me.json` | §9.10 | current user |
| `mock_validation_error.json` | any | **400** `ValidationProblemDetails` |
| `mock_unauthorized.json` | any | **401** (documents the EMPTY body) |
| **Content** | | |
| `mock_content_types.json` | §10.1 | lookup list |
| `mock_levels_list.json` | §10.2 | course/level list |
| `mock_level_detail.json` | §10.3 | single level |
| `mock_lessons_by_level.json` | §10.8 | lesson list (incl. an unpublished one) |
| `mock_lesson_detail.json` | §10.9 | lesson + all 3 content-block kinds |
| `mock_lesson_not_found.json` | §10.9 | **404** |
| `mock_image_upload.json` | §10.19 | uploaded image URL |
| **Assessment (child)** | | |
| `mock_start_attempt.json` | §11.1 | first attempt, 3 questions, `currentHint: null` |
| `mock_start_retry.json` | §11.1 | retry, 1 question **with** a hint |
| `mock_submit_attempt_request.json` | §11.2 | **request** body |
| `mock_submit_attempt_result.json` | §11.2 | result + mistakes + hints |
| `mock_submit_perfect_score.json` | §11.2 | 100%, empty `retryQuestions` |
| `mock_attempt_already_completed.json` | §11.2 | **409** |
| `mock_attempt_not_found.json` | §11.3 | **404** |
| `mock_attempt_forbidden.json` | §11.3 | **403** other user's attempt |
| `mock_attempt_ai_failure.json` | §11.2 | **500** AI unavailable |
| `mock_get_attempt.json` | §11.3 | resumed attempt |
| `mock_user_topic_stats.json` | §11.4 | stats list |
| `mock_user_topic_stats_empty.json` | §11.4 | zero state `[]` |
| **Assessment (admin)** | | |
| `mock_admin_quizzes_paged.json` | §11.6 | paginated quiz list |
| `mock_admin_questions.json` | §11.11 | 🔒 questions **with** `isCorrect` |
| `mock_admin_conflict.json` | §11.13 | **409** display-order race |

---

# 15. Backend ↔ Flutter integration issues

Concrete problems found while documenting. Ordered by impact on the Flutter team.

### 1. 🔴 Three different response envelopes
Identity/Content wrap in `ApiResponse<T>`; Assessment returns raw DTOs; errors use `{statusCode,message}`; binding failures use `ValidationProblemDetails`. Flutter needs **four** parsing paths and cannot share one HTTP interceptor.
**Suggested fix:** wrap Assessment in `ApiResponse<T>` too, and map `ExceptionMiddleware` onto the same shape.

### 2. 🔴 The `Admin` role can never be granted
`AuthService.NormalizeRole` only ever returns `"Parent"` or `"Child"`. Every `[Authorize(Roles="Admin")]` endpoint — **24 of the 49** — returns 403 for every real user. The entire admin surface is unreachable, and the `Users.Users` CHECK constraint may not even permit the value.
**Suggested fix:** add `Admin` to `UserRoles` + the DB CHECK, and a deliberate way to grant it (seed/manual). Not something Flutter can work around.

### 3. 🔴 No child-facing way to discover a quiz
`GET /api/quizzes` is Admin-only, and no Content response exposes the quiz attached to a lesson or level. A child app literally cannot find a `quizId` to start.
**Suggested fix:** either `GET /api/content/lessons/{id}/quizzes`, or add `quizId` to `LessonDetailResponse`.

### 4. 🟠 `DateTime` serialised in two different formats
In-memory values carry `Z`; DB-loaded values do not (§5). The **same field** on the same DTO differs between create and read.
**Suggested fix:** configure `DateTime.SpecifyKind(..., Utc)` on read, or a global JSON converter that always writes `Z`.
**Flutter workaround:** the defensive parser in §5.

### 5. 🟠 No input validation on Identity DTOs
`RegisterEmailRequest` has no `[Required]`, `[EmailAddress]` or length rules — the backend accepts `""` as an email and `"1"` as a password. There is no FluentValidation anywhere in the solution.
**Flutter workaround:** validate client-side; a `422` never comes.

### 6. 🟠 Unpublished lessons are returned to everyone
`GET /api/content/levels/{id}/lessons` and `GET /api/content/lessons/{id}` never filter on `isPublished`. Draft content is visible to any authenticated child.
**Flutter workaround:** filter `isPublished == true` yourself — but this is a backend content-leak that should be fixed server-side.

### 7. 🟡 Attempt `status` is never exposed
`InProgress`/`Completed`/`Abandoned` drive backend behaviour but appear in no DTO. Flutter cannot tell whether a fetched attempt is still open, and must track it locally.
**Suggested fix:** add `status` to `QuizAttemptResponseDto`.

### 8. 🟡 `mistakes` is a misleading field name
The client cannot know which answers are wrong (`isCorrect` is hidden), so it must send **all** answers into a field called `mistakes`. It works — the backend filters — but the name inverts the actual contract.
**Suggested fix:** rename to `answers`, or add a `SubmitAnswersDto`.

### 9. 🟡 Password reset does not revoke sessions
`ResetPasswordAsync` changes the hash but leaves every refresh token active. After "my account was hacked, I reset my password", the attacker stays signed in for up to 30 days.
**Suggested fix:** revoke all of the user's refresh tokens on reset.

### 10. 🟡 No progress/completion model anywhere
Nothing records "lesson completed" or "level unlocked". `LevelResponse`/`LessonSummaryResponse` have no `isCompleted`/`isLocked`, and no endpoint sets them. The mock states "completed lesson" and "locked lesson" **cannot be produced** by this backend.
**Suggested fix:** a Progress module. Until then Flutter must invent local-only progress.

### 11. 🟡 `Quiz.LevelId` / `LessonId` are not FK-validated
`POST /api/quizzes` accepts `lessonId: 99999` with no such lesson (deliberately loose cross-module coupling, no DB FK). Admin UIs can create orphan quizzes.

### 12. 🟡 Inconsistent id property naming
The same value is `id` in admin DTOs but `questionId`/`optionId` in child DTOs. Two Dart models for one entity.

### 13. ⚪ `POST /api/question-options` returns a misleading `Location`
It points at the by-question listing, because no single-option GET exists.

### 14. ⚪ Inconsistent success codes for creates
Assessment creates return `201`; Content and Identity creates return `200`. Deletes are `204` in Assessment, `200` in Content.

### 15. ⚪ `GET`/`PUT /api/users/me` disagree on the not-found code
GET returns `404`, PUT returns `400`, for the identical condition.

### 16. ⚪ Dead DTO
`AssessmentBL/DTOs/Question/QuestionResponseDto.cs` is an empty class — unused, ignore it.

### Verified NOT an issue

- **`isCorrect` never leaks to the student app.** Checked the whole child-facing graph: `QuizAnswerOptionDto` has no such field, and `QuizAttemptResponseDto` / `QuizAttemptResultDto` reference only it. `isCorrect` exists solely on `AdminQuestionOptionResponseDto`, behind `[Authorize(Roles="Admin")]`. The newer `correctOptionId` answer-key snapshot is also kept out of every child DTO.
- **No `UserId` is accepted from the client** on any authenticated endpoint. Every user-owned operation reads `sub` from the JWT. The `existingGuestUserId` field is a pre-auth account-linking id, not an identity claim.
- **IDOR is handled.** Reading, submitting or retrying another user's attempt returns `403` from the service layer.
- **Attempts are concurrency-safe.** A double submit yields exactly one completion; the loser gets `409` and writes nothing.

---

# 16. Missing / recommended APIs

Endpoints Flutter will likely need that **do not exist today**. All are `Planned / Recommended`, none are implemented.

| Need | Suggested endpoint | Why |
|---|---|---|
| Find a lesson's quiz | `GET /api/content/lessons/{id}/quizzes` | issue #3 — blocks the whole quiz flow |
| Topic names for stats | `GET /api/assessment/topics` | `topicId` is unlabelled (§11.4) |
| Attempt history | `GET /api/quiz-attempts?quizId=` | no way to recover a lost `attemptId`, or show past scores |
| Re-read a result | `GET /api/quiz-attempts/{id}/result` | score is returned exactly once and never again (§11.3) |
| Lesson progress | `POST /api/content/lessons/{id}/complete` | issue #10 |
| Child-facing quiz metadata | `GET /api/quizzes/{id}/summary` | title/question count before starting |
| Hint history | `GET /api/quiz-attempts/{id}/hints` | only the latest hint is served |
| Log out everywhere | `POST /api/auth/logout-all` | issue #9 |
| Delete account | `DELETE /api/users/me` | privacy/GDPR |
| Rate limiting | — | no `429` anywhere; OTP and AI endpoints are unprotected |

---

## Appendix — coverage checklist

- [x] Every one of the **49** implemented endpoints documented
- [x] Method, route, purpose, auth level, params, headers, body, validation
- [x] Sample request for every endpoint (`Request Body: None` where applicable)
- [x] Complete success response with real DTO field names for every endpoint
- [x] Error responses with real messages taken from the source
- [x] JWT claim mapping + explicit "Flutter must NOT send UserId"
- [x] Full Assessment flow with ID provenance and local-storage guidance
- [x] Question/Option/correct-answer/selected-answer distinction, `isCorrect` verified hidden
- [x] Pagination stated per endpoint (implemented on exactly one)
- [x] Nullability per field, from the C# DTOs
- [x] DateTime format documented, including the two-format inconsistency
- [x] Every enum value with exact casing
- [x] Summary table per module
- [x] Conceptual Flutter model for every DTO
- [x] Mock JSON covering success, empty, and failure states
- [x] Implemented vs. Planned separated throughout
- [x] Concrete integration issues reported, not silently resolved
