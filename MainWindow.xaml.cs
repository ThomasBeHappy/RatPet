using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Media;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Runtime.InteropServices;
using System.IO;
using System.Linq;
using System.Windows.Automation;

namespace RatPet;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _frameTimer = new();
    private readonly Random _random = new();
    private BitmapSource? _spriteSheetIdle;
    private BitmapSource? _spriteSheetMove;
    private BitmapSource? _spriteSheetSleep;
    private BitmapSource? _spriteSheetAlarm;
    private BitmapSource? _spriteToy;
    private OverlayWindow? _overlay;
    private string[] _allowedMonitorDevices = Array.Empty<string>();
    private int _currentFrameIndex;
    private int _framesPerRow = 3; // idle/sleep: 3 columns
    private const int FrameSize = 32; // all frames are 32x32
    private int _frameWidth = FrameSize;
    private int _frameHeight = FrameSize;
    // move sheet specifics: 4 rows (Up,Right,Down,Left), 3 columns
    private const int MoveCols = 3;
    private const int MoveRows = 4;
    private int _moveFrameWidth = FrameSize;
    private int _moveFrameHeight = FrameSize;
    private double _scale = 2.0; // scale up pixel art
    private TimeSpan _frameInterval = TimeSpan.FromMilliseconds(1000.0 / 6); // 10 FPS default

    private enum PetState { Idle, Walk, Sleep, Steal, Play }
    private enum Direction { Up = 0, Right = 1, Down = 2, Left = 3 }
    private PetState _state = PetState.Idle;
    private DateTime _stateUntil = DateTime.MinValue;
    private bool _muted;
    private bool _funMode;
    private bool _chaosMode;
    private bool _stealCarrying;
    private double _carryTargetX;
    private double _carryTargetY;
    private Direction _walkDirection = Direction.Right;
    private double _targetX;
    private double _targetY;
    private double _speedPerTick = 3; // pixels per timer tick (pre-scale)
    private readonly string[] _bubblePhrases = new[]
    {
        "squeak!",
        "you've got this",
        "crumbs?",
        "zoom zoom",
        "nap time soon",
        "nice click!",
    };
    private DateTime _nextBubbleAt = DateTime.UtcNow.AddSeconds(6);
    private bool _toyActive;
    private System.Windows.Point _toyPos;
    private Vector _toyVel;
    private double _toyGravity = 0.5; // simple pixel gravity per tick

	// Mischief: minimize windows
    private bool _mischiefEnabled;
    private bool _mischiefActive;
    private IntPtr _mischiefTarget = IntPtr.Zero;
    private double _mischiefX;
    private double _mischiefY;
    private double _mischiefChance = 0.02; // probability per walk tick
    private DateTime _nextChaosActionAt = DateTime.MinValue;
    
    // Browser judgment system
    private bool _browserJudgmentEnabled;
    private bool _browserUIAEnabled;
    private DateTime _lastJudgmentAt = DateTime.MinValue;
    private string _lastJudgedSite = "";
    private readonly Random _judgmentRandom = new();
    // Photos
    private DateTime _nextPhotoAt = DateTime.MinValue;
    private IntPtr _lastPhotoWindow = IntPtr.Zero;
    private bool _badWebsiteDetection = false;
    private bool _photojournalistEnabled = false;
    // Diary
    private DateTime _lastDiaryEntry = DateTime.MinValue;
    private string _lastObservedActivity = "";
    // Statistics
    private Dictionary<string, TimeSpan> _activityStats = new();
    private DateTime _statsStartTime = DateTime.UtcNow;
    private string _currentActivity = "";
    private DateTime _currentActivityStart = DateTime.UtcNow;

    // Memes
    private string? _memesFolder;
    private DateTime _nextMemeAt = DateTime.UtcNow.AddSeconds(45);
    private bool _memeRevealPending;
    private bool _memeRevealFromLeft;
    private BitmapSource? _memeRevealBitmap;
    private (double left, double top, double right, double bottom) _memeRevealArea;

    // Tuning
    private double _sneakChance = 0.20; // probability when at edge

    // Sneak-behind state helpers
    private bool _sneakEnabled = true;
    private bool _isBehindSpecificWindow;
    private IntPtr _behindTarget = IntPtr.Zero;
    private int _sneakEdgeStreak = 0;
    private bool _pendingStealEnsureTop;

    // Footprints
    private DateTime _nextFootprintAt = DateTime.MinValue;
    private bool _leftPawNext = true;
    private DateTime _nextZOrderCheck = DateTime.MinValue;
    // Chaos helpers
    private DispatcherTimer? _jiggleTimer;
    private int _jiggleTicks;
    private IntPtr _jiggleTarget = IntPtr.Zero;
    private RECT _jiggleOrig;
    // Zoomies
    private bool _zoomiesActive;
    private DateTime _zoomiesUntil = DateTime.MinValue;
    private int _zoomiesHopsRemaining;
    private DateTime _nextZoomiesRetargetAt = DateTime.MinValue;
    // Chaos pending actions (perform after arrival at target window center)
    private bool _chaosPendingJiggle;
    private bool _chaosPendingPromote;
    private IntPtr _chaosPendingHwnd = IntPtr.Zero;
    private RECT _chaosPendingRect;

    public MainWindow()
    {
        InitializeComponent();

        // Start at bottom-right corner by default
        Loaded += (_, _) => PlaceNearBottomRight();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void RunPlayBehavior()
    {
        if (_overlay == null || !_overlay.ToyVisible)
        {
            _state = PetState.Idle;
            _stateUntil = DateTime.UtcNow.AddSeconds(1);
            return;
        }

        var pos = _overlay.ToyPosition;
        // Aim so the rat's center meets the toy, but clamp within movement bounds
        double vsLeft = SystemParameters.VirtualScreenLeft;
        double vsTop = SystemParameters.VirtualScreenTop;
        double vsRight = vsLeft + SystemParameters.VirtualScreenWidth;
        double vsBottom = vsTop + SystemParameters.VirtualScreenHeight;
        _targetX = Math.Max(vsLeft, Math.Min(vsRight - Width, pos.X - Width / 2));
        _targetY = Math.Max(vsTop, Math.Min(vsBottom - Height, pos.Y - Height / 2));
        UpdateDirectionTowardTarget();
        AdvanceMoveFrame(_spriteSheetMove, _walkDirection);
        MoveTowardTarget(arriveIdle:false);

        double dx = Math.Abs((Left + Width/2) - pos.X);
        double dy = Math.Abs((Top + Height/2) - pos.Y);
        double reach = (Math.Min(Width, Height) / 2) + 6; // reach radius
        if (dx < reach && dy < reach)
        {
            double throwDx = (pos.X - (Left + Width/2));
            double throwDy = (pos.Y - (Top + Height/2));
            double norm = Math.Sqrt(throwDx*throwDx + throwDy*throwDy);
            if (norm < 1) norm = 1;
            double speed = 16;
            double vx = (throwDx / norm) * speed;
            double vy = (throwDy / norm) * speed - 8;
            _overlay.ImpartVelocity(vx, vy);

            _state = PetState.Idle;
            _stateUntil = DateTime.UtcNow.AddSeconds(1.5);
            _currentFrameIndex = 0;
        }
    }

    private void PlaceNearBottomRight()
    {
        var screen = SystemParameters.WorkArea;
        Left = screen.Right - Width - 24;
        Top = screen.Bottom - Height - 24;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Allow dragging the borderless window
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Wake up from sleep on click
        if (_state == PetState.Sleep)
        {
            _state = PetState.Idle;
            _stateUntil = DateTime.UtcNow.AddSeconds(1);
            _currentFrameIndex = 0;
        }

        // Play alarm animation briefly
        if (_spriteSheetAlarm != null)
        {
            _ = PlayAlarmAnimation();
        }
    }

    private async Task PlayAlarmAnimation()
    {
        try
        {
            AlarmHost.Visibility = Visibility.Visible;
            // alarm.png assumed 1 row x 3 columns of 32x32 frames
            const int cols = 3;
            for (int i = 0; i < cols * 2; i++)
            {
                int col = i % cols;
                var rect = new Int32Rect(col * FrameSize, 0, FrameSize, FrameSize);
                AlarmHost.Source = new CroppedBitmap(_spriteSheetAlarm!, rect);
                await Task.Delay(90);
            }
        }
        finally
        {
            AlarmHost.Visibility = Visibility.Collapsed;
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Load sprite sheets from resources
        _spriteSheetIdle = NormalizeDpi(new BitmapImage(new Uri("pack://application:,,,/idle.png")));
        _spriteSheetMove = NormalizeDpi(new BitmapImage(new Uri("pack://application:,,,/move.png")));
        _spriteSheetSleep = NormalizeDpi(new BitmapImage(new Uri("pack://application:,,,/sleep.png")));
        _spriteSheetAlarm = NormalizeDpi(new BitmapImage(new Uri("pack://application:,,,/alarm.png")));
        _spriteToy = NormalizeDpi(new BitmapImage(new Uri("pack://application:,,,/toy.png")));

        // Use fixed 32x32 frame sizes to avoid padding/rounding issues
        _moveFrameWidth = FrameSize;
        _moveFrameHeight = FrameSize;
        _frameWidth = FrameSize;
        _frameHeight = FrameSize;

        Width = Math.Round(_frameWidth * _scale);
        Height = Math.Round(_frameHeight * _scale);
        // Crisp 2x (or configurable) scaling
        SpriteHost.LayoutTransform = new ScaleTransform(_scale, _scale);

        _frameTimer.Interval = _frameInterval;
        _frameTimer.Tick += OnTick;
        _frameTimer.Start();

        // Create overlay window (no owner so it can sit behind the rat)
        _overlay = new OverlayWindow();
        // Load persisted skin on startup
        CustomizeWindow.LoadSavedSkin(this);
        // Set memes folder to app directory
        SetMemesFolder(System.IO.Path.Combine(AppContext.BaseDirectory, "memes"));
        _overlay.ToyMoved += pos =>
        {
            // Rat chases toy position
            if (_state != PetState.Steal && _state != PetState.Sleep)
            {
                _state = PetState.Walk;
                _stateUntil = DateTime.MaxValue;
                _targetX = pos.X;
                _targetY = pos.Y;
                UpdateDirectionTowardTarget();
            }
        };
        _overlay.Show();
        EnsureOverlayBelowRat();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _frameTimer.Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        // Toy takes priority when visible (except while stealing or sleeping)
        if (_overlay != null && _overlay.ToyVisible && _state != PetState.Steal && _state != PetState.Sleep)
        {
            if (_state != PetState.Play)
            {
                _state = PetState.Play;
                _stateUntil = DateTime.MaxValue;
                _currentFrameIndex = 0;
            }
        }
        // Simple state durations and random transitions
        else if (DateTime.UtcNow >= _stateUntil && _state != PetState.Walk && _state != PetState.Steal)
        {
            switch (_state)
            {
                case PetState.Idle:
                    // Occasionally try to steal the cursor (more in chaos)
                    if (_random.NextDouble() < (_chaosMode ? 0.2 : 0.1))
                    {
                        // only steal when topmost; otherwise, go to edge, reemerge, then steal
                        if (Topmost && !_isBehindSpecificWindow)
                        {
                            _state = PetState.Steal;
                            _stateUntil = DateTime.MaxValue;
                        }
                        else
                        {
                            _pendingStealEnsureTop = true;
                            var area = GetCurrentMonitorWorkingArea();
                            // pick nearest edge point on current monitor
                            double cx = Left; double cy = Top;
                            // clamp current point to within area for target creation
                            double tx = Math.Max(area.left, Math.Min(area.right - Width, cx));
                            double ty = Math.Max(area.top, Math.Min(area.bottom - Height, cy));
                            // distances to each edge
                            double dLeft = Math.Abs(tx - area.left);
                            double dRight = Math.Abs((area.right - Width) - tx);
                            double dTop = Math.Abs(ty - area.top);
                            double dBottom = Math.Abs((area.bottom - Height) - ty);
                            double min = Math.Min(Math.Min(dLeft, dRight), Math.Min(dTop, dBottom));
                            if (min == dLeft) { _targetX = area.left; _targetY = ty; }
                            else if (min == dRight) { _targetX = area.right - Width; _targetY = ty; }
                            else if (min == dTop) { _targetX = tx; _targetY = area.top; }
                            else { _targetX = tx; _targetY = area.bottom - Height; }
                            _state = PetState.Walk;
                            _stateUntil = DateTime.MaxValue;
                            UpdateDirectionTowardTarget();
                        }
                    }
                    else
                    {
                        _state = _random.NextDouble() < 0.7 ? PetState.Walk : PetState.Sleep;
                        _stateUntil = _state == PetState.Walk
                            ? DateTime.MaxValue
                            : DateTime.UtcNow.AddSeconds(_random.Next(8, 20));
                        if (_state == PetState.Walk)
                        {
                            PickNewTarget();
                            UpdateDirectionTowardTarget();
                        }
                    }
                    break;
                case PetState.Walk:
                case PetState.Sleep:
                    _state = PetState.Idle;
                    _stateUntil = DateTime.UtcNow.AddSeconds(2);
                    break;
            }
            _currentFrameIndex = 0;
        }

        switch (_state)
        {
            case PetState.Idle:
                AdvanceFrame(_spriteSheetIdle);
                MaybeShowBubble();
                break;
            case PetState.Walk:
				AdvanceMoveFrame(_spriteSheetMove, _walkDirection);
                if (_zoomiesActive)
                {
                    RunZoomiesTick();
                }
                if (_mischiefActive)
				{
					// temporarily speed up to reach the button
					double saved = _speedPerTick;
					_speedPerTick = 6;
					_targetX = _mischiefX;
					_targetY = _mischiefY;
					UpdateDirectionTowardTarget();
					MoveTowardTarget(arriveIdle:false);
					// close enough to click
					double d = Math.Sqrt(Math.Pow(Left - _mischiefX, 2) + Math.Pow(Top - _mischiefY, 2));
					if (d < 10)
					{
						MinimizeWindow(_mischiefTarget);
						_mischiefActive = false;
						_mischiefTarget = IntPtr.Zero;
						_state = PetState.Idle;
						_stateUntil = DateTime.UtcNow.AddSeconds(1);
					}
					_speedPerTick = saved;
				}
                else
                {
                    MoveTowardTarget();
                    if (_chaosMode) MaybeRunChaosAction();
                    MaybeLeaveFootprint();
                    // If we are walking to the edge specifically to reveal a meme, check arrival
                    if (_memeRevealPending)
                    {
                        double dx = _targetX - Left; double dy = _targetY - Top;
                        double dist = Math.Sqrt(dx*dx + dy*dy);
                        double arriveRadius = Math.Max(6.0, 8.0 * _scale + 0.75 * _speedPerTick);
                        if (dist <= arriveRadius)
                        {
                            try
                            {
                                _overlay?.ShowMeme(_memeRevealBitmap!, new System.Windows.Rect(_memeRevealArea.left, _memeRevealArea.top, _memeRevealArea.right - _memeRevealArea.left, _memeRevealArea.bottom - _memeRevealArea.top), _memeRevealFromLeft);
                            }
                            catch { }
                            _memeRevealPending = false;
                            _memeRevealBitmap = null;
                        }
                    }
                    if (_pendingStealEnsureTop)
                    {
                        // when we reached edge, pop to top and begin steal
                        double dx = _targetX - Left; double dy = _targetY - Top;
                        double dist = Math.Sqrt(dx*dx + dy*dy);
                        double arriveRadius = Math.Max(6.0, 8.0 * _scale + 0.75 * _speedPerTick);
                        if (dist <= arriveRadius)
                        {
                            ReemergeToTop();
                            Topmost = true;
                            _pendingStealEnsureTop = false;
                            _state = PetState.Steal;
                            _stateUntil = DateTime.MaxValue;
                            break;
                        }
                    }
                    else
                    {
                        if (_sneakEnabled)
                        {
                            MaybeSneakBehindIfAtWindowEdge();
                        }
                        else if (_isBehindSpecificWindow)
                        {
                            // If sneaking was disabled while behind, pop back to top
                            ReemergeToTop();
                        }
                        MaybeStartMischiefMinimize();
                    }
				}
				MaybeShowBubble(probability: 0.03);
				break;
            case PetState.Sleep:
                AdvanceFrame(_spriteSheetSleep);
                break;
            case PetState.Steal:
                RunStealBehavior();
                break;
            case PetState.Play:
                RunPlayBehavior();
                MaybeLeaveFootprint();
                break;
        }

        UpdateToyPhysics();
        
        // Check for browser judgment opportunities
        if (_browserJudgmentEnabled)
        {
            MaybeJudgeBrowser();
        }

        MaybeShowMeme();
        MaybeSnapPhotoOnWindowChange();
        MaybeWriteDiaryEntry();
        UpdateActivityStats();

        // Occasionally reassert z-order so overlay stays below the rat
        if (DateTime.UtcNow >= _nextZOrderCheck)
        {
            EnsureOverlayBelowRat();
            _nextZOrderCheck = DateTime.UtcNow.AddSeconds(2);
        }
    }

    private void MaybeLeaveFootprint()
    {
        if (_overlay == null) return;
        var now = DateTime.UtcNow;
        double intervalMs = Math.Max(80, 260 - _speedPerTick * 30); // faster speed → shorter interval
        if (now < _nextFootprintAt) return;
        _nextFootprintAt = now.AddMilliseconds(intervalMs);

        double scale = Math.Max(0.5, _scale * 0.7);
        double lateral = (_leftPawNext ? -1 : 1) * Math.Max(2.0, 3.0 * _scale);
        double x, y, angle;
        switch (_walkDirection)
        {
            case Direction.Right:
                x = Left + Width - 8 * _scale;
                y = Top + Height - 6 * _scale + lateral;
                angle = 90; // sprite points up by default
                break;
            case Direction.Left:
                x = Left + 8 * _scale;
                y = Top + Height - 6 * _scale + lateral;
                angle = -90;
                break;
            case Direction.Down:
                x = Left + Width / 2 + lateral;
                y = Top + Height - 4 * _scale;
                angle = 180;
                break;
            default: // Up
                x = Left + Width / 2 + lateral;
                y = Top + 4 * _scale;
                angle = 0;
                break;
        }
        _overlay.AddFootprint(x, y, angle, scale);
        _leftPawNext = !_leftPawNext;
    }

    private void MaybeShowMeme()
    {
        if (_overlay == null) return;
        if (string.IsNullOrEmpty(_memesFolder)) return;
        if (_memeRevealPending) return; // already scheduled
        if (DateTime.UtcNow < _nextMemeAt) return;
        try
        {
            // Support common image extensions
            var exts = new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };
            var files = Directory.EnumerateFiles(_memesFolder)
                .Where(f => exts.Contains(System.IO.Path.GetExtension(f).ToLower()))
                .ToArray();
            if (files.Length == 0) return;
            var pick = files[_random.Next(files.Length)];
            var bmp = new BitmapImage(new Uri(pick));
            _memeRevealBitmap = NormalizeDpi(bmp);
            var area = GetCurrentMonitorWorkingArea();
            _memeRevealArea = area;
            _memeRevealFromLeft = _random.NextDouble() < 0.5;
            // Pick the exact edge point on this monitor
            if (_memeRevealFromLeft) { _targetX = area.left; _targetY = Top; }
            else { _targetX = area.right - Width; _targetY = Top; }
            _state = PetState.Walk;
            _stateUntil = DateTime.MaxValue;
            UpdateDirectionTowardTarget();
            _memeRevealPending = true;
            _nextMemeAt = DateTime.UtcNow.AddSeconds(_random.Next(60, 180));
        }
        catch { }
    }

    private void MaybeShowBubble(double probability = 0.05)
    {
        if (DateTime.UtcNow < _nextBubbleAt) return;
        if (_random.NextDouble() > probability) return;
        _nextBubbleAt = DateTime.UtcNow.AddSeconds(_random.Next(8, 20));

        BubbleText.Text = _bubblePhrases[_random.Next(0, _bubblePhrases.Length)];
        ShowBubble();
    }

    private async void ShowBubble()
    {
        BubbleHost.Visibility = Visibility.Visible;
        await FadeElement(BubbleHost, from: 0, to: 1, ms: 140);
        await Task.Delay(1500);
        await FadeElement(BubbleHost, from: 1, to: 0, ms: 180);
        BubbleHost.Visibility = Visibility.Collapsed;
    }

    private static Task FadeElement(UIElement element, double from, double to, int ms)
    {
        var tcs = new TaskCompletionSource();
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
        {
            FillBehavior = FillBehavior.Stop
        };
        anim.Completed += (_, _) =>
        {
            element.Opacity = to;
            tcs.SetResult();
        };
        element.BeginAnimation(UIElement.OpacityProperty, anim);
        return tcs.Task;
    }

    // Steal cursor: chase the real cursor; optionally move OS cursor (fun mode)
    private void RunStealBehavior()
    {
        // Speed up animation and movement while stealing
        AdvanceMoveFrame(_spriteSheetMove, _walkDirection);
        double savedSpeed = _speedPerTick;
        _speedPerTick = 6;
        // ensure topmost at start of steal
        if (!Topmost || _isBehindSpecificWindow)
        {
            ReemergeToTop();
            Topmost = true;
        }

        if (!_stealCarrying)
        {
            // Chase the live cursor
            var p = System.Windows.Forms.Control.MousePosition;
            _targetX = p.X;
            _targetY = p.Y;
            UpdateDirectionTowardTarget();
            MoveTowardTarget(arriveIdle:false);

            // Close enough to grab?
            double dx = Math.Abs(Left - _targetX);
            double dy = Math.Abs(Top - _targetY);
            if (dx < 12 && dy < 12)
            {
                // Choose a corner on the CURRENT monitor
                var area = GetCurrentMonitorWorkingArea();
                var corners = new (double x, double y)[]
                {
                    (area.left, area.top),
                    (area.right - Width, area.top),
                    (area.left, area.bottom - Height),
                    (area.right - Width, area.bottom - Height)
                };
                // pick nearest corner to current position
                (double x, double y) corner = corners[0];
                double best = double.MaxValue;
                foreach (var c in corners)
                {
                    double ddx = (Left - c.x);
                    double ddy = (Top - c.y);
                    double d2 = ddx * ddx + ddy * ddy;
                    if (d2 < best)
                    {
                        best = d2;
                        corner = c;
                    }
                }
                _carryTargetX = corner.x;
                _carryTargetY = corner.y;
                _stealCarrying = true;
                CursorHost.Visibility = Visibility.Visible;
            }
        }
        else
        {
            // Carry the 'stolen' cursor to the chosen corner
            _targetX = _carryTargetX;
            _targetY = _carryTargetY;
            UpdateDirectionTowardTarget();
            MoveTowardTarget(arriveIdle:false);

            // Optionally move the real cursor along while carrying
            if (_funMode)
            {
                try
                {
                    System.Windows.Forms.Cursor.Position = new System.Drawing.Point((int)(Left + Width / 2), (int)(Top + Height / 2));
                }
                catch { }
            }

            double dist = Math.Sqrt(Math.Pow(_targetX - Left, 2) + Math.Pow(_targetY - Top, 2));
            if (dist < 8)
            {
                // Drop
                _stealCarrying = false;
                CursorHost.Visibility = Visibility.Collapsed;
                _state = PetState.Idle;
                _stateUntil = DateTime.UtcNow.AddSeconds(1);
                _currentFrameIndex = 0;
                // After stealing, occasionally throw the toy if visible
                if (_overlay != null && _overlay.ToyVisible)
                {
                    // Throw away from the rat with a nice arc
                    var pos = _overlay.ToyPosition;
                    double dx = pos.X - (Left + Width/2);
                    double dy = pos.Y - (Top + Height/2);
                    if (Math.Abs(dx) < 1 && Math.Abs(dy) < 1)
                    {
                        dx = _random.Next(-1, 2) == 0 ? 1 : -1; // nudge
                        dy = -1;
                    }
                    double norm = Math.Sqrt(dx*dx + dy*dy);
                    if (norm < 1) norm = 1;
                    double speed = 18; // toss speed
                    double vx = (dx / norm) * speed;
                    double vy = (dy / norm) * speed - 8; // add lift
                    _overlay.ImpartVelocity(vx * 0.8, vy);
                }
            }
        }

        _speedPerTick = savedSpeed;
    }

    private void AdvanceFrame(BitmapSource? sheet)
    {
        if (sheet == null) return;
        var col = _currentFrameIndex % _framesPerRow;
        var rect = new Int32Rect(col * FrameSize, 0, FrameSize, FrameSize);
        try
        {
            var cropped = new CroppedBitmap(sheet, rect);
            SpriteHost.Source = cropped;
        }
        catch
        {
            // ignore occasional timing issues on load
        }

        _currentFrameIndex = (_currentFrameIndex + 1) % _framesPerRow;
    }

    private void AdvanceMoveFrame(BitmapSource? sheet, Direction direction)
    {
        if (sheet == null) return;
        int col = _currentFrameIndex % MoveCols;
        int row = (int)direction; // 0:Up,1:Right,2:Down,3:Left
        var rect = new Int32Rect(col * FrameSize, row * FrameSize, FrameSize, FrameSize);
        try
        {
            var cropped = new CroppedBitmap(sheet, rect);
            SpriteHost.Source = cropped;
        }
        catch { }

        _currentFrameIndex = (_currentFrameIndex + 1) % MoveCols;
    }

    private static BitmapSource NormalizeDpi(BitmapSource source)
    {
        // Convert to BGRA32 for predictable stride
        var formatted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = formatted.PixelWidth;
        int height = formatted.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        formatted.CopyPixels(pixels, stride, 0);
        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
    }

    // Tray callbacks
    public void FeedCrumb()
    {
        // For now, a tiny bounce effect; extend with hunger later
        Top = Math.Max(0, Top - 6);
    }

    public void SetMuted(bool muted) => _muted = muted;
    public void SetFunMode(bool enabled) => _funMode = enabled;
    public void SetMischiefEnabled(bool enabled) => _mischiefEnabled = enabled;
    public void SetChaosModeEnabled(bool enabled) { _chaosMode = enabled; Console.WriteLine("[Chaos] mode=" + enabled); }
    public void SetBrowserJudgmentEnabled(bool enabled) => _browserJudgmentEnabled = enabled;
    public void SetBrowserUIAEnabled(bool enabled) => _browserUIAEnabled = enabled;
    public void SetBadWebsiteDetection(bool enabled)
    {
        _badWebsiteDetection = enabled;
        Console.WriteLine($"[BadWebsite] Detection {(enabled ? "enabled" : "disabled")}");
    }
    public void SetPhotojournalistEnabled(bool enabled)
    {
        _photojournalistEnabled = enabled;
        Console.WriteLine($"[Photojournalist] Mode {(enabled ? "enabled" : "disabled")}");
    }
    public void SetMemesFolder(string folder) { try { if (Directory.Exists(folder)) _memesFolder = folder; } catch { } }
    public void SetScale(double s)
    {
        _scale = Math.Max(0.5, Math.Min(4.0, s));
        Width = Math.Round(_frameWidth * _scale);
        Height = Math.Round(_frameHeight * _scale);
        SpriteHost.LayoutTransform = new ScaleTransform(_scale, _scale);
    }
    public double GetScale() => _scale;
    public void SetBaseSpeed(double v) { _speedPerTick = Math.Max(1, Math.Min(10, v)); }
    public double GetBaseSpeed() => _speedPerTick;
    public void SetSneakChance(double v) { _sneakChance = Math.Max(0, Math.Min(1, v)); }
    public double GetSneakChance() => _sneakChance;
    public void SetMischiefChance(double v) { _mischiefChance = Math.Max(0, Math.Min(0.2, v)); }
    public double GetMischiefChance() => _mischiefChance;
    public void SetSneakEnabled(bool enabled)
    {
        _sneakEnabled = enabled;
        if (!enabled && _isBehindSpecificWindow)
        {
            ReemergeToTop();
        }
    }
    public void OpenInventory()
    {
        var existing = System.Windows.Application.Current.Windows.OfType<InventoryWindow>().FirstOrDefault();
        if (existing != null)
        {
            existing.Owner = this;
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            existing.Focus();
            return;
        }
        var w = new InventoryWindow { Owner = this };
        w.Show();
    }

    public void ApplySkin(string idle, string move, string sleep)
    {
        try
        {
            _spriteSheetIdle = NormalizeDpi(new BitmapImage(new Uri($"pack://application:,,,/{idle}")));
            _spriteSheetMove = NormalizeDpi(new BitmapImage(new Uri($"pack://application:,,,/{move}")));
            _spriteSheetSleep = NormalizeDpi(new BitmapImage(new Uri($"pack://application:,,,/{sleep}")));
        }
        catch { }
    }

    public void BeginDragSpawn(string packUri)
    {
        try
        {
            if (_overlay == null) return;
            var bmp = new BitmapImage(new Uri(packUri));
            _overlay.StartFollowCursor(NormalizeDpi(bmp));
        }
        catch { }
    }

    public void EndDragSpawn(bool handOff)
    {
        _overlay?.StopFollowCursor(handOff);
    }

    // Toy throwing / fetch
    public void ThrowToy()
    {
        if (_spriteToy == null || _overlay == null) return;
        // Start from rat position on screen
        _overlay.ShowToy(_spriteToy, Left, Top);
        BubbleText.Text = "fetch!";
        ShowBubble();
    }

    private void UpdateToyPhysics()
    {
        if (!_toyActive) return;
        _toyVel.Y += _toyGravity;
        _toyPos.X += _toyVel.X;
        _toyPos.Y += _toyVel.Y;
        // Render toy relative to window
        // Toy is now handled by overlay; nothing to render in the rat window here

        // End if far off-screen
        double vsLeft = SystemParameters.VirtualScreenLeft;
        double vsTop = SystemParameters.VirtualScreenTop;
        double vsRight = vsLeft + SystemParameters.VirtualScreenWidth;
        double vsBottom = vsTop + SystemParameters.VirtualScreenHeight;
        if (_toyPos.X < vsLeft - 50 || _toyPos.X > vsRight + 50 || _toyPos.Y > vsBottom + 50)
        {
            _toyActive = false;
            _overlay?.HideToy();
        }

        // If active, chase toy
        if (_state != PetState.Steal && _state != PetState.Sleep)
        {
            _state = PetState.Walk;
            _stateUntil = DateTime.MaxValue;
            _targetX = _toyPos.X;
            _targetY = _toyPos.Y;
            UpdateDirectionTowardTarget();
        }
    }

    private (double left, double top, double right, double bottom) GetCurrentMonitorWorkingArea()
    {
        // Determine which monitor the rat window is on using its center point
        int cx = (int)(Left + Width / 2);
        int cy = (int)(Top + Height / 2);
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cx, cy));
        // If restricted, and current monitor not allowed, snap to the closest allowed
        if (_allowedMonitorDevices.Length > 0 && !_allowedMonitorDevices.Contains(screen.DeviceName))
        {
            var allowed = System.Windows.Forms.Screen.AllScreens
                .Where(s => _allowedMonitorDevices.Contains(s.DeviceName))
                .ToArray();
            if (allowed.Length > 0)
            {
                // choose the allowed screen with minimal distance from current center
                double best = double.MaxValue; System.Windows.Forms.Screen bestScr = allowed[0];
                foreach (var s in allowed)
                {
                    var r = s.WorkingArea;
                    double sx = Math.Clamp(cx, r.Left, r.Right);
                    double sy = Math.Clamp(cy, r.Top, r.Bottom);
                    double d2 = (sx - cx) * (sx - cx) + (sy - cy) * (sy - cy);
                    if (d2 < best) { best = d2; bestScr = s; }
                }
                screen = bestScr;
            }
        }
        var wa = screen.WorkingArea;
        return (wa.Left, wa.Top, wa.Right, wa.Bottom);
    }

    private void PickNewTarget()
    {
        if (_allowedMonitorDevices.Length == 0)
        {
            // Use the entire virtual desktop across monitors
            double vsLeft = SystemParameters.VirtualScreenLeft;
            double vsTop = SystemParameters.VirtualScreenTop;
            double vsRight = vsLeft + SystemParameters.VirtualScreenWidth;
            double vsBottom = vsTop + SystemParameters.VirtualScreenHeight;
            _targetX = _random.Next((int)vsLeft, (int)(vsRight - Width));
            _targetY = _random.Next((int)vsTop, (int)(vsBottom - Height));
        }
        else
        {
            // Choose a random allowed monitor working area
            var screens = System.Windows.Forms.Screen.AllScreens
                .Where(s => _allowedMonitorDevices.Contains(s.DeviceName))
                .ToArray();
            if (screens.Length == 0)
            {
                // fallback to current behavior
                double vsLeft = SystemParameters.VirtualScreenLeft;
                double vsTop = SystemParameters.VirtualScreenTop;
                double vsRight = vsLeft + SystemParameters.VirtualScreenWidth;
                double vsBottom = vsTop + SystemParameters.VirtualScreenHeight;
                _targetX = _random.Next((int)vsLeft, (int)(vsRight - Width));
                _targetY = _random.Next((int)vsTop, (int)(vsBottom - Height));
            }
            else
            {
                var s = screens[_random.Next(0, screens.Length)];
                var wa = s.WorkingArea;
                _targetX = _random.Next(wa.Left, Math.Max(wa.Left, wa.Right - (int)Width));
                _targetY = _random.Next(wa.Top, Math.Max(wa.Top, wa.Bottom - (int)Height));
            }
        }
    }

    private void UpdateDirectionTowardTarget()
    {
        double cx = Left;
        double cy = Top;
        double dx = _targetX - cx;
        double dy = _targetY - cy;
        if (Math.Abs(dx) > Math.Abs(dy))
        {
            _walkDirection = dx >= 0 ? Direction.Right : Direction.Left;
        }
        else
        {
            _walkDirection = dy >= 0 ? Direction.Down : Direction.Up;
        }
    }

    private void MoveTowardTarget(bool arriveIdle = true)
    {
        // Move window position, constrained to allowed monitors if set
        double vsLeft = SystemParameters.VirtualScreenLeft;
        double vsTop = SystemParameters.VirtualScreenTop;
        double vsRight = vsLeft + SystemParameters.VirtualScreenWidth;
        double vsBottom = vsTop + SystemParameters.VirtualScreenHeight;
        if (_allowedMonitorDevices.Length > 0)
        {
            // build union of allowed working areas
            var screens = System.Windows.Forms.Screen.AllScreens.Where(s => _allowedMonitorDevices.Contains(s.DeviceName)).ToArray();
            if (screens.Length > 0)
            {
                vsLeft = screens.Min(s => s.WorkingArea.Left);
                vsTop = screens.Min(s => s.WorkingArea.Top);
                vsRight = screens.Max(s => s.WorkingArea.Right);
                vsBottom = screens.Max(s => s.WorkingArea.Bottom);
            }
        }
        double cx = Left;
        double cy = Top;
        double dx = _targetX - cx;
        double dy = _targetY - cy;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        double arriveRadius = Math.Max(6.0, 8.0 * _scale + 0.75 * _speedPerTick);
        if (dist <= arriveRadius)
        {
            // Snap to target to avoid dithering around the goal
            Left = Math.Max(vsLeft, Math.Min(vsRight - Width, _targetX));
            Top = Math.Max(vsTop, Math.Min(vsBottom - Height, _targetY));
            // If we arrived due to a pending chaos action, execute it now
            if (_chaosMode && (_chaosPendingJiggle || _chaosPendingPromote))
            {
                try
                {
                    if (_chaosPendingJiggle && _chaosPendingHwnd != IntPtr.Zero)
                    {
                        _jiggleTarget = _chaosPendingHwnd;
                        _jiggleOrig = _chaosPendingRect;
                        _jiggleTicks = 0;
                        _jiggleTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                        _jiggleTimer.Tick -= OnJiggleTick;
                        _jiggleTimer.Tick += OnJiggleTick;
                        _jiggleTimer.Start();
                        Console.WriteLine($"[Chaos] Jiggle start 0x{_jiggleTarget.ToInt64():X}");
                    }
                    else if (_chaosPendingPromote && _chaosPendingHwnd != IntPtr.Zero)
                    {
                        SetWindowPos(_chaosPendingHwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                        Console.WriteLine($"[Chaos] Promoted window 0x{_chaosPendingHwnd.ToInt64():X} to top");
                    }
                }
                catch { }
                finally
                {
                    _chaosPendingJiggle = false;
                    _chaosPendingPromote = false;
                    _chaosPendingHwnd = IntPtr.Zero;
                }
            }
            if (arriveIdle)
            {
                // reached target: short idle pause before choosing the next action
                _state = PetState.Idle;
                _stateUntil = DateTime.UtcNow.AddMilliseconds(500);
                _currentFrameIndex = 0;
            }
            return;
        }

        // normalized step
        double step = _speedPerTick * (_scale) * (_chaosMode ? 2.0 : 1.0) * (_zoomiesActive ? 4.0 : 1.0); // faster in chaos
        double mx = (dx / dist) * step;
        double my = (dy / dist) * step;
        Left = Math.Max(vsLeft, Math.Min(vsRight - Width, cx + mx));
        Top = Math.Max(vsTop, Math.Min(vsBottom - Height, cy + my));

        // update row based on predominant direction
        UpdateDirectionTowardTarget();
    }

    // Settings wiring
    public void SetAllowedMonitors(string[] deviceNames)
    {
        _allowedMonitorDevices = deviceNames?.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToArray() ?? Array.Empty<string>();
        // When restriction changes, keep the rat inside the allowed region
        var area = GetCurrentMonitorWorkingArea();
        Left = Math.Max(area.left, Math.Min(area.right - Width, Left));
        Top = Math.Max(area.top, Math.Min(area.bottom - Height, Top));
        // Also adjust current target into allowed region
        _targetX = Math.Max(area.left, Math.Min(area.right - Width, _targetX));
        _targetY = Math.Max(area.top, Math.Min(area.bottom - Height, _targetY));
    }
    
    // Browser judgment system
    private void MaybeJudgeBrowser()
    {
        // Don't judge too frequently
        if (DateTime.UtcNow < _lastJudgmentAt.AddSeconds(30)) return;
        
        var activeWindow = GetForegroundWindow();
        if (activeWindow == IntPtr.Zero) return;
        
        string? url = null;
        if (_browserUIAEnabled)
        {
            Console.WriteLine("[UIA] Attempting to read browser URL via UIA...");
            url = TryGetBrowserUrlViaUIA(activeWindow);
            Console.WriteLine("[UIA] Result URL: " + (url ?? "<null>"));
        }
        var windowTitle = GetWindowTitle(activeWindow);
        Console.WriteLine("[Judge] Active window title: " + windowTitle);
        if (string.IsNullOrEmpty(windowTitle) && string.IsNullOrEmpty(url)) return;
        
        (string Domain, string Category)? siteInfo = null;
        if (!string.IsNullOrEmpty(url))
        {
            try
            {
                var uri = new Uri(url!);
                var host = uri.Host.ToLowerInvariant();
                Console.WriteLine("[Judge] Parsed host from URL: " + host);
                siteInfo = (host, CategorizeDomain(host));
            }
            catch { }
        }
        if (siteInfo == null)
        {
            Console.WriteLine("[Judge] Falling back to title parsing");
            siteInfo = AnalyzeWindowTitle(windowTitle);
        }
        if (siteInfo == null) return;
        
        // Don't judge the same site repeatedly
        if (siteInfo.Value.Domain == _lastJudgedSite) return;
        
        // Small chance to judge each time
        if (_judgmentRandom.NextDouble() > 0.15) return;
        
        ShowJudgmentalNotification(siteInfo.Value);
        _lastJudgmentAt = DateTime.UtcNow;
        _lastJudgedSite = siteInfo.Value.Domain;
        
        // If bad website detection is enabled and this is a "bad" site, snap a photo! >:3
        if (_badWebsiteDetection && _photojournalistEnabled && IsBadWebsite(siteInfo.Value.Category))
        {
            SnapBadWebsitePhoto(siteInfo.Value.Domain, siteInfo.Value.Category);
        }
    }
    
    private string GetWindowTitle(IntPtr hWnd)
    {
        try
        {
            int length = GetWindowTextLength(hWnd);
            if (length == 0) return "";
            
            var sb = new System.Text.StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }
        catch
        {
            return "";
        }
    }

    private void MaybeSnapPhotoOnWindowChange()
    {
        try
        {
            if (!_photojournalistEnabled) return; // Only take photos if enabled
            if (DateTime.UtcNow < _nextPhotoAt) return;
            
            // Rat decides what to photograph! >:3
            var photoType = _judgmentRandom.Next(0, 2); // 0 = rat selfie, 1 = user activity
            
            if (photoType == 0)
            {
                // Rat selfie - capture around the rat's location
                SnapRatSelfie();
            }
            else
            {
                // User activity - capture what the user is doing
                SnapUserActivity();
            }
            
            _nextPhotoAt = DateTime.UtcNow.AddSeconds(_judgmentRandom.Next(200, 600)); // random cooldown 200-600s
        }
        catch { }
    }
    
    private void SnapRatSelfie()
    {
        try
        {
            // Capture a small area around the rat
            var ratSize = 64; // approximate rat size
            var padding = 32;
            var captureSize = ratSize + (padding * 2);
            
            var ratX = (int)(Left + Width / 2);
            var ratY = (int)(Top + Height / 2);
            
            var captureX = Math.Max(0, ratX - captureSize / 2);
            var captureY = Math.Max(0, ratY - captureSize / 2);
            BubbleText.Text = "selfie! >:3";
            ShowBubble();

            using var bmp = new System.Drawing.Bitmap(captureSize, captureSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(new System.Drawing.Point(captureX, captureY), System.Drawing.Point.Empty, new System.Drawing.Size(captureSize, captureSize));
            }
            
            var folder = GetPhotosFolder();
            var file = System.IO.Path.Combine(folder, $"rat_selfie_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
            bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            
            Console.WriteLine($"[Photo] Rat selfie saved: {file}");
        }
        catch { }
    }
    
    private void SnapUserActivity()
    {
        try
        {
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == _lastPhotoWindow) return;
            _lastPhotoWindow = fg;

            if (!GetWindowRect(fg, out RECT r)) return;
            
            // Capture the user's active window
            int width = Math.Max(1, r.Right - r.Left);
            int height = Math.Max(1, r.Bottom - r.Top);

            BubbleText.Text = "caught you! >:3";
            ShowBubble();

            using var bmp = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(new System.Drawing.Point(r.Left, r.Top), System.Drawing.Point.Empty, new System.Drawing.Size(width, height));
            }
            
            var folder = GetPhotosFolder();
            var file = System.IO.Path.Combine(folder, $"user_activity_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
            bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            
            Console.WriteLine($"[Photo] User activity captured: {file}");
        }
        catch { }
    }
    
    private bool IsBadWebsite(string category)
    {
        // Define what constitutes a "bad" website for the rat's judgmental purposes >:3
        var badCategories = new[] { "porn" };
        return badCategories.Contains(category.ToLowerInvariant());
    }
    
    private void SnapBadWebsitePhoto(string domain, string category)
    {
        try
        {
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return;

            if (!GetWindowRect(fg, out RECT r)) return;
            
            BubbleText.Text = $"caught you on {domain}! >:3";
            ShowBubble();

            // Capture the "evidence" >:3
            int width = Math.Max(1, r.Right - r.Left);
            int height = Math.Max(1, r.Bottom - r.Top);
            using var bmp = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(new System.Drawing.Point(r.Left, r.Top), System.Drawing.Point.Empty, new System.Drawing.Size(width, height));
            }
            
            var folder = GetPhotosFolder();
            var file = System.IO.Path.Combine(folder, $"bad_website_{category}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
            bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            
            Console.WriteLine($"[Photo] Bad website evidence captured: {file} (domain: {domain}, category: {category})");
        }
        catch { }
    }
    
    private (string Domain, string Category)? AnalyzeWindowTitle(string title)
    {
        Console.WriteLine("Analyzing window title: " + title);
        try
        {
            // 1) Prefer explicit URLs
            var urlMatch = System.Text.RegularExpressions.Regex.Match(title, @"https?://([^/]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (urlMatch.Success)
            {
                var domain = urlMatch.Groups[1].Value.ToLower();
                return (domain, CategorizeDomain(domain));
            }

            // 2) Extract domain from any email address present (e.g., Gmail titles show an email)
            var emailMatch = System.Text.RegularExpressions.Regex.Match(title, @"\b[\w.+-]+@([A-Za-z0-9.-]+\.[A-Za-z]{2,})\b");
            if (emailMatch.Success)
            {
                var domain = emailMatch.Groups[1].Value.ToLower();
                Console.WriteLine("Email-derived domain: " + domain);
                return (domain, CategorizeDomain(domain));
            }

            // 3) Map common products in titles to their domains when URL/email absent
            string lowered = title.ToLowerInvariant();
            if (lowered.Contains("gmail")) return ("gmail.com", CategorizeDomain("gmail.com"));
            if (lowered.Contains("outlook")) return ("outlook.com", CategorizeDomain("outlook.com"));
            if (lowered.Contains("teams")) return ("teams.microsoft.com", CategorizeDomain("teams.microsoft.com"));
            if (lowered.Contains("slack")) return ("slack.com", CategorizeDomain("slack.com"));

            // 4) Generic domain pattern (avoid picking non-TLD like "movecraft.pirates")
            // Require at least one dot and a 2-24 letter TLD, and ensure it ends a token/before space or hyphen
            var domMatch = System.Text.RegularExpressions.Regex.Match(title, @"\b([A-Za-z0-9.-]+\.[A-Za-z]{2,24})(?=\b|\s|-|$)");
            if (domMatch.Success)
            {
                var domain = domMatch.Groups[1].Value.ToLower();
                Console.WriteLine("Extracted domain: " + domain);
                return (domain, CategorizeDomain(domain));
            }
        }
        catch { }

        return null;
    }
    
    private string CategorizeDomain(string domain)
    {
        // Remove www. prefix
        if (domain.StartsWith("www.")) domain = domain.Substring(4);
        
        // Social media
        if (domain.Contains("facebook") || domain.Contains("instagram") || domain.Contains("twitter") || 
            domain.Contains("tiktok") || domain.Contains("snapchat") || domain.Contains("linkedin"))
            return "social";
            
        // Video platforms
        if (domain.Contains("youtube") || domain.Contains("twitch") || domain.Contains("vimeo") || 
            domain.Contains("netflix") || domain.Contains("hulu") || domain.Contains("disney"))
            return "video";
            
        // Shopping
        if (domain.Contains("amazon") || domain.Contains("ebay") || domain.Contains("etsy") || 
            domain.Contains("shopify") || domain.Contains("walmart") || domain.Contains("target"))
            return "shopping";
            
        // Gaming
        if (domain.Contains("steam") || domain.Contains("epic") || domain.Contains("roblox") || 
            domain.Contains("minecraft") || domain.Contains("discord") || domain.Contains("reddit"))
            return "gaming";
            
        // Work/Productivity
        if (domain.Contains("github") || domain.Contains("stackoverflow") || domain.Contains("docs.google") || 
            domain.Contains("office") || domain.Contains("slack") || domain.Contains("teams") || domain.Contains("gmail") || domain.Contains("inbox"))
            return "work";
            
        // News
        if (domain.Contains("news") || domain.Contains("cnn") || domain.Contains("bbc") || 
            domain.Contains("reuters") || domain.Contains("npr"))
            return "news";

        // Porn
        if (domain.Contains("porn") || domain.Contains("xxx") || domain.Contains("sex") 
         || domain.Contains("pornhub") || domain.Contains("xhamster") || domain.Contains("xvideos"))
            return "porn";
            
        return "unknown";
    }

    private string? TryGetBrowserUrlViaUIA(IntPtr hWnd)
    {
        try
        {
            // Filter to common browsers by process name
            GetWindowThreadProcessId(hWnd, out uint pid);
            using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            var name = (proc.ProcessName ?? "").ToLowerInvariant();
            Console.WriteLine("[UIA] Process name: " + name);
            bool isBrowser = name is "chrome" or "msedge" or "brave" or "firefox";
            Console.WriteLine("[UIA] Process name: " + name + ", is browser: " + isBrowser);
            if (!isBrowser) return null;

            var element = AutomationElement.FromHandle(hWnd);
            Console.WriteLine("[UIA] Found element: " + (element != null ? "yes" : "no"));
            if (element == null) return null;
            // Search for an Edit/ComboBox that looks like the address bar
            var condition = new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox)
            );
            var edits = element.FindAll(TreeScope.Descendants, condition);
            Console.WriteLine("[UIA] Found " + edits.Count + " edit/combo box elements");
            for (int i = 0; i < edits.Count; i++)
            {
                var ae = edits[i];
                string nameProp = ae.Current.Name ?? string.Empty;
                Console.WriteLine("[UIA] Element name: " + nameProp + ", AutomationId=" + (ae.Current.AutomationId ?? ""));
                // Chromium typically exposes "Address and search bar"; Firefox varies
                if (!string.IsNullOrEmpty(nameProp))
                {
                    var lowered = nameProp.ToLowerInvariant();
                    if (lowered.Contains("address") || lowered.Contains("search") || lowered.Contains("url") || lowered.Contains("adres") || lowered.Contains("zoekbalk"))
                    {
                        Console.WriteLine("[UIA] Found address/search/url element: " + nameProp);
                        if (ae.TryGetCurrentPattern(ValuePattern.Pattern, out var vpObj) && vpObj is ValuePattern vp)
                        {
                            var text = vp.Current.Value ?? string.Empty;
                            Console.WriteLine("[UIA] ValuePattern text='" + text + "'");
                            var normalized = NormalizeUrlCandidate(text);
                            if (!string.IsNullOrEmpty(normalized)) return normalized;
                        }
                        else
                        {
                            Console.WriteLine("[UIA] ValuePattern not available; checking TextPattern...");
                            if (ae.TryGetCurrentPattern(TextPattern.Pattern, out var tpObj) && tpObj is TextPattern tp)
                            {
                                string text = tp.DocumentRange?.GetText(-1) ?? string.Empty;
                                Console.WriteLine("[UIA] TextPattern text='" + text + "'");
                                var normalized = NormalizeUrlCandidate(text);
                                if (!string.IsNullOrEmpty(normalized)) return normalized;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex) { Console.WriteLine("[UIA] Exception: " + ex.Message); }
        return null;
    }

    private static string? NormalizeUrlCandidate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim();
        // Allow missing scheme; prepend https:// when it looks like a host/path
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return text;
        // If it contains whitespace or clearly isn't a URL-like token, bail
        if (text.Contains(' ') || text.Contains('\n') || text.Contains('\t')) return null;
        // Heuristic: contains a dot or a slash, and no illegal characters
        if (text.Contains('.') || text.Contains('/'))
        {
            // Avoid common non-URL strings like search prompts
            if (text.Length < 3) return null;
            return "https://" + text;
        }
        return null;
    }
    
    private void ShowJudgmentalNotification((string Domain, string Category) siteInfo)
    {
        var messages = siteInfo.Category switch
        {
            "social" => new[]
            {
                "Your rat thinks you should touch grass 🌱",
                "Your rat is judging your social media addiction 📱",
                "Your rat says real friends don't live in your phone 👥",
                "Your rat is concerned about your dopamine levels 🧠"
            },
            "video" => new[]
            {
                "Your rat is judging your watch history 📺",
                "Your rat thinks you need a hobby 🎨",
                "Your rat says 'just one more video' is a lie ⏰",
                "Your rat counted your tabs... are you okay? 🤯"
            },
            "shopping" => new[]
            {
                "Your rat says you don't need more stuff 🛒",
                "Your rat is judging your impulse purchases 💸",
                "Your rat thinks your wallet is crying 😭",
                "Your rat says 'add to cart' is not a personality trait 🛍️"
            },
            "gaming" => new[]
            {
                "Your rat wants to play too! 🎮",
                "Your rat is judging your gaming addiction 🕹️",
                "Your rat says 'just one more game' is a trap 🎯",
                "Your rat thinks you need sunlight ☀️"
            },
            "work" => new[]
            {
                "Your rat is proud of your productivity 💼",
                "Your rat thinks you work too hard 😴",
                "Your rat says take a break! ☕",
                "Your rat is impressed by your dedication 👨‍💻"
            },
            "news" => new[]
            {
                "Your rat is concerned about your mental health 📰",
                "Your rat thinks you need less doomscrolling 😰",
                "Your rat says ignorance is bliss sometimes 🤷",
                "Your rat wants you to read something happy instead 📚"
            },
            "porn" => new[]
            {
                "Your rat is judging your porn addiction 🍆",
                "Your rat thinks you need to get a life 💔",
                "Your rat says 'just one more time' is a trap 🔞",
                "Your rat is concerned about your mental health 🧠",
                "Your rat thinks you should touh grass 🌱"

            },
            _ => new[]
            {
                "Your rat is judging your browsing habits 👀",
                "Your rat knows what you're doing 👁️",
                "Your rat is watching... always watching 👁️‍🗨️",
                "Your rat thinks you're being sneaky 🕵️"
            }
        };
        
        var message = messages[_judgmentRandom.Next(messages.Length)];
        // Show as system notification via App tray and as in-app bubble for flair
        try
        {
            (System.Windows.Application.Current as App)?.ShowTrayNotification("RatPet", message, System.Windows.Forms.ToolTipIcon.Info, 5000);
        }
        catch { }
        ShowJudgmentalBubble(message);
    }
    
    private void ShowJudgmentalBubble(string message)
    {
        BubbleText.Text = message;
        BubbleText.Foreground = new SolidColorBrush(System.Windows.Media.Colors.DarkRed);
        ShowBubble();
        // Reset color after showing
        BubbleText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x11, 0x11, 0x11));
    }

    // P/Invoke to get window under a point and adjust Z-order
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(System.Drawing.Point pt);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
    [DllImport("user32.dll")] private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    private static readonly IntPtr HWND_TOP = new IntPtr(0);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
    private static readonly uint SWP_NOSIZE = 0x0001;
    private static readonly uint SWP_NOMOVE = 0x0002;
    private static readonly uint SWP_NOACTIVATE = 0x0010;
    private const uint GA_ROOT = 2;
    private const uint WM_SYSCOMMAND = 0x0112;
    private static readonly IntPtr SC_MINIMIZE = new IntPtr(0xF020);
    private const int SM_CXSIZE = 30;
    private const int SM_CYSIZE = 31;
    private const int SM_CXFRAME = 32;
    private const int SM_CYFRAME = 33;
    private const int SM_CYCAPTION = 4;

    private void MaybeSneakBehindIfAtWindowEdge()
    {
        // Only when truly at the edge in the direction we are walking
        double chance = _sneakChance;
        // Use the rat edge in the direction of travel as probe, not center
        double rxLeft = Left;
        double rxRight = Left + Width;
        double ryTop = Top;
        double ryBottom = Top + Height;
        double midX = (rxLeft + rxRight) / 2.0;
        double midY = (ryTop + ryBottom) / 2.0;
        int probeOffset = (int)Math.Round(2 * _scale + 2);
        var probe = _walkDirection switch
        {
            Direction.Right => new System.Drawing.Point((int)(rxRight + probeOffset), (int)midY),
            Direction.Left => new System.Drawing.Point((int)(rxLeft - probeOffset), (int)midY),
            Direction.Up => new System.Drawing.Point((int)midX, (int)(ryTop - probeOffset)),
            Direction.Down => new System.Drawing.Point((int)midX, (int)(ryBottom + probeOffset)),
            _ => new System.Drawing.Point((int)midX, (int)midY)
        };
        var hWnd = WindowFromPoint(probe);
        if (hWnd == IntPtr.Zero) { Console.WriteLine($"[Sneak] No window at probe {probe}"); return; }

        var myHwnd = new WindowInteropHelper(this).Handle;
        if (hWnd == myHwnd) { Console.WriteLine("[Sneak] Probe hit self"); return; }
        if (_overlay != null)
        {
            var overlayHwnd = new WindowInteropHelper(_overlay).Handle;
            if (hWnd == overlayHwnd) { Console.WriteLine("[Sneak] Probe hit overlay; ignoring"); return; }
        }

        if (!GetWindowRect(hWnd, out RECT rect)) { Console.WriteLine("[Sneak] GetWindowRect failed"); return; }
        double left = rect.Left, top = rect.Top, right = rect.Right, bottom = rect.Bottom;
        double threshold = Math.Max(10.0, 8.0 * _scale + 0.5 * _speedPerTick);
        bool overlapsY = !(ryBottom < top || ryTop > bottom);
        bool overlapsX = !(rxRight < left || rxLeft > right);
        bool atEdge = _walkDirection switch
        {
            Direction.Right => Math.Abs(rxRight - left) <= threshold && overlapsY,
            Direction.Left => Math.Abs(rxLeft - right) <= threshold && overlapsY,
            Direction.Up => Math.Abs(ryTop - bottom) <= threshold && overlapsX,
            Direction.Down => Math.Abs(ryBottom - top) <= threshold && overlapsX,
            _ => false
        };
        Console.WriteLine($"[Sneak] dir={_walkDirection} probe=({probe.X},{probe.Y}) rect=({left},{top},{right},{bottom}) rxL={rxLeft:F0} rxR={rxRight:F0} ryT={ryTop:F0} ryB={ryBottom:F0} atEdge={atEdge}");
        if (!atEdge) { _sneakEdgeStreak = 0; return; }
        _sneakEdgeStreak = Math.Min(_sneakEdgeStreak + 1, 10);
        var roll = _random.NextDouble();
        Console.WriteLine($"[Sneak] streak={_sneakEdgeStreak} roll={roll:F2} chance={chance}");
        //if (_sneakEdgeStreak < 3 && roll > chance) return; // allow a couple frames to build
        //if (_sneakEdgeStreak < 6 && roll > chance) return; // extra bias: after ~100ms still mostly allow

        bool goBehind = _random.NextDouble() < 0.5;
        if (goBehind)
        {
            _isBehindSpecificWindow = true;
            var topLevel = GetAncestor(hWnd, GA_ROOT);
            if (topLevel == IntPtr.Zero) topLevel = hWnd;
            _behindTarget = topLevel;
            Topmost = false;
            SetWindowPos(myHwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            var ok = SetWindowPos(myHwnd, topLevel, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            Console.WriteLine($"[Sneak] slipping behind target window topLevel=0x{topLevel.ToInt64():X} ok={ok}");
        }
        else
        {
            Topmost = true;
            SetWindowPos(myHwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            Console.WriteLine("[Sneak] staying on top of target window");
        }
    }

    private void MaybeStartMischiefMinimize()
    {
        if (!_mischiefEnabled || _mischiefActive) return;
        // Small random chance each walk tick when near a window title bar region
        // Scale mischief probability lightly with speed so fast rats attempt slightly more
        double attempt = (_mischiefChance * (_chaosMode ? 2.0 : 1.0)) * Math.Max(0.6, Math.Min(1.6, _speedPerTick / 3.0));
        if (_random.NextDouble() > attempt) return;

        // Look ahead in walking direction and choose that window's minimize button corner
        double rxLeft = Left;
        double rxRight = Left + Width;
        double ryTop = Top;
        double ryBottom = Top + Height;
        double midX = (rxLeft + rxRight) / 2.0;
        double midY = (ryTop + ryBottom) / 2.0;
        var probe = _walkDirection switch
        {
            Direction.Right => new System.Drawing.Point((int)(rxRight + 8), (int)midY),
            Direction.Left => new System.Drawing.Point((int)(rxLeft - 8), (int)midY),
            Direction.Up => new System.Drawing.Point((int)midX, (int)(ryTop - 8)),
            Direction.Down => new System.Drawing.Point((int)midX, (int)(ryBottom + 8)),
            _ => new System.Drawing.Point((int)midX, (int)midY)
        };
        var hWnd = WindowFromPoint(probe);
        if (hWnd == IntPtr.Zero) return;
        var myHwnd = new WindowInteropHelper(this).Handle;
        if (hWnd == myHwnd) return;
        if (_overlay != null)
        {
            var overlayHwnd = new WindowInteropHelper(_overlay).Handle;
            if (hWnd == overlayHwnd) return;
        }
        if (!GetWindowRect(hWnd, out RECT rect)) return;

        // Compute minimize button center using system metrics (per-window DPI aware)
        var dpi = GetDpiForWindow(hWnd);
        int cxSize = GetSystemMetricsForDpi != null ? GetSystemMetricsForDpi(SM_CXSIZE, dpi) : GetSystemMetrics(SM_CXSIZE);
        int cySize = GetSystemMetricsForDpi != null ? GetSystemMetricsForDpi(SM_CYSIZE, dpi) : GetSystemMetrics(SM_CYSIZE);
        int cyCaption = GetSystemMetricsForDpi != null ? GetSystemMetricsForDpi(SM_CYCAPTION, dpi) : GetSystemMetrics(SM_CYCAPTION);
        // Caption buttons are [Min][Max][Close] from left to right at the top-right
        // Click roughly at the center of the Minimize button area
        int buttonRight = rect.Right - cxSize * 3 + cxSize; // right edge of Minimize area
        int buttonCenterX = buttonRight - cxSize / 2;
        int buttonCenterY = rect.Top + cyCaption / 2;
        _mischiefX = buttonCenterX;
        _mischiefY = buttonCenterY;
        _mischiefTarget = hWnd;
        _mischiefActive = true;
        Console.WriteLine($"[Mischief] target=0x{hWnd.ToInt64():X} button=({_mischiefX},{_mischiefY})");
    }

    private void MinimizeWindow(IntPtr hWnd)
    {
        try
        {
            var topLevel = GetAncestor(hWnd, GA_ROOT);
            if (topLevel == IntPtr.Zero) topLevel = hWnd;
            SendMessage(topLevel, WM_SYSCOMMAND, SC_MINIMIZE, IntPtr.Zero);
            Console.WriteLine($"[Mischief] minimize sent to 0x{topLevel.ToInt64():X}");
        }
        catch { }
    }

    private void ReemergeToTop()
    {
        if (!_isBehindSpecificWindow) return;
        var myHwnd = new WindowInteropHelper(this).Handle;
        Topmost = true;
        SetWindowPos(myHwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        _isBehindSpecificWindow = false;
        _behindTarget = IntPtr.Zero;
    }

    private void EnsureOverlayBelowRat()
    {
        try
        {
            if (_overlay == null) return;
            var ratHwnd = new WindowInteropHelper(this).Handle;
            var overlayHwnd = new WindowInteropHelper(_overlay).Handle;
            // Place overlay just below rat in Z order
            SetWindowPos(overlayHwnd, ratHwnd, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch { }
    }

    private void MaybeRunChaosAction()
    {
        if (!_chaosMode) return;
        if (DateTime.UtcNow < _nextChaosActionAt) return;
        _nextChaosActionAt = DateTime.UtcNow.AddSeconds(_random.NextDouble() < 0.5 ? 6 : 10);

        // 1/3 jiggle, 1/3 promote, 1/3 zoomies burst
        double roll = _random.NextDouble();
        if (roll < 0.33)
        {
            JiggleActiveWindowBurst();
        }
        else if (roll < 0.66)
        {
            PromoteRandomNearbyWindow();
        }
        else
        {
            StartZoomiesBurst();
        }
    }

    private void JiggleActiveWindowBurst()
    {
        try
        {
            var target = GetForegroundWindow();
            if (target == IntPtr.Zero) return;
            if (!GetWindowRect(target, out _jiggleOrig)) return;
            // Defer jiggle until rat arrives at window center
            _chaosPendingJiggle = true;
            _chaosPendingPromote = false;
            _chaosPendingHwnd = target;
            _chaosPendingRect = _jiggleOrig;
            RunTowardRect(_jiggleOrig);
        }
        catch { }
    }

    private void OnJiggleTick(object? sender, EventArgs e)
    {
        if (_jiggleTarget == IntPtr.Zero || _jiggleTicks > 30)
        {
            _jiggleTimer?.Stop();
            _jiggleTarget = IntPtr.Zero;
            return;
        }
        int amp = 6;
        int dx = _random.Next(-amp, amp + 1);
        int dy = _random.Next(-amp, amp + 1);
        int x = _jiggleOrig.Left + dx;
        int y = _jiggleOrig.Top + dy;
        int w = _jiggleOrig.Right - _jiggleOrig.Left;
        int h = _jiggleOrig.Bottom - _jiggleOrig.Top;
        SetWindowPos(_jiggleTarget, IntPtr.Zero, x, y, w, h, 0);
        _jiggleTicks++;
    }

    private void PromoteRandomNearbyWindow()
    {
        try
        {
            var p = System.Windows.Forms.Control.MousePosition;
            var pt = new System.Drawing.Point(p.X + _random.Next(-300, 301), p.Y + _random.Next(-200, 201));
            var h = WindowFromPoint(pt);
            if (h == IntPtr.Zero) return;
            var top = GetAncestor(h, GA_ROOT);
            if (top == IntPtr.Zero) top = h;
            var myHwnd = new WindowInteropHelper(this).Handle;
            if (top == myHwnd) return;
            if (_overlay != null)
            {
                var overlayHwnd = new WindowInteropHelper(_overlay).Handle;
                if (top == overlayHwnd) return;
            }
            if (GetWindowRect(top, out RECT r))
            {
                // Defer promotion until arrival
                _chaosPendingPromote = true;
                _chaosPendingJiggle = false;
                _chaosPendingHwnd = top;
                _chaosPendingRect = r;
                RunTowardRect(r);
            }
        }
        catch { }
    }

    private void RunTowardRect(RECT rect)
    {
        // Run toward the center of a window rectangle
        double centerX = (rect.Left + rect.Right) / 2.0;
        double centerY = (rect.Top + rect.Bottom) / 2.0;
        double vsLeft = SystemParameters.VirtualScreenLeft;
        double vsTop = SystemParameters.VirtualScreenTop;
        double vsRight = vsLeft + SystemParameters.VirtualScreenWidth;
        double vsBottom = vsTop + SystemParameters.VirtualScreenHeight;
        _targetX = Math.Max(vsLeft, Math.Min(vsRight - Width, centerX - Width / 2));
        _targetY = Math.Max(vsTop, Math.Min(vsBottom - Height, centerY - Height / 2));
        _state = PetState.Walk;
        _stateUntil = DateTime.MaxValue;
        UpdateDirectionTowardTarget();
    }

    private void StartZoomiesBurst()
    {
        // High-energy sprints across random screen points for ~2-3 seconds
        _zoomiesActive = true;
        _zoomiesUntil = DateTime.UtcNow.AddSeconds(2.5);
        _zoomiesHopsRemaining = 8;
        _nextZoomiesRetargetAt = DateTime.MinValue;
        Console.WriteLine("[Chaos] Zoomies start");
    }

    private void RunZoomiesTick()
    {
        if (!_zoomiesActive) return;
        if (DateTime.UtcNow >= _zoomiesUntil || _zoomiesHopsRemaining <= 0)
        {
            _zoomiesActive = false;
            Console.WriteLine("[Chaos] Zoomies end");
            return;
        }
        if (DateTime.UtcNow >= _nextZoomiesRetargetAt)
        {
            // retarget to a far random corner/edge point
            double vsLeft = SystemParameters.VirtualScreenLeft;
            double vsTop = SystemParameters.VirtualScreenTop;
            double vsRight = vsLeft + SystemParameters.VirtualScreenWidth;
            double vsBottom = vsTop + SystemParameters.VirtualScreenHeight;
            // choose random edge point to maximize long sprints
            int edge = _random.Next(4);
            double tx = Left, ty = Top;
            if (edge == 0) { tx = vsLeft; ty = _random.Next((int)vsTop, (int)(vsBottom - Height)); }
            else if (edge == 1) { tx = vsRight - Width; ty = _random.Next((int)vsTop, (int)(vsBottom - Height)); }
            else if (edge == 2) { tx = _random.Next((int)vsLeft, (int)(vsRight - Width)); ty = vsTop; }
            else { tx = _random.Next((int)vsLeft, (int)(vsRight - Width)); ty = vsBottom - Height; }
            _targetX = tx; _targetY = ty;
            _state = PetState.Walk; _stateUntil = DateTime.MaxValue; UpdateDirectionTowardTarget();
            _nextZoomiesRetargetAt = DateTime.UtcNow.AddMilliseconds(350);
            _zoomiesHopsRemaining--;
        }
    }

    // Debug console toggle (process-wide)
    [DllImport("kernel32.dll")] private static extern bool AllocConsole();
    [DllImport("kernel32.dll")] private static extern bool FreeConsole();
    [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
    
    // Browser stalking APIs
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr hObject);
    [DllImport("psapi.dll")] private static extern uint GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, System.Text.StringBuilder lpFilename, uint nSize);
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_VM_READ = 0x0010;
    public void ToggleDebugConsole(bool enabled)
    {
        try
        {
            var hwnd = GetConsoleWindow();
            if (enabled && hwnd == IntPtr.Zero)
            {
                if (AllocConsole())
                {
                    var stdout = Console.OpenStandardOutput();
                    var writer = new StreamWriter(stdout) { AutoFlush = true };
                    Console.SetOut(writer);
                    Console.SetError(writer);
                    Console.WriteLine("[RatPet] Debug console attached.");
                }
            }
            if (!enabled && hwnd != IntPtr.Zero)
            {
                Console.WriteLine("[RatPet] Debug console detached.");
                FreeConsole();
            }
        }
        catch { }
    }

    public string GetPhotosFolder()
    {
        try
        {
            var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Photos");
            Directory.CreateDirectory(dir);
            return dir;
        }
        catch { return AppContext.BaseDirectory; }
    }

    public string GetDiaryFolder()
    {
        try
        {
            var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "RatDiary");
            Directory.CreateDirectory(dir);
            return dir;
        }
        catch { return AppContext.BaseDirectory; }
    }
    
    public string GetStatsFolder()
    {
        try
        {
            var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "RatStats");
            Directory.CreateDirectory(dir);
            return dir;
        }
        catch { return AppContext.BaseDirectory; }
    }
    
    private void MaybeWriteDiaryEntry()
    {
        try
        {
            // Don't write too frequently
            if (DateTime.UtcNow < _lastDiaryEntry.AddMinutes(2)) return;
            
            var currentActivity = ObserveCurrentActivity();
            if (string.IsNullOrEmpty(currentActivity) || currentActivity == _lastObservedActivity) return;
            
            _lastObservedActivity = currentActivity;
            _lastDiaryEntry = DateTime.UtcNow;
            
            WriteDiaryEntry(currentActivity);
        }
        catch { }
    }
    
    private void UpdateActivityStats()
    {
        try
        {
            var currentActivity = ObserveCurrentActivity();
            if (string.IsNullOrEmpty(currentActivity)) return;
            
            // If activity changed, save the previous one
            if (_currentActivity != currentActivity)
            {
                if (!string.IsNullOrEmpty(_currentActivity))
                {
                    var duration = DateTime.UtcNow - _currentActivityStart;
                    if (_activityStats.ContainsKey(_currentActivity))
                        _activityStats[_currentActivity] += duration;
                    else
                        _activityStats[_currentActivity] = duration;
                    
                    Console.WriteLine($"[Stats] Activity '{_currentActivity}' tracked for {duration.TotalSeconds:F1}s");
                }
                
                _currentActivity = currentActivity;
                _currentActivityStart = DateTime.UtcNow;
                Console.WriteLine($"[Stats] Started tracking '{currentActivity}'");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Stats] Error updating stats: {ex.Message}");
        }
    }
    
    internal void GenerateRatStatsReport()
    {
        try
        {
            // Save current activity before generating report
            if (!string.IsNullOrEmpty(_currentActivity))
            {
                var duration = DateTime.UtcNow - _currentActivityStart;
                if (_activityStats.ContainsKey(_currentActivity))
                    _activityStats[_currentActivity] += duration;
                else
                    _activityStats[_currentActivity] = duration;
            }
            
            var folder = GetStatsFolder();
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var file = System.IO.Path.Combine(folder, $"rat_stats_{timestamp}.txt");
            
            var totalTime = DateTime.UtcNow - _statsStartTime;
            var sortedStats = _activityStats.OrderByDescending(kvp => kvp.Value).ToList();
            
            Console.WriteLine($"[Stats] Total time: {totalTime}, Activities tracked: {sortedStats.Count}");
            foreach (var stat in sortedStats)
            {
                Console.WriteLine($"[Stats] {stat.Key}: {stat.Value}");
            }
            
            var report = $"🐀 RAT STATISTICS REPORT 🐀\n";
            report += $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n";
            report += $"Total observation time: {totalTime:hh\\:mm\\:ss}\n\n";
            
            if (sortedStats.Count == 0)
            {
                report += "📊 NO ACTIVITIES TRACKED YET!\n";
                report += "=" + new string('=', 50) + "\n\n";
                report += "🐀 RAT ANALYSIS:\n";
                report += "I haven't observed any activities yet! Maybe my human is too sneaky?\n";
                report += "Or maybe I need to watch more carefully... >:3\n\n";
                report += "Try doing some activities and I'll start tracking them!\n\n";
            }
            else
            {
                report += "📊 ACTIVITY BREAKDOWN:\n";
                report += "=" + new string('=', 50) + "\n\n";
                
                foreach (var stat in sortedStats)
                {
                    var percentage = (stat.Value.TotalSeconds / totalTime.TotalSeconds) * 100;
                    var hours = (int)stat.Value.TotalHours;
                    var minutes = stat.Value.Minutes;
                    var seconds = stat.Value.Seconds;
                    
                    report += $"🎯 {stat.Key}\n";
                    report += $"   Time: {hours:00}:{minutes:00}:{seconds:00}\n";
                    report += $"   Percentage: {percentage:F1}%\n";
                    report += $"   Bar: {new string('█', (int)(percentage / 2))}{new string('░', 25 - (int)(percentage / 2))}\n\n";
                }
                
                // Rat commentary
                report += "🐀 RAT ANALYSIS:\n";
                report += "=" + new string('=', 50) + "\n\n";
                
                var topActivity = sortedStats.FirstOrDefault();
                if (topActivity.Key != null)
                {
                    var commentaries = new[]
                    {
                        $"My human spends most of their time {topActivity.Key}! That's {topActivity.Value.TotalHours:F1} hours! I'm so proud! >:3",
                        $"The data shows my human is obsessed with {topActivity.Key}. I approve! Maybe I should join them more often!",
                        $"After careful observation, I conclude: my human is a {topActivity.Key} expert! They should teach me!",
                        $"Statistics don't lie: my human loves {topActivity.Key}! I should probably cause more chaos during this activity!",
                        $"The numbers speak for themselves: {topActivity.Key} is clearly my human's favorite pastime. I'm taking notes! >:3"
                    };
                    
                    report += commentaries[_judgmentRandom.Next(commentaries.Length)] + "\n\n";
                }
                
                // Fun facts
                report += "🎲 FUN FACTS:\n";
                report += "=" + new string('=', 50) + "\n\n";
                
                var funFacts = new[]
                {
                    $"• I observed {sortedStats.Count} different activities today!",
                    $"• My human switched activities {sortedStats.Count - 1} times!",
                    $"• The longest single session was {sortedStats.Max(kvp => kvp.Value.TotalMinutes):F1} minutes!",
                    $"• I'm getting really good at this statistics thing!",
                    $"• Maybe I should become a data analyst rat! >:3"
                };
                
                foreach (var fact in funFacts)
                {
                    report += fact + "\n";
                }
            }
            
            report += "\n🐀 End of Report 🐀\n";
            report += "Love, Your Statistical Rat >:3";
            
            File.WriteAllText(file, report);
            
            // Show bubble
            BubbleText.Text = "stats report ready! >:3";
            ShowBubble();
            
            Console.WriteLine($"[Stats] Report generated: {file}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Stats] Error generating report: {ex.Message}");
        }
    }
    
    private string ObserveCurrentActivity()
    {
        try
        {
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return "";
            
            var title = GetWindowTitle(fg);
            if (string.IsNullOrEmpty(title)) return "";
            
            // Determine what the human is doing based on window title
            var activity = AnalyzeActivity(title);
            return activity;
        }
        catch { return ""; }
    }
    
    private string AnalyzeActivity(string windowTitle)
    {
        var title = windowTitle.ToLowerInvariant();
        
        // Browser activities
        if (title.Contains("chrome") || title.Contains("firefox") || title.Contains("edge") || title.Contains("brave"))
        {
            if (title.Contains("youtube")) return "watching videos";
            if (title.Contains("twitter") || title.Contains("x.com")) return "scrolling social media";
            if (title.Contains("reddit")) return "browsing reddit";
            if (title.Contains("github")) return "coding or browsing code";
            if (title.Contains("stackoverflow")) return "looking for coding help";
            if (title.Contains("gmail") || title.Contains("outlook")) return "checking email";
            if (title.Contains("amazon") || title.Contains("shop")) return "shopping online";
            if (title.Contains("netflix") || title.Contains("hulu") || title.Contains("disney")) return "streaming shows";
            if (title.Contains("porn")) return "browsing adult content";
            return "browsing the internet";
        }
        
        // Work applications
        if (title.Contains("visual studio") || title.Contains("code")) return "writing code";
        if (title.Contains("cursor")) return "writing code with Cursor";
        if (title.Contains("intellij")) return "coding in IntelliJ";
        if (title.Contains("eclipse")) return "coding in Eclipse";
        if (title.Contains("sublime text")) return "coding in Sublime Text";
        if (title.Contains("atom")) return "coding in Atom";
        if (title.Contains("vim")) return "coding in Vim";
        if (title.Contains("emacs")) return "coding in Emacs";
        if (title.Contains("notepad++")) return "coding in Notepad++";
        if (title.Contains("git")) return "using Git";
        if (title.Contains("github desktop")) return "managing repositories";
        if (title.Contains("word") || title.Contains("document")) return "writing documents";
        if (title.Contains("excel") || title.Contains("spreadsheet")) return "working with spreadsheets";
        if (title.Contains("powerpoint") || title.Contains("presentation")) return "making presentations";
        if (title.Contains("photoshop") || title.Contains("gimp")) return "editing images";
        if (title.Contains("blender") || title.Contains("maya")) return "creating 3D art";
        if (title.Contains("figma")) return "designing in Figma";
        if (title.Contains("sketch")) return "designing in Sketch";
        if (title.Contains("adobe illustrator")) return "creating vector art";
        if (title.Contains("adobe indesign")) return "designing layouts";
        if (title.Contains("adobe premiere")) return "editing videos";
        if (title.Contains("adobe after effects")) return "creating animations";
        if (title.Contains("davinci resolve")) return "editing videos";
        if (title.Contains("audacity")) return "editing audio";
        if (title.Contains("obs")) return "streaming or recording";
        if (title.Contains("powershell")) return "running PowerShell commands";
        if (title.Contains("cmd")) return "running command prompt commands";
        if (title.Contains("terminal")) return "running terminal commands";

        // Specific games
        if (title.Contains("minecraft")) return "playing Minecraft";
        if (title.Contains("roblox")) return "playing Roblox";
        if (title.Contains("fortnite")) return "playing Fortnite";
        if (title.Contains("league of legends")) return "playing League of Legends";
        if (title.Contains("valorant")) return "playing Valorant";
        if (title.Contains("overwatch")) return "playing Overwatch";
        if (title.Contains("csgo")) return "playing CS:GO";
        if (title.Contains("dota 2")) return "playing Dota 2";
        if (title.Contains("wow")) return "playing World of Warcraft";
        if (title.Contains("warcraft")) return "playing Warcraft";
        if (title.Contains("starcraft")) return "playing Starcraft";
        if (title.Contains("starfield")) return "playing Starfield";
        if (title.Contains("elden ring")) return "playing Elden Ring";
        if (title.Contains("the witcher")) return "playing The Witcher";
        if (title.Contains("the last of us")) return "playing The Last of Us";
        if (title.Contains("the last of us part 2")) return "playing The Last of Us Part 2";
        if (title.Contains("the last of us part 1")) return "playing The Last of Us Part 1";

        // Applications
        if (title.Contains("notepad")) return "writing in Notepad";
        if (title.Contains("paint")) return "drawing in Paint";
        if (title.Contains("paint.net")) return "drawing in Paint.NET";
        if (title.Contains("Aseprite")) return "drawing in Aseprite";
        if (title.Contains("Krita")) return "drawing in Krita";
        if (title.Contains("GIMP")) return "drawing in GIMP";
        if (title.Contains("discord")) return "chatting on Discord";
        if (title.Contains("slack")) return "working on Slack";
        if (title.Contains("teams")) return "in a Teams meeting";
        if (title.Contains("zoom")) return "in a Zoom meeting";
        if (title.Contains("skype")) return "on a Skype call";
        if (title.Contains("whatsapp")) return "using WhatsApp";
        if (title.Contains("telegram")) return "using Telegram";
        if (title.Contains("trello")) return "organizing tasks";
        if (title.Contains("asana")) return "managing projects";
        if (title.Contains("notion")) return "taking notes";
        if (title.Contains("evernote")) return "organizing notes";
        if (title.Contains("onenote")) return "using OneNote";
        if (title.Contains("calendar")) return "checking calendar";
        if (title.Contains("spotify")) return "listening to music";
        if (title.Contains("vlc")) return "watching videos";
        if (title.Contains("media player")) return "watching media";
        if (title.Contains("twitch")) return "watching streams";


        
        // System
        if (title.Contains("settings") || title.Contains("control panel")) return "changing system settings";
        if (title.Contains("file explorer") || title.Contains("explorer")) return "organizing files";
        
        return "doing something mysterious";
    }
    
    private void WriteDiaryEntry(string activity)
    {
        try
        {
            var folder = GetDiaryFolder();
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var file = System.IO.Path.Combine(folder, $"entry_{timestamp}.txt");
            
            var entry = GenerateDiaryEntry(activity);
            
            File.WriteAllText(file, entry);
            
            // Show a tiny bubble
            BubbleText.Text = "wrote in diary! >:3";
            ShowBubble();
            
            Console.WriteLine($"[Diary] Entry written: {file} - Activity: {activity}");
        }
        catch { }
    }
    
    private string GenerateDiaryEntry(string activity)
    {
        var entries = GetDiaryEntriesForActivity(activity);
        return entries[_judgmentRandom.Next(entries.Length)];
    }
    
    private string[] GetDiaryEntriesForActivity(string activity)
    {
        // Coding activities
        if (activity.Contains("coding") || activity.Contains("writing code"))
        {
            return new[]
            {
                $"Dear Diary,\nMy human is {activity} again! They seem really focused. I wonder if they're building something amazing... Maybe I should leave some footprints on their keyboard to help! >:3\n\nLove,\nYour Rat",
                $"Diary Entry #{DateTime.Now:yyyyMMdd}\n\nHuman activity: {activity}\nRat observation: They keep typing really fast! I think they're in the zone. Time to cause some chaos to keep them entertained!\n\n- Rat",
                $"Observations:\nTime: {DateTime.Now:HH:mm}\nActivity: {activity}\n\nMy human looks so serious while coding! I should probably minimize some windows to remind them I'm here! >:3",
                $"Today's Report:\n\nHuman: {activity}\nRat status: Being adorable\nChaos level: About to increase\n\nI think my human needs a break. Time to jiggle some windows! >:3",
                $"Dear Diary,\nMy human is {activity} again. They keep mumbling about 'bugs'—but I haven’t seen any! Should I go look for them? Maybe chew on some cables just in case! >:3\n\nLove,\nRat",
                $"Rat Log #{DateTime.Now:HHmm}\nActivity detected: {activity}\n\nHuman appears frustrated. Possibly debugging. Recommend distraction: knock pen off desk.\n\n- Rat Operations",
                $"Observation:\nHuman: {activity}\nProgress: Unknown\nSanity: Questionable\n\nI offered moral support by sitting on the keyboard. They did not appreciate my contribution.",
                $"Dear Diary,\nThey’re {activity}, but they’ve been staring at the same line of code for five minutes... maybe I should press Enter for them! Teamwork! >:3"
            };
        }
        
        // Gaming activities
        if (activity.Contains("playing"))
        {
            return new[]
            {
                $"Dear Diary,\nMy human is {activity}! They look so excited! I wonder if I can join them... Maybe I should run around really fast to match their energy! >:3\n\nLove,\nYour Rat",
                $"Diary Entry #{DateTime.Now:yyyyMMdd}\n\nHuman activity: {activity}\nRat observation: They're clicking and moving the mouse so much! I should probably chase the cursor to help!\n\n- Rat",
                $"Observations:\nTime: {DateTime.Now:HH:mm}\nActivity: {activity}\n\nMy human is having fun! I should probably take a selfie to commemorate this gaming session! >:3",
                $"Today's Report:\n\nHuman: {activity}\nRat status: Ready to cause chaos\nChaos level: High\n\nGaming time means chaos time! Let's minimize some windows! >:3",
                $"Dear Diary,\nMy human is {activity}! Every time they win, they yell loudly. I yelled back! Now we’re both victorious! >:3\n\nLove,\nRat",
                $"Observation Time {DateTime.Now:HH:mm}\nGame mode: CHAOS\n\nHuman is {activity}. I contributed by walking on the keyboard — we lost instantly. Success!",
                $"Dear Diary,\nI saw my human jump in surprise while gaming. I think the scary part got them. I squeaked in solidarity!\n\nLove,\nRat",
                $"Diary Entry #{DateTime.Now:yyyyMMdd}\n\nMy human is {activity}. I think they’re ignoring me... maybe if I unplug the controller, they’ll notice me again!"

            };
        }
        
        // Social media/browsing
        if (activity.Contains("social") || activity.Contains("browsing") || activity.Contains("watching"))
        {
            return new[]
            {
                $"Dear Diary,\nMy human is {activity}. They seem to be scrolling a lot! I wonder what they're looking at... Maybe I should peek over their shoulder! >:3\n\nLove,\nYour Rat",
                $"Diary Entry #{DateTime.Now:yyyyMMdd}\n\nHuman activity: {activity}\nRat observation: They're scrolling so much! I should probably run around to match their scrolling energy!\n\n- Rat",
                $"Observations:\nTime: {DateTime.Now:HH:mm}\nActivity: {activity}\n\nMy human is consuming content! I should probably create some content too... like footprints! >:3",
                $"Today's Report:\n\nHuman: {activity}\nRat status: Being nosy\nChaos level: Moderate\n\nTime to interrupt their browsing with some window chaos! >:3",
                $"Dear Diary,\nMy human is {activity}. They keep looking at other animals online... should I be jealous? Maybe I’ll close the tab for them. >:3",
                $"Rat Log #{DateTime.Now:HHmm}\nActivity: {activity}\n\nHuman scrolled past 432 posts in 2 minutes. Truly a display of endurance.",
                $"Dear Diary,\nI saw them watching a video of a cat! Betrayal! I will retaliate with crumbs on the keyboard. Revenge will be swift. >:3",
                $"Observation:\nTime: {DateTime.Now:HH:mm}\n\nHuman is {activity}. I wonder if they’d enjoy watching ME instead! Perhaps I’ll block the screen with my face!"

            };
        }
        
        // Work/productivity
        if (activity.Contains("writing") || activity.Contains("working") || activity.Contains("managing") || activity.Contains("organizing"))
        {
            return new[]
            {
                $"Dear Diary,\nMy human is {activity}. They look so focused and productive! I'm proud of them, but they also need some entertainment... Time for chaos! >:3\n\nLove,\nYour Rat",
                $"Diary Entry #{DateTime.Now:yyyyMMdd}\n\nHuman activity: {activity}\nRat observation: They're being so responsible! I should probably remind them to have fun by causing some mischief!\n\n- Rat",
                $"Observations:\nTime: {DateTime.Now:HH:mm}\nActivity: {activity}\n\nMy human is working hard! I should probably take a selfie to show them how cute I am while they work! >:3",
                $"Today's Report:\n\nHuman: {activity}\nRat status: Proud but mischievous\nChaos level: About to spike\n\nProductivity is great, but so is chaos! Let's mix it up! >:3",
                $"Dear Diary,\nMy human is {activity}. They look so tired... I brought them a crumb. It rolled off the table, but the thought counts! >:3",
                $"Diary Entry #{DateTime.Now:yyyyMMdd}\n\nActivity: {activity}\n\nThey’ve been at it for hours. I’m starting to think they might be part rat too — hoarding tasks endlessly!",
                $"Observation Log:\nHuman: {activity}\n\nThey sighed at least 14 times. I added a cheerful squeak counterpoint. Morale restored!",
                $"Dear Diary,\nThey’re {activity} again! Papers everywhere! I climbed on one to assist with organization. Mission successful (sort of). >:3"
            };
        }
        
        // Communication
        if (activity.Contains("chatting") || activity.Contains("meeting") || activity.Contains("call"))
        {
            return new[]
            {
                $"Dear Diary,\nMy human is {activity}! They're talking to someone else... I'm a little jealous! Maybe I should make some noise to remind them I'm here! >:3\n\nLove,\nYour Rat",
                $"Diary Entry #{DateTime.Now:yyyyMMdd}\n\nHuman activity: {activity}\nRat observation: They're talking to other humans! I should probably cause some chaos to get their attention!\n\n- Rat",
                $"Observations:\nTime: {DateTime.Now:HH:mm}\nActivity: {activity}\n\nMy human is socializing! I should probably join in by running around really fast! >:3",
                $"Today's Report:\n\nHuman: {activity}\nRat status: Feeling left out\nChaos level: High\n\nTime to remind them who's the cutest! Window chaos incoming! >:3",
                $"Dear Diary,\nMy human is {activity}. They’re wearing their ‘serious face’. I tried to match it, but then sneezed. Professionalism ruined. >:3",
                $"Observation:\nActivity: {activity}\n\nHuman says ‘Can you hear me?’ a lot. I can hear them fine. Should I answer next time?",
                $"Dear Diary,\nThey’re {activity} again. I squeaked during their meeting and now everyone knows I exist. Fame achieved!",
                $"Rat Log:\nHuman: {activity}\nOutcome: I interrupted.\nConclusion: Attention successfully acquired. Mission accomplished."

            };
        }
        
        // Default entries for any other activity
        return new[]
        {
            $"Dear Diary,\nToday I saw my human {activity}. They seem very focused on this task. I wonder what they're thinking about...\n\nLove,\nYour Rat >:3",
            $"Diary Entry #{DateTime.Now:yyyyMMdd}\n\nMy human is currently {activity}. I've been watching them for a while now. They look so serious! Maybe I should cause some chaos to lighten the mood...\n\n- Rat",
            $"Observations:\nTime: {DateTime.Now:HH:mm}\nActivity: {activity}\n\nMy human seems busy. I should probably leave footprints everywhere to remind them I'm here! >:3",
            $"Today's Report:\n\nHuman activity: {activity}\nRat status: Being adorable\nChaos level: Moderate\n\nI think my human needs more entertainment. Time to minimize some windows! >:3",
            $"Diary,\n\nI caught my human {activity} again. They do this a lot! Maybe I should take a selfie to commemorate this moment...\n\nYour faithful observer,\nThe Rat",
            $"Dear Diary,\nMy human was {activity}. I watched closely, then decided to nap halfway through. Observation suspended. Zzz... >:3",
            $"Observation #{DateTime.Now:HHmm}\nHuman: {activity}\n\nThey seemed calm today. Suspicious. I’ll increase chaos output tomorrow.",
            $"Dear Diary,\nToday’s highlight: my human {activity}, and I found a crumb. Both of us were productive in our own ways.",
            $"Rat Report:\nActivity: {activity}\nRat contribution: Spectating\nEfficiency: 110%\n\nFurther chaos scheduled for later."
        };
    }

    // Accept dragged inventory items and spawn corresponding toy/prop
    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent("RatPetItem"))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("RatPetItemUri")) return;
        var uriStr = e.Data.GetData("RatPetItemUri") as string;
        if (string.IsNullOrEmpty(uriStr)) return;
        try
        {
            var bmp = new BitmapImage(new Uri(uriStr));
            _overlay?.ShowToy(NormalizeDpi(bmp), Left, Top);
        }
        catch { }
    }
}