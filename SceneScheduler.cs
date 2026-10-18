namespace HueDesk;

public sealed class SceneScheduler : IDisposable
{
    private const int CheckIntervalSeconds = 30;

    private readonly AppSettings settings;
    private readonly Func<IEnumerable<HueLight>> connectedLights;
    private readonly Func<bool> isPaused;
    private readonly HashSet<HueLight> lightsInCurrentScene = [];
    private readonly System.Windows.Forms.Timer timer = new() { Interval = CheckIntervalSeconds * 1000 };
    private string? trackedSceneName;
    private bool isCurrentSceneAlreadyOnLights;

    public SceneScheduler(AppSettings settings, Func<IEnumerable<HueLight>> connectedLights, Func<bool> isPaused)
    {
        this.settings = settings;
        this.connectedLights = connectedLights;
        this.isPaused = isPaused;
        timer.Tick += (_, _) =>
        {
            ApplyDue();
            Ticked?.Invoke();
        };
        timer.Start();
    }

    public event Action? Ticked;

    public Scene CurrentScene => Scenes.ScheduledAt(TimeOnly.FromDateTime(DateTime.Now));

    public bool IsEnabled
    {
        get => settings.AutoScenesEnabled;
        set
        {
            settings.AutoScenesEnabled = value;
            settings.LastAppliedSceneName = null;
            settings.Save();
            trackedSceneName = null;
            ApplyDue();
        }
    }

    public void ApplyDue()
    {
        if (!IsEnabled || isPaused())
        {
            return;
        }

        var scene = EnterCurrentScene();
        var lightsToUpdate = connectedLights().Where(light => lightsInCurrentScene.Add(light)).ToList();
        if (lightsToUpdate.Count == 0)
        {
            return;
        }

        RememberAppliedScene(scene);
        if (isCurrentSceneAlreadyOnLights)
        {
            return;
        }

        ActivityLog.Write($"Scene \"{scene.Name}\" applied to {lightsToUpdate.Count} light(s)");
        foreach (var light in lightsToUpdate)
        {
            _ = LightCommands.RunSafely(light, () => light.ApplyScene(scene));
        }
    }

    public void ApplyCurrentSceneNow()
    {
        EnterCurrentScene();
        isCurrentSceneAlreadyOnLights = false;
        lightsInCurrentScene.Clear();
        ApplyDue();
    }

    public void MarkCurrentSceneApplied(IEnumerable<HueLight> lights)
    {
        var scene = EnterCurrentScene();
        lightsInCurrentScene.UnionWith(lights);
        RememberAppliedScene(scene);
    }

    public void Forget(HueLight light)
    {
        lightsInCurrentScene.Remove(light);
    }

    public void Dispose()
    {
        timer.Dispose();
    }

    private Scene EnterCurrentScene()
    {
        var scene = CurrentScene;
        if (scene.Name == trackedSceneName)
        {
            return scene;
        }

        var isFirstEvaluationAfterStart = trackedSceneName is null;
        isCurrentSceneAlreadyOnLights = isFirstEvaluationAfterStart && settings.LastAppliedSceneName == scene.Name;
        trackedSceneName = scene.Name;
        lightsInCurrentScene.Clear();
        return scene;
    }

    private void RememberAppliedScene(Scene scene)
    {
        if (settings.LastAppliedSceneName == scene.Name)
        {
            return;
        }

        settings.LastAppliedSceneName = scene.Name;
        settings.Save();
    }
}
