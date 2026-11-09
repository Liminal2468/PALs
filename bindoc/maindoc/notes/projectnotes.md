# PALs Research Notes

## Source and status

- Source: [Group2-Hunter_PR1_Chapter1-2_ProposalDefendedRevised.pdf](../../pdf/Group2-Hunter_PR1_Chapter1-2_ProposalDefendedRevised.pdf)
- Proposal title: **Feasibility of PALs: An Algorithmic Peer-Assisted Learning Web Application Among Senior High School Students at STI College Cubao**
- Presented to the Senior High School Department of STI College Cubao for Practical Research 1 (Applied 1013); dated May 2026.
- The PDF contains Chapters 1 and 2, the initial interface blueprint, and references. Treat its contents as a defended/revised proposal, not as evidence that the system, algorithm, or evaluations have already been completed.
- The notes below distinguish what the proposal says from implementation implications and items that need clarification. Bibliographic references have not been independently verified.

## Executive summary

The study proposes a browser-based peer-assisted learning (PAL) platform for STI College Cubao senior high school students. It aims to make peer tutoring and study-group formation more structured and accessible than informal arrangements by allowing students to find peers based on academic need, subject proficiency, schedule, and learner characteristics.

The pedagogical rationale is reciprocal: tutees receive explanations from peers, while tutors reinforce their own understanding by teaching. The proposed matching approach is multi-attribute and heterogeneous rather than random or based only on friendship. The system is to be evaluated for usability, perceived usefulness, matching, and software quality, with a second, voluntary one-month phase that observes usage and asks about sustained engagement and perceived learning outcomes.

The study is scoped to the STI College Cubao SHS population and is planned for academic year 2026–2027. It is quantitative and descriptive-developmental; the described evaluation does not establish causal learning gains or prove the algorithm is objectively superior to another matching method.

## Context and problem

- The target community is STI College Cubao SHS students across academic and technical-professional strands and grade levels.
- Students have differing subjects, proficiency, learning preferences, schedules, and academic needs.
- The proposal identifies the lack of a centralized way to find appropriate study partners or tutors, and notes that students may interact mainly within their existing social circles.
- Informal peer support can therefore be hard to access consistently. The proposed web app is intended to structure access to tutoring and group learning beyond those immediate circles.
- The platform is supplemental support, not a replacement for teachers or the school's eLMS.

## Objectives and research questions

The broad objective is to design a functional, effective, and accepted PAL web application for STI College Cubao SHS students.

The proposal asks:

1. What is the app's level of functionality as evaluated by SHS students in terms of:
   - interface usability;
   - matching accuracy; and
   - system performance, including efficiency and reliability?
2. What is the level of user acceptance in terms of:
   - perceived usefulness; and
   - perceived ease of use?
3. How effective is the matching algorithm at pairing tutors and tutees based on:
   - profile alignment;
   - compatibility; and
   - similarity?

### Hypothesis as written

The document labels this as H₀: “The developed Peer-Assisted Learning Web Application will significantly meet the acceptability criteria ... and will not be accepted by users.” This sentence is internally contradictory: it asserts both meeting acceptability criteria and not being accepted. No clear alternative hypothesis or testable null hypothesis is supplied. See the clarification list below before using this as an analysis plan.

## Theoretical and conceptual framework

### Zone of Proximal Development (ZPD)

- Based on Vygotsky (1978): learning can progress through guided interaction with a more capable peer, within the space between what a learner can do independently and what they can do with support.
- In the proposal's tutoring model, a tutor acts as the more knowledgeable peer and scaffolds the tutee's progress.
- Matching should consider a useful subject-proficiency difference so that the pair can exchange knowledge rather than simply duplicate ability.
- Support is expected to recede as the tutee develops competence. Session feedback is proposed as one input to future match quality.
- Peer learning is bidirectional: tutors can deepen mastery through teaching, while tutees receive personalized, lower-pressure explanations.

### Technology Acceptance Model (TAM)

- Based on Davis (1989), with later references to Teo (2011) and Venkatesh and Bala (2008).
- The two central constructs are perceived usefulness and perceived ease of use. The proposal treats these as important to students' adoption of the platform.
- Interface clarity, useful matches, scheduling, and feedback should support both constructs.

