# PALs matching algorithm research notes

## Purpose and evidence boundary

This note translates the matching discussion in the defended/revised proposal
into implementable candidate approaches. It is a design exploration, not a
claim that the proposal selected or validated any one algorithm. The proposal
does **not** give a final scoring formula, weights, proficiency scale, capacity
rules, group size, fairness metric, or definition of algorithmic accuracy.
Those are open decisions and should be agreed with the research team before
the production matcher is treated as final.

The study's target is STI College Cubao SHS students. Matching should help
students find academic peer support by subject/topic need, proficiency,
schedule, and learner characteristics; it should support both one-to-one and
study-group interactions. The pedagogical goal is complementary skill
alignment (a tutor can scaffold a tutee), not indiscriminate similarity across
all attributes.

**What the proposal says**

1. The research questions ask how effective matching is in terms of **profile
   alignment, compatibility, and similarity** (Chapter 1, p. 3).
2. The conceptual framework names **schedule, subject, and role** as matching
   criteria. Its discussion also links subject need and academic level to ZPD
   (Chapter 1, pp. 5, 7–8).
3. The literature review says data-/goal-informed group formation may improve
   engagement and outcomes compared with random grouping, and describes
   heterogeneous ability groupings as potentially complementary (Chapter 1,
   pp. 12, 15–17).
4. It mentions recommender systems and optimization/grouping methods as
   precedents, including minimum-entropy heterogeneous grouping. It does not
   prescribe their implementation or prove that a particular algorithm will
   work for PALs (Chapter 1, pp. 15–17).
5. It raises fairness and interpretability and mentions WordCloud
   visualizations to illustrate why groups were formed, but gives no fairness
   definition, explanation design, or validation procedure (Chapter 1, p. 16).
6. Questionnaire 1 is planned to ask about matching effectiveness using
   compatibility, alignment, and similarity; respondents use a 4-point
   agreement scale after exploring/simulating matches (Chapter 2, pp. 22–23,
   27).

**Proposal references relevant to matching**

The following are cited in the proposal's Chapter 1 literature review and
reference list. The proposal's summaries below are not an independent review
of those papers; verify original publications and their precise findings before
using them as evidence for algorithm selection.

- Doroudi, S., Holstein, K., Aleven, V., & Brunskill, E. (2023). “Three
  algorithms for grouping students: A bridge between personalized tutoring
  system data and classroom pedagogy.” *International Journal of Artificial
  Intelligence in Education, 33*, 843–884.
  https://doi.org/10.1007/s40593-022-00309-y
- Kanika, Chakraverty, S., Chakraborty, P., & Madan, M. (2023). “Effect of
  different grouping arrangements on students' achievement and experience in
  collaborative learning environment.” *Interactive Learning Environments,
  31*(10), 6366–6378.
  https://doi.org/10.1080/10494820.2022.2036764
- Liang, C., Majumdar, R., Nakamizo, Y., Flanagan, B., & Ogata, H. (2022).
  “Algorithmic group formation and group work evaluation in a learning
  analytics-enhanced environment: Implementation study in a Japanese junior
  high school.” *Interactive Learning Environments*, 1–24. The proposal's DOI
  link is incomplete.
- Chen, L., & Pu, P. (2023). “Matching learners in peer learning environments
  using recommender systems.” *Computers & Education: Artificial Intelligence,
  4*, 100120. https://doi.org/10.1016/j.caeai.2023.100120
- Vallès-Català, T., & Palau, R. (2023). “Minimum entropy collaborative
  groupings: A tool for an automatic heterogeneous learning group formation.”
  *PLOS ONE, 18*(1), e0280604. The proposal lists an incomplete DOI link.

**Modeling the matching problem**

Let a learning request be $r$, a candidate tutor be $t$, and a subject or
topic be $s$. Each request and tutor has attributes such as subject
proficiency, availability, preferred format, and learning preferences. Use
only attributes that are collected with a clear purpose and appropriate
consent.

1. **Separate eligibility from ranking**

First exclude candidates who cannot or should not be matched. Examples of
hard constraints:

- tutor has an active tutor role and is eligible for the requested subject;
- requested subject/topic is active and supported;
- tutor has declared availability overlapping the requested time window;
- tutor has remaining capacity and account is active;
- candidate is not the requester;
- request is open and the selected format is supported.

Represent eligibility as:

$$
E(r,t)=
\begin{array}{ll}
1 & \mathrm{if\ all\ mandatory\ constraints\ pass},\\
0 & \mathrm{otherwise}.
\end{array}
$$

Do not compensate for a failed mandatory constraint with a high numeric score.
Eligibility checks also provide concrete explanation reasons such as
`subject_not_supported` or `no_schedule_overlap`.

2. **Candidate feature scores**

For eligible candidates, normalize each feature to $[0,1]$. A simple
weighted ranking score is:

$$
S(r,t) =
w_s S_{\mathrm{subject}} +
w_n S_{\mathrm{need}} +
w_a S_{\mathrm{availability}} +
w_p S_{\mathrm{preference}} +
w_c S_{\mathrm{compatibility}},
\qquad
w_i \geq 0,\quad \sum_i w_i = 1.
$$

This is a candidate design, not a proposal-specified formula. Keep
**complementarity** and **similarity** distinct:

- Subject fit can be binary for exact subject support or graded for topic
  coverage.
- Need/proficiency fit should reward a useful tutor–tutee difference, not
  simply the smallest distance.
- Availability can be a hard overlap requirement and/or a soft score based on
  the amount of shared time.
- Preference similarity is appropriate only for preference dimensions where
  similarity is intended to help (e.g. preferred learning format). Do not
  maximize similarity on proficiency if heterogeneous pairing is the goal.
- Compatibility should not become an opaque proxy for sensitive traits.

One possible proficiency-gap function, assuming both levels are on a validated
ordered scale $1,\ldots,L$, is:

$$
g = p_t - p_r \qquad S_{\mathrm{need}}(r,t) = \max\left(0, 1 -
\frac{g-g^*}{\max(g^* L-g^*)}\right),
$$

where $p_t$ is tutor proficiency, $p_r$ is the learner's self-reported
current proficiency, and $g^*>0$ is a target gap chosen with educators.
The candidate is ineligible if $g\leq0$. The scale and target gap must be
defined and validated; if the proficiency estimate is unreliable, use a
conservative rule or collect better evidence rather than treating this score
as objective ability.

For availability intervals $A_r$ and $A_t$, a candidate overlap score can
be:

$$
S_{\mathrm{availability}}(r,t)
= \min\left(1,\frac{\mathrm{minutes}(A_r \cap A_t)}{m^*}\right),
$$

where $m^*$ is the minimum useful shared time for a session. Time-zone and
recurrence normalization must happen before computing the intersection.

3. **Similarity for preferences (optional)**

For explicitly chosen categorical preferences $P_r,P_t$, a Jaccard score is:

$$
S_{\mathrm{preference}}(r,t)
= \frac{|P_r \cap P_t|}{|P_r \cup P_t|},
$$

with a documented value for the empty/unknown case. For scalar ratings on a
known $1,\ldots,L$ scale, a normalized agreement can be:

$$
S_{\mathrm{similarity}}(r,t)
= 1 - \frac{1}{dL}\sum_{j=1}^{d}|x_{rj}-x_{tj}|,
$$

where $d$ is the number of comparable dimensions. Only compare dimensions
that are meaningful and voluntarily supplied. Similarity is a feature to test
against user ratings, not a universal objective.

## Candidate approaches

**Approach A**: *explainable weighted ranking (best first baseline)*

1. Apply hard eligibility constraints.
2. Compute individual normalized feature scores.
3. Return a ranked list with short, non-sensitive reasons and visible
   availability.
4. Let the student choose or request the top suitable candidate.

**Advantages:** simple to explain, tune, and test; supports a transparent
fallback; useful for a first pilot where there is not enough interaction data
for a learned recommender.

**Risks:** manually chosen weights can encode bias; independent ranking can
overbook popular tutors; a high score is not proof of learning effectiveness.
Record score components and algorithm version, review feedback, and avoid
claiming the score is a probability.

Pseudocode:

