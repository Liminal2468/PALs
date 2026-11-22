

# BUild 4.0+

This document records the current matching design and API progress. The matching endpoint uses algorithm #1 by default, with algorithm #2 available as a developer-selectable alternative.

**Current project status:**

> [!NOTE]
> Rechecked against the current controllers, backend, and frontend as of 2026-11-22. Server and Mentos Release builds succeed, all ten server tests pass, and the active login JavaScript passes syntax checks. DBSERV and Mentos are deployed and healthy on apmain and pals. The public landing page and login dialog were browser-smoke-tested after fixing a stale cached login-script URL; no database-backed browser end-to-end test was run for this review.

## Corrected API status

| Route                                     | Current status                                                              |
| ----------------------------------------- | --------------------------------------------------------------------------- |
| `GET /api/v1/health`                      | Implemented                                                                 |
| `GET /api/v1/auth/options`                | Implemented                                                                 |
| `POST /api/v1/auth/login`                 | Implemented; directs existing password accounts to password entry, otherwise starts school-email verification |
| `POST /api/v1/auth/resend`                | Implemented; replaces the pending attempt after successfully resending a code |
| `POST /api/v1/verify`                     | Implemented; verifies the six-digit code bound to the HttpOnly pending-attempt cookie |
| `POST /api/v1/auth/password`              | Implemented; optional password setup after verified email sign-in            |
| `POST /api/v1/auth/password/login`        | Implemented; verifies password then starts required email verification       |
| `POST /api/v1/auth/logout`                | Implemented; revokes DBSERV session                                         |
| `GET /api/v1/session`                     | Implemented; persisted through DBSERV `web_sessions`, not HOST memory       |
| `DELETE /api/v1/session`                  | Implemented                                                                 |
| `POST /api/v1/auth/admin/login`           | Implemented; ASP.NET Identity-backed, requires schema v5 and seeded account |
| `GET/PATCH /api/v1/me`                    | Implemented through DBSERV; returns profile name, role, grade, section, and bio |
| `GET /api/v1/users`                       | Implemented; admin/developer restricted                                     |
| `GET /api/v1/pages`                       | Implemented with database-backed page metadata and role filtering            |
| `GET /api/v1/tutors`                      | Implemented                                                                 |
| `GET /api/v1/tutees`                      | Implemented                                                                 |
| `GET/POST /api/v1/learning-requests`      | Implemented                                                                 |
| `GET/POST/PATCH /api/v1/sessions`         | Implemented; POST aliases the validated connection flow, and invitees confirm requests |
| `GET/POST/PATCH/DELETE /api/v1/resources` | Implemented                                                                 |
| `GET/POST /api/v1/feedback`               | Implemented                                                                 |
| `POST /api/v1/feedback/{id}/votes`        | Implemented                                                                 |
| `POST /api/v1/support`                    | Implemented                                                                 |
| `POST /api/v1/analytics/events`           | Implemented with consent check                                              |
| `GET/POST /api/v1/analytics/consent`      | Implemented; status, grant, and withdrawal                                 |
| `GET /api/v1/analytics/summary`           | Implemented; current-user data is scoped to active consent                  |
| `GET/POST/PATCH/DELETE /api/v1/schedule`  | Implemented; requires schema v7                                             |
| `GET /api/v1/dashboard`                   | Implemented                                                                 |
| `GET /api/v1/frontend/status`             | Implemented                                                                 |
| `GET /api/v1/connect/recommendations`     | Implemented; role-aware, ranked, non-persistent recommendations              |
| `POST /api/v1/connect`                    | Implemented; revalidates a match and creates a scheduled pending session     |
| `GET /api/v1/rec-peer-tutor`              | Implemented compatibility alias                                              |
| `GET /api/v1/rec-peer-tutee`              | Implemented compatibility alias                                              |
| Provider start/callback routes            | Not implemented                                                             |
| Signup route                              | Not implemented                                                             |

Email verification uses the raw HTML template at
[`server/Identity/email/email.html`](server/Identity/email/email.html), with
only `{{ verification_code }}` replaced. The template is included in the DBSERV
publish output. The resend control starts a new verification attempt after its
30-second countdown. Accounts with passwords require password then email-code
verification; accounts without passwords start with email verification and may
set a password afterward or skip to `/pals`.
Identity passwords require at least eight characters, with uppercase and
lowercase letters, a digit, and a punctuation mark or symbol; whitespace alone
does not satisfy the symbol requirement.

