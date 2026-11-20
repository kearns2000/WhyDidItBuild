namespace WhyDidItBuild.Core.Comparison;

public static class Repetition
{
    public const string Label = ReasonText.RepeatedInTwoBuilds;

    public static BuildReport Apply(BuildReport latest, BuildReport? previousComparable)
    {
        if (!latest.Comparable || previousComparable is not { Comparable: true })
        {
            return latest with
            {
                Projects = latest.Projects.Select(project => project with { RepeatedLabel = "" }).ToList()
            };
        }

        var previousKeys = previousComparable.Projects
            .Select(project => project.RepeatKey)
            .Where(key => key.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var projects = latest.Projects.Select(project =>
        {
            var repeated = project.RepeatKey.Length > 0 && previousKeys.Contains(project.RepeatKey);
            return project with { RepeatedLabel = repeated ? Label : "" };
        }).ToList();

        return latest with { Projects = projects };
    }
}
