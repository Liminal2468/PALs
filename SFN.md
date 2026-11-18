As of the moment this is the current progress on the latest updated build with the new requested back end. Some parts requires further discussion to with the leader and group in order to proceed, such as the `/connect` endpoint as this will require an algorithm. Further assesment of the research provides ambigous algorithms as it introduce multiple algorithms, so i'll ask if i would integrate all or integrate one 

**Current project status:**

> [!NOTE]
> This is a rechecked of the current controllers, backend, and frontend as of 2026-11-18. Both .NET projects build successfully with zero warnings/errors. 

## Corrected API status

| Route                                     | Current status                                                              |
| ----------------------------------------- | --------------------------------------------------------------------------- |
| `GET /api/v1/health`                      | Implemented                                                                 |
| `GET /api/v1/auth/options`                | Implemented                                                                 |
| `POST /api/v1/auth/login`                 | Implemented; school-email provisioning, not OAuth/password login            |
| `POST /api/v1/auth/logout`                | Implemented; revokes DBSERV session                                         |
| `GET /api/v1/session`                     | Implemented; persisted through DBSERV `web_sessions`, not HOST memory       |
| `DELETE /api/v1/session`                  | Implemented                                                                 |
| `POST /api/v1/auth/admin/login`           | Implemented; ASP.NET Identity-backed, requires schema v5 and seeded account |
| `GET/PATCH /api/v1/me`                    | Implemented through DBSERV; profile fields remain partial                   |
| `GET /api/v1/users`                       | Implemented; admin/developer restricted                                     |
| `GET /api/v1/pages`                       | Implemented as a static page catalog, not full page management              |
| `GET /api/v1/tutors`                      | Implemented                                                                 |
| `GET /api/v1/tutees`                      | Implemented                                                                 |
| `GET/POST /api/v1/learning-requests`      | Implemented                                                                 |
| `GET/POST/PATCH /api/v1/sessions`         | Implemented                                                                 |
| `GET/POST/PATCH/DELETE /api/v1/resources` | Implemented                                                                 |
| `GET/POST /api/v1/feedback`               | Implemented                                                                 |
| `POST /api/v1/feedback/{id}/votes`        | Implemented                                                                 |
| `POST /api/v1/support`                    | Implemented                                                                 |
| `POST /api/v1/analytics/events`           | Implemented with consent check                                              |
| `POST /api/v1/analytics/consent`          | Implemented                                                                 |
| `GET /api/v1/analytics/summary`           | Implemented                                                                 |
| `GET/POST/PATCH/DELETE /api/v1/schedule`  | Implemented; requires schema v7                                             |
| `GET /api/v1/dashboard`                   | Implemented                                                                 |
| `GET /api/v1/frontend/status`             | Implemented                                                                 |
| `GET /api/v1/rec-peer-tutor`              | Not implemented                                                             |
| `GET /api/v1/recommended-peer-tutors`     | Not implemented alias                                                       |
| `GET /api/v1/tutors/recommended`          | Not implemented alias                                                       |
| Provider start/callback routes            | Not implemented                                                             |
| Signup route                              | Not implemented                                                             |


> [!NOTE]
> After integration of the backend to frontend, some placeholder or templates are removed. Although, some parts of `Mentos/public` HTML front end are yet to be integrated to the backend.

## Corrected frontend status

| Area                            | Current status                                                              |
| ------------------------------- | --------------------------------------------------------------------------- |
| Login/protected pages           | Connected                                                                   |
| Student authentication          | Connected to email provisioning; not complete password/OAuth authentication |
| Admin authentication            | Connected to `/api/v1/auth/admin/login`                                     |
| Profile display/update          | Partially connected; display name, grade, and section update live           |
| Bio, roles, subject selections  | Still not sent to the backend                                               |
| Tutors                          | Live list and request creation connected                                    |
| Tutor favorites/search/details  | Client-side only                                                            |
| Tutees                          | Live requests connected                                                     |
| Learning requests               | Connected                                                                   |
| Sessions list                   | Live list connected                                                         |
| Session creation                | Partially connected                                                         |
| Session cancel/complete actions | Still browser `localStorage` only                                           |
| Resources                       | Live listing and creation connected                                         |
| Resource update/delete UI       | Not connected                                                               |
| Feedback/posts/votes            | Live API wiring exists, but legacy browser-storage fallback remains         |
| Support form                    | Connected to the support endpoint                                           |
| Analytics summary/consent       | Connected                                                                   |
| Analytics event emission        | Not currently emitted by the active frontend                                |
| Home statistics                 | Now connected to live `/dashboard` data                                     |
| Home recommended tutors         | Deferred with `/connect`                                                    |
| Help/FAQ                        | Static content                                                              |
| Admin management UI             | Authentication connected; dedicated admin management UI is still incomplete |

<sub style="display:flex; color: gray; font-style: italic; justify-content: center;">End-to-end operation still requires the DBSERV database, schemas v5–v7, and seeded Identity accounts.</sub>


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

As of the moment `Mentos` currently applies ISO/IEC 25010:2023 quality model partially.  

> [!IMPORTANT]
>  product-quality characteristics for evaluation, not a certification pass/fail test. [Official ISO reference](https://www.iso.org/standard/78176.html)

|Characteristic|Score|
|---|--:|
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
The project is currently at prototype/integration stage. The main quality gaps are authentication, matching, frontend placeholders/local storage fallbacks, missing performance/security testing, and unapplied database migrations. 