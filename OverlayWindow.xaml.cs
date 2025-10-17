using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;
using System.IO;
using System.Windows.Media;
using System.Collections.Generic;

namespace RatPet;

public class VisualHost : FrameworkElement
{
    private readonly VisualCollection _children;
    public VisualHost()
    {
        _children = new VisualCollection(this);
        IsHitTestVisible = false;
    }
    public void AddVisual(Visual visual) => _children.Add(visual);
    public void RemoveVisual(Visual visual) => _children.Remove(visual);
    protected override int VisualChildrenCount => _children.Count;
    protected override Visual GetVisualChild(int index) => _children[index];
}

public partial class OverlayWindow : Window
{
    private readonly DispatcherTimer _timer = new();
    private bool _toyActive;
    private System.Windows.Point _toyPos;
    private System.Windows.Vector _toyVel;
    private double _toySpin;
    private bool _dragging;
    private System.Windows.Point _dragStart;
    private DateTime _dragStartTime;
    private BitmapSource? _footprintBmp;
    private readonly Random _rng = new();
    // stage: 0,1,2 discrete fade levels; we only redraw when stage changes
    private readonly List<(DrawingVisual vis, DateTime created, TimeSpan life, double x, double y, double angle, double scale, int stage)> _footprints = new();
    private readonly Stack<DrawingVisual> _footprintPool = new();

    public event Action<System.Windows.Point>? ToyMoved; // screen coords
    private bool _followCursor;
    private System.Windows.Point _lastCursorPos;
    private DateTime _lastCursorTime;
    private bool _wasLeftDown;