### Conceptual model described in the proposal

- Theories: ZPD and TAM inform system design and evaluation.
- Matching inputs: schedule, subject, and role.
- Platform features: registration and matching; the scope elsewhere also includes profiles, scheduling, and feedback.
- Evaluation reference: ISO/IEC 25010:2023 is described as an external quality-evaluation standard.
- Users: tutors and tutees have a reciprocal relationship.
- Process outcome: peer-interaction quality.
- Expected outcomes: scaffolded learning and user acceptance.

## Related-literature themes reported by the proposal

These are the proposal's synthesis of cited works, not independently revalidated findings:

- Structured peer-assisted learning is described as beneficial across academic performance, communication, self-efficacy, engagement, and self-regulated learning. Tutors and tutees may both benefit.
- Formal roles and guided interactions are presented as more dependable than unstructured peer arrangements.
- Local and secondary-school examples are used to support reciprocal tutoring and collaborative learning, including mathematics and reading comprehension.
- Grouping students by learning goals and learner data is presented as potentially more useful than random or teacher-only grouping.
- Heterogeneous groups and complementary skills are proposed to reduce redundancy and support knowledge exchange.
- Recommender systems and optimization/grouping approaches are cited as precedent for multi-variable learner matching.
- TAM is used to justify measuring usefulness and ease of use, while ISO/IEC 25010:2023 is proposed as a software-product-quality reference.
- The claimed research gap is the lack of a study integrating ZPD-informed peer tutoring, automated group matching, TAM-informed adoption, and ISO/IEC 25010:2023 evaluation for Filipino SHS students.
- The proposal mentions algorithmic fairness and interpretability, including WordCloud visualizations to explain group formation. It does not define a fairness metric, a word-cloud design, or a validation protocol; these are ideas to clarify, not established requirements.

## Proposed product scope and user experience

### Core scope

- Student registration and profile creation.
- Subject- and topic-based tutor/tutee and study-group matching.
- Session scheduling.
- User feedback mechanisms.
- Browser-based access on devices with a web browser; no native mobile app is proposed.
- Integration with the STI College Cubao eLMS for materials, resources, subjects, lessons, and courses.
- Only STI College Cubao eLMS school accounts are intended to be accepted for user validation. The blueprint gives `@cubao.sti.edu.ph` as the account domain.
- The study is limited to STI College Cubao SHS students in both academic and technical-professional strands.

### Blueprint flow and interface ideas

1. User logs in or registers with an STI College Cubao eLMS account.
2. Terms and conditions are presented after account confirmation and before entering the site.
3. Users complete further profile personalization before matching or other features are accessible.
4. The home page is envisioned to expose current tutors, scheduled sessions, resources, and analytics.
5. A header provides branding, quick-access actions, and a light/dark mode toggle.
6. A sidebar is envisioned for chat rooms, detailed statistics, achievements, resources, other users, settings, and profile.
7. Chat distinguishes study-group and one-to-one sessions.
8. The blueprint proposes AI monitoring of messages to flag profanity, personal information, and ill-intended behavior. The proposal does not specify the moderation model, its accuracy, review process, retention, or appeal path.

### Conduct and safety constraints in the proposal

- Interactions are intended to be academic only.
- Personal information, profanity, and discouraged behavior are to be filtered and flagged for investigation.
- Users are to receive terms and conditions and be asked for permissions.
- Phase 2 passive analytics requires separate, explicit, voluntary informed consent.
- Logs should not collect personally identifying behavioral information beyond what is necessary for session-level analysis.

## Research design and participants