```text
function recommend(request, tutors):
    results = []
    for tutor in tutors:
        reasons = check_hard_constraints(request, tutor)
        if reasons is not empty:
            continue

        components = {
            subject: score_subject(request, tutor),
            need: score_proficiency_gap(request, tutor),
            availability: score_overlap(request, tutor),
            preference: score_opt_in_preferences(request, tutor),
            compatibility: score_compatible_format(request, tutor)
        }
        score = sum(weight[k] * components[k] for k in components)
        results.append({
            tutor_id: tutor.id,
            score: score,
            components: components,
            explanation: explain_top_components(components)
        })

    return sort_descending(results, by="score")
```

**Approach B**: *global one-to-one assignment*

When matching multiple open requests together, independent top-ranked
recommendations can assign the same tutor too many times. Model it as a
maximum-weight bipartite matching problem.

Let $R$ be open requests, $T$ available tutors, $x_{rt}\in\{0,1\}$
indicate an assignment, and $S_{rt}$ be the eligible pair score:

$$
\max_{x}\ \sum_{r\in R}\sum_{t\in T} S_{rt}x_{rt}
$$

subject to:

$$
\sum_{t\in T}x_{rt}\leq1\quad \forall r\in R,\qquad
\sum_{r\in R}x_{rt}\leq c_t\quad \forall t\in T,\qquad
x_{rt}=0\ \mathrm{if}\ E(r,t)=0,
$$

where $c_t$ is tutor capacity in the batch/time period. The Hungarian
algorithm handles one-to-one assignment; min-cost max-flow or a general
integer optimizer can represent capacities and additional constraints.
Unmatched requests are valid outcomes; do not lower eligibility standards
merely to force every request into a match.

Illustrative Python using SciPy (for optimization experiments, not a runtime
dependency decision):

```python
import numpy as np
from scipy.optimize import linear_sum_assignment

def assign_batch(requests, tutors, score_pair, eligible):
    # Add one zero-score "unmatched" column per request, so no request
    # is forced into an ineligible or negative-value match.
    scores = np.zeros((len(requests), len(tutors) + len(requests)))
    scores[:, :len(tutors)] = -1e6

    for i, request in enumerate(requests):
        for j, tutor in enumerate(tutors):
            if eligible(request, tutor):
                scores[i, j] = score_pair(request, tutor)

    row_indices, col_indices = linear_sum_assignment(scores, maximize=True)
    return [
        (requests[i], tutors[j])
        for i, j in zip(row_indices, col_indices)
        if j < len(tutors) and scores[i, j] > 0
    ]
```

This illustrative solver assumes each tutor may be assigned once in the
batch. For $c_t>1$, expand tutor capacity into assignment slots or use a
min-cost-flow formulation. Ensure assignments are committed transactionally
with capacity rechecks to prevent concurrent requests from overbooking.

#**Approach C**: *study-group formation as constrained optimization*

For groups, first generate eligible candidate sets, then optimize composition.
Let $G$ be a proposed group with one or more tutors and a bounded number of
tutees. A simple objective can balance request coverage, schedule overlap,
useful skill complementarity, and group compatibility:

$$
Q(G) =
\alpha C_{\mathrm{coverage}}(G)
+ \beta C_{\mathrm{schedule}}(G)
+ \gamma C_{\mathrm{scaffold}}(G)
+ \delta C_{\mathrm{preference}}(G)
- \lambda C_{\mathrm{conflict}}(G),
$$

subject to minimum/maximum group size, subject/topic compatibility, at least
one eligible tutor, participant consent, and no schedule conflict. The terms
and coefficients require domain review and validation.

One explicit heterogeneity measure for a categorical proficiency band $k$
with group proportion $p_k$ is Shannon entropy:

$$
H(G)=-\sum_k p_k\log p_k.
$$

If heterogeneity is desired, one candidate objective is to reward appropriate
spread while also requiring at least one member with sufficient proficiency:

$$
Q_{\mathrm{skill}}(G)
= \eta H(G) + \rho\cdot\mathbf{1}[
\max_{u\in G}p_u \geq p_{\mathrm{support}}],
$$

