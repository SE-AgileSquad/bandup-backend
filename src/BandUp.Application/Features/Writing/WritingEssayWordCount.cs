using BandUp.Domain.Writing;

namespace BandUp.Application.Features.Writing;

public static class WritingEssayWordCount
{
    public static bool IsValid(string? essay) => EssayPolicy.IsValid(essay);
    public static int Count(string? essay) => EssayPolicy.Count(essay);
}