- Design: quantitative descriptive-developmental.
- Developmental component: design, create, and implement the web app.
- Descriptive component: evaluate the developed system through structured questionnaires.
- The study explicitly says it does not aim to test causal relationships or compare control groups; evaluation is after implementation in a controlled or simulated environment.
- Population: STI College Cubao senior high school students; the future population is estimated to be under 1,000, but the exact academic-year 2026–2027 count is not yet known.
- Strata are principally grade level and strand; demographic sections are to collect stratum-identifying information.
- The proposal cites Slovin's formula, `n = N / (1 + N e²)`, with `e = 0.05`, and says it will determine sample size per stratum.
- It also states a minimum of 30 respondents per applicable stratum, citing Gay et al. (2009), while smaller strata may fall short and be interpreted cautiously.
- Phase 1 distribution is intended to reach as many available students as possible, followed by post-stratification of responses.
- Phase 2 is a voluntary subset of Phase 1 respondents who separately consent to a one-month monitored evaluation. Attrition is acknowledged.

## Instruments and evaluation procedure

### Phase 1: initial evaluation

- Respondents interact with the app, including browsing, matching functions, and simulated tutor-tutee pairings.
- Questionnaire 1 is completed after initial interaction.
- Parts/constructs:
  1. respondent profile, grade and strand for post-stratification;
  2. usability: navigation, interface clarity, and overall user experience;
  3. perceived effectiveness and satisfaction: usefulness, reliability, and acceptability;
  4. matching effectiveness: compatibility, alignment, and similarity using such attributes as strand, subject need, and learning preferences.
- Each item uses a four-point Likert scale: 4 strongly agree, 3 agree, 2 disagree, 1 strongly disagree. There is no neutral option.

### Phase 2: one-month monitored evaluation

- Only Phase 1 respondents may be invited; enrollment is separate, voluntary, and requires explicit informed consent.
- The proposal says participants will be expected to use the platform for a set average amount of time per week but does not state the actual usage target.
- The analytics module is to record login frequency, features accessed per session, average session duration, tutor-matching attempts, and peer-interaction frequency.
- Users below a minimum usage threshold are to be categorized as low engagement and either analyzed separately or excluded. The threshold and this decision are not specified and should be fixed before data collection.
- Questionnaire 2 is administered at the end of the month. It covers:
  1. respondent profile/stratum confirmation;
  2. habitual use and sustained engagement;
  3. long-term usability and feature satisfaction;
  4. perceived learning outcomes;
  5. two or three identically worded bridging items on satisfaction/usefulness for comparison with Questionnaire 1.
- It uses the same four-point scale.

### Validity and reliability plans

- ICT instructors or the research adviser review content validity and alignment with study objectives.
- A small group of SHS students outside the study respondents review face validity and clarity.
- A pilot is planned; Cronbach's alpha is to be calculated per construct and for each questionnaire. Alpha of at least 0.70 is treated as acceptable in the proposal. Low item-total correlation prompts review or removal.
- Bridging-item response consistency is to be examined as an additional check.
- Analytics validity is to be established through functional verification that events are logged accurately, completely, and consistently and map to the intended behavioral measures.
- The proposal considers analytics reliability deterministic, while requiring technical failures or data gaps to be documented.

### Data gathering and analysis

- Phase 1: stabilize and internally test the system; coordinate respondent recruitment through faculty; invite broad participation across strands and grade levels; respondents explore core features; administer and collect Questionnaire 1; encode and post-stratify responses.
- Phase 2: invite Phase 1 respondents; obtain separate consent; passively monitor usage for one month; administer Questionnaire 2; combine survey and analytics data for analysis.
- The stated survey statistic is weighted mean: `Σ(f × x) / N`, where `f` is frequency for a response value, `x` is the Likert score, and `N` is the number of respondents.
- Each criterion is to be analyzed separately; an overall system result is a general weighted mean over items.
- Interpretation bands in the proposal:
  - 3.26–4.00: Strongly Agree / Highly Positive Evaluation
  - 2.51–3.25: Agree / Positive Evaluation
  - 1.76–2.50: Disagree / Negative Evaluation
  - 1.00–1.75: Strongly Disagree / Highly Negative Evaluation
- The proposal does not specify inferential tests, confidence intervals, missing-data handling, weighting formula for post-stratification, or the exact computation of matching accuracy.

## Matching-algorithm implications

The research points toward a multi-attribute, explainable match rather than a single-factor recommendation. Candidate attributes named in the proposal include:

- subject and topic requested;
- the tutee's academic need/current proficiency;
- tutor proficiency or capability in the requested subject;
- availability/schedule;
- role (tutor/tutee);
- academic strand and grade level;
- learning preferences;
- compatibility, collaboration, and communication characteristics.

The intended pedagogical match is complementary: the tutor should be more capable in the relevant subject/topic, but the proposal does not define an ideal proficiency gap, eligibility threshold, scoring formula, weights, tie-breaking, group size, tutor capacity, or fallback when there is no suitable match. These need to be researched and approved before implementing the matching logic.

For defensible evaluation, distinguish:

- **Profile alignment**: whether objective user attributes and request constraints match.
- **Compatibility**: whether users can feasibly and appropriately work together (e.g. schedule and stated collaboration preferences).
- **Similarity**: which common traits are deliberately beneficial; maximizing similarity everywhere could conflict with the proposal's goal of heterogeneous skill pairing.
- **Matching accuracy/effectiveness**: define observable ground truth or a transparent user-rated proxy; a survey perception score alone does not establish algorithmic accuracy or learning impact.
- **Fairness and explanation**: define protected/sensitive attributes, group-level checks, explanation format, and human review before claiming fairness. Avoid exposing student-level sensitive attributes in public explanations.

## Items to resolve before finalizing implementation or analysis

1. **Correct the hypothesis.** The current H₀ contradicts itself and mixes acceptance with system quality. Decide whether the study is purely descriptive (in which case a null hypothesis may not be needed) or specify a coherent, testable H₀/H₁ and analysis.
2. **Align research questions, framework, instruments, and standards.** ISO/IEC 25010:2023 appears in the framework and rationale, but the described survey constructs are not mapped to its characteristics, and the methodology does not identify a standard-based instrument. Clarify which quality characteristics are actually evaluated and how.
3. **Resolve the sampling design.** “Post-stratified sampling,” broad distribution to available students, probability/random sampling, proportional representation, Slovin's formula per stratum, and a minimum 30 per stratum are described together but are not clearly one coherent procedure. Confirm whether recruitment is a census/open invitation, probability sample, or convenience/volunteer sample; define any random selection and post-stratification weights.
4. **Clarify the population and sample calculation.** Confirm the actual 2026–2027 population counts by grade/strand and whether the sample-size calculation is overall or separately per stratum. Do not treat undersized strata as statistical outliers without a preplanned rationale.
5. **Operationalize Phase 2.** Specify the minimum weekly use, low-engagement threshold, treatment of participants below it, attrition reporting, and whether survey analytics will be linked at a pseudonymous participant level.
6. **Define matching evaluation.** Specify score/labels for accuracy, how profile alignment/compatibility/similarity differ, whether evaluation is expert-, user-, or outcome-based, and what baseline (if any) is used. The current design does not compare the algorithm to a control or alternative.
7. **Bound learning-effect claims.** Self-reported perceived learning after one month is not equivalent to measured academic achievement. A causal claim would need a different design and appropriate outcome measures.
8. **Set privacy and safeguarding details.** Define data minimization, consent/withdrawal, retention/deletion, access controls, pseudonymous analytics, incident review, and who reviews moderation flags. Participants are school-age students; confirm school approval and appropriate consent/assent processes before collecting data.
9. **Clarify eLMS integration and identity verification.** The proposal specifies eLMS accounts and the `@cubao.sti.edu.ph` domain but not an approved authentication protocol, data access, or integration agreement. A domain check alone would not demonstrate account ownership.
10. **Scope AI moderation.** Decide whether moderation is actually in scope for the study build; specify what is detected, the false-positive/false-negative handling, human review, data retention, and safeguards. Do not promise that it can prevent all harmful content.
11. **Validate the references.** Several bibliography entries have incomplete or generic links (e.g. “Doi.org”), and some citation-to-claim fit needs checking. In particular, verify source details and the claims attributed to Karamimehr et al. (2023), Yin et al. (2024), market-size projections, and the claimed matching/grouping findings before publication.
12. **Fix document-level drafting issues.** The scope has “survey.a”; sampling text has “ost-stratified”; and wording around “random selection” conflicts with broad availability-based distribution. Check all edits against the defended version before revising.