## Matching implementation and query

The default is **#1 Weighted multi-criteria scoring**. Its transparent prototype baseline is:

- Require an exact subject match, an active tutee request, and an active tutor. Group-only requests and tutors marked `rejected` are excluded.
- When a tutee proficiency baseline exists, only tutors with a higher subject proficiency are candidates. The proficiency-gap score is 100 for a one-level gap, 75 for two levels, 50 for three, and 25 for four.
- Score available profile criteria with proficiency gap at 70%, same strand at 20%, and tutor grade at least the learner's at 10%. If a profile criterion is missing, omit it and renormalize over the remaining criteria.
- Recommendation reads do not persist data. `POST /api/v1/connect` revalidates the selected open request and match, then records the recommendation, score, pending session, both participants, and their link in one transaction.
- A request creator may withdraw a pending request; only the invited peer may confirm or decline it. Confirmation marks the learning request as matched. Declining leaves it open for other eligible tutors. Cancelling a confirmed session reopens the learning request.

Algorithm **#2 ZPD-gap matching** is the only selected alternative. It requires a tutee proficiency baseline and ranks by the same one-level-optimal gap scale; if the baseline is missing or the tutor is not above it, that pair is not returned. Algorithms #3–#6 remain design references and are not selectable.

The logical candidate query joins open `learning_requests` to `subjects`, active tutee and tutor accounts, `tutor_subjects`, tutor-role assignments, and both `student_profiles`. Parameters are `@user_id`, `@is_tutor`, and optional `@request_id`. For a tutor, the query returns their matching tutee requests; for a student/tutee, it returns tutors for their requests. Both paths use the same scorer and sort by score descending, then candidate name.

The browser-facing routes are `GET /api/v1/connect/recommendations` and `POST /api/v1/connect`; Mentos proxies them to the host-only DBSERV matching routes. The legacy `/api/v1/rec-peer-tutor` and `/api/v1/rec-peer-tutee` routes are aliases for recommendations. The developer System page can switch between #1 and #2. The selected algorithm is process-local and resets to #1 when DBSERV restarts.

> [!NOTE]
> The populated dashboard, profile, tutor/tutee matching, session, feedback, and analytics views use the authenticated APIs. Resources and Settings were not part of this page update. Resources retain their prior API wiring. A browser/database end-to-end verification is still outstanding.

## Corrected frontend status

| Area                            | Current status                                                              |
| ------------------------------- | --------------------------------------------------------------------------- |
| Login/protected pages           | Connected                                                                   |
| Student authentication          | Six-digit email verification and optional Identity password sign-in connected; OAuth is not implemented |
| Admin authentication            | Connected to `/api/v1/auth/admin/login`                                     |
| Profile display/update          | Live account name, role, grade, section, bio, roles, subjects, email, and initials avatar |
| Tutors                          | Ranked recommendations use current requests and tutor-subject profiles      |
| Tutor search/filter/sort        | Client-side filters operate on live recommendations; availability and ratings are not provided |
| Tutees                          | Tutors see ranked, open requests matched to their tutor subjects             |
| Learning requests               | Creation and listing connected                                              |
| Session requests                | Explicit start/end time, pending invitation, and invitee confirm/decline     |
| Session actions                 | Cancel, complete, and supported status transitions persist through the API   |
| Resources                       | Existing API wiring remains; no resource-page changes were made in this update |
| Feedback/posts/votes            | Live list, posting, vote counts, and per-account vote state                  |
| Support form                    | Connected to the support endpoint                                           |
| Analytics summary/consent       | Current-user summary; consent can be granted or withdrawn                   |
| Analytics event emission        | Consent-gated page views and page duration on in-app navigation              |
| Home statistics/recommendations | Live `/dashboard` aggregates and ranked matching recommendations             |
| Help/FAQ                        | Static content                                                              |
| Admin management UI             | Authentication connected; dedicated admin management UI is still incomplete |