where $p_{\mathrm{support}}$ is a reviewed tutor qualification threshold.
Entropy alone is not a tutoring-quality metric: a diverse group can lack
someone able to help, and larger groups can mechanically increase entropy.
Normalize or compare only groups of the same size, and test the measure with
educators and students. The proposal mentions minimum-entropy heterogeneous
grouping as related work; the objective above is a possible PAL-specific
formulation, **not** a reproduction of that paper's method.

For small candidate pools, enumerate feasible groups and rank them. For larger
instances, consider integer linear programming, constraint programming, or
min-cost flow with carefully defined constraints. Begin with a transparent
heuristic if the pilot scale does not justify a solver.

**Approach D**: *recommender/hybrid approach (later phase)*

A content-based recommender can use explicit user-request attributes and tutor
subject profiles to rank candidates. A hybrid can later incorporate
opt-in, aggregated session feedback or successful pair history. This should
only follow adequate data collection, consent, and an evaluation plan.

Do not begin with collaborative filtering or a machine-learned ranker:
the proposal supplies no historical interaction dataset, ground-truth labels,
or sample-size basis for training one. A small or biased pilot can make a
learned model less fair and less interpretable than a rule-based baseline.
Never infer sensitive traits or use message contents as recommendation
features without an explicit, reviewed research and privacy basis.

**Fairness, transparency, and student safeguards**

- Use subject, topic, schedule, proficiency relevant to the request, and
  voluntary learning preferences only for stated matching purposes.
- Do not use protected/sensitive attributes as ranking shortcuts. If
  demographic variables are needed to audit group fairness, keep the audit
  separated from individual ranking and obtain appropriate approval.
- Measure availability and match outcomes by relevant cohorts only under
  approved research/privacy rules; report aggregate counts and uncertainty.
- Make "no suitable match yet" available rather than silently relaxing hard
  constraints.
- Provide understandable reasons from actual score components (e.g. subject
  fit, overlap, proficiency support). Do not expose private profile details or
  label candidates using psychological/personality judgments.
- WordClouds can illustrate aggregate terms only if the underlying text is
  appropriate and de-identified. For individual explanations, structured
  reason codes are more reliable, accessible, and less likely to leak
  information.
- Keep a human support/reporting path for inappropriate behavior and
  accessibility needs; automated ranking must not block a student from
  requesting help.

**Evaluation plan (consistent with the proposal)**

The proposal's planned Phase 1 survey measures perceived matching effectiveness
using profile alignment, compatibility, and similarity after students explore
or simulate matches. That measures **user perception**, not objective accuracy
or learning gains.

For a stronger evaluation, log an algorithm version and score components, and
predefine:

1. **Feasibility:** proportion of requests with at least one eligible candidate.
2. **Availability:** proportion of recommendations that can actually be
   scheduled.
3. **Acceptance:** accepted recommendations / recommendations shown, with
   declined and unresponded separated.
4. **Session completion:** completed sessions / accepted recommendations.
5. **Perceived match quality:** the planned questionnaire constructs,
   reported separately rather than collapsing them prematurely.
6. **Exposure/fairness checks:** tutor workload distribution and match
   availability by approved cohorts; avoid small-cell disclosure.
7. **Baseline comparison:** compare the weighted baseline with a simple
   subject-plus-schedule heuristic or shuffled feasible baseline in offline
   simulation; only make claims supported by the study design.

Do not call acceptance rate "accuracy": a user's choice depends on availability,
timing, trust, and other factors. Do not claim learning efficacy from a
descriptive one-month study without an appropriate learning outcome and
comparison design.

**Recommended implementation sequence**

1. Agree on the operational proficiency scale and verified/declared status.
2. Define hard eligibility constraints, tutor capacity, one-to-one/group rules,
   and what schedule overlap means.
3. Implement and version a deterministic, explainable weighted-ranking
   baseline; persist score components and explanation reasons.
4. Validate sample recommendations with students/educators before exposing
   automatic acceptance.
5. Add batch assignment or group optimization only when workload, capacity,
   and group constraints demand it.
6. Evaluate against prespecified usability, acceptance, feasibility, and
   fairness measures. Revise the proposal's contradictory hypothesis and
   clarify its sampling plan before inferential claims.
