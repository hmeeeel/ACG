using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
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

    public MainWindow()
    {
        InitializeComponent();

        _render = new Render(_avRender);
        RenderCanvas.Source = _avRender.FrontBitmap;

        LoadButton.Click         += OnLoadClick;
        LoadDiffuseButton.Click  += OnLoadDiffuseClick;
        LoadNormalButton.Click   += OnLoadNormalClick;
        LoadSpecularButton.Click += OnLoadSpecularClick;
        ResetButton.Click        += OnResetClick;

        FilledButton.IsCheckedChanged += (_, _) =>
        {
            _filledMode = FilledButton.IsChecked ?? false;
            FilledButton.Content = _filledMode ? "Заполненный" : "Каркас";
            RequestRender();
        };

        CanvasBorder.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(CanvasBorder).Properties.IsLeftButtonPressed)
            {
                _dragging  = true;
                _lastMouse = e.GetPosition(CanvasBorder);
            }
        };
        CanvasBorder.PointerReleased += (_, _) => _dragging = false;
        CanvasBorder.PointerMoved   += (_, e) =>
        {
            if (!_dragging) return;
            var pos   = e.GetPosition(CanvasBorder);
            var delta = pos - _lastMouse;
            _lastMouse = pos;
            _camera.Rotate((float)delta.X, (float)delta.Y);
            RequestRender();
        };
        CanvasBorder.PointerWheelChanged += (_, e) =>
        {
            _camera.Zoom((float)e.Delta.Y);
            RequestRender();
        };

        KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Avalonia.Input.Key.Left:  _camera.Rotate(-20f, 0f); RequestRender(); break;
                case Avalonia.Input.Key.Right: _camera.Rotate( 20f, 0f); RequestRender(); break;
                case Avalonia.Input.Key.Up:    _camera.Rotate(0f, -20f); RequestRender(); break;
                case Avalonia.Input.Key.Down:  _camera.Rotate(0f,  20f); RequestRender(); break;
                case Avalonia.Input.Key.D1:    _camera.Zoom( 1f);        RequestRender(); break;
                case Avalonia.Input.Key.D2:    _camera.Zoom(-1f);        RequestRender(); break;

                case Avalonia.Input.Key.L: SetShadingMode(ShadingMode.Lambert);    break;
                case Avalonia.Input.Key.G: SetShadingMode(ShadingMode.Gouraud);    break;
                case Avalonia.Input.Key.B: SetShadingMode(ShadingMode.PhongBlinn); break;
                case Avalonia.Input.Key.P: SetShadingMode(ShadingMode.Phong);      break;
                case Avalonia.Input.Key.A: SetShadingMode(ShadingMode.Ambient);    break;
                case Avalonia.Input.Key.D: SetShadingMode(ShadingMode.Diffuse);    break;
                case Avalonia.Input.Key.S: SetShadingMode(ShadingMode.Specular);   break;

                case Avalonia.Input.Key.T:    SetTexMode(TextureMode.Diffuse);  break;
                case Avalonia.Input.Key.N:    SetTexMode(TextureMode.Normal);   break;
                case Avalonia.Input.Key.M:    SetTexMode(TextureMode.Specular); break;
                case Avalonia.Input.Key.O:    SetTexMode(TextureMode.All);      break;
                case Avalonia.Input.Key.D0:   SetTexMode(TextureMode.None);     break;
            }
        };

        RequestRender();
    }

    private void SetShadingMode(ShadingMode mode)
    {
        _light.Mode = mode;
        ModeText.Text = mode switch
        {
            ShadingMode.Lambert    => "Ламберт [L]",
            ShadingMode.Gouraud    => "Гуро [G]",
            ShadingMode.PhongBlinn => "Блинн-Фонг [B]",
            ShadingMode.Phong      => "Фонг [P]",
            ShadingMode.Ambient    => "Ambient [A]",
            ShadingMode.Diffuse    => "Diffuse [D]",
            ShadingMode.Specular   => "Specular [S]",
            _                      => "?"
        };
        RequestRender();
    }

    private void SetTexMode(TextureMode mode)
    {
        _light.TexMode = mode;
        TexModeText.Text = mode switch
        {
            TextureMode.None     => "Нет [0]",
            TextureMode.Diffuse  => "Diffuse [T]",
            TextureMode.Normal   => "Normal [N]",
            TextureMode.Specular => "Specular [M]",
            TextureMode.All      => "Все [O]",
            _                    => "?"
        };

        bool warn = mode switch
        {
            TextureMode.Diffuse  => _diffuseTex  is null,
            TextureMode.Normal   => _normalTex   is null,
            TextureMode.Specular => _specularTex is null,
            TextureMode.All      => _diffuseTex is null && _normalTex is null
                                    && _specularTex is null,
            _                    => false
        };
        if (warn) StatusText.Text = " Текстура не загружена";

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
        Task.Run(() =>
        {
            RenderFrame(eye, target, model, diff, norm, spec);
            _avRender.SwapBuffers();

            _frameCount++;
            if (_fpsWatch.ElapsedMilliseconds >= 500)
            {
                double fps = _frameCount * 1000.0 / _fpsWatch.ElapsedMilliseconds;
                _frameCount = 0;
                _fpsWatch.Restart();
                Dispatcher.UIThread.Post(() => FpsText.Text = $"{fps:F0} fps");
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
        TextureMap? diff, TextureMap? norm, TextureMap? spec)
    {
        Matrix44 modelMat = Matrix44.Identity();
        Matrix44 view     = Matrix44.LookAt(eye, target, new Vec3(0f, 1f, 0f));
        float    aspect   = (float)_avRender.Width / _avRender.Height;
        Matrix44 proj     = Matrix44.Perspective(PI / 3f, aspect, 1.0f, 100f);

        if (_filledMode)
            _render.DrawFilled(model, modelMat, view, proj, eye, _light,
                               diff, norm, spec);
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
            StatusText.Text = Path.GetFileName(files[0].Path.LocalPath);
            ResetView();
        }
        catch (Exception ex) { StatusText.Text = $"Ошибка: {ex.Message}"; }
    }

    private async void OnLoadDiffuseClick(object? sender, RoutedEventArgs e)
    {
        var tex = await LoadTexture("Загрузить диффузную карту");
        if (tex is null) return;
        _diffuseTex = tex;
        StatusText.Text = "Diffuse загружена";
        RequestRender();
    }

    private async void OnLoadNormalClick(object? sender, RoutedEventArgs e)
    {
        var tex = await LoadTexture("Загрузить карту нормалей");
        if (tex is null) return;
        _normalTex = tex;
        StatusText.Text = "Normal map загружена";
        RequestRender();
    }

    private async void OnLoadSpecularClick(object? sender, RoutedEventArgs e)
    {
        var tex = await LoadTexture("Загрузить зеркальную карту");
        if (tex is null) return;
        _specularTex = tex;
        StatusText.Text = "Specular загружена";
        RequestRender();
    }

    private async Task<TextureMap?> LoadTexture(string title)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title          = title,
            AllowMultiple  = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Image")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp"]
                }
            ]
        });
        if (files.Count == 0) return null;
        try   { return TextureMap.Load(files[0].Path.LocalPath); }
        catch (Exception ex) { StatusText.Text = $"Ошибка: {ex.Message}"; return null; }
    }

    private void OnResetClick(object? sender, RoutedEventArgs e) => ResetView();

    private void ResetView()
    {
        _camera.Reset();
        RequestRender();
    }
}