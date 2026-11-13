namespace SnapBar.Core;

public enum OnboardingStatus { Presented, Skipped, Completed }
public sealed record OnboardingState(
    [property: System.Text.Json.Serialization.JsonRequired] int Version,
    [property: System.Text.Json.Serialization.JsonRequired] OnboardingStatus Status);
public enum OnboardingLoadKind { Missing, Valid, Unavailable, Invalid, Unsupported }
public sealed record OnboardingLoadResult(OnboardingLoadKind Kind, OnboardingState? State = null);

public static class OnboardingPolicy
{
    public static bool ShouldOffer(bool background, bool settingsExistedAtStartup,
        bool settingsReadable, OnboardingLoadResult onboarding) =>
        !background && !settingsExistedAtStartup && settingsReadable
        && onboarding.Kind == OnboardingLoadKind.Missing;
}
