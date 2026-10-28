namespace CaptureDemo.Element;

/// <summary>
/// Cursor-following UI Automation hit test for the unified snip overlay.
/// <see cref="ElementOverlayForm"/> has its own drag/scroll state machine; this is the
/// part the capture capsule needs: which element is under the pointer, and one level
/// up or down the parent chain when the wheel turns.
/// </summary>
internal sealed class ElementHover : IDisposable
{
    private readonly ElementPicker _picker;
    private IReadOnlyList<ElementSnapshot> _chain = Array.Empty<ElementSnapshot>();
    private int _index = -1;
    private Point _last;
    private bool _hasLast;

    /// <exception cref="InvalidOperationException">UI Automation is unavailable.</exception>
    public ElementHover()
    {
        _picker = new ElementPicker();
    }

    public ElementSnapshot? Current =>
        _index >= 0 && _index < _chain.Count && _chain[_index].IsUseful ? _chain[_index] : null;

    /// <summary>Re-runs the hit test when the pointer has moved far enough to matter.</summary>
    public ElementSnapshot? Update(Point screen)
    {
        if (_hasLast && _chain.Count > 0 && ScreenRects.DistanceSquared(_last, screen) < 16)
        {
            return Current;
        }

        _last = screen;
        _hasLast = true;
        _chain = _picker.CaptureChain(screen);
        _index = FindUseful(-1, 1);
        return Current;
    }

    /// <summary>Walks the parent chain. Positive goes outward, negative inward.</summary>
    public ElementSnapshot? Cycle(int delta)
    {
        if (delta == 0 || _chain.Count == 0)
        {
            return Current;
        }

        var next = FindUseful(_index, delta > 0 ? 1 : -1);
        if (next == _index)
        {
            return Current;
        }

        _index = next;
        return Current;
    }

    public void Dispose() => _picker.Dispose();

    private int FindUseful(int current, int step)
    {
        if (_chain.Count == 0)
        {
            return -1;
        }

        var index = current;
        for (var n = 0; n < _chain.Count; n++)
        {
            if (index < 0)
            {
                index = step > 0 ? 0 : _chain.Count - 1;
            }
            else
            {
                index += step;
                if (index < 0)
                {
                    index = _chain.Count - 1;
                }
                else if (index >= _chain.Count)
                {
                    index = 0;
                }
            }

            if (_chain[index].IsUseful)
            {
                return index;
            }
        }

        return current;
    }
}
