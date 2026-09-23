namespace Poches.Services;

/// <summary>Choices offered when creating a pocket, picked to stay readable under white text.</summary>
public static class Palette
{
    public static readonly IReadOnlyList<string> Colors =
    [
        "#6366F1", "#8B5CF6", "#EC4899", "#F43F5E", "#F97316", "#D97706",
        "#10B981", "#0D9488", "#0891B2", "#3B82F6", "#65A30D", "#475569",
    ];

    public static readonly IReadOnlyList<string> Emojis =
    [
        "💰", "🛟", "📈", "🏖️", "✈️", "🏠",
        "🚗", "🎁", "🎓", "🏥", "💍", "👶",
        "🐶", "🎮", "💻", "📱", "🛒", "🍽️",
        "🏋️", "🎸", "🌱", "🎉", "⛰️", "🪙",
    ];

    public static Color Soft(Color color) => color.WithAlpha(0.16f);

    /// <summary>Gradient used behind white text on a pocket's hero card.</summary>
    public static Brush HeroBrush(Color color) => new LinearGradientBrush(
        [new GradientStop(color.AddLuminosity(-0.02f), 0f), new GradientStop(color.AddLuminosity(-0.16f), 1f)],
        new Point(0, 0), new Point(1, 1));

    public static void Haptic()
    {
        try
        {
            HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        }
        catch (Exception)
        {
            // Haptics are a nicety: never let a missing capability break an action.
        }
    }
}
