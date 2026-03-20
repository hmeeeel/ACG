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
    private readonly AvaloniaRender _avRender = new(800, 600);
    private readonly Render         _render;
    private          ObjModel?      _model;
    private readonly OrbitCamera    _camera = new();

    private bool           _dragging;
    private Avalonia.Point _lastMouse;
    
    private int _rendering = 0;
    private int _uiPending = 0; 
    private volatile bool _dirty = false;

    private readonly System.Diagnostics.Stopwatch _fpsWatch =  System.Diagnostics.Stopwatch.StartNew();
    private int _frameCount = 0;
    private bool _filledMode = false;
    
    private LightSettings _light = LightSettings.Default;
    public MainWindow()
    {
        InitializeComponent();

        _render = new Render(_avRender);

        RenderCanvas.Source = _avRender.FrontBitmap;

        LoadButton.Click  += OnLoadClick;
        ResetButton.Click += OnResetClick;

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

        CanvasBorder.PointerMoved += (_, e) =>
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
            }
        };

        RequestRender();
    }

    private void RequestRender()
    {
        _dirty = true;

        if (Interlocked.CompareExchange(ref _rendering, 1, 0) == 0)
        {
            _dirty = false;
            ScheduleRenderTask(_camera.EyePosition(), _camera.Target, _model);
        }
    }

    private void ScheduleRenderTask(Vec3 eye, Vec3 target, ObjModel? model)
    {
        Task.Run(() =>
        {
            RenderFrame(eye, target, model);
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
                });
            }

            Interlocked.Exchange(ref _rendering, 0);
            if (_dirty)
            {
                _dirty = false;
                if (Interlocked.CompareExchange(ref _rendering, 1, 0) == 0)
                {
                    ScheduleRenderTask(_camera.EyePosition(), _camera.Target, _model);
                }
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

    private void RenderFrame(Vec3 eye, Vec3 target, ObjModel? model)
    {
        Matrix44 modelMat = Matrix44.Identity();
        Matrix44 view     = Matrix44.LookAt(eye, target, new Vec3(0f, 1f, 0f));

        float    aspect = (float)_avRender.Width / _avRender.Height;
        Matrix44 proj   = Matrix44.Perspective(MathF.PI / 3f, aspect, 0.1f, 100f);
        Vec3 lightColor = new Vec3(1f, 1f, 1f);
        if (_filledMode)
           _render.DrawFilled(model, modelMat, view, proj, eye, _light);
            else
            _render.DrawWireframe(model, modelMat, view, proj);
    }

    private async void OnLoadClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title          = "Открыть .obj",
            AllowMultiple  = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("OBJ") { Patterns = new[] { "*.obj" } }
            }
        });

        if (files.Count == 0) return;

        try
        {
            _model          = new Parser().Parse(files[0].Path.LocalPath);
            StatusText.Text = Path.GetFileName(files[0].Path.LocalPath);
            ResetView();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Ошибка: {ex.Message}";
        }
    }

    private void OnResetClick(object? sender, RoutedEventArgs e) => ResetView();

    private void ResetView()
    {
        _camera.Reset();
        RequestRender();
    }
}