<sub style="display:flex; color: gray; font-style: italic; justify-content: center;">Production DBSERV and Mentos are deployed as of 2026-11-22; the active DBSERV configuration uses the palsys database and six-digit email codes. Database-backed browser end-to-end testing is not recorded here; developer Identity routes require a seeded Identity account.</sub>


---
<sub style="color: gray; font-style: oblique;">The following is not related to the file but i had to use this for notes purposes.</sub>


**Comparison of the six algorithms**

| N.  | Algorithm                       | Computable alone | Best for        | Data needed                   | Interpretability | Cold start                 | Fit (1-on-1)      |
| --- | ------------------------------- | ---------------- | --------------- | ----------------------------- | ---------------- | -------------------------- | ----------------- |
| 1   | Weighted multi-criteria scoring | Yes              | 1-on-1 ranking  | Profile fields                | High             | None                       | High              |
| 2   | ZPD-gap matching                | Yes (one factor) | 1-on-1 ranking  | Proficiency per subject       | High             | Needs proficiency baseline | High as component |
| 3   | Heterogeneous pairing           | No (principle)   | Both            | Any ability measure           | Medium           | Depends on measure         | Medium            |
| 4   | Min-entropy grouping            | Yes              | Study groups    | Multi-attribute, whole cohort | Low-Medium       | Needs whole cohort         | Low (batch)       |
| 5   | Recommender system              | No (umbrella)    | Both            | Profiles (+ ratings)          | Varies           | Content-based OK           | Medium-High       |
| 6   | Feedback re-ranking             | Yes (layer only) | Refining either | Session ratings               | Medium           | Weak until sessions        | Medium            |

**Relation and comparison**

| Relationship   | Algorithms                                     | Relation                                                                                                                                                                                                       |
| -------------- | ---------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Contains       | #5 Recommender system → #1, #2, #6             | #5 is an umbrella. A content-based recommender is essentially #1 plus #2, with #6 as its feedback layer.                                                                                                       |
| Implements     | #2 ZPD-gap matching → #3 Heterogeneous pairing | #3 is the principle (prefer different levels). #2 is how you compute it.                                                                                                                                       |
| Layers on      | #6 Feedback re-ranking → #1, #2                | #6 refines the scores from #1 and #2 once session ratings exist. It cannot run alone.                                                                                                                          |
| Combines into  | #2 → #1                                        | The ZPD-gap works best as a scoring term inside #1, since it only measures one factor.                                                                                                                         |
| Alternative to | #4 Min-entropy grouping ↔ #1/#2                | #4 optimizes a whole group at once (study groups). #1/#2 rank candidates for one tutee (1-on-1). Choose by use case.                                                                                           |
| Tension with   | #3 Heterogeneous pairing ↔ RQ3 "Similarity"    | The literature says pair different levels, while RQ3 measures similarity. This resolves if similarity is computed on non-proficiency attributes (preferences, schedule, strand) and difference on proficiency. |

**Quick dependency view**

| Layer                 | Algorithms             | Pipeline Roles                                                         |
| --------------------- | ---------------------- | ---------------------------------------------------------------------- |
| Label / justification | #5, #3                 | Names the overall system and explains why it prefers a proficiency gap |
| Base ranker           | #1 (with #2 as a term) | Scores and ranks tutors on day one                                     |
| Refinement            | #6                     | Adjusts rankings as ratings accumulate                                 |
| Separate module       | #4                     | Study-group formation only                                             |

`Mentos` partially applies the ISO/IEC 25010:2023 product-quality model. The numeric table below is the prior internal baseline; it was not re-scored for this status update.

> [!IMPORTANT]
>  product-quality characteristics for evaluation, not a certification pass/fail test. [Official ISO reference](https://www.iso.org/standard/78176.html)

|Characteristic|Score|
|---|---|
|Functional suitability|5.0|
|Performance efficiency|4.5|
|Compatibility|6.0|
|Interaction capability|5.0|
|Reliability|4.5|
|Security|5.0|
|Maintainability|6.5|
|Flexibility|6.0|
|Safety|3.5|
|Overall|**5.1/10**|

The project remains at prototype/integration stage. Matching, populated-page data, session-request handling, and production deployment are implemented. Database-backed browser end-to-end testing, formal performance/security/privacy reviews, and some administrative and resource-management UI remain outstanding.