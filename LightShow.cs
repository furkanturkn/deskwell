namespace Deskwell;

public static class LightShow
{
    private const int FlashCount = 6;
    private const int StepDelayMilliseconds = 450;
    private const int HueStepDegrees = 60;
    private const int HueOffsetPerLightDegrees = 180;
    private const int FullCircleDegrees = 360;
    private const double FullSaturation = 1.0;
    private const byte ShowBrightness = HueProtocol.MaximumBrightness;

    public static async Task PlayCelebration(IReadOnlyList<HueLight> lights)
    {
        var snapshots = new List<(HueLight Light, LightSnapshot Snapshot)>();
        foreach (var light in lights)
        {
            if (light.CaptureSnapshot() is { } snapshot)
            {
                snapshots.Add((light, snapshot));
            }
        }

        await Task.WhenAll(lights.Select(light => LightCommands.RunSafely(light, () => light.SetBrightness(ShowBrightness))));

        for (var step = 0; step < FlashCount * 2; step++)
        {
            var currentStep = step;
            await Task.WhenAll(lights.Select((light, index) => LightCommands.RunSafely(light, () => PlayStep(light, index, currentStep))));
            await Task.Delay(StepDelayMilliseconds);
        }

        await Task.WhenAll(snapshots.Select(entry => LightCommands.RunSafely(entry.Light, () => entry.Light.Restore(entry.Snapshot))));
    }

    private static async Task PlayStep(HueLight light, int lightIndex, int step)
    {
        var isLitStep = step % 2 == 0;
        if (!isLitStep)
        {
            await light.SetPower(false);
            return;
        }

        var hue = (step / 2 * HueStepDegrees + lightIndex * HueOffsetPerLightDegrees) % FullCircleDegrees;
        await light.SetColor(hue, FullSaturation);
        await light.SetPower(true);
    }
}