7. Consider feedback-driven personalization only after sufficient,
   consented, quality-checked data exists.

**Current database support and gaps**

The current v1 schema has fields that could store a first baseline:

- `tutor_subjects.proficiency_level` and `verification_status`;
- `learning_requests.current_proficiency`, `subject_id`, `topic_id`, and
  `preferred_format`;
- `availability_slots`;
- `match_recommendations.algorithm_version`, `match_score`,
  `score_components`, and `explanation`.

The schema does not yet define proficiency-level semantics, scoring weights,
availability overlap queries, tutor capacity, group match membership, explicit
compatibility/preference features, fairness audit records, or target
recommendation count. These belong in the algorithm/API design or a later
migration after the research team settles those requirements. Do not add
arbitrary weights to the schema.

**Provisional implementation now in** `/connect`

The REST module implements Approach A as a draft baseline in
`server/connect/src/matching.js`:

- It has two directions over the same scorer: tutee request to tutor candidates
  and tutor to open tutee requests.
- Eligibility requires role, active/open state, exact subject/topic
  compatibility, a higher tutor proficiency, compatible session format, and
  overlapping UTC availability.
- Prototype proficiency is currently constrained to integers 1–5.
- The draft score uses weights 0.35 proficiency gap, 0.35 availability, 0.20
  Jaccard similarity over explicit preference tags, and 0.10 format fit.
- Availability contribution reaches its maximum at 30 overlapping minutes.
- The endpoint is stateless: it scores only supplied input, does not query or
  persist database records, reserve capacity, or authenticate user ownership.
  It is an internal integration prototype until account authentication and
  server-side candidate loading are wired in.

These are engineering defaults for testing the REST shape, **not research
findings or validated weights**. Global assignment, tutor capacity handling,
and group formation remain unimplemented until their constraints and policies
are agreed. The routes are `POST /connect/match` (tutee request to tutors) and
`POST /connect/tutorreco` (tutor to open tutee requests); both call
the same scorer. Do not use prototype scores as calibrated probabilities or
claim that a high score establishes learning effectiveness.

## References

### Proposal

Group 2. (2026, May). *Feasibility of PALs: An Algorithmic Peer-Assisted
Learning Web Application Among Senior High School Students at STI College
Cubao* (defended revised proposal), Chapters 1–2, especially pp. 3–8,
12–17, and 22–23. Local source:
[`../pdf/Group2-Hunter_PR1_Chapter1-2_ProposalDefendedRevised.pdf`](../pdf/Group2-Hunter_PR1_Chapter1-2_ProposalDefendedRevised.pdf).

### Matching/grouping sources cited in that proposal

Full citation details are transcribed from the proposal's reference list; DOI
or publication details marked incomplete in the proposal should be checked:

- Chen, L., & Pu, P. (2023). Matching learners in peer learning environments
  using recommender systems. *Computers & Education: Artificial Intelligence,
  4*, 100120. https://doi.org/10.1016/j.caeai.2023.100120
- Doroudi, S., Holstein, K., Aleven, V., & Brunskill, E. (2023). Three
  algorithms for grouping students: A bridge between personalized tutoring
  system data and classroom pedagogy. *International Journal of Artificial
  Intelligence in Education, 33*, 843–884.
  https://doi.org/10.1007/s40593-022-00309-y
- Kanika, Chakraverty, S., Chakraborty, P., & Madan, M. (2023). Effect of
  different grouping arrangements on students' achievement and experience in
  collaborative learning environment. *Interactive Learning Environments,
  31*(10), 6366–6378.
  https://doi.org/10.1080/10494820.2022.2036764
- Liang, C., Majumdar, R., Nakamizo, Y., Flanagan, B., & Ogata, H. (2022).
  Algorithmic group formation and group work evaluation in a learning
  analytics-enhanced environment: Implementation study in a Japanese junior
  high school. *Interactive Learning Environments*, 1–24. DOI incomplete in
  proposal.
- Vallès-Català, T., & Palau, R. (2023). Minimum entropy collaborative
  groupings: A tool for an automatic heterogeneous learning group formation.
  *PLOS ONE, 18*(1), e0280604. DOI link incomplete in proposal.
