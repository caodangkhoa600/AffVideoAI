namespace AffiVideo.Domain;

/// <summary>How a single Scene is produced.</summary>
public enum Technique
{
    /// <summary>A photo held still.</summary>
    StaticImage,

    /// <summary>A photo that is scaled, moved, rotated or faded, and never repainted.</summary>
    ImageMotion,

    /// <summary>A video generated from a photo. Generative.</summary>
    ImageToVideo,

    /// <summary>A video the Organization supplied.</summary>
    VideoAsset,

    /// <summary>Type that moves.</summary>
    TextAnimation,

    /// <summary>A render of a 3D model.</summary>
    ThreeDRender,
}

/// <summary>The per-video choice that limits which Techniques the planning engine may use.</summary>
public enum RenderMode
{
    /// <summary>No generative Techniques, so the Product's appearance is never altered.</summary>
    ProductLock,

    /// <summary>Generative Techniques are allowed alongside the others.</summary>
    Hybrid,
}

public static class RenderModes
{
    /// <summary>Product Lock allows static image, image motion and text animation, and nothing else. Hybrid allows every Technique.</summary>
    public static bool Allows(this RenderMode renderMode, Technique technique) =>
        renderMode == RenderMode.Hybrid
        || technique is Technique.StaticImage or Technique.ImageMotion or Technique.TextAnimation;
}

/// <summary>
/// The planning engine: a rules-based function from the available assets, the
/// creative template, the duration and the Render Mode to a Technique per Scene.
/// </summary>
public static class PlanningEngine
{
    /// <summary>The shortest Scene in which a move of the Product can be seen as one.</summary>
    public const int MinImageMotionMilliseconds = 1500;

    /// <summary>
    /// One Technique for each Scene of the template, in order: the first of the
    /// Scene's own preferences that the Render Mode allows and that can be made
    /// from what there is.
    /// </summary>
    public static IReadOnlyList<Technique> AssignTechniques(
        int usablePhotos, CreativeTemplateDefinition template, int targetDurationSeconds, RenderMode renderMode)
    {
        var durations = template.SceneDurations(targetDurationSeconds);
        return template.Scenes.Select((scene, index) => TechniqueFor(scene, usablePhotos, durations[index], renderMode)).ToArray();
    }

    /// <summary>The Technique for one Scene of a template that lasts this long, chosen the same way.</summary>
    public static Technique TechniqueFor(SceneSlot scene, int usablePhotos, int sceneMilliseconds, RenderMode renderMode) =>
        scene.Techniques
            .Where(technique => renderMode.Allows(technique) && CanBeMade(technique, usablePhotos, sceneMilliseconds))
            // Type needs nothing but the text, so it is what is left when nothing else can be made.
            .DefaultIfEmpty(Technique.TextAnimation)
            .First();

    private static bool CanBeMade(Technique technique, int usablePhotos, int sceneMilliseconds) => technique switch
    {
        Technique.StaticImage => usablePhotos > 0,
        Technique.ImageMotion => usablePhotos > 0 && sceneMilliseconds >= MinImageMotionMilliseconds,
        Technique.TextAnimation => true,
        // No provider generates video or renders 3D yet, and nothing stores a video
        // asset, so these are never chosen in either Render Mode.
        _ => false,
    };
}
