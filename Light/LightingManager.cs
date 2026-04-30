using System.Collections.Generic;


public class LightingManager
{
    private readonly List<LightSource> _lights = new();
    public Vec3 AmbientColor { get; set; } = new Vec3(0.2f, 0.2f, 0.2f);
    public LightSource? ActiveLight { get; set; }

    public IReadOnlyList<LightSource> Lights => _lights;

    public LightingManager()
    {
        var defaultLight = new DirectionalLight(0.577f, -0.816f, 0.577f)
        {
            Color = new Vec3(1f, 1f, 1f),
            Intensity = 1f
        };
        _lights.Add(defaultLight);
        ActiveLight = defaultLight;
    }

    public void AddLight(LightSource light)
    {
        _lights.Add(light);
        ActiveLight = light;
    }

    public void RemoveLight(LightSource light)
    {
        _lights.Remove(light);
        if (ActiveLight == light)
            ActiveLight = _lights.Count > 0 ? _lights[0] : null;
    }

    public void ClearLights()
    {
        _lights.Clear();
        ActiveLight = null;
    }

    public Vec3 GetPrimaryLightDirection(Vec3 fragmentPos)
    {
        if (_lights.Count == 0)
            return new Vec3(0f, 1f, 0f);
        
        return _lights[0].GetLightDirection(fragmentPos);
    }

    public void SetupThreePointLighting(Vec3 targetPos)
    {
        ClearLights();

        var keyLight = new DirectionalLight(0.5f, 0.7f, 0.5f)
        {
            Color = new Vec3(1f, 0.95f, 0.9f),
            Intensity = 1.2f
        };
        AddLight(keyLight);

        var fillLight = new DirectionalLight(-0.5f, 0.3f, -0.3f)
        {
            Color = new Vec3(0.8f, 0.9f, 1f),
            Intensity = 0.5f
        };
        AddLight(fillLight);

        var backLight = new DirectionalLight(0f, 0.5f, -1f)
        {
            Color = new Vec3(1f, 1f, 1f),
            Intensity = 0.8f
        };
        AddLight(backLight);

        ActiveLight = keyLight;
    }
}