    public OverlayWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            FitToVirtualDesktop();
            _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / 60);
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
            try { var bmp = new BitmapImage(new Uri("pack://application:,,,/footprint.png")); bmp.Freeze(); _footprintBmp = bmp; } catch { _footprintBmp = null; }
        };
        AllowDrop = true;
        DragOver += OverlayWindow_DragOver;
        Drop += OverlayWindow_Drop;
    }

    public void ShowMeme(BitmapSource bmp, Rect screenEdgeRect, bool fromLeft)
    {
        MemeImage.Source = bmp;
        MemeImage.Visibility = Visibility.Visible;
        // Position just outside the edge and slide in a bit
        double targetWidth = Math.Min(300, bmp.PixelWidth);
        double targetHeight = Math.Min(300, bmp.PixelHeight);
        MemeImage.Width = targetWidth;
        MemeImage.Height = targetHeight;
        double startX = fromLeft ? screenEdgeRect.Left - targetWidth : screenEdgeRect.Right;
        double y = screenEdgeRect.Bottom - targetHeight;
        if (y < screenEdgeRect.Top) y = screenEdgeRect.Top;
        Canvas.SetLeft(MemeImage, startX - SystemParameters.VirtualScreenLeft);
        Canvas.SetTop(MemeImage, y - SystemParameters.VirtualScreenTop);
        // Simple slide animation using dispatcher timer for compatibility
        var start = DateTime.UtcNow;
        var duration = TimeSpan.FromMilliseconds(450);
        var endX = fromLeft ? screenEdgeRect.Left : screenEdgeRect.Right - targetWidth;
        var animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0/60) };
        animTimer.Tick += (_, _) =>
        {
            var t = (DateTime.UtcNow - start).TotalMilliseconds / duration.TotalMilliseconds;
            if (t >= 1)
            {
                Canvas.SetLeft(MemeImage, endX - SystemParameters.VirtualScreenLeft);
                animTimer.Stop();
                // auto-hide after a few seconds
                var hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
                hideTimer.Tick += (_, _) => { MemeImage.Visibility = Visibility.Collapsed; hideTimer.Stop(); };
                hideTimer.Start();
                return;
            }
            // ease-out
            double ease = 1 - Math.Pow(1 - t, 3);
            double x = startX + (endX - startX) * ease;
            Canvas.SetLeft(MemeImage, x - SystemParameters.VirtualScreenLeft);
        };
        animTimer.Start();
    }

    private void FitToVirtualDesktop()
    {
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    public void ShowToy(BitmapSource toyBitmap, double screenX, double screenY)
    {
        ToyImage.Source = toyBitmap;
        Canvas.SetLeft(ToyImage, screenX - SystemParameters.VirtualScreenLeft);
        Canvas.SetTop(ToyImage, screenY - SystemParameters.VirtualScreenTop);
        ToyImage.Visibility = Visibility.Visible;
        _toyPos = new System.Windows.Point(screenX, screenY);
        _toyActive = true;
    }

    public void MoveToy(double screenX, double screenY)
    {
        Canvas.SetLeft(ToyImage, screenX - SystemParameters.VirtualScreenLeft);
        Canvas.SetTop(ToyImage, screenY - SystemParameters.VirtualScreenTop);
        _toyPos = new System.Windows.Point(screenX, screenY);
        ToyMoved?.Invoke(_toyPos);
    }

    public void HideToy()
    {
        ToyImage.Visibility = Visibility.Collapsed;
        _toyActive = false;
        _followCursor = false;
    }

    public bool ToyVisible => ToyImage.Visibility == Visibility.Visible;
    public System.Windows.Point ToyPosition => _toyPos;
    public void ImpartVelocity(double vx, double vy)
    {
        _toyVel = new System.Windows.Vector(vx, vy);
        _followCursor = false;
        _toyActive = true;
        // ensure visible and inform listeners so the rat immediately reacts
        if (ToyImage.Visibility != Visibility.Visible) ToyImage.Visibility = Visibility.Visible;
        ToyMoved?.Invoke(_toyPos);
    }

    // Footprints
    public void AddFootprint(double screenX, double screenY, double angleDeg, double scale)
    {
        if (_footprintBmp == null) return;
        var vis = _footprintPool.Count > 0 ? _footprintPool.Pop() : new DrawingVisual();
        using (var dc = vis.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(-SystemParameters.VirtualScreenLeft, -SystemParameters.VirtualScreenTop));
            dc.PushTransform(new RotateTransform(angleDeg, screenX, screenY));
            double w = _footprintBmp.PixelWidth * scale;
            double h = _footprintBmp.PixelHeight * scale;
            Rect rect = new Rect(screenX - w / 2, screenY - h / 2, w, h);
            dc.DrawImage(_footprintBmp, rect);
        }
        FootprintLayer.AddVisual(vis);
        var created = DateTime.UtcNow;
        // Shorter lifetime when there are already many footprints to keep cost bounded
        double baseLife = 2.0;
        if (_footprints.Count > 24) baseLife = 1.4;
        if (_footprints.Count > 40) baseLife = 1.0;
        var lifetime = TimeSpan.FromSeconds(baseLife + _rng.NextDouble() * 0.5);
        _footprints.Add((vis, created, lifetime, screenX, screenY, angleDeg, scale, 0));
        const int maxFootprints = 32;
        if (_footprints.Count > maxFootprints)
        {
            var removeCount = _footprints.Count - maxFootprints;
            for (int i = 0; i < removeCount; i++)
            {
                var (oldVis, _, _, _, _, _, _, _) = _footprints[0];
                FootprintLayer.RemoveVisual(oldVis);
                if (_footprintPool.Count < 64) _footprintPool.Push(oldVis);
                _footprints.RemoveAt(0);
            }
        }
    }

    private void Tick()
    {
        // Update and fade footprints
        if (_footprints.Count > 0)
        {
            var now = DateTime.UtcNow;
            for (int i = _footprints.Count - 1; i >= 0; i--)
            {
                var (vis, created, life, x, y, angle, scale, stage) = _footprints[i];
                double t = (now - created).TotalMilliseconds / life.TotalMilliseconds;
                if (t >= 1)
                {
                    FootprintLayer.RemoveVisual(vis);
                    if (_footprintPool.Count < 64) _footprintPool.Push(vis);
                    _footprints.RemoveAt(i);
                }
                else
                {
                    // Step-wise fade to minimize redraws (only when stage changes)
                    int newStage;
                    if (t < 0.66) newStage = 0; // full
                    else if (t < 0.88) newStage = 1; // mid
                    else newStage = 2; // low
                    if (newStage != stage)
                    {
                        double opacity = newStage == 0 ? 0.9 : (newStage == 1 ? 0.6 : 0.3);
                        using (var dc = vis.RenderOpen())
                        {
                            dc.PushTransform(new TranslateTransform(-SystemParameters.VirtualScreenLeft, -SystemParameters.VirtualScreenTop));
                            dc.PushTransform(new RotateTransform(angle, x, y));
                            dc.PushOpacity(opacity);
                            double w = _footprintBmp!.PixelWidth * scale;
                            double h = _footprintBmp!.PixelHeight * scale;
                            Rect rect = new Rect(x - w / 2, y - h / 2, w, h);
                            dc.DrawImage(_footprintBmp, rect);
                        }
                        _footprints[i] = (vis, created, life, x, y, angle, scale, newStage);
                    }
                }
            }
        }
        if (_followCursor)
        {
            var mp = WinForms.Control.MousePosition;
            var now = DateTime.UtcNow;
            var cur = new System.Windows.Point(mp.X, mp.Y);
            if (_lastCursorTime != default)
            {
                var dt = Math.Max((now - _lastCursorTime).TotalSeconds, 0.016);
                _toyVel = new System.Windows.Vector((cur.X - _lastCursorPos.X) / dt * 0.02, (cur.Y - _lastCursorPos.Y) / dt * 0.02);
            }
            _lastCursorPos = cur;
            _lastCursorTime = now;
            MoveToy(cur.X, cur.Y);
            // If the user released the mouse anywhere, hand off to physics immediately
            var leftDown = System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed;
            if (_wasLeftDown && !leftDown)
            {
                StopFollowCursor(handOff: true);
                return;
            }
            _wasLeftDown = leftDown;
            return;
        }

        if (!_toyActive || _dragging) return;
        _toyVel.Y += 0.5; // gravity
        _toyPos.X += _toyVel.X;
        _toyPos.Y += _toyVel.Y;
        // spin proportional to horizontal speed
        _toySpin += _toyVel.X * 4.0; // tweak factor
        // bounce at edges of current monitor working area (respect taskbar)
        var screen = WinForms.Screen.FromPoint(new System.Drawing.Point((int)_toyPos.X, (int)_toyPos.Y));
        var wa = screen.WorkingArea; // in screen coords
        double L = wa.Left;
        double T = wa.Top;
        double R = wa.Right;
        double B = wa.Bottom;
        const double bounce = -0.6;
        bool bounced = false;
        if (_toyPos.X < L) { _toyPos.X = L; _toyVel.X *= bounce; bounced = true; }
        if (_toyPos.X > R) { _toyPos.X = R; _toyVel.X *= bounce; bounced = true; }
        if (_toyPos.Y < T) { _toyPos.Y = T; _toyVel.Y *= bounce; bounced = true; }
        if (_toyPos.Y > B) { _toyPos.Y = B; _toyVel.Y *= bounce; bounced = true; }
        // ground friction and air drag
        double ground = Math.Abs(_toyPos.Y - B) < 0.1 ? 0.85 : 0.98; // stronger friction when on ground
        _toyVel.X *= ground;
        _toyVel.Y *= 0.99; // tiny air drag
        MoveToy(_toyPos.X, _toyPos.Y);
        ToyImage.RenderTransform = new System.Windows.Media.RotateTransform(_toySpin);
        if (bounced && ToyImage.Visibility != Visibility.Visible)
        {
            ToyImage.Visibility = Visibility.Visible;
        }
    }

    private void ToyImage_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragStart = e.GetPosition(this);
        // Map to absolute screen coords; overlay Left/Top are already VirtualScreenLeft/Top
        _dragStart = new System.Windows.Point(_dragStart.X + Left, _dragStart.Y + Top);
        _dragStartTime = DateTime.UtcNow;
        _toyVel = new System.Windows.Vector(0, 0);
        ToyImage.CaptureMouse();
    }

    private void ToyImage_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(this);
        var sx = p.X + Left;
        var sy = p.Y + Top;
        MoveToy(sx, sy);
    }

    private void ToyImage_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ToyImage.ReleaseMouseCapture();
        var now = DateTime.UtcNow;
        var p = e.GetPosition(this);
        var sx = p.X + Left;
        var sy = p.Y + Top;
        // If dropped over the inventory window, delete the toy
        if (IsOverInventory(sx, sy))
        {
            HideToy();
            return;
        }
        var dt = Math.Max((now - _dragStartTime).TotalSeconds, 0.016);
        _toyVel = new System.Windows.Vector((sx - _dragStart.X) / dt * 0.02, (sy - _dragStart.Y) / dt * 0.02);
        _toyActive = true;
    }

    private void OverlayWindow_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent("RatPetItemUri"))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OverlayWindow_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("RatPetItemUri")) return;
        var uriStr = e.Data.GetData("RatPetItemUri") as string;
        if (string.IsNullOrEmpty(uriStr)) return;
        var bmp = new BitmapImage(new Uri(uriStr));
        // Spawn at cursor so even if overlay is click-through, we get correct monitor & coords
        var mp = WinForms.Control.MousePosition;
        ShowToy(bmp, mp.X, mp.Y);
    }

    // Inventory-assisted behavior: while dragging from inventory, call StartFollowCursor/StopFollowCursor
    public void StartFollowCursor(BitmapSource toyBitmap)
    {
        var mp = WinForms.Control.MousePosition;
        ShowToy(toyBitmap, mp.X, mp.Y);
        _followCursor = true;
        _lastCursorPos = new System.Windows.Point(mp.X, mp.Y);
        _lastCursorTime = DateTime.UtcNow;
        _wasLeftDown = System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed;
    }

    public void StopFollowCursor(bool handOff)
    {
        _followCursor = false;
        if (!handOff)
        {
            HideToy();
        }
        else
        {
            // give a small toss based on recent cursor delta so it doesn't stick
            var mp = WinForms.Control.MousePosition;
            var now = DateTime.UtcNow;
            var dt = Math.Max((now - _lastCursorTime).TotalSeconds, 0.016);
            var vx = (mp.X - _lastCursorPos.X) / dt * 0.015;
            var vy = (mp.Y - _lastCursorPos.Y) / dt * 0.015 - 2;
            _toyVel = new System.Windows.Vector(vx, vy);
            _toyActive = true;
        }
    }

    private bool IsOverInventory(double screenX, double screenY)
    {
        foreach (Window w in System.Windows.Application.Current.Windows)
        {
            if (w is InventoryWindow inv && inv.IsVisible)
            {
                var rect = new Rect(inv.Left, inv.Top, inv.Width, inv.Height);
                if (rect.Contains(new System.Windows.Point(screenX, screenY))) return true;
            }
        }
        return false;
    }
}


