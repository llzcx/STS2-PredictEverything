using Godot;

namespace PredictEverything;

/// <summary>
/// Reusable drag handler for Godot controls. Attaches to any panel with a
/// draggable header area. Uses a timer-polled state machine with a 0.3s
/// hold-to-activate delay (same as RoutePlanner) to avoid accidental drags.
/// Call Init once, then Start() to enable. Stop() pauses; Start() resumes.
/// </summary>
public class DragHandler
{
    private readonly Control _panel;
    private readonly Control _header;
    private readonly Timer _pollTimer;
    private readonly System.Action? _onDragStart;
    private readonly System.Action? _onDragEnd;

    private enum State { None, Waiting, Dragging }
    private State _state;
    private float _holdTimer;
    private Vector2 _dragStartMouse;
    private Vector2 _dragStartPanel;
    private bool _started;

    private const float DragThreshold = 4f;
    private const float HoldTime = 0.15f;
    private const float PollInterval = 0.05f;

    public DragHandler(Control panel, Control header, System.Action? onDragStart = null, System.Action? onDragEnd = null)
    {
        _panel = panel;
        _header = header;
        _onDragStart = onDragStart;
        _onDragEnd = onDragEnd;

        _pollTimer = new Timer
        {
            WaitTime = PollInterval,
            OneShot = false,
        };
        _pollTimer.Timeout += OnPoll;
        panel.AddChild(_pollTimer);
    }

    public void Start()
    {
        _started = true;
        _state = State.None;
        _pollTimer.Start();
    }

    public void Stop()
    {
        _started = false;
        _state = State.None;
        _pollTimer.Stop();
        _panel.MouseDefaultCursorShape = Control.CursorShape.Arrow;
    }

    private void OnPoll()
    {
        if (!_started || !_panel.Visible || !_panel.IsInsideTree()) return;

        var mousePos = _panel.GetGlobalMousePosition();
        bool leftDown = Input.IsMouseButtonPressed(MouseButton.Left);
        bool inHeader = IsInHeader(mousePos);

        if (leftDown)
        {
            if (_state == State.None)
            {
                if (!inHeader) return;
                _state = State.Waiting;
                _holdTimer = 0f;
                _dragStartMouse = mousePos;
                _dragStartPanel = _panel.Position;
            }
            else if (_state == State.Waiting)
            {
                if ((mousePos - _dragStartMouse).Length() > DragThreshold)
                {
                    _state = State.None;
                    return;
                }
                _holdTimer += PollInterval;
                if (_holdTimer >= HoldTime)
                {
                    _state = State.Dragging;
                    _panel.MouseDefaultCursorShape = Control.CursorShape.Drag;
                    _onDragStart?.Invoke();
                }
            }
            else if (_state == State.Dragging)
            {
                _panel.Position = _dragStartPanel + (mousePos - _dragStartMouse);
                ClampToViewport();
            }
        }
        else
        {
            if (_state == State.Dragging)
            {
                _panel.MouseDefaultCursorShape = Control.CursorShape.Arrow;
                _onDragEnd?.Invoke();
            }
            _state = State.None;
        }
    }

    private bool IsInHeader(Vector2 mouseGlobal)
    {
        if (_header == null || !_header.IsInsideTree()) return false;
        // Only drag within header bounds, excluding button area
        float hx = _header.GlobalPosition.X;
        float hy = _header.GlobalPosition.Y;
        float hw = _header.Size.X;
        float hh = _header.Size.Y;
        return mouseGlobal.X >= hx && mouseGlobal.X <= hx + hw
            && mouseGlobal.Y >= hy && mouseGlobal.Y <= hy + hh;
    }

    private void ClampToViewport()
    {
        var vp = _panel.GetViewport();
        if (vp == null) return;
        var vs = vp.GetVisibleRect().Size;
        _panel.Position = new Vector2(
            Mathf.Clamp(_panel.Position.X, 20, vs.X - _panel.Size.X - 20),
            Mathf.Clamp(_panel.Position.Y, 20, vs.Y - 40));
    }
}
