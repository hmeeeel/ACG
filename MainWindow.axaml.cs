using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace ACG;

public partial class MainWindow : Window
{
    private readonly AvaloniaRender _avRender = new(1280, 1024);
    private readonly Render         _render;
    private          ObjModel?      _model;
    private readonly OrbitCamera    _camera = new();

    private TextureMap? _diffuseTex;
    private TextureMap? _normalTex;
    private TextureMap? _specularTex;

    private readonly LightingManager _lightingManager = new();

    private bool           _dragging;
    private Avalonia.Point _lastMouse;

    private int  _rendering = 0;
    private int  _uiPending = 0;
    private volatile bool _dirty = false;
    public const float PI = 3.14159274f;
    private readonly System.Diagnostics.Stopwatch _fpsWatch =
        System.Diagnostics.Stopwatch.StartNew();
    private int  _frameCount = 0;
    private bool _filledMode = false;

    private LightSettings _light = LightSettings.Default;
    private bool _settingLightPosition = false;

    public MainWindow()
    {
        InitializeComponent();

        _render = new Render(_avRender);
        RenderCanvas.Source = _avRender.FrontBitmap;

        LoadButton.Click         += OnLoadClick;
        LoadTexturesButton.Click += OnLoadTexturesClick;
        ResetButton.Click        += OnResetClick;

        FilledButton.IsCheckedChanged += (_, _) =>
        {
            _filledMode = FilledButton.IsChecked ?? false;
            FilledButton.Content = _filledMode ? "Заполненный" : "Каркас";
            RequestRender();
        };

        LightTypeCombo.SelectionChanged += OnLightTypeChanged;

        CanvasBorder.PointerPressed += OnPointerPressed;
        CanvasBorder.PointerReleased += OnPointerReleased;
        CanvasBorder.PointerMoved   += OnPointerMoved;
        CanvasBorder.PointerWheelChanged += (_, e) =>
        {
            _camera.Zoom((float)e.Delta.Y);
            RequestRender();
        };

        KeyDown += OnKeyDown;

        SetShadingMode(ShadingMode.PhongBlinn);
        SetTexMode(TextureMode.None);
        UpdateTextureIndicators();
        RequestRender();
    }

    private void OnLightTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (LightTypeCombo.SelectedIndex < 0) return;

        var lightType = (LightType)LightTypeCombo.SelectedIndex;
        
        LightSource newLight = lightType switch
        {
            LightType.Directional => CreateDirectional(),
            LightType.Point       => CreatePoint(),
            LightType.Spot        => CreateSpot(),
            _ => throw new ArgumentOutOfRangeException(nameof(lightType), lightType, null)
        };

        DirectionalLight CreateDirectional() => new(0.577f, 0.816f, 0.577f) 
        { 
            Color = new Vec3(1f, 1f, 1f), 
            Intensity = 1f 
        };

        PointLight CreatePoint() => new(0f, 3f, 3f) 
        { 
            Color = new Vec3(1f, 0.9f, 0.8f), 
            Intensity = 10f 
        };

        SpotLight CreateSpot()
        {
            var spot = SpotLight.FromDegrees(
                position: new Vec3(0f, 3f, 3f),
                direction: new Vec3(0f, -1f, 0f),
                innerAngleDegrees: 15f,
                outerAngleDegrees: 25f);
            spot.Color = new Vec3(1f, 1f, 0.9f);
            spot.Intensity = 20f;
            spot.Falloff = 1.5f;
            return spot;
        }

        _lightingManager.ClearLights();
        _lightingManager.AddLight(newLight);
        
        StatusText.Text = $"Выбран: {GetLightTypeName(lightType)}";
        RequestRender();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(CanvasBorder);
        
        if (point.Properties.IsRightButtonPressed)
        {
            _settingLightPosition = true;
            SetLightFromScreenPosition(e.GetPosition(CanvasBorder));
            e.Handled = true;
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            _dragging  = true;
            _lastMouse = e.GetPosition(CanvasBorder);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragging = false;
        _settingLightPosition = false;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_settingLightPosition)
        {
            SetLightFromScreenPosition(e.GetPosition(CanvasBorder));
            return;
        }