## Current Mentos implementation context (separate from proposal claims)

The existing project context has a modular API skeleton. The following is a possible mapping from proposal features to those API areas, not a statement that these features are already implemented or that the proposal prescribes this architecture:

- `/auth/v1` and `/acc`: school-account authentication and student profile lifecycle.
- `/connect`: tutor/tutee and study-group recommendations; matching algorithm remains to be researched.
- `/sched`: session availability and scheduling.
- `/fb`: post-session feedback and match-quality signals.
- `/chat/v1`: one-to-one and study-group communication.
- `/adata`: privacy-conscious, consent-gated research usage measures.
- `/db`: persistence behind the database API.

The proposal's eLMS-only identity requirement and monitoring consent should inform later requirements. Do not infer the final login flow, matching score, research schema, analytics retention, or moderation technology from the current API skeleton.

## Literature and reference roster in the proposal

The following authors/works appear in the proposal's review or bibliography. The short topic labels below are orientation only; confirm the original publications before relying on exact claims.

- Abadiano & Gatcho (2022) — differentiated instruction in Philippine SHS.
- Abdulkarim et al. (2022) — peer learning and mathematics performance.
- Abran et al. (2003) — usability concepts in ISO standards.
- Alismaiel, Cifuentes-Faura & Al-Rahmi (2022) — educational social media and TAM.
- Ariningsih & Muhammad (2024) — ticketing-system quality evaluation using ISO/IEC 25010:2023 and AHP.
- Creswell & Creswell (2018) — quantitative and mixed-method research design.
- Chen & Pu (2023) — recommender systems for matching peer learners.
- Cheng (2022) — extending TAM for educational platforms.
- Davis (1989) — perceived usefulness, ease of use, and technology acceptance.
- Department of Education (2022) — learning continuity / post-pandemic interventions.
- Department of Education (2025) — strengthened SHS program.
- Doroudi, Holstein, Aleven & Brunskill (2023) — algorithms for grouping students.
- Feng et al. (2024) — peer-assisted learning in health-professional education.
- Fernandez-Martin, Arco-Tirado & Hervas-Torres (2022) — peer tutoring and self-regulated learning.
- Gay, Mills & Airasian (2009) — educational research and survey sampling guidance.
- ISO (2023) — ISO/IEC 25010:2023 product quality model.
- Kanika et al. (2023) — grouping arrangements and collaborative learning.
- Karamimehr et al. (2023) — personalized emotion-aware e-learning systems.
- Li, Wang & Wu (2025) — meta-analysis of PAL in medical education.
- Liang et al. (2022) — algorithmic group formation in a learning-analytics environment.
- Lohr (2022) — sampling design and analysis.
- Pitolan (2023) — STAD cooperative learning among SHS students.
- Robison (2022) — peer tutoring and Grade 10 reading comprehension.
- Sarmiento & Pacleb (2023) — learning styles and academic performance of Filipino SHS students.
- Saunders, Lewis & Thornhill (2019) — research methods for business students.
- Teo (2011) — factors influencing teachers' technology-use intention.
- Topping (1996, 2005) — peer tutoring effectiveness and peer-learning trends.
- Torres & Caballes (2022) — group learning and student heterogeneity in Philippine classrooms.
- Vallès-Català & Palau (2023) — minimum-entropy heterogeneous group formation.
- Venkatesh & Bala (2008) — TAM3 and intervention research agenda.
- Vygotsky (1978) — *Mind in Society* and ZPD foundation.
- Wahyuningsih & Retnawati (2019) — learning-media development using ISO 9126.
- Yin, Chng & Chiu (2024) — collaborative learning in K–12 computational-thinking education.
- Zhang & Maconochie (2022) — meta-analysis of PAL in clinical knowledge and skills.

## Proposal authors

The cover lists De Guzman, Vhinze Jarelle Ramirez; Egenias, Darniel Prince Joe Angeles; Mabunga, Stephen Bolonias; Mosante, Prince Alfred Almonte; Nicart, Nesabel Bardon; Piol, Edzel Jhon Bernardo; and Suarez, Aldred Bitua.
