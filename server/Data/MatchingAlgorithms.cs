namespace Palsys.Server.Data;

public sealed record MatchingAlgorithmDefinition(string Id, string Name, string Description, bool IsDefault);

public sealed record MatchingPair(
    long LearningRequestId,
    long TuteeUserId,
    string TuteeDisplayName,
    int? TuteeGrade,
    string? TuteeStrand,
    int? CurrentProficiency,
    string? RequestedHelp,
    long TutorUserId,
    string TutorDisplayName,
    int? TutorGrade,
    string? TutorStrand,
    int TutorProficiency,
    string TutorVerificationStatus,
    string Subject);

public sealed record MatchingScore(double Value, IReadOnlyDictionary<string, double?> Components, string Explanation);

public sealed record MatchingRecommendation(
    long RequestId,
    long CandidateUserId,
    string CandidateName,
    string CandidateRole,
    string Subject,
    string? RequestedHelp,
    int? LearnerProficiency,
    int TutorProficiency,
    int? Grade,
    string? Strand,
    string TutorVerificationStatus,
    double Score,
    IReadOnlyDictionary<string, double?> ScoreComponents,
    string Explanation);

public sealed class MatchingAlgorithmRegistry
{
    public const string WeightedMultiCriteria = "weighted-multi-criteria";
    public const string ZpdGap = "zpd-gap";

    private static readonly IReadOnlyList<MatchingAlgorithmDefinition> Definitions =
    [
        new(WeightedMultiCriteria, "Weighted multi-criteria scoring", "Default 1-on-1 ranker using proficiency gap, strand, and grade.", true),
        new(ZpdGap, "ZPD-gap matching", "Ranks candidates by the tutor-to-learner proficiency gap.", false),
    ];

    private int activeAlgorithmIndex;

    public IReadOnlyList<MatchingAlgorithmDefinition> Algorithms => Definitions;

    public MatchingAlgorithmDefinition ActiveAlgorithm => Definitions[Volatile.Read(ref activeAlgorithmIndex)];

    public bool TrySetActive(string? algorithmId)
    {
        var index = algorithmId is null
            ? -1
            : Definitions.ToList().FindIndex(item => item.Id.Equals(algorithmId, StringComparison.Ordinal));
        if (index < 0) return false;
        Interlocked.Exchange(ref activeAlgorithmIndex, index);
        return true;
    }
}

public static class MatchingScorer
{
    private const double ProficiencyWeight = 0.70;
    private const double StrandWeight = 0.20;
    private const double GradeWeight = 0.10;

    public static MatchingScore? Score(MatchingPair pair, string algorithmId)
    {
        var gap = pair.CurrentProficiency is int current ? pair.TutorProficiency - current : (int?)null;
        if (gap is <= 0) return null;

        var proficiencyScore = gap is int proficiencyGap
            ? Math.Clamp(100d - Math.Abs(proficiencyGap - 1) * 25d, 0d, 100d)
            : (double?)null;

        if (algorithmId == MatchingAlgorithmRegistry.ZpdGap)
        {
            if (proficiencyScore is not double zpdScore) return null;
            return new MatchingScore(
                zpdScore,
                new Dictionary<string, double?> { ["proficiencyGap"] = zpdScore },
                $"Exact subject match; tutor proficiency is {gap} level(s) above the learner baseline, with a one-level gap preferred.");
        }

        if (algorithmId != MatchingAlgorithmRegistry.WeightedMultiCriteria)
            throw new ArgumentOutOfRangeException(nameof(algorithmId), algorithmId, "The matching algorithm is not supported.");

        var components = new Dictionary<string, double?>();
        var weightedTotal = 0d;
        var availableWeight = 0d;

        AddComponent("proficiencyGap", proficiencyScore, ProficiencyWeight);

        var strandScore = !string.IsNullOrWhiteSpace(pair.TuteeStrand) && !string.IsNullOrWhiteSpace(pair.TutorStrand)
            ? (string.Equals(pair.TuteeStrand, pair.TutorStrand, StringComparison.OrdinalIgnoreCase) ? 100d : 0d)
            : (double?)null;
        AddComponent("strand", strandScore, StrandWeight);

        var gradeScore = pair.TuteeGrade.HasValue && pair.TutorGrade.HasValue
            ? (pair.TutorGrade >= pair.TuteeGrade ? 100d : 0d)
            : (double?)null;
        AddComponent("gradeLevel", gradeScore, GradeWeight);

        var score = availableWeight == 0 ? 0 : weightedTotal / availableWeight;
        return new MatchingScore(
            score,
            components,
            "Exact subject match; weighted score uses proficiency gap (70%), same strand (20%), and tutor grade at least the learner's (10%), renormalized over available profile data.");

        void AddComponent(string key, double? value, double weight)
        {
            components[key] = value;
            if (value is not double componentScore) return;
            weightedTotal += componentScore * weight;
            availableWeight += weight;
        }
    }
}