        if (!_dragging) return;
        var pos   = e.GetPosition(CanvasBorder);
        var delta = pos - _lastMouse;
        _lastMouse = pos;
        _camera.Rotate((float)delta.X, (float)delta.Y);
        RequestRender();
    }

    //свет  в экранных координатах
    private void SetLightFromScreenPosition(Avalonia.Point screenPos)
    {
        if (_lightingManager.ActiveLight == null) return;

        // Конверт в мировое направление
        float aspect = (float)_avRender.Width / _avRender.Height;
        float ndcX = ((float)screenPos.X / (float)CanvasBorder.Bounds.Width) * 2f - 1f;
        float ndcY = (1f - (float)screenPos.Y / (float)CanvasBorder.Bounds.Height) * 2f - 1f;

        // Создаём луч из камеры через клик
        Vec3 eye = _camera.EyePosition();
        Vec3 target = _camera.Target;
        Vec3 forward = (target - eye).Normalized();
        Vec3 right = Vec3.Cross(forward, new Vec3(0f, 1f, 0f)).Normalized();
        Vec3 up = Vec3.Cross(right, forward).Normalized();

        float tanHalfFov = float.Tan((PI / 3f) * 0.5f);
        Vec3 rayDir = (forward 
                      + right * (ndcX * tanHalfFov * aspect) 
                      + up * (ndcY * tanHalfFov)).Normalized();

        var light = _lightingManager.ActiveLight;

        switch (light.Type)
        {
            case LightType.Directional:
                ((DirectionalLight)light).Direction = -rayDir;
                break;

            case LightType.Point:
                ((PointLight)light).Position = eye + rayDir * 5f;
                break;

            case LightType.Spot:
                var spot = (SpotLight)light;
                spot.Position = eye + rayDir * 5f;
                spot.Direction = (target - spot.Position).Normalized();
                break;
        }

       // DrawLightGizmo();
        RequestRender();
    }


    private void DrawLightGizmo()
    {
        LightGizmoCanvas.Children.Clear();

        var light = _lightingManager.ActiveLight;
        if (light == null) return;

        Vec3? worldPos = light.Type switch
        {
            LightType.Point => ((PointLight)light).Position,
            LightType.Spot => ((SpotLight)light).Position,
            _ => null
        };

        if (worldPos == null) return;

        Matrix44 view = _camera.ViewMatrix();
        float aspect = (float)_avRender.Width / _avRender.Height;
        Matrix44 proj = Matrix44.Perspective(PI / 3f, aspect, 0.3f, 50f);
        Matrix44 vp = proj * view;

        Vec4 clipPos = vp.Multiply(new Vec4(worldPos.Value, 1f));
        if (clipPos.W <= 0) return;

        Vec3 ndc = clipPos.PerspectiveDivide();
        float screenX = ((ndc.X + 1f) * 0.5f) * (float)CanvasBorder.Bounds.Width;
        float screenY = ((1f - ndc.Y) * 0.5f) * (float)CanvasBorder.Bounds.Height;

        var ellipse = new Ellipse
        {
            Width = 20,
            Height = 20,
            Fill = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
            Stroke = new SolidColorBrush(Colors.White),
            StrokeThickness = 2
        };
        Canvas.SetLeft(ellipse, screenX - 10);
        Canvas.SetTop(ellipse, screenY - 10);
        LightGizmoCanvas.Children.Add(ellipse);
    }


    private string GetLightTypeName(LightType type) => type switch
    {
        LightType.Directional => "Направленный свет",
        LightType.Point => "Точечный свет",
        LightType.Spot => "Прожектор",
        _ => "Неизвестный"
    };

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:  _camera.Rotate(-20f, 0f); RequestRender(); break;
            case Key.Right: _camera.Rotate( 20f, 0f); RequestRender(); break;
            case Key.Up:    _camera.Rotate(0f, -20f); RequestRender(); break;
            case Key.Down:  _camera.Rotate(0f,  20f); RequestRender(); break;
            case Key.D1:    _camera.Zoom( 1f);        RequestRender(); break;
            case Key.D2:    _camera.Zoom(-1f);        RequestRender(); break;

            case Key.L: SetShadingMode(ShadingMode.Lambert);    break;
            case Key.G: SetShadingMode(ShadingMode.Gouraud);    break;
            case Key.B: SetShadingMode(ShadingMode.PhongBlinn); break;
            case Key.P: SetShadingMode(ShadingMode.Phong);      break;
            case Key.A: SetShadingMode(ShadingMode.Ambient);    break;
            case Key.D: SetShadingMode(ShadingMode.Diffuse);    break;
            case Key.S: SetShadingMode(ShadingMode.Specular);   break;

            case Key.T:  SetTexMode(TextureMode.Diffuse);  break;
            case Key.N:  SetTexMode(TextureMode.Normal);   break;
            case Key.M:  SetTexMode(TextureMode.Specular); break;
            case Key.O:  SetTexMode(TextureMode.All);      break;
            case Key.D0: SetTexMode(TextureMode.None);     break;

            case Key.F1: LightTypeCombo.SelectedIndex = 0; break;
            case Key.F2: LightTypeCombo.SelectedIndex = 1; break;
            case Key.F3: LightTypeCombo.SelectedIndex = 2; break;
        }
    }

    private void SetShadingMode(ShadingMode mode)
    {
        _light.Mode = mode;
        ModeText.Text = mode switch
        {
            ShadingMode.Lambert    => "Lambert",
            ShadingMode.Gouraud    => "Gouraud",
            ShadingMode.PhongBlinn => "Blinn-Phong",
            ShadingMode.Phong      => "Phong",
            ShadingMode.Ambient    => "Ambient",
            ShadingMode.Diffuse    => "Diffuse",
            ShadingMode.Specular   => "Specular",
            _                      => "?"
        };
        RequestRender();
    }

    private void SetTexMode(TextureMode mode)
    {
        _light.TexMode = mode;
        TexModeText.Text = mode switch
        {
            TextureMode.None     => "Нет",
            TextureMode.Diffuse  => "Diffuse",
            TextureMode.Normal   => "Normal",
            TextureMode.Specular => "Specular",
            TextureMode.All      => "Все",
            _                    => "?"
        };
        RequestRender();
    }

    private void RequestRender()
    {
        _dirty = true;
        if (Interlocked.CompareExchange(ref _rendering, 1, 0) == 0)
        {
            _dirty = false;
            ScheduleRenderTask(
                _camera.EyePosition(), _camera.Target, _model,
                _diffuseTex, _normalTex, _specularTex);
        }
    }

    private void ScheduleRenderTask(
        Vec3 eye, Vec3 target, ObjModel? model,
        TextureMap? diff, TextureMap? norm, TextureMap? spec)
    {
        var activeLight = _lightingManager.ActiveLight;
        var ambientColor = _lightingManager.AmbientColor;
        
        Task.Run(() =>
        {
            RenderFrame(eye, target, model, diff, norm, spec, activeLight, ambientColor);
            _avRender.SwapBuffers();

            _frameCount++;
            if (_fpsWatch.ElapsedMilliseconds >= 500)
            {
                double fps = _frameCount * 1000.0 / _fpsWatch.ElapsedMilliseconds;
                _frameCount = 0;
                _fpsWatch.Restart();
                Dispatcher.UIThread.Post(() => 
                {
                    FpsText.Text = $"{fps:F0} fps";
                   // DrawLightGizmo();
                });
            }

            Interlocked.Exchange(ref _rendering, 0);
            if (_dirty)
            {
                _dirty = false;
                if (Interlocked.CompareExchange(ref _rendering, 1, 0) == 0)
                    ScheduleRenderTask(
                        _camera.EyePosition(), _camera.Target, _model,
                        _diffuseTex, _normalTex, _specularTex);
            }

            if (Interlocked.CompareExchange(ref _uiPending, 1, 0) == 0)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Interlocked.Exchange(ref _uiPending, 0);
                    RenderCanvas.InvalidateVisual();
                }, DispatcherPriority.Render);
            }
        });
    }

    private void RenderFrame(
        Vec3 eye, Vec3 target, ObjModel? model,
        TextureMap? diff, TextureMap? norm, TextureMap? spec,
        LightSource? light, Vec3 ambientColor)
    {
        Matrix44 modelMat = Matrix44.Identity();
        Matrix44 view     = Matrix44.LookAt(eye, target, new Vec3(0f, 1f, 0f));
        float    aspect   = (float)_avRender.Width / _avRender.Height;
        Matrix44 proj     = Matrix44.Perspective(PI / 3f, aspect, 0.3f, 50f);

        if (_filledMode)
            _render.DrawFilled(model, modelMat, view, proj, eye, _light,
                               diff, norm, spec, light, ambientColor);
        else
            _render.DrawWireframe(model, modelMat, view, proj);
    }


    private async void OnLoadClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title          = "Открыть .obj",
            AllowMultiple  = false,
            FileTypeFilter = [new FilePickerFileType("OBJ") { Patterns = ["*.obj"] }]
        });
        if (files.Count == 0) return;
        try
        {
_model          = new Parser().Parse(files[0].Path.LocalPath);
    StatusText.Text = $" {System.IO.Path.GetFileName(files[0].Path.LocalPath)}";
    ResetView();
        }
        catch (Exception ex) { StatusText.Text = $" {ex.Message}"; }
    }

    private async void OnLoadTexturesClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title          = "Загрузить текстуры",
            AllowMultiple  = true,
            FileTypeFilter = [new FilePickerFileType("Image") 
                { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp"] }]
        });

        if (files.Count == 0) return;

        int loaded = 0;
        var loadedTypes = new List<string>();

        try
        {
            foreach (var file in files)
            {
                string filename = System.IO.Path.GetFileNameWithoutExtension(file.Path.LocalPath).ToLowerInvariant();
                string fullPath = file.Path.LocalPath;

                if (IsDiffuse(filename))
                {
                    _diffuseTex = TextureMap.Load(fullPath);
                    if (_diffuseTex != null) { loaded++; loadedTypes.Add("Diffuse"); }
                }
                else if (IsNormal(filename))
                {
                    _normalTex = TextureMap.Load(fullPath);
                    if (_normalTex != null) { loaded++; loadedTypes.Add("Normal"); }
                }
                else if (IsSpecular(filename))
                {
                    _specularTex = TextureMap.Load(fullPath);
                    if (_specularTex != null) { loaded++; loadedTypes.Add("Specular"); }
                }
            }

            UpdateTextureIndicators();

            if (loaded > 0)
            {
                StatusText.Text = $" {string.Join(", ", loadedTypes)}";
                RequestRender();
            }
            else
            {
                StatusText.Text = " Текстуры не распознаны";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $" {ex.Message}";
        }
    }

    private void UpdateTextureIndicators()
    {
        if (_diffuseTex != null)
        {
            DiffuseIndicator.Text = "+";
            DiffuseIndicator.Foreground = new SolidColorBrush(Color.Parse("#66FF66"));
        }
        else
        {
            DiffuseIndicator.Text = "-";
            DiffuseIndicator.Foreground = new SolidColorBrush(Color.Parse("#444466"));
        }

        if (_normalTex != null)
        {
            NormalIndicator.Text = "+";
            NormalIndicator.Foreground = new SolidColorBrush(Color.Parse("#6666FF"));
        }
        else
        {
            NormalIndicator.Text = "-";
            NormalIndicator.Foreground = new SolidColorBrush(Color.Parse("#444466"));
        }

        if (_specularTex != null)
        {
            SpecularIndicator.Text = "+";
            SpecularIndicator.Foreground = new SolidColorBrush(Color.Parse("#FFAA66"));
        }
        else
        {
            SpecularIndicator.Text = "-";
            SpecularIndicator.Foreground = new SolidColorBrush(Color.Parse("#444466"));
        }
    }

    private static bool IsDiffuse(string name) =>
        name.Contains("diffuse") || name.Contains("diff") || 
        name.Contains("albedo") || name.Contains("color") || name.Contains("base");

    private static bool IsNormal(string name) =>
        name.Contains("normal") || name.Contains("norm") || 
        name.Contains("nm") || name.Contains("bump");

    private static bool IsSpecular(string name) =>
        name.Contains("specular") || name.Contains("spec") || 
        name.Contains("gloss") || name.Contains("rough");

    private void OnResetClick(object? sender, RoutedEventArgs e) => ResetView();

    private void ResetView()
    {
        _camera.Reset();
        RequestRender();
    }
}